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

                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    return PrintArgumentError("Error: deploy sqlserver requires --connection-string <value>.");
                }

                try
                {
                    var result = await services.SqlServerDeploymentService
                        .DeployAsync(options.ScriptsDirectory, options.ConnectionString, options.DatabaseName)
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

    (bool Ok, string ScriptsDirectory, string ConnectionString, string? DatabaseName, string ErrorMessage)
        ParseSqlServerDeployOptions(string[] commandArgs, int startIndex)
    {
        var scriptsDirectory = string.Empty;
        var connectionString = string.Empty;
        string? databaseName = null;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--scripts", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionString, databaseName, "Error: --scripts requires a directory path.");
                }

                scriptsDirectory = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--connection-string", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionString, databaseName, "Error: --connection-string requires a value.");
                }

                connectionString = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--database", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, scriptsDirectory, connectionString, databaseName, "Error: --database requires a database name.");
                }

                databaseName = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                PrintSqlServerDeployHelp();
                return (false, scriptsDirectory, connectionString, databaseName, string.Empty);
            }

            return (false, scriptsDirectory, connectionString, databaseName, $"Error: unknown option '{arg}'.");
        }

        return (true, scriptsDirectory, connectionString, databaseName, string.Empty);
    }

    void PrintSqlServerDeployHelp()
    {
        presenter.WriteInfo("Command: deploy sqlserver");
        presenter.WriteInfo("Usage:");
        presenter.WriteInfo("  meta deploy sqlserver --scripts <dir> --connection-string <value> [--database <name>]");
        presenter.WriteInfo("Notes:");
        presenter.WriteInfo("  Deploys SQL scripts in deterministic file-name order.");
        presenter.WriteInfo("  If _meta-sqlserver-order.txt exists, that manifest defines the deployment order.");
        presenter.WriteInfo("  Supports GO batch separators inside each script.");
        presenter.WriteInfo("  If --database is provided, the database is created if missing and used as the deploy target.");
    }
}

