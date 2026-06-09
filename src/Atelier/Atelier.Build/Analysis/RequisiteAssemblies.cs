namespace Atelier.Build.Analysis;

public class RequisiteAssembly
{
    public required string AssemblyName { get; init; }
    public required string AssemblyPath { get; init; }
}

public class RequisiteAssemblies
{
    private readonly List<RequisiteAssembly> _assemblies = new();

    public IReadOnlyList<RequisiteAssembly> Assemblies => _assemblies;

    public int Count => _assemblies.Count;

    public void Add(string name, string? path = null)
    {
        _assemblies.Add(new RequisiteAssembly
        {
            AssemblyName = name,
            AssemblyPath = path ?? string.Empty
        });
    }

    public bool Contains(string name)
        => _assemblies.Any(a => a.AssemblyName == name);

    public void Merge(RequisiteAssemblies other)
    {
        _assemblies.AddRange(other.Assemblies);
    }

    public IReadOnlyList<string> ToNameList()
        => _assemblies.Select(a => a.AssemblyName).ToList();
}
