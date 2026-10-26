using System.Text.Json;
using Atelier.Build.Pipeline;
using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Generation;

/// <summary>
/// The code-derived artifacts a boutique host carries. The host source generator
/// computes them at compile time; smash only asks the built host to write them out
/// (<c>dotnet Host.dll --atelier-artifacts dir</c>) and reads the files back as data.
/// </summary>
public static class HostArtifacts
{
    public const string REQUISITE_MANIFEST = "requisite-manifest.json";
    public const string NETWORK_ZONES = "network-zones.json";
    public const string NETWORK_POLICIES = "network-policies.yaml";

    private const string ARTIFACTS_SWITCH = "--atelier-artifacts";
    private static readonly TimeSpan EXTRACT_TIMEOUT = TimeSpan.FromMinutes(1);

    public static string HostAssemblyPath(string hostProjectPath, string compiledAssembliesDirectory)
    {
        return Path.Combine(compiledAssembliesDirectory, Path.GetFileNameWithoutExtension(hostProjectPath) + ".dll");
    }

    public static async Task<bool> ExtractAsync(BuildContext context, string hostAssemblyPath, string outputDirectory)
    {
        if (!File.Exists(hostAssemblyPath))
        {
            AnsiConsole.MarkupLine($"[yellow]    ⚠ Host not built, no code-derived artifacts: {Markup.Escape(hostAssemblyPath)}[/]");
            return false;
        }

        var result = await new ProcessExecutor(context).ExecuteAsync(
            "dotnet",
            [hostAssemblyPath, ARTIFACTS_SWITCH, outputDirectory],
            outputDirectory,
            ProcessOptions.WithTimeout(EXTRACT_TIMEOUT)).ConfigureAwait(false);

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[yellow]    ⚠ Host artifact extraction failed ({result.ExitCode}): {Markup.Escape(result.StandardError)}[/]");
        }

        return result.Success;
    }

    public static ResolvedNetworkZoning ReadNetworkZoning(string artifactsDirectory)
    {
        using var document = ReadJson(Path.Combine(artifactsDirectory, NETWORK_ZONES));
        return new ResolvedNetworkZoning
        {
            IsolatedNetworks = document is null
                ? []
                : StringArray(document.RootElement, "isolatedNetworks"),
        };
    }

    public static IReadOnlyList<string> ReadRequisiteAssemblies(string artifactsDirectory)
    {
        using var document = ReadJson(Path.Combine(artifactsDirectory, REQUISITE_MANIFEST));
        return document is null
            ? []
            : StringArray(document.RootElement, "requisiteAssemblies");
    }

    private static JsonDocument? ReadJson(string path)
    {
        return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
    }

    private static IReadOnlyList<string> StringArray(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
            : [];
    }
}
