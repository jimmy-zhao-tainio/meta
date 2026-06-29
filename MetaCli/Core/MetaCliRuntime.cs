using Meta.Core.Serialization;

namespace MetaCli.Core;

public delegate void MetaCliCommandHandler(MetaCliInvocation invocation);

public delegate Task MetaCliAsyncCommandHandler(MetaCliInvocation invocation);

public delegate void MetaCliModelCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model)
    where TModel : IMetaWorkspaceModel<TModel>;

public delegate Task MetaCliAsyncModelCommandHandler<TModel>(
    MetaCliInvocation invocation,
    TModel model)
    where TModel : IMetaWorkspaceModel<TModel>;

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
    where TModel : IMetaWorkspaceModel<TModel>
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

    public MetaCliRuntime<TModel> UseDefaultHelp(TextWriter? output = null, TextWriter? error = null)
    {
        useDefaultHelp = true;
        helpOutput = output;
        helpError = error;
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

        try
        {
            model = MetaCliModel.LoadFromXmlWorkspace(commandWorkspacePath);
        }
        catch (Exception exception)
        {
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.CommandSurfaceLoadFailed,
                4,
                $"Cannot load command surface workspace '{Path.GetFullPath(commandWorkspacePath)}'. {exception.Message}",
                Exception: exception));
            return;
        }

        if (useDefaultHelp)
        {
            var help = new MetaCliHelpService(helpOutput, helpError ?? error);
            if (help.TryWriteHelp(model, applicationId, arguments, out var helpExitCode))
            {
                setExitCode(helpExitCode);
                return;
            }
        }

        var parse = new MetaCliParser(model, applicationId).Parse(arguments);
        if (!parse.Succeeded)
        {
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.ParseFailed,
                2,
                parse.Message ?? "Command line could not be parsed."));
            return;
        }

        var invocation = parse.RequireInvocation();
        if (!handlers.TryGetValue(invocation.ExecutableCommand.Id, out var handler))
        {
            Fail(new MetaCliRuntimeFailure(
                MetaCliRuntimeFailureKind.HandlerMissing,
                4,
                $"Command '{invocation.CommandRoute}' is modeled but has no implementation.",
                invocation));
            return;
        }

        try
        {
            if (handler.WorkspaceHandler is not null || handler.AsyncWorkspaceHandler is not null)
            {
                var workspacePath = ResolveWorkspacePath(invocation);
                var domainModel = TModel.LoadFromXmlWorkspace(workspacePath);
                if (handler.WorkspaceHandler is not null)
                {
                    handler.WorkspaceHandler(invocation, domainModel);
                }
                else
                {
                    handler.AsyncWorkspaceHandler!(invocation, domainModel).GetAwaiter().GetResult();
                }
            }
            else if (handler.AsyncHandler is not null)
            {
                handler.AsyncHandler(invocation).GetAwaiter().GetResult();
            }
            else
            {
                handler.Handler!(invocation);
            }
        }
        catch (MetaCliExitException exception)
        {
            if (!string.IsNullOrWhiteSpace(exception.Message))
            {
                error.WriteLine(exception.Message);
            }

            setExitCode(exception.ExitCode);
            return;
        }
        catch (Exception exception)
        {
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

    private string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        try
        {
            var value = invocation.Optional(workspaceParameter);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Path.GetFullPath(value);
            }
        }
        catch (KeyNotFoundException)
        {
        }

        return Directory.GetCurrentDirectory();
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
        MetaCliModelCommandHandler<TModel>? WorkspaceHandler,
        MetaCliAsyncModelCommandHandler<TModel>? AsyncWorkspaceHandler)
    {
        public static HandlerBinding WithoutWorkspace(MetaCliCommandHandler handler) =>
            new(handler, null, null, null);

        public static HandlerBinding WithoutWorkspace(MetaCliAsyncCommandHandler handler) =>
            new(null, handler, null, null);

        public static HandlerBinding WithWorkspace(MetaCliModelCommandHandler<TModel> handler) =>
            new(null, null, handler, null);

        public static HandlerBinding WithWorkspace(MetaCliAsyncModelCommandHandler<TModel> handler) =>
            new(null, null, null, handler);
    }
}
