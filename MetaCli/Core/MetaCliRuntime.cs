using Meta.Integration;
using Meta.Operations;

namespace MetaCli.Core;

public delegate void MetaCliCommandHandler(MetaCliInvocation invocation);

public delegate Task MetaCliAsyncCommandHandler(MetaCliInvocation invocation);

public delegate void MetaCliWorkspaceCommandHandler(
    MetaCliInvocation invocation,
    IMetaWorkspace workspace);

public delegate Task MetaCliAsyncWorkspaceCommandHandler(
    MetaCliInvocation invocation,
    IMetaWorkspace workspace);

public delegate void MetaCliModelCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model)
    where TModel : class;

public delegate Task MetaCliAsyncModelCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model)
    where TModel : class;

public delegate void MetaCliModelCompletionCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model,
    MetaCliCommandCompletion completion)
    where TModel : class;

public delegate Task MetaCliAsyncModelCompletionCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model,
    MetaCliCommandCompletion completion)
    where TModel : class;

public delegate void MetaCliWorkspacesCommandHandler(
    MetaCliInvocation invocation,
    MetaCliWorkspaces workspaces);

public delegate Task MetaCliAsyncWorkspacesCommandHandler(
    MetaCliInvocation invocation,
    MetaCliWorkspaces workspaces);

public delegate void MetaCliModelWorkspacesCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model,
    MetaCliWorkspaces workspaces)
    where TModel : class;

public delegate Task MetaCliAsyncModelWorkspacesCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model,
    MetaCliWorkspaces workspaces)
    where TModel : class;

public delegate int MetaCliRuntimeFailureHandler(MetaCliRuntimeFailure failure);

public sealed record MetaCliRuntimeFailure(
    MetaCliRuntimeFailureKind Kind,
    int ExitCode,
    string Message,
    MetaCliInvocation? Invocation = null,
    Exception? Exception = null);

public enum MetaCliRuntimeFailureKind
{
    CommandSurfaceLoadFailed,
    ParseFailed,
    HandlerMissing,
    HandlerFailed
}

public sealed class MetaCliRuntime<TModel>
    where TModel : class, new()
{
    private readonly string commandWorkspacePath;
    private readonly string? applicationId;
    private readonly string workspaceParameter;
    private readonly TextWriter error;
    private readonly Action<int> setExitCode;
    private readonly Dictionary<string, HandlerBinding> handlers = new(StringComparer.Ordinal);
    private bool useDefaultHelp;
    private TextWriter? helpOutput;
    private TextWriter? helpError;
    private MetaCliHelpOptions? helpOptions;
    private MetaCliRuntimeFailureHandler? failureHandler;
    private MetaCliModel model = MetaCliModel.CreateEmpty();

    public MetaCliRuntime(
        string commandWorkspacePath,
        string? applicationId = null,
        string workspaceParameter = "workspace",
        TextWriter? error = null,
        Action<int>? setExitCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandWorkspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceParameter);

        this.commandWorkspacePath = commandWorkspacePath;
        this.applicationId = string.IsNullOrWhiteSpace(applicationId) ? null : applicationId.Trim();
        this.workspaceParameter = workspaceParameter.Trim();
        this.error = error ?? Console.Error;
        this.setExitCode = setExitCode ?? (code => Environment.ExitCode = code);
    }

    public MetaCliRuntime<TModel> UseDefaultHelp(
        TextWriter? output = null,
        TextWriter? error = null,
        MetaCliHelpOptions? options = null)
    {
        useDefaultHelp = true;
        helpOutput = output;
        helpError = error;
        helpOptions = options;
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithoutWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliAsyncCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithoutWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliWorkspaceCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliAsyncWorkspaceCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliModelCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(string executableCommandId, MetaCliAsyncModelCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> BindReadOnly(
        string executableCommandId,
        MetaCliModelCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding
            .WithWorkspace(handler) with { PersistModelChanges = false };
        return this;
    }

    public MetaCliRuntime<TModel> BindReadOnly(
        string executableCommandId,
        MetaCliAsyncModelCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding
            .WithWorkspace(handler) with { PersistModelChanges = false };
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        MetaCliModelCompletionCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        MetaCliAsyncModelCompletionCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspace(handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
        MetaCliWorkspacesCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspaces(
            workspaces,
            handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
        MetaCliAsyncWorkspacesCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspaces(
            workspaces,
            handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
        MetaCliModelWorkspacesCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspaces(
            workspaces,
            handler);
        return this;
    }

    public MetaCliRuntime<TModel> Bind(
        string executableCommandId,
        IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
        MetaCliAsyncModelWorkspacesCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding.WithWorkspaces(
            workspaces,
            handler);
        return this;
    }

    public MetaCliRuntime<TModel> BindTarget(
        string executableCommandId,
        IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
        MetaCliAsyncModelWorkspacesCommandHandler<TModel> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableCommandId);
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(handler);
        handlers[executableCommandId.Trim()] = HandlerBinding
            .WithWorkspaces(workspaces, handler) with
            {
                PrimaryWorkspaceOptional = true,
            };
        return this;
    }

    public MetaCliRuntime<TModel> OnFailure(MetaCliRuntimeFailureHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        failureHandler = handler;
        return this;
    }

    public void Run(params string[] arguments) =>
        Run((IReadOnlyList<string>)arguments);

    public void Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var startup = MetaCliActivityIndicator.TryStart();

        try
        {
            model = MetaCliWorkspace.LoadModelAsync(commandWorkspacePath)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            startup?.Dispose();
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.CommandSurfaceLoadFailed,
                4,
                $"Cannot load command surface workspace '{Path.GetFullPath(commandWorkspacePath)}'. {exception.Message}",
                Exception: exception));
            return;
        }

        if (useDefaultHelp)
        {
            if (MetaCliHelpService.IsHelpRequest(arguments))
            {
                startup?.Dispose();
            }

            var help = new MetaCliHelpService(helpOutput, helpError ?? error, helpOptions);
            if (help.TryWriteHelp(model, applicationId, arguments, out var helpExitCode))
            {
                setExitCode(helpExitCode);
                return;
            }
        }

        var parse = new MetaCliParser(model, applicationId).Parse(arguments);
        if (!parse.Succeeded)
        {
            startup?.Dispose();
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.ParseFailed,
                2,
                parse.Message ?? "Command line could not be parsed."));
            return;
        }

        var invocation = parse.RequireInvocation();
        if (!handlers.TryGetValue(invocation.ExecutableCommand.Id, out var handler))
        {
            startup?.Dispose();
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.HandlerMissing,
                4,
                $"Command '{invocation.CommandRoute}' is modeled but has no implementation.",
                invocation));
            return;
        }

        try
        {
            if (handler.HasPrimaryWorkspaceHandler)
            {
                ExecuteWorkspaceHandlerAsync(invocation, handler, startup)
                    .GetAwaiter()
                    .GetResult();
            }
            else if (handler.HasAdditionalWorkspacesHandler)
            {
                ExecuteAdditionalWorkspacesHandlerAsync(invocation, handler, startup)
                    .GetAwaiter()
                    .GetResult();
            }
            else if (handler.AsyncHandler is not null)
            {
                startup?.Dispose();
                handler.AsyncHandler(invocation).GetAwaiter().GetResult();
            }
            else
            {
                startup?.Dispose();
                handler.Handler!(invocation);
            }
        }
        catch (MetaCliExitException exception)
        {
            startup?.Dispose();
            if (!string.IsNullOrWhiteSpace(exception.Message))
            {
                error.WriteLine(exception.Message);
            }

            setExitCode(exception.ExitCode);
            return;
        }
        catch (Exception exception)
        {
            startup?.Dispose();
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.HandlerFailed,
                4,
                $"Command '{invocation.CommandRoute}' failed. {exception.Message}",
                invocation,
                exception));
            return;
        }

        setExitCode(0);
    }

    private async Task ExecuteWorkspaceHandlerAsync(
        MetaCliInvocation invocation,
        HandlerBinding handler,
        MetaCliActivityIndicator? startup)
    {
        var hasPrimaryWorkspace = !handler.PrimaryWorkspaceOptional ||
            HasValue(invocation, workspaceParameter) ||
            !HasSelectedOutput(invocation, handler.Workspaces);

        await using var workspace = hasPrimaryWorkspace
            ? await MetaCliWorkspaceResolver.OpenAsync(
                    invocation,
                    workspaceParameter)
                .ConfigureAwait(false)
            : null;

        if (handler.GenericWorkspaceHandler is not null)
        {
            if (workspace is null)
            {
                throw new InvalidOperationException("This command requires an existing workspace.");
            }

            startup?.Dispose();
            handler.GenericWorkspaceHandler(invocation, workspace);
            return;
        }

        if (handler.AsyncGenericWorkspaceHandler is not null)
        {
            if (workspace is null)
            {
                throw new InvalidOperationException("This command requires an existing workspace.");
            }

            startup?.Dispose();
            await handler.AsyncGenericWorkspaceHandler(invocation, workspace)
                .ConfigureAwait(false);
            return;
        }

        var baseline = workspace is null
            ? TypedWorkspaceModelMapper.ToInMemoryWorkspace(new TModel())
            : await WorkspaceComposition.MaterializeAsync(workspace)
                .ConfigureAwait(false);
        var domainModel = TypedWorkspaceModelMapper.FromInMemoryWorkspace(
            baseline,
            static () => new TModel());
        var completion = new MetaCliCommandCompletion();

        await using var additionalWorkspaces = await ResolveWorkspacesAsync(
                invocation,
                handler.Workspaces)
            .ConfigureAwait(false);

        startup?.Dispose();

        if (handler.WorkspaceHandler is not null)
        {
            handler.WorkspaceHandler(invocation, domainModel);
        }
        else if (handler.AsyncWorkspaceHandler is not null)
        {
            await handler.AsyncWorkspaceHandler(invocation, domainModel)
                .ConfigureAwait(false);
        }
        else if (handler.CompletionWorkspaceHandler is not null)
        {
            handler.CompletionWorkspaceHandler(invocation, domainModel, completion);
        }
        else if (handler.AsyncCompletionWorkspaceHandler is not null)
        {
            await handler.AsyncCompletionWorkspaceHandler(invocation, domainModel, completion)
                .ConfigureAwait(false);
        }
        else if (handler.WorkspacesHandler is not null)
        {
            handler.WorkspacesHandler(
                invocation,
                domainModel,
                additionalWorkspaces);
        }
        else
        {
            await handler.AsyncWorkspacesHandler!(
                    invocation,
                    domainModel,
                    additionalWorkspaces)
                .ConfigureAwait(false);
        }

        if (!handler.PersistModelChanges || workspace is null)
        {
            return;
        }

        var desired = TypedWorkspaceModelMapper.ToInMemoryWorkspace(
            domainModel);
        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            baseline,
            desired);
        if (operations.Count > 0)
        {
            await workspace.ExecuteAsync(operations).ConfigureAwait(false);
        }

        completion.Complete();
    }

    private static bool HasValue(
        MetaCliInvocation invocation,
        string parameter)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(invocation.Optional(parameter));
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool HasSelectedOutput(
        MetaCliInvocation invocation,
        IReadOnlyList<MetaCliWorkspaceParameter> parameters) =>
        parameters
            .OfType<MetaCliWorkspaceOutput>()
            .Any(output =>
                IsPresent(invocation, output.XmlParameter) ||
                IsPresent(invocation, output.CSharpParameter) ||
                IsPresent(invocation, output.SqlParameter));

    private static bool IsPresent(MetaCliInvocation invocation, string parameter)
    {
        try
        {
            return invocation.IsPresent(parameter);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static async Task ExecuteAdditionalWorkspacesHandlerAsync(
        MetaCliInvocation invocation,
        HandlerBinding handler,
        MetaCliActivityIndicator? startup)
    {
        await using var workspaces = await ResolveWorkspacesAsync(
                invocation,
                handler.Workspaces)
            .ConfigureAwait(false);
        startup?.Dispose();
        if (handler.AdditionalWorkspacesHandler is not null)
        {
            handler.AdditionalWorkspacesHandler(invocation, workspaces);
        }
        else
        {
            await handler.AsyncAdditionalWorkspacesHandler!(
                    invocation,
                    workspaces)
                .ConfigureAwait(false);
        }
    }

    private static async Task<MetaCliWorkspaces> ResolveWorkspacesAsync(
        MetaCliInvocation invocation,
        IReadOnlyList<MetaCliWorkspaceParameter> parameters)
    {
        var opened = new Dictionary<string, IMetaWorkspace>(
            StringComparer.OrdinalIgnoreCase);
        var outputs = new Dictionary<string, MetaCliWorkspaceOutput>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var parameter in parameters)
            {
                if (opened.ContainsKey(parameter.Name) ||
                    outputs.ContainsKey(parameter.Name))
                {
                    throw new InvalidOperationException(
                        $"Workspace parameter '{parameter.Name}' is bound more than once.");
                }

                if (parameter is MetaCliWorkspaceInput input)
                {
                    _ = invocation.Binding(input.Parameter);
                    opened.Add(
                        input.Parameter,
                        await MetaCliWorkspaceResolver.OpenAsync(
                                invocation,
                                input.Parameter,
                                useCurrentDirectoryWhenLocationIsMissing: false)
                            .ConfigureAwait(false));
                }
                else if (parameter is MetaCliOptionalWorkspaceInput optionalInput)
                {
                    _ = invocation.Binding(optionalInput.Parameter);
                    if (IsPresent(invocation, optionalInput.Parameter))
                    {
                        opened.Add(
                            optionalInput.Parameter,
                            await MetaCliWorkspaceResolver.OpenAsync(
                                    invocation,
                                    optionalInput.Parameter,
                                    useCurrentDirectoryWhenLocationIsMissing: false)
                                .ConfigureAwait(false));
                    }
                }
                else if (parameter is MetaCliWorkspaceTarget target)
                {
                    if (HasSelectedOutput(invocation, parameters))
                    {
                        continue;
                    }

                    opened.Add(
                        target.Parameter,
                        await MetaCliWorkspaceResolver.OpenAsync(
                                invocation,
                                target.Parameter,
                                useCurrentDirectoryWhenLocationIsMissing: true)
                            .ConfigureAwait(false));
                }
                else if (parameter is MetaCliWorkspaceOutput output)
                {
                    _ = invocation.Binding(output.XmlParameter);
                    _ = invocation.Binding(output.CSharpParameter);
                    _ = invocation.Binding(output.SqlParameter);
                    if (output.LocationParameter is not null)
                    {
                        _ = invocation.Binding(output.LocationParameter);
                    }
                    _ = invocation.Binding(output.ConnectionEnvironmentParameter);
                    outputs.Add(output.Output, output);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unsupported workspace parameter '{parameter.Name}'.");
                }
            }

            return new MetaCliWorkspaces(invocation, opened, outputs);
        }
        catch
        {
            await new MetaCliWorkspaces(invocation, opened, outputs)
                .DisposeAsync()
                .ConfigureAwait(false);
            throw;
        }
    }

    private void Fail(MetaCliRuntimeFailure failure)
    {
        var exitCode = failure.ExitCode;
        if (failureHandler is not null)
        {
            exitCode = failureHandler(failure);
        }
        else
        {
            error.WriteLine(failure.Message);
        }

        setExitCode(exitCode);
    }

    private sealed record HandlerBinding(
        MetaCliCommandHandler? Handler,
        MetaCliAsyncCommandHandler? AsyncHandler,
        MetaCliWorkspaceCommandHandler? GenericWorkspaceHandler,
        MetaCliAsyncWorkspaceCommandHandler? AsyncGenericWorkspaceHandler,
        MetaCliModelCommandHandler<TModel>? WorkspaceHandler,
        MetaCliAsyncModelCommandHandler<TModel>? AsyncWorkspaceHandler,
        MetaCliModelCompletionCommandHandler<TModel>? CompletionWorkspaceHandler,
        MetaCliAsyncModelCompletionCommandHandler<TModel>? AsyncCompletionWorkspaceHandler,
        IReadOnlyList<MetaCliWorkspaceParameter> Workspaces,
        MetaCliWorkspacesCommandHandler? AdditionalWorkspacesHandler,
        MetaCliAsyncWorkspacesCommandHandler? AsyncAdditionalWorkspacesHandler,
        MetaCliModelWorkspacesCommandHandler<TModel>? WorkspacesHandler,
        MetaCliAsyncModelWorkspacesCommandHandler<TModel>? AsyncWorkspacesHandler)
    {
        public bool PersistModelChanges { get; init; } = true;
        public bool PrimaryWorkspaceOptional { get; init; }

        public bool HasPrimaryWorkspaceHandler =>
            GenericWorkspaceHandler is not null ||
            AsyncGenericWorkspaceHandler is not null ||
            WorkspaceHandler is not null ||
            AsyncWorkspaceHandler is not null ||
            CompletionWorkspaceHandler is not null ||
            AsyncCompletionWorkspaceHandler is not null ||
            WorkspacesHandler is not null ||
            AsyncWorkspacesHandler is not null;

        public bool HasAdditionalWorkspacesHandler =>
            AdditionalWorkspacesHandler is not null ||
            AsyncAdditionalWorkspacesHandler is not null;

        public static HandlerBinding WithoutWorkspace(MetaCliCommandHandler handler) =>
            new(handler, null, null, null, null, null, null, null, [], null, null, null, null);

        public static HandlerBinding WithoutWorkspace(MetaCliAsyncCommandHandler handler) =>
            new(null, handler, null, null, null, null, null, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliWorkspaceCommandHandler handler) =>
            new(null, null, handler, null, null, null, null, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliAsyncWorkspaceCommandHandler handler) =>
            new(null, null, null, handler, null, null, null, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliModelCommandHandler<TModel> handler) =>
            new(null, null, null, null, handler, null, null, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliAsyncModelCommandHandler<TModel> handler) =>
            new(null, null, null, null, null, handler, null, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliModelCompletionCommandHandler<TModel> handler) =>
            new(null, null, null, null, null, null, handler, null, [], null, null, null, null);

        public static HandlerBinding WithWorkspace(MetaCliAsyncModelCompletionCommandHandler<TModel> handler) =>
            new(null, null, null, null, null, null, null, handler, [], null, null, null, null);

        public static HandlerBinding WithWorkspaces(
            IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
            MetaCliWorkspacesCommandHandler handler) =>
            new(null, null, null, null, null, null, null, null, workspaces.ToArray(), handler, null, null, null);

        public static HandlerBinding WithWorkspaces(
            IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
            MetaCliAsyncWorkspacesCommandHandler handler) =>
            new(null, null, null, null, null, null, null, null, workspaces.ToArray(), null, handler, null, null);

        public static HandlerBinding WithWorkspaces(
            IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
            MetaCliModelWorkspacesCommandHandler<TModel> handler) =>
            new(null, null, null, null, null, null, null, null, workspaces.ToArray(), null, null, handler, null);

        public static HandlerBinding WithWorkspaces(
            IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
            MetaCliAsyncModelWorkspacesCommandHandler<TModel> handler) =>
            new(null, null, null, null, null, null, null, null, workspaces.ToArray(), null, null, null, handler);
    }
}
