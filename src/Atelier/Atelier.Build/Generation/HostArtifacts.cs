using System.Text.Json;

namespace Atelier.Build.Generation;

/// <summary>
/// The code-derived artifacts a boutique host carries. The host source generator
/// computes them at compile time and the host's AtelierWriteBoutiqueArtifacts build
/// target writes them out. smash passes the destination as
/// <c>-p:AtelierArtifactsDirectory</c> when it builds each boutique; a boutique is a
/// host exactly when its build left these files there.
/// </summary>
public static class HostArtifacts
{
    public const string REQUISITE_MANIFEST = "requisite-manifest.json";
    public const string NETWORK_POLICIES = "network-policies.yaml";

    public static void Clear(string artifactsDirectory)
    {
        if (!Directory.Exists(artifactsDirectory))
        {
            return;
        }

        foreach (var file in new[] { REQUISITE_MANIFEST, NETWORK_POLICIES })
        {
            File.Delete(Path.Combine(artifactsDirectory, file));
        }
    }

    public static bool Exist(string artifactsDirectory)
    {
        return File.Exists(Path.Combine(artifactsDirectory, REQUISITE_MANIFEST));
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
