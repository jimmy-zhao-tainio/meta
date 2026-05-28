var presenter = new Meta.Core.Presentation.ConsolePresenter();
if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(presenter, "meta", args, out var versionExitCode))
{
    return versionExitCode;
}

return await new CliRuntime().RunAsync(args).ConfigureAwait(false);
