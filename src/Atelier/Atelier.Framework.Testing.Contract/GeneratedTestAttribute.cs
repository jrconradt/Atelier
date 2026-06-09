namespace Atelier.Framework.Testing;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedTestAttribute : Attribute
{
        public string Invariant { get; }

        public string Target { get; }

    public GeneratedTestAttribute(string invariant, string target)
    {
        Invariant = invariant;
        Target = target;
    }
}

public sealed class NeedsFixtureException : Exception
{
    public NeedsFixtureException(string message) : base(message) { }
}
