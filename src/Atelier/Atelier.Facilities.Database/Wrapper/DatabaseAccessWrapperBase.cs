using Atelier.Framework.Outcomes;
using System.Threading;
using System.Threading.Tasks;

namespace Atelier.Facilities.Database;

public abstract class DatabaseAccessWrapperBase : IDatabase
{
    public abstract Task<Outcome<DatabaseResult>> QueryAsync(
        DatabaseQuery query,
        CancellationToken cancellationToken = default);

    public abstract Task<Outcome> ExecuteAsync(
        DatabaseCommand command,
        CancellationToken cancellationToken = default);
}
