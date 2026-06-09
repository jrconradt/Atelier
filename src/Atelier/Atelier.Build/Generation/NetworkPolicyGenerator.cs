using Atelier.Build.Analysis;
using Atelier.Build.Pipeline;
using Templar.Rendering;
using T = Atelier.Build.Templates.NetworkPolicy;

namespace Atelier.Build.Generation;

public class NetworkPolicyGenerator
{
    private readonly BuildContext _context;

    public NetworkPolicyGenerator(BuildContext context)
    {
        _context = context;
    }

    public async Task<string?> GenerateAsync(IReadOnlyList<ResolvedBoutique> resolved)
    {
        var policies = resolved
            .SelectMany(boutique => boutique.NetworkZoning.ZonePolicies)
            .GroupBy(policy => policy.Zone, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(policy => policy.Zone, StringComparer.Ordinal)
            .ToList();

        if (policies.Count == 0)
        {
            return null;
        }

        var content = string.Join("---\n", policies.Select(RenderPolicy));

        var outputPath = Path.Combine(_context.SolutionRoot, "network-policies.yaml");
        await File.WriteAllTextAsync(outputPath, content).ConfigureAwait(false);

        return outputPath;
    }

    private static string RenderPolicy(ZonePolicyInfo policy)
    {
        var yaml = new T.NetworkPolicy
        {
            Name = $"{policy.Zone}-zone-policy",
            Zone = policy.Zone,
            Mtls = policy.RequiresMutualTls ? "true" : "false",
            Ingress = RenderPeers("    - from:", policy.AllowedInbound),
            Egress = RenderPeers("    - to:", policy.AllowedOutbound),
        }.Render();

        var lines = yaml
            .Split('\n')
            .Where(line => line.Trim().Length > 0);

        return string.Join("\n", lines) + "\n";
    }

    private static IComposable RenderPeers(string ruleLine, IReadOnlyList<string> zones)
    {
        if (zones.Count == 0)
        {
            return new Verbatim { Text = "    []" };
        }

        var peers = zones.Select(zone => new T.ZonePeer { Zone = zone }.Render().TrimEnd('\n'));
        return new Verbatim { Text = $"{ruleLine}\n{string.Join("\n", peers)}" };
    }
}
