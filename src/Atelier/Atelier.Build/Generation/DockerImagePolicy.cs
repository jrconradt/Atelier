namespace Atelier.Build.Generation;

public static class DockerImagePolicy
{
    private const string SDK_REPO = "mcr.microsoft.com/dotnet/sdk";
    private const string ASPNET_REPO = "mcr.microsoft.com/dotnet/aspnet";
    private const string RUNTIME_REPO = "mcr.microsoft.com/dotnet/runtime";

    private static readonly IReadOnlyDictionary<string, string> FrameworkToTag = new Dictionary<string, string>
    {
        ["net10.0"] = "10.0",
        ["net9.0"] = "9.0",
        ["net8.0"] = "8.0",
    };

    public static string Tag(string targetFramework)
    {
        if (FrameworkToTag.TryGetValue(targetFramework, out var tag))
        {
            return tag;
        }
        throw new InvalidOperationException($"Unsupported target framework '{targetFramework}'. Supported: net8.0, net9.0, net10.0.");
    }

    public static string SdkImage(string targetFramework, string? digest = null)
    {
        return Pin($"{SDK_REPO}:{Tag(targetFramework)}", digest);
    }

    public static string AspNetAlpineImage(string targetFramework, string? digest = null)
    {
        return Pin($"{ASPNET_REPO}:{Tag(targetFramework)}-alpine", digest);
    }

    public static string RuntimeImage(string targetFramework, string? digest = null)
    {
        return Pin($"{RUNTIME_REPO}:{Tag(targetFramework)}", digest);
    }

    private static string Pin(string reference, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return reference;
        }
        return $"{reference}@{digest}";
    }
}
