using Meta.Core.Connections;

internal sealed partial class CliRuntime
{
    async Task<int> DeployAsync(string[] commandArgs)
    {
        if (commandArgs.Length < 2)
        {
            return PrintUsageError("Usage: deploy <sqlserver> ...");
        }

        var mode = commandArgs[1].Trim().ToLowerInvariant();
        switch (mode)
        {
            case "sqlserver":
                var options = ParseSqlServerDeployOptions(commandArgs, startIndex: 2);
                if (!options.Ok)
                {
                    return string.IsNullOrWhiteSpace(options.ErrorMessage) ? 0 : PrintArgumentError(options.ErrorMessage);
                }

                if (string.IsNullOrWhiteSpace(options.ScriptsDirectory))
                {
                    return PrintArgumentError("Error: deploy sqlserver requires --scripts <dir>.");
                }

                if (string.IsNullOrWhiteSpace(options.ConnectionEnvironmentVariableName))
                {
                    return PrintArgumentError("Error: deploy sqlserver requires --connection-env <name>.");
                }

                try
                {
                    var connectionString = ConnectionEnvironmentVariableResolver.ResolveRequired(
                        options.ConnectionEnvironmentVariableName);
                    var result = await services.SqlServerDeploymentService
                        .DeployAsync(options.ScriptsDirectory, connectionString, options.DatabaseName)
                        .ConfigureAwait(false);

                    presenter.WriteOk(
                        "deployed sqlserver scripts",
                        ("Scripts", Path.GetFullPath(options.ScriptsDirectory)),
                        ("Database", result.DatabaseName),
                        ("Files", result.ScriptFileCount.ToString(CultureInfo.InvariantCulture)),
                        ("Batches", result.BatchCount.ToString(CultureInfo.InvariantCulture)));

                    return 0;
                }
                catch (Exception exception)
                {
                    return PrintGenerationError("E_DEPLOY", exception.Message);
                }

            default:
                return PrintArgumentError($"Error: unknown deploy mode '{mode}'.");
        }
    }

    (bool Ok, string ScriptsDirectory, string ConnectionEnvironmentVariableName, string? DatabaseName, string ErrorMessage)
        ParseSqlServerDeployOptions(string[] commandArgs, int startIndex)
    {
        var scriptsDirectory = string.Empty;
        var connectionEnvironmentVariableName = string.Empty;
        string? databaseName = null;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--scripts", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionEnvironmentVariableName, databaseName, "Error: --scripts requires a directory path.");
                }

                scriptsDirectory = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--connection-env", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionEnvironmentVariableName, databaseName, "Error: --connection-env requires a name.");
                }

                connectionEnvironmentVariableName = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--database", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionEnvironmentVariableName, databaseName, "Error: --database requires a database name.");
                }

                databaseName = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintSqlServerDeployHelp();
                return (false, scriptsDirectory, connectionEnvironmentVariableName, databaseName, string.Empty);
            }

            return (false, scriptsDirectory, connectionEnvironmentVariableName, databaseName, $"Error: unknown option '{arg}'.");
        }

        return (true, scriptsDirectory, connectionEnvironmentVariableName, databaseName, string.Empty);
    }

    void PrintSqlServerDeployHelp()
    {
        presenter.WriteInfo("Command: deploy sqlserver");
        presenter.WriteInfo("Usage:");
        presenter.WriteInfo("  meta deploy sqlserver --scripts <dir> --connection-env <name> [--database <name>]");
        presenter.WriteInfo("Notes:");
        presenter.WriteInfo("  Deploys SQL scripts in dependency-derived order.");
        presenter.WriteInfo("  Table creation, foreign-key references, and inserts are analyzed to determine execution order.");
        presenter.WriteInfo("  Supports GO batch separators inside each script.");
        presenter.WriteInfo("  --connection-env names the environment variable that contains the SQL Server connection string.");
        presenter.WriteInfo("  If --database is provided, the database is created if missing and used as the deploy target.");
    }
}


