using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Contract;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed partial class ContractValidator : IContractValidator
{
    private readonly IContractRegistry _registry;
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> PropertyMaps = new();

        public ContractValidator(IContractRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

        public ContractValidationResult Validate<T>(T contract) where T : class
    {
        ArgumentNullException.ThrowIfNull(contract);
        return Validate(
            contract!,
            typeof(T));
    }

        public ContractValidationResult Validate(
        object contract,
        Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contractType);

        var metadata = _registry.Resolve(contractType);
        if (metadata == null)
        {
            return ContractValidationResult.Failure(
                new ValidationError
                {
                    Field = contractType.Name,
                    Message = $"Contract type {contractType.FullName} is not registered",
                    Code = "CONTRACT_NOT_REGISTERED"
                });
        }

        return ValidateAgainstBaseline(
            contract,
            metadata);
    }

        public ContractValidationResult Validate(
        object contract,
        string contractName,
        string version,
        string? @namespace = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contractName);
        ArgumentNullException.ThrowIfNull(version);

        var metadata = _registry.Resolve(
            contractName,
            version,
            @namespace);
        if (metadata == null)
        {
            return ContractValidationResult.Failure(
                new ValidationError
                {
                    Field = contractName,
                    Message = $"Contract {contractName} version {version} is not registered",
                    Code = "CONTRACT_NOT_REGISTERED"
                });
        }

        return ValidateAgainstBaseline(
            contract,
            metadata);
    }

        private static ContractValidationResult ValidateAgainstBaseline(
        object contract,
        ContractMetadata baseline)
    {
        var errors = new List<ValidationError>();
        var presentFields = PropertyMaps.GetOrAdd(
            contract.GetType(),
            static type =>
            {
                var map = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    map[property.Name] = property;
                }

                return map;
            });

        foreach (var requiredField in baseline.RequiredFields)
        {
            if (!presentFields.TryGetValue(
                requiredField,
                out var property))
            {
                errors.Add(new ValidationError
                {
                    Field = requiredField,
                    Message = $"Required field '{requiredField}' is missing from the payload for version {baseline.Version}",
                    Code = "REQUIRED_FIELD_MISSING"
                });
                continue;
            }

            var value = property.GetValue(contract);
            if (value == null)
            {
                errors.Add(new ValidationError
                {
                    Field = requiredField,
                    Message = $"Required field '{requiredField}' cannot be null",
                    Code = "REQUIRED_FIELD_NULL"
                });
            }
        }

        return errors.Count > 0
            ? ContractValidationResult.Failure(errors.ToArray())
            : ContractValidationResult.Success();
    }

        public ContractValidationResult ValidateCompatibility(
        string contractName,
        string sourceVersion,
        string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(contractName);
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);

        if (!_registry.IsCompatible(
            contractName,
            sourceVersion,
            targetVersion))
        {
            return ContractValidationResult.Failure(
                new ValidationError
                {
                    Field = "Version",
                    Message = $"Contract {contractName} version {sourceVersion} is not compatible with {targetVersion}",
                    Code = "VERSION_INCOMPATIBLE"
                });
        }

        return ContractValidationResult.Success();
    }
}
