namespace Atelier.Framework.Contract;

public static class ContractVersion
{
    public static bool TryParse(
        string version,
        out Version parsed)
    {
        return Version.TryParse(
            version,
            out parsed!);
    }

    public static Version Parse(string version)
    {
        if (!TryParse(
            version,
            out var parsed))
        {
            throw new FormatException(
                $"Contract version '{version}' is not a valid version");
        }

        return parsed;
    }

    public static bool TryCompare(
        string left,
        string right,
        out int comparison)
    {
        comparison = 0;

        if (!TryParse(
            left,
            out var leftVersion)
            || !TryParse(
                right,
                out var rightVersion))
        {
            return false;
        }

        comparison = leftVersion.CompareTo(rightVersion);
        return true;
    }

    public static bool Equals(
        string left,
        string right)
    {
        if (!TryCompare(
            left,
            right,
            out var comparison))
        {
            return string.Equals(
                left,
                right,
                StringComparison.Ordinal);
        }

        return comparison == 0;
    }
}
