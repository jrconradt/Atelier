namespace Atelier.Build.Utils;

public static class Naming
{
    public static string ToPascalCase(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var words = text.Split(
            new[] { ' ', '-', '_', '.' },
            StringSplitOptions.RemoveEmptyEntries);

        return string.Concat(words.Select(w =>
            char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : string.Empty)));
    }

    public static string ToBoutiqueDir(string name)
    {
        return name.Replace("atelier-", string.Empty).ToLowerInvariant();
    }

    public static string ToBoutiqueAssemblyIdentifier(string name)
    {
        return ToPascalCase(name.Replace("atelier-", string.Empty).Replace("-", " "));
    }
}
