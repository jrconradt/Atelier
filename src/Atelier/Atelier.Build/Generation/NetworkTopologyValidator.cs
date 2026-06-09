using YamlDotNet.Serialization;

namespace Atelier.Build.Generation;

public sealed record NetworkTopologyViolation(string Kind, string Detail);

public static class NetworkTopologyValidator
{
    private sealed class ComposeDocument
    {
        [YamlMember(Alias = "services")]
        public Dictionary<string, ComposeService>? Services { get; set; }

        [YamlMember(Alias = "networks")]
        public Dictionary<string, object?>? Networks { get; set; }
    }

    private sealed class ComposeService
    {
        [YamlMember(Alias = "networks")]
        public List<string>? Networks { get; set; }
    }

    public static IReadOnlyList<NetworkTopologyViolation> ValidateComposeFile(string composeFilePath)
    {
        var violations = new List<NetworkTopologyViolation>();

        if (!File.Exists(composeFilePath))
        {
            return violations;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var document = deserializer.Deserialize<ComposeDocument>(File.ReadAllText(composeFilePath));
        if (document is null)
        {
            return violations;
        }

        var declared = new HashSet<string>(
            document.Networks?.Keys ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);

        var attached = new HashSet<string>(StringComparer.Ordinal);
        if (document.Services is not null)
        {
            foreach (var service in document.Services.Values)
            {
                if (service.Networks is null)
                {
                    continue;
                }

                foreach (var network in service.Networks)
                {
                    attached.Add(network);
                }
            }
        }

        foreach (var network in declared)
        {
            if (!attached.Contains(network))
            {
                violations.Add(new NetworkTopologyViolation(
                    "OrphanNetwork",
                    $"network '{network}' is declared but no service attaches to it"));
            }
        }

        foreach (var network in attached)
        {
            if (!declared.Contains(network))
            {
                violations.Add(new NetworkTopologyViolation(
                    "DanglingNetwork",
                    $"a service attaches to network '{network}' that is not declared"));
            }
        }

        return violations;
    }
}
