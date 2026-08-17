using Atelier.Framework.Attributes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Contract;

public static class ContractMigratorTests
{
    private const string TARGET = "global::Atelier.Framework.Contract.ContractMigrator";

    [Contract("MigratableContract", Version = "1.0", Namespace = "Framework.Contract.Tests")]
    private sealed class ContractV1
    {
        public int Value { get; set; }
    }

    [Contract("MigratableContract", Version = "2.0", Namespace = "Framework.Contract.Tests", IsBackwardCompatible = true)]
    private sealed class ContractV2
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    [Contract("MigratableContract", Version = "3.0", Namespace = "Framework.Contract.Tests", IsBackwardCompatible = true)]
    private sealed class ContractV3
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool Final { get; set; }
    }

    [GeneratedTest("contract.migrator.transitive-path", TARGET)]
    public static void MigrateComposesTransitivePath()
    {
        var registry = new ContractRegistry();
        registry.Register<ContractV1>();
        registry.Register<ContractV2>();
        registry.Register<ContractV3>();

        var migrator = new ContractMigrator(registry,
                                            null);

        var hopOne = migrator.RegisterMigration<ContractV1, ContractV2>(
            "1.0",
            "2.0",
            v1 => new ContractV2 { Value = v1.Value, Label = $"v{v1.Value}" });
        if (!hopOne.IsSuccess)
        {
            throw new InvalidOperationException("Registering 1.0=>2.0 failed");
        }

        var hopTwo = migrator.RegisterMigration<ContractV2, ContractV3>(
            "2.0",
            "3.0",
            v2 => new ContractV3 { Value = v2.Value, Label = v2.Label, Final = true });
        if (!hopTwo.IsSuccess)
        {
            throw new InvalidOperationException("Registering 2.0=>3.0 failed");
        }

        if (!migrator.CanMigrate("MigratableContract", "1.0", "3.0"))
        {
            throw new InvalidOperationException("Expected a transitive migration path from 1.0 to 3.0");
        }

        var result = migrator.Migrate<ContractV1, ContractV3>(
            new ContractV1 { Value = 11 },
            "3.0");

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Transitive migration failed");
        }

        var migrated = result.Data;
        if (migrated == null)
        {
            throw new InvalidOperationException("Transitive migration produced null");
        }

        if (migrated.Value != 11
            || migrated.Label != "v11"
            || !migrated.Final)
        {
            throw new InvalidOperationException($"Unexpected migrated contract: Value={migrated.Value}, Label='{migrated.Label}', Final={migrated.Final}");
        }
    }

    [GeneratedTest("contract.migrator.duplicate-registration-rejected", TARGET)]
    public static void DuplicateRegistrationIsRejected()
    {
        var registry = new ContractRegistry();
        registry.Register<ContractV1>();
        registry.Register<ContractV2>();

        var migrator = new ContractMigrator(registry,
                                            null);

        var first = migrator.RegisterMigration<ContractV1, ContractV2>(
            "1.0",
            "2.0",
            v1 => new ContractV2 { Value = v1.Value });
        if (!first.IsSuccess)
        {
            throw new InvalidOperationException("Initial registration failed");
        }

        var duplicate = migrator.RegisterMigration<ContractV1, ContractV2>(
            "1.0",
            "2.0",
            v1 => new ContractV2 { Value = v1.Value });

        if (duplicate.IsSuccess)
        {
            throw new InvalidOperationException("Duplicate migration registration should fail");
        }
    }

    [GeneratedTest("contract.migrator.target-version-mismatch-rejected", TARGET)]
    public static void TargetVersionMismatchIsRejected()
    {
        var registry = new ContractRegistry();
        registry.Register<ContractV1>();
        registry.Register<ContractV3>();

        var migrator = new ContractMigrator(registry,
                                            null);

        var result = migrator.Migrate(
            new ContractV1 { Value = 1 },
            typeof(ContractV1),
            typeof(ContractV3),
            "2.0");

        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Migration with a target version that does not match the target contract should fail");
        }
    }
}
