using Meta.Core.Connections;

internal sealed partial class CliRuntime
{
    async Task<int> DeployAsync(string[] commandArgs)
    {
        var mode = CommandToken().Trim().ToLowerInvariant();
        switch (mode)
        {
            case "sqlserver":
                var options = ReadSqlServerDeployOptions(commandArgs, startIndex: 2);
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
        ReadSqlServerDeployOptions(string[] commandArgs, int startIndex)
    {
        return (true, RequiredValue("scripts"), RequiredValue("connection-env"), OptionalValue("database"), string.Empty);
    }
}


