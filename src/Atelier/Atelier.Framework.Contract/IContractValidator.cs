namespace Atelier.Framework.Contract;

public interface IContractValidator
{
        public ContractValidationResult Validate<T>(T contract) where T : class;

        public ContractValidationResult Validate(
        object contract,
        Type contractType);

        public ContractValidationResult Validate(
        object contract,
        string contractName,
        string version,
        string? @namespace = null);

        public ContractValidationResult ValidateCompatibility(
        string contractName,
        string sourceVersion,
        string targetVersion);
}
