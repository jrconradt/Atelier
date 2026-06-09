using System.Reflection;
using System.Text;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Contract;

[TestFixtureRegistry]
public static class ContractTestFixtures
{
    private const string HAPPY_NAME = "atelier-happy";

    private const string HAPPY_VERSION = "1.0";

    private const string HAPPY_NAMESPACE = "Framework.Contract.Happy";

    private static ContractMetadata HappyMetadata()
    {
        return new ContractMetadata
        {
            Name = HAPPY_NAME,
            Version = HAPPY_VERSION,
            Namespace = HAPPY_NAMESPACE,
            ContractType = typeof(object),
            IsBackwardCompatible = true,
            RequiredFields = new List<string>(),
            OptionalFields = new List<string>(),
        };
    }

    private static ContractRegistry HappyRegistry()
    {
        var registry = new ContractRegistry();
        registry.Register(HappyMetadata());
        return registry;
    }

    [Fixture(typeof(SerializedContract))]
    public static SerializedContract Envelope()
    {
        return new SerializedContract
        {
            ContractName = HAPPY_NAME,
            ContractVersion = HAPPY_VERSION,
            ContractNamespace = HAPPY_NAMESPACE,
            Payload = Encoding.UTF8.GetBytes("{}"),
            SerializationFormat = "application/json",
        };
    }

    [Fixture(typeof(JsonContractSerializer), Operation = "DeserializeWithMetadata")]
    public static JsonContractSerializer DeserializeReceiver()
    {
        var registry = HappyRegistry();
        var migrator = Construct<ContractMigrator>(registry);
        var validator = Construct<ContractValidator>(registry);

        var ctor = typeof(JsonContractSerializer)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var arguments = ctor.GetParameters()
            .Select(p => ResolveServiceArgument(p.ParameterType, registry, migrator, validator))
            .ToArray();

        return (JsonContractSerializer)ctor.Invoke(arguments);
    }

    private static T Construct<T>(IContractRegistry registry)
    {
        var ctor = typeof(T)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var arguments = ctor.GetParameters()
            .Select(p => ResolveServiceArgument(p.ParameterType, registry, null, null))
            .ToArray();

        return (T)ctor.Invoke(arguments);
    }

    private static object? ResolveServiceArgument(Type parameterType,
                                                  IContractRegistry registry,
                                                  IContractMigrator? migrator,
                                                  IContractValidator? validator)
    {
        if (parameterType == typeof(IContractRegistry))
        {
            return registry;
        }
        if (parameterType == typeof(IContractMigrator)
            && migrator is not null)
        {
            return migrator;
        }
        if (parameterType == typeof(IContractValidator)
            && validator is not null)
        {
            return validator;
        }
        return AutoMockProvider.For(parameterType);
    }
}
