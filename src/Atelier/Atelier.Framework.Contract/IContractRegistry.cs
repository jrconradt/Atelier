using System.Reflection;

namespace Atelier.Framework.Contract;

public interface IContractRegistry
{
    public void Register(ContractMetadata metadata);

    public void Register<T>() where T : class;

    public void RegisterFromAssembly(Assembly assembly);

    public void RegisterFromAssemblies(params Assembly[] assemblies);

    public ContractMetadata? Resolve(
        string name,
        string? version = null,
        string? @namespace = null);

    public ContractMetadata? Resolve(Type type);

    public ContractMetadata? Resolve<T>() where T : class;

    public IEnumerable<ContractMetadata> GetAll();

    public IEnumerable<ContractMetadata> GetByName(string name);

    public IEnumerable<ContractMetadata> GetByNamespace(string @namespace);

    public bool IsCompatible(
        string name,
        string sourceVersion,
        string targetVersion);
}
