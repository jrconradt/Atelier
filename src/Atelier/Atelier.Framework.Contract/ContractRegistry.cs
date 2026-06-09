using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Contract;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed partial class ContractRegistry : IContractRegistry
{
        private const char QualifiedNameSeparator = '.';

        private const char VersionSeparator = ':';

        private const char LatestKeySeparator = '\n';

        private readonly ConcurrentDictionary<string, ContractMetadata> _contracts = new();

        private readonly ConcurrentDictionary<Type, string> _typeIndex = new();

        private readonly ConcurrentDictionary<string, ContractMetadata> _latestByName = new();

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ContractMetadata>> _byName = new();

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ContractMetadata>> _byNamespace = new();

        public void Register(ContractMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        RejectReservedChars(
            "Name",
            metadata.Name,
            QualifiedNameSeparator,
            VersionSeparator,
            LatestKeySeparator);
        RejectReservedChars(
            "Version",
            metadata.Version,
            VersionSeparator,
            LatestKeySeparator);
        if (!ContractVersion.TryParse(
            metadata.Version,
            out _))
        {
            throw new ArgumentException(
                $"Contract Version '{metadata.Version}' is not a valid version");
        }
        if (!string.IsNullOrEmpty(metadata.Namespace))
        {
            RejectReservedChars(
                "Namespace",
                metadata.Namespace!,
                VersionSeparator,
                LatestKeySeparator);
        }

        var key = ContractKey(
            metadata.Name,
            metadata.Version,
            metadata.Namespace);
        _contracts[key] = metadata;
        _typeIndex[metadata.ContractType] = key;

        IndexByGroup(
            _byName,
            metadata.Name,
            key,
            metadata);
        IndexByGroup(
            _byNamespace,
            metadata.Namespace ?? string.Empty,
            key,
            metadata);

        UpdateLatestIndex(LatestKey(metadata.Name,
                                   metadata.Namespace),
                          metadata);
        UpdateLatestIndex(LatestKey(metadata.Name,
                                   null),
                          metadata);
    }

        private static void IndexByGroup(
        ConcurrentDictionary<string, ConcurrentDictionary<string, ContractMetadata>> index,
        string groupKey,
        string contractKey,
        ContractMetadata metadata)
    {
        var group = index.GetOrAdd(
            groupKey,
            _ => new ConcurrentDictionary<string, ContractMetadata>());
        group[contractKey] = metadata;
    }

        private static string ContractKey(
        string name,
        string version,
        string? @namespace)
    {
        return string.IsNullOrEmpty(@namespace)
            ? $"{name}{VersionSeparator}{version}"
            : $"{@namespace}{QualifiedNameSeparator}{name}{VersionSeparator}{version}";
    }

        private static void RejectReservedChars(
        string field,
        string value,
        params char[] reserved)
    {
        if (value.IndexOfAny(reserved) >= 0)
        {
            throw new ArgumentException(
                $"Contract {field} '{value}' must not contain reserved separator characters");
        }
    }

        private void UpdateLatestIndex(
        string key,
        ContractMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(metadata);

        var candidateVersion = ContractVersion.Parse(metadata.Version);

        _latestByName.AddOrUpdate(
            key,
            metadata,
            (_, existing) => candidateVersion >= ContractVersion.Parse(existing.Version) ? metadata : existing);
    }

        private static string LatestKey(
        string name,
        string? @namespace)
    {
        return string.IsNullOrEmpty(@namespace)
            ? $"{LatestKeySeparator}{name}"
            : $"{@namespace}{LatestKeySeparator}{name}";
    }

        public void Register<T>() where T : class => Register(typeof(T));

        public void RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var contractTypes = assembly.GetTypes()
            .Where(t => t.IsClass &&
                       !t.IsAbstract &&
                       t.GetCustomAttribute<ContractAttribute>() != null);

        foreach (var type in contractTypes)
        {
            Register(type);
        }
    }

        public void RegisterFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            RegisterFromAssembly(assembly);
        }
    }

        private void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var attribute = type.GetCustomAttribute<ContractAttribute>();
        if (attribute == null)
        {
            throw new InvalidOperationException(
                $"Type {type.FullName} does not have [Contract] attribute");
        }

        var metadata = ExtractMetadata(
            type,
            attribute);
        Register(metadata);
    }

        public ContractMetadata? Resolve(
        string name,
        string? version = null,
        string? @namespace = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (version != null)
        {
            var fullName = ContractKey(
                name,
                version,
                @namespace);

            return _contracts.TryGetValue(
                fullName,
                out var metadata) ? metadata : null;
        }

        return _latestByName.TryGetValue(
            LatestKey(name,
                      @namespace),
            out var latest) ? latest : null;
    }

        public ContractMetadata? Resolve(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (_typeIndex.TryGetValue(
            type,
            out var key))
        {
            return _contracts.TryGetValue(
                key,
                out var metadata) ? metadata : null;
        }

        return null;
    }

        public ContractMetadata? Resolve<T>() where T : class => Resolve(typeof(T));

        public IEnumerable<ContractMetadata> GetAll() => _contracts.Values;

        public IEnumerable<ContractMetadata> GetByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(
            name,
            out var group) ? group.Values : Enumerable.Empty<ContractMetadata>();
    }

        public IEnumerable<ContractMetadata> GetByNamespace(string @namespace)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        return _byNamespace.TryGetValue(
            @namespace,
            out var group) ? group.Values : Enumerable.Empty<ContractMetadata>();
    }

        public bool IsCompatible(
        string name,
        string sourceVersion,
        string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);

        var source = Resolve(
            name,
            sourceVersion);
        var target = Resolve(
            name,
            targetVersion);

        if (source == null || target == null)
        {
            return false;
        }

        if (!target.IsBackwardCompatible)
        {
            return false;
        }

        if (!ContractVersion.TryCompare(
            targetVersion,
            sourceVersion,
            out var comparison)
            || comparison < 0)
        {
            return false;
        }

        return FieldShapeIsCompatible(
            source,
            target);
    }

        private static bool FieldShapeIsCompatible(
        ContractMetadata source,
        ContractMetadata target)
    {
        var sourceFields = new HashSet<string>(
            source.RequiredFields,
            StringComparer.Ordinal);
        foreach (var optional in source.OptionalFields)
        {
            sourceFields.Add(optional);
        }

        foreach (var requiredField in target.RequiredFields)
        {
            if (!sourceFields.Contains(requiredField))
            {
                return false;
            }
        }

        return true;
    }

        private static ContractMetadata ExtractMetadata(
        Type type,
        ContractAttribute attribute)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var required = new List<string>();
        var optional = new List<string>();

        foreach (var prop in properties)
        {
            if (IsRequired(prop))
            {
                required.Add(prop.Name);
            }
            else
            {
                optional.Add(prop.Name);
            }
        }

        return new ContractMetadata
        {
            Name = attribute.Name,
            Version = attribute.Version,
            Namespace = attribute.Namespace,
            ContractType = type,
            IsBackwardCompatible = attribute.IsBackwardCompatible,
            RequiredFields = required,
            OptionalFields = optional
        };
    }

        private static bool IsRequired(PropertyInfo property)
    {
        var nullabilityInfo = new NullabilityInfoContext().Create(property);
        return nullabilityInfo.WriteState == NullabilityState.NotNull;
    }

}
