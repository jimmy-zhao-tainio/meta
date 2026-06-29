using MetaCli;
using MetaCli.Core;

const string AppName = "meta";
const string ApplicationId = "app-meta";
const string CommandWorkspaceDirectoryName = "meta.MetaCli";

var presenter = new Meta.Core.Presentation.ConsolePresenter();
if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(presenter, AppName, args, out var versionExitCode))
{
    return versionExitCode;
}

var handlers = new CliRuntime();
handlers.UseArguments(args);

Environment.ExitCode = 0;
var runtime = new MetaCliRuntime<MetaCliModel>(CommandWorkspacePath(), ApplicationId)
    .UseDefaultHelp()
    .OnFailure(handlers.HandleRuntimeFailure);

handlers.BindCommandHandlers(runtime, args);
runtime.Run(args);
return Environment.ExitCode;

static string CommandWorkspacePath() =>
    Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);
