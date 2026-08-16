using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atelier.Facilities.Database;

[Facility("Database",
          RequiresAuthentication = true,
          AllowAnonymous = false,
          RequiredScopes = new[] { "database.access" })]
public interface IDatabase
{
    public Task<Outcome<DatabaseResult>> QueryAsync(
        DatabaseQuery query,
        CancellationToken cancellationToken = default);

    public Task<Outcome> ExecuteAsync(
        DatabaseCommand command,
        CancellationToken cancellationToken = default);
}

[Contract("DatabaseQuery", Version = "1.0", Namespace = "Facilities.Database")]
public class DatabaseQuery
{
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

[Contract("DatabaseCommand", Version = "1.0", Namespace = "Facilities.Database")]
public class DatabaseCommand
{
    public string Sql { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

[Contract("DatabaseResult", Version = "1.0", Namespace = "Facilities.Database")]
public class DatabaseResult
{
    public List<Dictionary<string, object>> Rows { get; set; } = new();
}
