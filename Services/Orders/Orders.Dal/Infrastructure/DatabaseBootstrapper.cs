using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Orders.Dal.Infrastructure;

// A database in a fresh container is empty, so on startup the service applies the scripts of Lab 01.
// The scripts are idempotent. Text of the scripts comes from the assembly, not from user input.
public sealed partial class DatabaseBootstrapper(string connectionString, ILogger<DatabaseBootstrapper> logger)
{
    public async Task EnsureCreatedAsync(bool seed, CancellationToken ct = default)
    {
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var masterConnectionString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.ExecuteAsync(new CommandDefinition(
                "IF DB_ID(@Name) IS NULL EXEC('CREATE DATABASE [' + @Name + ']')", new { Name = databaseName }, cancellationToken: ct));
        }

        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync(ct);

        await RunScriptAsync(db, "schema.sql", ct);
        await RunScriptAsync(db, "procedures.sql", ct);
        if (seed)
            await RunScriptAsync(db, "seed.sql", ct);

        logger.LogInformation("Database {Database} is ready, seed applied: {Seed}", databaseName, seed);
    }

    private static async Task RunScriptAsync(SqlConnection db, string name, CancellationToken ct)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded script {name} is missing");
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(ct);

        // GO is a command of sqlcmd/SSMS, not of the server, so batches are executed one by one
        foreach (var batch in GoSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
            await db.ExecuteAsync(new CommandDefinition(batch, cancellationToken: ct));
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
