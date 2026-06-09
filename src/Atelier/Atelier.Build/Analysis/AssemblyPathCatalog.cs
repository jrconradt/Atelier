namespace Atelier.Build.Analysis;

public static class AssemblyPathCatalog
{
    public static IReadOnlyList<string> GetAssemblyPaths(string directory)
    {
        var paths = new List<string>();
        paths.AddRange(GetDllsInDirectory(directory));

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir != null)
        {
            paths.AddRange(GetDllsInDirectory(runtimeDir));
        }

        return paths;
    }

    private static string[] GetDllsInDirectory(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.dll")
            : [];
    }
}
