namespace Atelier.Build.Utils;

public static class PathSegments
{
    private static readonly char[] Separators = ['/', '\\'];

    public static bool ContainsSegment(string path, string segment)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var parts = path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (string.Equals(part, segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUnderBinOrObj(string path)
    {
        return ContainsSegment(path, "obj")
            || ContainsSegment(path, "bin");
    }
}
