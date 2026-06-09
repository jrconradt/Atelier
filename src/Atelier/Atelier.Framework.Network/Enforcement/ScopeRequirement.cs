namespace Atelier.Framework.Network.Enforcement;

public readonly struct ScopeRequirement
{
    private readonly HashSet<string> _scopes;

    public bool FailClosed { get; }

    public ScopeRequirement(HashSet<string> scopes,
                            bool failClosed)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        _scopes = scopes;
        FailClosed = failClosed;
    }

    public int Count
    {
        get
        {
            return _scopes.Count;
        }
    }

    public bool Contains(string scope)
    {
        return _scopes.Contains(scope);
    }

    public IEnumerable<string> Scopes
    {
        get
        {
            return _scopes;
        }
    }

    public static implicit operator ScopeRequirement(HashSet<string> scopes)
    {
        return new ScopeRequirement(scopes, failClosed: false);
    }
}
