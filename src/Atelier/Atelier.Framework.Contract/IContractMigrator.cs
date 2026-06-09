using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Contract
{
        public interface IContractMigrator
    {
                public Outcome RegisterMigration<TSource, TTarget>(
            string sourceVersion,
            string targetVersion,
            Func<TSource, TTarget> migrator)
            where TSource : class
            where TTarget : class;

                public Outcome<TTarget?> Migrate<TSource, TTarget>(
            TSource source,
            string targetVersion,
            bool allowBreaking = false)
            where TSource : class
            where TTarget : class;

                public Outcome<object?> Migrate(
            object source,
            Type sourceType,
            Type targetType,
            string targetVersion,
            bool allowBreaking = false);

                public bool CanMigrate(
            string contractName,
            string sourceVersion,
            string targetVersion);

                public bool IsBackwardCompatiblePath(
            string contractName,
            string sourceVersion,
            string targetVersion);
    }
}
