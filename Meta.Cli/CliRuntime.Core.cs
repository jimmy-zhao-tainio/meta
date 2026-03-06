internal sealed partial class CliRuntime
{
    private readonly ServiceCollection services = new();
    private readonly Meta.Core.Presentation.ConsolePresenter presenter = new();
    private const int SupportedContractMajorVersion = 1;
    private const int SupportedContractMinorVersion = 0;
    private string[] args = Array.Empty<string>();
    private string? globalWorkspacePath;
    private bool globalStrict;
    private Dictionary<string, CliCommandRegistration> commandRegistry = new(StringComparer.OrdinalIgnoreCase);

    public async Task<int> RunAsync(string[] cliArgs)
    {
        var globalOptions = ParseGlobalOptions(cliArgs);
        globalWorkspacePath = globalOptions.WorkspacePath;
        globalStrict = globalOptions.Strict;
        if (!globalOptions.Ok)
        {
            return PrintArgumentError(globalOptions.ErrorMessage);
        }

        args = globalOptions.CommandArgs;
        if (args.Length == 0)
        {
            return PrintUsageError("Usage: meta <command> [options]");
        }

        commandRegistry = BuildCommandRegistry();

        if (TryHandleHelpRequest(args, out var helpExitCode))
        {
            return helpExitCode;
        }

        var command = args[0].Trim().ToLowerInvariant();
        try
        {
            if (commandRegistry.TryGetValue(command, out var registration))
            {
                return await registration.Handler(args).ConfigureAwait(false);
            }

            return PrintCommandUnknownError(command);
        }
        catch (XmlException exception)
        {
            return PrintDataError("E_WORKSPACE_XML_INVALID", exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return PrintDataError("E_WORKSPACE_INVALID", exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            return PrintDataError("E_FILE_NOT_FOUND", exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            return PrintDataError("E_DIRECTORY_NOT_FOUND", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return PrintDataError("E_IO", exception.Message);
        }
        catch (IOException exception)
        {
            return PrintDataError("E_IO", exception.Message);
        }
        catch (ArgumentException exception)
        {
            if (Regex.IsMatch(exception.Message, "illegal characters in path", RegexOptions.IgnoreCase))
            {
                return PrintDataError("E_IO", exception.Message);
            }

            return PrintArgumentError(exception.Message);
        }
        catch (FormatException exception)
        {
            return PrintArgumentError(exception.Message);
        }
        catch (OverflowException exception)
        {
            return PrintArgumentError(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return PrintDataError("E_NOT_SUPPORTED", exception.Message);
        }
        catch (TimeoutException exception)
        {
            return PrintDataError("E_TIMEOUT", exception.Message);
        }
        catch (OperationCanceledException exception)
        {
            return PrintDataError("E_CANCELLED", exception.Message);
        }
        catch (SqlException exception)
        {
            return PrintDataError("E_SQL", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
        catch (Exception exception)
        {
            return PrintFormattedError(
                "E_INTERNAL",
                NormalizeErrorMessage(exception.Message),
                exitCode: 6,
                hints: new[] { "Next: rerun the command and inspect the human-readable error details above." });
        }
    }

    async Task<int> ExecuteOperationAsync(
        string workspacePath,
        WorkspaceOp operation,
        string commandName,
        string successMessage,
        params (string Key, string Value)[] successDetails)
    {
        try
        {
            var workspace = await LoadWorkspaceForCommandAsync(workspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            return await ExecuteOperationsAgainstLoadedWorkspaceAsync(
                    workspace,
                    new[] { operation },
                    commandName,
                    successMessage,
                    successDetails)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }
    
    async Task<int> ExecuteOperationsAgainstLoadedWorkspaceAsync(
        Workspace workspace,
        IReadOnlyList<WorkspaceOp> operations,
        string commandName,
        string successMessage,
        IReadOnlyList<(string Key, string Value)>? successDetails = null)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }
    
        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }
    
        var before = WorkspaceSnapshotCloner.Capture(workspace);
        try
        {
            foreach (var operation in operations)
            {
                services.OperationService.Execute(workspace, operation);
            }

            ApplyImplicitNormalization(workspace);
        }
        catch
        {
            WorkspaceSnapshotCloner.Restore(workspace, before);
            throw;
        }
    
        var diagnostics = services.ValidationService.Validate(workspace);
        workspace.Diagnostics = diagnostics;
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            WorkspaceSnapshotCloner.Restore(workspace, before);
            return PrintOperationValidationFailure(commandName, operations, diagnostics);
        }
    
        await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);
        var details = new List<(string Key, string Value)>();
        if (successDetails is { Count: > 0 })
        {
            details.AddRange(successDetails);
        }

        presenter.WriteOk(successMessage, details.ToArray());

        if (diagnostics.WarningCount > 0)
        {
            presenter.WriteInfo(
                $"Validation: warnings={diagnostics.WarningCount.ToString(CultureInfo.InvariantCulture)}, total={diagnostics.Issues.Count.ToString(CultureInfo.InvariantCulture)} (no errors)");
        }
    
        return 0;
    }

    void ApplyImplicitNormalization(Workspace workspace)
    {
        var normalizeOps = NormalizationService.BuildNormalizeOperations(
            workspace,
            new NormalizeOptions
            {
                DropUnknown = false,
            });
        foreach (var normalizeOp in normalizeOps)
        {
            services.OperationService.Execute(workspace, normalizeOp);
        }
    }
    
    
    
    
    
    (bool Ok, string WorkspacePath, bool Strict, string[] CommandArgs, string ErrorMessage)
        ParseGlobalOptions(string[] allArgs)
    {
        var workspacePath = string.Empty;
        var strict = false;
        var commandArgs = new List<string>(allArgs.Length);
    
        for (var index = 0; index < allArgs.Length; index++)
        {
            var arg = allArgs[index];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= allArgs.Length)
                {
                    return (false, workspacePath, strict, Array.Empty<string>(),
                        "Error: global --workspace requires a path.");
                }
    
                workspacePath = allArgs[++index];
                continue;
            }
    
            if (string.Equals(arg, "--strict", StringComparison.OrdinalIgnoreCase))
            {
                strict = true;
                continue;
            }
    
            commandArgs.Add(arg);
        }
    
        return (true, workspacePath, strict, commandArgs.ToArray(), string.Empty);
    }
    
    (bool Ok, string WorkspacePath, string ErrorMessage)
        ParseWorkspaceOnlyOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, workspacePath, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, string.Empty);
    }
    
    (bool Ok, string NewWorkspacePath, string ErrorMessage)
        ParseRequiredNewWorkspaceOption(string[] commandArgs, int startIndex)
    {
        var newWorkspacePath = string.Empty;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, newWorkspacePath, "Error: --new-workspace requires a path.");
                }
    
                if (!string.IsNullOrWhiteSpace(newWorkspacePath))
                {
                    return (false, newWorkspacePath, "Error: --new-workspace can only be provided once.");
                }
    
                newWorkspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, newWorkspacePath, $"Error: unknown option '{arg}'.");
        }
    
        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, newWorkspacePath, "Error: import requires --new-workspace <path>.");
        }

        return (true, newWorkspacePath, string.Empty);
    }

    (bool Ok, string EntityName, bool UseNewWorkspace, string WorkspacePath, string NewWorkspacePath, string ErrorMessage)
        ParseImportCsvOptions(string[] commandArgs, int startIndex)
    {
        var entityName = string.Empty;
        var workspacePath = DefaultWorkspacePath();
        var workspaceSelected = !string.IsNullOrWhiteSpace(globalWorkspacePath);
        var newWorkspacePath = string.Empty;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--entity", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --entity requires a value.");
                }

                if (!string.IsNullOrWhiteSpace(entityName))
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --entity can only be provided once.");
                }

                entityName = commandArgs[++i].Trim();
                if (string.IsNullOrWhiteSpace(entityName))
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --entity requires a non-empty value.");
                }

                continue;
            }

            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --workspace requires a path.");
                }

                if (!string.IsNullOrWhiteSpace(newWorkspacePath))
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath,
                        "Error: use either --workspace <path> or --new-workspace <path>, not both.");
                }

                workspacePath = commandArgs[++i];
                workspaceSelected = true;
                continue;
            }

            if (string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --new-workspace requires a path.");
                }

                if (workspaceSelected)
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath,
                        "Error: use either --workspace <path> or --new-workspace <path>, not both.");
                }

                if (!string.IsNullOrWhiteSpace(newWorkspacePath))
                {
                    return (false, entityName, false, workspacePath, newWorkspacePath, "Error: --new-workspace can only be provided once.");
                }

                newWorkspacePath = commandArgs[++i];
                continue;
            }

            return (false, entityName, false, workspacePath, newWorkspacePath, $"Error: unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            return (false, entityName, false, workspacePath, newWorkspacePath, "Error: import csv requires --entity <EntityName>.");
        }

        return (true, entityName, !string.IsNullOrWhiteSpace(newWorkspacePath), workspacePath, newWorkspacePath, string.Empty);
    }
    
    
    (bool Ok, string WorkspacePath, string ErrorMessage)
        ParseValidateOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, workspacePath, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, string.Empty);
    }
    
    (bool Ok, string WorkspacePath, int TopN, int CycleSampleLimit, string ErrorMessage)
        ParseGraphStatsOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var topN = 10;
        var cycleSampleLimit = 10;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, topN, cycleSampleLimit, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            if (string.Equals(arg, "--top", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out topN) || topN <= 0)
                {
                    return (false, workspacePath, topN, cycleSampleLimit, "Error: --top requires an integer > 0.");
                }
    
                continue;
            }
    
            if (string.Equals(arg, "--cycles", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out cycleSampleLimit) || cycleSampleLimit < 0)
                {
                    return (false, workspacePath, topN, cycleSampleLimit, "Error: --cycles requires an integer >= 0.");
                }
    
                continue;
            }
    
            return (false, workspacePath, topN, cycleSampleLimit, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, topN, cycleSampleLimit, string.Empty);
    }
    
    (bool Ok, string WorkspacePath, int Top, string ErrorMessage)
        ParseGraphInboundOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var top = 20;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, top, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            if (string.Equals(arg, "--top", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out top) || top <= 0)
                {
                    return (false, workspacePath, top, "Error: --top requires an integer > 0.");
                }
    
                continue;
            }
    
            return (false, workspacePath, top, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, top, string.Empty);
    }
    
    (bool Ok, string WorkspacePath, IReadOnlyList<(string Mode, string Field, string Value)> Filters, int Top, string ErrorMessage)
        ParseQueryCommandOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var filters = new List<(string Mode, string Field, string Value)>();
        var top = 200;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--equals", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 >= commandArgs.Length)
                {
                    return (false, workspacePath, filters, top, "Error: --equals requires <Field> <Value>.");
                }
    
                var field = commandArgs[++i].Trim();
                var value = commandArgs[++i];
                if (string.IsNullOrWhiteSpace(field))
                {
                    return (false, workspacePath, filters, top, "Error: --equals field is empty.");
                }
    
                filters.Add(("equals", field, value));
                continue;
            }
    
            if (string.Equals(arg, "--contains", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 >= commandArgs.Length)
                {
                    return (false, workspacePath, filters, top, "Error: --contains requires <Field> <Value>.");
                }
    
                var field = commandArgs[++i].Trim();
                var value = commandArgs[++i];
                if (string.IsNullOrWhiteSpace(field))
                {
                    return (false, workspacePath, filters, top, "Error: --contains field is empty.");
                }
    
                filters.Add(("contains", field, value));
                continue;
            }
    
            if (string.Equals(arg, "--top", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out top) || top <= 0)
                {
                    return (false, workspacePath, filters, top, "Error: --top requires an integer > 0.");
                }
    
                continue;
            }
    
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, filters, top, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, workspacePath, filters, top, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, filters, top, string.Empty);
    }
    
    (bool Ok, string WorkspacePath, string OutputDirectory, bool IncludeTooling, string ErrorMessage)
        ParseGenerateOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var outputDirectory = string.Empty;
        var includeTooling = false;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, outputDirectory, includeTooling, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            if (string.Equals(arg, "--out", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, outputDirectory, includeTooling, "Error: --out requires a directory path.");
                }
    
                outputDirectory = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--tooling", StringComparison.OrdinalIgnoreCase))
            {
                includeTooling = true;
                continue;
            }
    
            return (false, workspacePath, outputDirectory, includeTooling, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, outputDirectory, includeTooling, string.Empty);
    }
    
    (bool Ok, string RelationshipSelector, string ToId, string WorkspacePath, string ErrorMessage)
        ParseInstanceRelationshipSetOptions(string[] commandArgs, int startIndex)
    {
        var relationshipSelector = string.Empty;
        var toId = string.Empty;
        var workspacePath = DefaultWorkspacePath();
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--to", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 >= commandArgs.Length)
                {
                    return (false, relationshipSelector, toId, workspacePath, "Error: --to requires <RelationshipSelector> <ToId>.");
                }
    
                relationshipSelector = commandArgs[++i];
                toId = commandArgs[++i];
                continue;
            }
    
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, relationshipSelector, toId, workspacePath, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, relationshipSelector, toId, workspacePath, $"Error: unknown option '{arg}'.");
        }
    
        return (true, relationshipSelector, toId, workspacePath, string.Empty);
    }
    
    (bool Ok, Dictionary<string, string> SetValues, string WorkspacePath, bool AutoId, string ErrorMessage)
        ParseMutatingEntityOptions(string[] commandArgs, int startIndex, bool allowAutoId = false)
    {
        var setValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var workspacePath = DefaultWorkspacePath();
        var autoId = false;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--set", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, setValues, workspacePath, autoId, "Error: --set requires Field=Value.");
                }
    
                var assignment = commandArgs[++i];
                var separator = assignment.IndexOf('=');
                if (separator <= 0)
                {
                    return (false, setValues, workspacePath, autoId,
                        $"Error: invalid --set assignment '{assignment}'. Expected Field=Value.");
                }
    
                var field = assignment[..separator].Trim();
                var value = assignment[(separator + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(field))
                {
                    return (false, setValues, workspacePath, autoId,
                        $"Error: invalid --set assignment '{assignment}'. Field is empty.");
                }
    
                setValues[field] = value;
                continue;
            }

            if (allowAutoId && string.Equals(arg, "--auto-id", StringComparison.OrdinalIgnoreCase))
            {
                if (autoId)
                {
                    return (false, setValues, workspacePath, autoId, "Error: --auto-id specified more than once.");
                }

                autoId = true;
                continue;
            }
    
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, setValues, workspacePath, autoId, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, setValues, workspacePath, autoId, $"Error: unknown option '{arg}'.");
        }
    
        return (true, setValues, workspacePath, autoId, string.Empty);
    }
    
    (bool Ok, string WorkspacePath, string ErrorMessage)
        ParseMutatingCommonOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, "Error: --workspace requires a path.");
                }
    
                workspacePath = commandArgs[++i];
                continue;
            }
    
            return (false, workspacePath, $"Error: unknown option '{arg}'.");
        }
    
        return (true, workspacePath, string.Empty);
    }

    (bool Ok, string Role, string DefaultId, string WorkspacePath, string ErrorMessage)
        ParseModelAddRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var role = string.Empty;
        var defaultId = string.Empty;
        var workspacePath = DefaultWorkspacePath();

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--role", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, role, defaultId, workspacePath, "Error: --role requires a value.");
                }

                role = commandArgs[++i];
                if (string.IsNullOrWhiteSpace(role))
                {
                    return (false, role, defaultId, workspacePath, "Error: --role requires a non-empty value.");
                }

                continue;
            }

            if (string.Equals(arg, "--default-id", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, role, defaultId, workspacePath, "Error: --default-id requires a value.");
                }

                defaultId = commandArgs[++i];
                if (string.IsNullOrWhiteSpace(defaultId))
                {
                    return (false, role, defaultId, workspacePath, "Error: --default-id requires a non-empty value.");
                }

                continue;
            }

            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, role, defaultId, workspacePath, "Error: --workspace requires a path.");
                }

                workspacePath = commandArgs[++i];
                continue;
            }

            return (false, role, defaultId, workspacePath, $"Error: unknown option '{arg}'.");
        }

        return (true, role, defaultId, workspacePath, string.Empty);
    }
    
    (bool Ok, string Format, string FilePath, bool UseStdin, string WorkspacePath, IReadOnlyList<string> KeyFields, bool AutoId, string ErrorMessage)
        ParseUpsertOptions(string[] commandArgs, int startIndex)
    {
        var format = string.Empty;
        var filePath = string.Empty;
        var useStdin = false;
        var workspacePath = DefaultWorkspacePath();
        var keyFields = new List<string>();
        var autoId = false;
    
        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            switch (arg.ToLowerInvariant())
            {
                case "--from":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                            "Error: --from requires a value (tsv|csv).");
                    }
    
                    format = commandArgs[++i].Trim().ToLowerInvariant();
                    break;
    
                case "--file":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                            "Error: --file requires a path.");
                    }
    
                    filePath = commandArgs[++i];
                    break;
    
                case "--stdin":
                    useStdin = true;
                    break;

                case "--auto-id":
                    if (autoId)
                    {
                        return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                            "Error: --auto-id specified more than once.");
                    }

                    autoId = true;
                    break;
    
                case "--workspace":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                            "Error: --workspace requires a path.");
                    }
    
                    workspacePath = commandArgs[++i];
                    break;
    
                case "--key":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                            "Error: --key requires a comma-separated field list.");
                    }
    
                    keyFields = commandArgs[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => item.Trim())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
    
                default:
                    return (false, format, filePath, useStdin, workspacePath, keyFields, autoId,
                        $"Error: unknown bulk-insert option '{arg}'.");
            }
        }
    
        return (true, format, filePath, useStdin, workspacePath, keyFields, autoId, string.Empty);
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    IReadOnlyList<(string Key, string Value)> ParseWherePairs(string? where)
    {
        if (string.IsNullOrWhiteSpace(where))
        {
            return Array.Empty<(string Key, string Value)>();
        }
    
        var pairs = new List<(string Key, string Value)>();
        var parts = where.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var item = part.Trim();
            var separator = item.IndexOf('=');
            if (separator <= 0 || separator == item.Length - 1)
            {
                continue;
            }
    
            var key = item[..separator].Trim();
            var value = item[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                pairs.Add((key, value));
            }
        }
    
        return pairs;
    }
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    Dictionary<string, CliCommandRegistration> BuildCommandRegistry()
    {
        var registry = new Dictionary<string, CliCommandRegistration>(StringComparer.OrdinalIgnoreCase);
    
        Register("init", "Workspace", "Initialize workspace.", InitWorkspaceAsync);
        Register("status", "Workspace", "Show workspace summary.", StatusWorkspaceAsync);
        Register("workspace", "Workspace", "Merge workspaces and inspect workspace-level operations.", WorkspaceAsync);

        Register("check", "Model", "Check model and instance integrity.", CheckWorkspaceAsync);
        Register("graph", "Model", "Graph stats and inbound relationships.", GraphAsync);
        Register("list", "Model", "List entities, properties, and relationships.", ListAsync);
        Register("model", "Model", "Inspect and mutate model entities, properties, and relationships.", ModelAsync);
        Register("view", "Model", "View entity or instance details.", ViewAsync);

        Register("instance", "Instance", "Diff and merge instance artifacts.", InstanceAsync);
        Register("insert", "Instance", "Insert one instance: <Entity> <Id> or --auto-id for brand-new rows.", InsertAsync);
        Register("delete", "Instance", "Delete one instance: <Entity> <Id>.", DeleteAsync);
        Register("query", "Instance", "Search instances with equals/contains filters.", QueryAsync);
        Register("bulk-insert", "Instance", "Insert many instances from tsv/csv input (supports --auto-id for new rows only).", BulkInsertAsync);

        Register("import", "Pipeline", "Import xml/sql into NEW workspace or csv into NEW/existing workspace.", ImportAsync);
        Register("export", "Pipeline", "Export workspace data to external formats.", ExportAsync);
        Register("generate", "Pipeline", "Generate artifacts from the workspace.", GenerateAsync);
    
        return registry;
    
        void Register(string commandName, string domain, string description, Func<string[], Task<int>> handler)
        {
            registry[commandName] = new CliCommandRegistration(domain, description, handler);
            HelpTopics.RegisterCommand(commandName, domain, description);
        }
    }
    
    readonly record struct CliCommandRegistration(
        string Domain,
        string Description,
        Func<string[], Task<int>> Handler);
    
    readonly record struct RandomWorkspaceResult(
        Workspace Workspace,
        int MaxDepth,
        int TotalRelationships,
        int TotalRows);
}

