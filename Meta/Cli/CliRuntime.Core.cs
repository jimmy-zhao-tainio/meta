using Meta.Core.Connections;
using Meta.Operations;
using MetaCli.Core;

internal sealed partial class CliRuntime
{
    private readonly ServiceCollection services = new();
    private readonly Meta.Core.Presentation.ConsolePresenter presenter = new();
    private const int SupportedContractMajorVersion = 1;
    private const int SupportedContractMinorVersion = 0;
    private string[] args = Array.Empty<string>();
    private MetaCliInvocation? currentInvocation;
    private IMetaWorkspace? currentWorkspace;
    private MetaCliWorkspaces? currentWorkspaces;
    private string? globalWorkspacePath;
    private bool globalStrict;

    public void UseArguments(IReadOnlyList<string> cliArgs)
    {
        ArgumentNullException.ThrowIfNull(cliArgs);
        args = cliArgs.ToArray();
    }

    public int HandleRuntimeFailure(MetaCliRuntimeFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Kind switch
        {
            MetaCliRuntimeFailureKind.CommandSurfaceLoadFailed => PrintDataError(
                "E_COMMAND_SURFACE",
                failure.Message),

            MetaCliRuntimeFailureKind.ParseFailed => PrintArgumentError(
                failure.Message),

            MetaCliRuntimeFailureKind.HandlerMissing => PrintFormattedError(
                "E_COMMAND_NOT_IMPLEMENTED",
                failure.Message,
                exitCode: failure.ExitCode,
                hints: new[] { $"Next: meta help {failure.Invocation?.CommandRoute ?? string.Empty}".TrimEnd() }),

            _ => PrintDataError(
                "E_OPERATION",
                failure.Exception?.Message ?? failure.Message),
        };
    }

    public async Task ExecuteBoundAsync(
        MetaCliInvocation invocation,
        IReadOnlyList<string> cliArgs,
        Func<string[], Task<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(cliArgs);
        ArgumentNullException.ThrowIfNull(handler);

        args = cliArgs.ToArray();
        currentInvocation = invocation;
        globalWorkspacePath = OptionalValue("workspace", string.Empty);
        globalStrict = Flag("strict");

        var exitCode = 0;
        try
        {
            exitCode = await handler(args).ConfigureAwait(false);
        }
        catch (XmlException exception)
        {
            exitCode = PrintDataError("E_WORKSPACE_XML_INVALID", exception.Message);
        }
        catch (InvalidDataException exception)
        {
            exitCode = PrintDataError("E_WORKSPACE_INVALID", exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            exitCode = PrintDataError("E_FILE_NOT_FOUND", exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            exitCode = PrintDataError("E_DIRECTORY_NOT_FOUND", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            exitCode = PrintDataError("E_IO", exception.Message);
        }
        catch (IOException exception)
        {
            exitCode = PrintDataError("E_IO", exception.Message);
        }
        catch (ConnectionEnvironmentVariableException exception)
        {
            exitCode = PrintArgumentError(exception.Message);
        }
        catch (ArgumentException exception)
        {
            if (Regex.IsMatch(exception.Message, "illegal characters in path", RegexOptions.IgnoreCase))
            {
                exitCode = PrintDataError("E_IO", exception.Message);
            }
            else
            {
                exitCode = PrintArgumentError(exception.Message);
            }
        }
        catch (FormatException exception)
        {
            exitCode = PrintArgumentError(exception.Message);
        }
        catch (OverflowException exception)
        {
            exitCode = PrintArgumentError(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            exitCode = PrintDataError("E_NOT_SUPPORTED", exception.Message);
        }
        catch (TimeoutException exception)
        {
            exitCode = PrintDataError("E_TIMEOUT", exception.Message);
        }
        catch (OperationCanceledException exception)
        {
            exitCode = PrintDataError("E_CANCELLED", exception.Message);
        }
        catch (SqlException exception)
        {
            exitCode = PrintDataError("E_SQL", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            exitCode = PrintDataError("E_OPERATION", exception.Message);
        }
        catch (Exception exception)
        {
            exitCode = PrintFormattedError(
                "E_INTERNAL",
                NormalizeErrorMessage(exception.Message),
                exitCode: 6,
                hints: new[] { "Next: rerun the command and inspect the human-readable error details above." });
        }

        if (exitCode != 0)
        {
            throw new MetaCliExitException(exitCode);
        }
    }

    public async Task ExecuteBoundAsync(
        MetaCliInvocation invocation,
        IReadOnlyList<string> cliArgs,
        MetaCliWorkspaces workspaces,
        Func<string[], Task<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        currentWorkspaces = workspaces;
        currentWorkspace = workspaces.Optional("workspace");
        try
        {
            await ExecuteBoundAsync(invocation, cliArgs, handler)
                .ConfigureAwait(false);
        }
        finally
        {
            currentWorkspace = null;
            currentWorkspaces = null;
        }
    }

    public async Task ExecuteBoundAsync(
        MetaCliInvocation invocation,
        IReadOnlyList<string> cliArgs,
        IMetaWorkspace workspace,
        Func<string[], Task<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        currentWorkspace = workspace;
        try
        {
            await ExecuteBoundAsync(invocation, cliArgs, handler)
                .ConfigureAwait(false);
        }
        finally
        {
            currentWorkspace = null;
        }
    }

    async Task<int> ExecuteOperationAsync(
        Operation operation,
        string commandName,
        string successMessage,
        params (string Key, string Value)[] successDetails)
        => await ExecuteOperationsAsync(
                [operation],
                commandName,
                successMessage,
                successDetails)
            .ConfigureAwait(false);

    async Task<int> ExecuteOperationsAsync(
        IReadOnlyList<Operation> operations,
        string commandName,
        string successMessage,
        IReadOnlyList<(string Key, string Value)>? successDetails = null,
        Func<IReadOnlyList<OperationResult>, IReadOnlyList<(string Key, string Value)>>? buildSuccessDetails = null,
        Action<IReadOnlyList<OperationResult>>? writeSuccessOutput = null)
    {
        return await ExecuteOperationsOnWorkspaceAsync(
                CurrentWorkspace,
                operations,
                commandName,
                successMessage,
                successDetails,
                buildSuccessDetails,
                writeSuccessOutput)
            .ConfigureAwait(false);
    }

    async Task<int> ExecuteOperationsOnWorkspaceAsync(
        IMetaWorkspace workspace,
        IReadOnlyList<Operation> operations,
        string commandName,
        string successMessage,
        IReadOnlyList<(string Key, string Value)>? successDetails = null,
        Func<IReadOnlyList<OperationResult>, IReadOnlyList<(string Key, string Value)>>? buildSuccessDetails = null,
        Action<IReadOnlyList<OperationResult>>? writeSuccessOutput = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(operations);
        try
        {
            var results = await workspace.ExecuteAsync(operations)
                .ConfigureAwait(false);
            presenter.WriteOk(
                successMessage,
                (buildSuccessDetails != null
                    ? buildSuccessDetails(results)
                    : successDetails ?? Array.Empty<(string Key, string Value)>()).ToArray());
            writeSuccessOutput?.Invoke(results);
            return 0;
        }
        catch (MetaOperationException exception) when (exception.Diagnostics != null)
        {
            return PrintOperationValidationFailure(
                commandName,
                operations,
                exception.Diagnostics);
        }
        catch (MetaOperationException exception)
        {
            var cause = exception.InnerException ?? exception;
            while (cause is MetaOperationException operationException &&
                   operationException.InnerException != null)
            {
                cause = operationException.InnerException;
            }

            return PrintDataError("E_OPERATION", cause.Message);
        }
    }

    private MetaCliInvocation Invocation =>
        currentInvocation ?? throw new InvalidOperationException("Command invocation has not been parsed.");

    private IMetaWorkspace CurrentWorkspace =>
        currentWorkspace ?? throw new InvalidOperationException("Command does not have an opened workspace.");

    private MetaCliWorkspaces CurrentWorkspaces =>
        currentWorkspaces ?? throw new InvalidOperationException("Command does not have bound workspaces.");

    private string WorkspacePath() =>
        OptionalValue("workspace", DefaultWorkspacePath());

    private string CommandToken() =>
        Invocation.Command.Token ?? Invocation.Command.Name;

    private bool IsPresent(string parameter)
    {
        try
        {
            return Invocation.IsPresent(parameter);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private bool Flag(string parameter)
    {
        try
        {
            return Invocation.Flag(parameter);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private string RequiredValue(string parameter) =>
        Invocation.Required(parameter);

    private string OptionalValue(string parameter, string defaultValue = "")
    {
        try
        {
            var value = Invocation.Optional(parameter);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }
        catch (KeyNotFoundException)
        {
            return defaultValue;
        }
    }

    private IReadOnlyList<string> Values(string parameter)
    {
        try
        {
            return Invocation.Values(parameter);
        }
        catch (KeyNotFoundException)
        {
            return Array.Empty<string>();
        }
    }

    private IReadOnlyList<IReadOnlyList<string>> OccurrenceValues(string parameter)
    {
        try
        {
            var binding = Invocation.Binding(parameter);
            var values = binding.Values.ToArray();
            if (values.Length == 0)
            {
                return Array.Empty<IReadOnlyList<string>>();
            }

            var width = 1;
            if (int.TryParse(binding.Parameter.ValueShape.ValueArity.MinValueCount, out var parsedWidth) && parsedWidth > 0)
            {
                width = parsedWidth;
            }

            var groups = new List<IReadOnlyList<string>>();
            for (var index = 0; index < values.Length; index += width)
            {
                groups.Add(values.Skip(index).Take(width).ToArray());
            }

            return groups;
        }
        catch (KeyNotFoundException)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }
    }

    private bool TryOptionalInt(
        string parameter,
        int defaultValue,
        Func<int, bool> isValid,
        string errorMessage,
        out int value,
        out string error)
    {
        var raw = OptionalValue(parameter);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = defaultValue;
            error = string.Empty;
            return true;
        }

        if (!int.TryParse(raw, out value) || !isValid(value))
        {
            error = errorMessage;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryReadAssignments(
        string parameter,
        out Dictionary<string, string> setValues,
        out string error)
    {
        setValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in Values(parameter))
        {
            var separator = assignment.IndexOf('=');
            if (separator <= 0)
            {
                error = $"Error: invalid --{parameter} assignment '{assignment}'. Expected Field=Value.";
                return false;
            }

            var field = assignment[..separator].Trim();
            var value = assignment[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(field))
            {
                error = $"Error: invalid --{parameter} assignment '{assignment}'. Field is empty.";
                return false;
            }

            setValues[field] = value;
        }

        error = string.Empty;
        return true;
    }

    (bool Ok, string WorkspacePath, string ErrorMessage)
        ReadWorkspaceOnlyOptions(string[] commandArgs, int startIndex)
    {
        return (true, WorkspacePath(), string.Empty);
    }

    (bool Ok, string WorkspacePath, string ErrorMessage)
        ReadValidateOptions(string[] commandArgs, int startIndex)
    {
        return (true, WorkspacePath(), string.Empty);
    }

    (bool Ok, string WorkspacePath, int TopN, int CycleSampleLimit, string ErrorMessage)
        ReadGraphStatsOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        if (!TryOptionalInt("top", 10, value => value > 0, "Error: --top requires an integer > 0.", out var topN, out var topError))
        {
            return (false, workspacePath, topN, 10, topError);
        }

        if (!TryOptionalInt("cycles", 10, value => value >= 0, "Error: --cycles requires an integer >= 0.", out var cycleSampleLimit, out var cycleError))
        {
            return (false, workspacePath, topN, cycleSampleLimit, cycleError);
        }

        return (true, workspacePath, topN, cycleSampleLimit, string.Empty);
    }

    (bool Ok, string WorkspacePath, int Top, string ErrorMessage)
        ReadGraphInboundOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        if (!TryOptionalInt("top", 20, value => value > 0, "Error: --top requires an integer > 0.", out var top, out var error))
        {
            return (false, workspacePath, top, error);
        }

        return (true, workspacePath, top, string.Empty);
    }

    (bool Ok, string WorkspacePath, IReadOnlyList<(string Mode, string Field, string Value)> Filters, int Top, string ErrorMessage)
        ReadQueryCommandOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        var filters = new List<(string Mode, string Field, string Value)>();
        foreach (var values in OccurrenceValues("equals"))
        {
            if (values.Count != 2 || string.IsNullOrWhiteSpace(values[0]))
            {
                return (false, workspacePath, filters, 200, "Error: --equals requires <Field> <Value>.");
            }

            filters.Add(("equals", values[0].Trim(), values[1]));
        }

        foreach (var values in OccurrenceValues("contains"))
        {
            if (values.Count != 2 || string.IsNullOrWhiteSpace(values[0]))
            {
                return (false, workspacePath, filters, 200, "Error: --contains requires <Field> <Value>.");
            }

            filters.Add(("contains", values[0].Trim(), values[1]));
        }

        if (!TryOptionalInt("top", 200, value => value > 0, "Error: --top requires an integer > 0.", out var top, out var error))
        {
            return (false, workspacePath, filters, top, error);
        }

        return (true, workspacePath, filters, top, string.Empty);
    }

    (bool Ok, string RelationshipSelector, string ToId, string WorkspacePath, string ErrorMessage)
        ReadInstanceRelationshipSetOptions(string[] commandArgs, int startIndex)
    {
        var values = Values("to");
        return (true, values[0], values[1], WorkspacePath(), string.Empty);
    }

    (bool Ok, Dictionary<string, string> SetValues, string WorkspacePath, bool AutoId, string ErrorMessage)
        ReadMutatingEntityOptions(string[] commandArgs, int startIndex, bool allowAutoId = false)
    {
        if (!TryReadAssignments("set", out var setValues, out var error))
        {
            return (false, setValues, WorkspacePath(), false, error);
        }

        var autoId = allowAutoId && Flag("auto-id");
        return (true, setValues, WorkspacePath(), autoId, string.Empty);
    }

    (bool Ok, string WorkspacePath, string ErrorMessage)
        ReadMutatingCommonOptions(string[] commandArgs, int startIndex)
    {
        return (true, WorkspacePath(), string.Empty);
    }

    (bool Ok, string Role, string DefaultId, bool Required, string WorkspacePath, string ErrorMessage)
        ReadModelAddRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var role = OptionalValue("role");
        var defaultId = OptionalValue("default-id");
        var required = !IsPresent("required") || bool.Parse(RequiredValue("required"));
        var workspacePath = WorkspacePath();
        return (true, role, defaultId, required, workspacePath, string.Empty);
    }

    (bool Ok, string Format, string FilePath, bool UseStdin, string WorkspacePath, IReadOnlyList<string> KeyFields, bool AutoId, string ErrorMessage)
        ReadUpsertOptions(string[] commandArgs, int startIndex)
    {
        var format = OptionalValue("from").Trim().ToLowerInvariant();
        var filePath = OptionalValue("file");
        var useStdin = Flag("stdin");
        var workspacePath = WorkspacePath();
        var keyFields = OptionalValue("key")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var autoId = Flag("auto-id");
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
}
