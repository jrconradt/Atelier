using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;
using Spectre.Console;
using Templar.Rendering;
using T = Atelier.Build.Templates.Docker;

namespace Atelier.Build.Generation;

public class DockerfileGenerator
{
    private const string LOG_DIR = "/var/atelier/logs";
    private const string CERT_DIR = "/etc/atelier/certs";

    private readonly BuildContext _context;

    public DockerfileGenerator(BuildContext context)
    {
        _context = context;
    }

    private static bool IsValidTypeName(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.IsValidTypeName(value);
    }

    private static string SanitizeHealthPath(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.SanitizeHealthPath(value);
    }

    private static string SanitizeScalar(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.SanitizeScalar(value);
    }

    private static string SanitizeEnvValue(string? value)
    {
        return Atelier.Build.Utils.GeneratorText.EscapeQuotedScalar(value);
    }

    public async Task<string> GenerateAsync(
        BoutiqueYamlSchema schema,
        ResolvedBoutique resolved,
        string outputDirectory)
    {
        if (string.IsNullOrEmpty(schema.HostProject)
            || !schema.HostProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Boutique host '{schema.Name}' must name its host .csproj as the smash.yml 'solution'; got '{schema.HostProject}'.");
        }

        var hostProject = Path.GetRelativePath(_context.RepositoryRoot, schema.HostProject).Replace('\\', '/');
        var assemblyName = Path.GetFileNameWithoutExtension(schema.HostProject);

        if (!IsValidTypeName(assemblyName))
        {
            AnsiConsole.MarkupLine($"[red]Error: refusing to generate Dockerfile for host project with invalid assembly name: {Markup.Escape(assemblyName)}[/]");
            throw new InvalidOperationException($"Host project '{schema.HostProject}' yields an invalid assembly name '{assemblyName}'.");
        }

        var code = new T.Dockerfile
        {
            BuildStage = RenderBuildStage(resolved, hostProject),
            RuntimeStage = RenderRuntimeStage(resolved, assemblyName),
        }.Render();

        var outputPath = Path.Combine(outputDirectory, "Dockerfile");
        await File.WriteAllTextAsync(outputPath, code).ConfigureAwait(false);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"[dim]    → Generated Dockerfile ({code.Split('\n').Length} lines)[/]");
        }

        return outputPath;
    }

    private static Compositor RenderBuildStage(ResolvedBoutique resolved, string hostProject)
    {
        return new T.DockerfileBuildStage
        {
            SdkImage = resolved.ImageConfig.SdkImage,
            HostProject = hostProject,
        };
    }

    private static Compositor RenderRuntimeStage(ResolvedBoutique resolved, string assemblyName)
    {
        var baseImage = resolved.ImageConfig.RuntimeImage;
        var uid = resolved.SecurityContext.Uid;
        var gid = resolved.SecurityContext.Gid;
        var username = resolved.SecurityContext.Username;
        var groupName = resolved.SecurityContext.GroupName;

        var installDeps = resolved.ImageConfig.IsAlpine
            ? "RUN apk add --no-cache curl bash"
            : "RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*";

        var userSetup = resolved.ImageConfig.IsAlpine
            ? $"RUN addgroup -g {gid} {groupName} && adduser -u {uid} -G {groupName} -S -H {username}"
            : $"RUN groupadd --gid {gid} {groupName} && useradd --uid {uid} --gid {gid} --system --no-create-home --shell /usr/sbin/nologin {username}";

        return new T.DockerfileRuntimeStage
        {
            BaseImage = SanitizeScalar(baseImage),
            InstallDeps = installDeps,
            UserSetup = userSetup,
            Uid = uid,
            Gid = gid,
            Username = username,
            LogDir = LOG_DIR,
            CertSection = BuildCertSection(resolved),
            EnvSection = BuildEnvSection(resolved),
            ExposeSection = BuildExposeSection(resolved),
            HealthInterval = resolved.Health.ReadinessIntervalSeconds,
            HealthTimeout = resolved.Health.TimeoutSeconds,
            HealthRetries = resolved.Health.Retries,
            StartupDelay = resolved.Health.ReadinessStartupDelaySeconds,
            HealthPort = resolved.Health.HealthcheckPort,
            HealthPath = SanitizeHealthPath(resolved.Health.ReadinessPath),
            AssemblyName = assemblyName,
        };
    }

    private static IComposable BuildCertSection(ResolvedBoutique resolved)
    {
        var fileCerts = resolved.Tls.EndpointConfigs
            .Where(c => !string.IsNullOrWhiteSpace(c.CertPath))
            .ToList();

        if (fileCerts.Count == 0)
        {
            return new Verbatim { Text = string.Empty };
        }

        var copies = new List<Compositor>();

        foreach (var cert in fileCerts)
        {
            copies.Add(new T.CertCopy
            {
                Uid = resolved.SecurityContext.Uid,
                Gid = resolved.SecurityContext.Gid,
                Source = SanitizeScalar(cert.CertPath),
                Destination = $"{CERT_DIR}/{SanitizeScalar(cert.EndpointName)}.pfx",
            });

            if (!string.IsNullOrWhiteSpace(cert.KeyPath))
            {
                copies.Add(new T.CertCopy
                {
                    Uid = resolved.SecurityContext.Uid,
                    Gid = resolved.SecurityContext.Gid,
                    Source = SanitizeScalar(cert.KeyPath),
                    Destination = $"{CERT_DIR}/{SanitizeScalar(cert.EndpointName)}.key",
                });
            }
        }

        return Sequence.Lines(copies);
    }

    private static IComposable BuildEnvSection(ResolvedBoutique resolved)
    {
        if (resolved.Environment.AllVariables.Count == 0)
        {
            return new Verbatim { Text = string.Empty };
        }
        return Sequence.Lines(resolved.Environment.AllVariables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => (Compositor)new T.EnvLine
            {
                Key = SanitizeScalar(kvp.Key),
                Value = SanitizeEnvValue(kvp.Value),
            }));
    }

    private static Compositor BuildExposeSection(ResolvedBoutique resolved)
    {
        if (resolved.Ports.AllEndpoints.Count == 0)
        {
            return new Verbatim { Text = string.Empty };
        }

        var gravityPort = resolved.Ports.GravityPort;

        var portSpecs = new Sequence(resolved.Ports.AllEndpoints.Select(endpoint => (Compositor)new T.PortSpec
            {
                Port = endpoint.Port,
                UdpSuffix = EndpointResolution.UdpSuffixFor(endpoint.Port, gravityPort),
            }),
            string.Empty);

        return new T.ExposeSection { PortSpecs = portSpecs };
    }
}
