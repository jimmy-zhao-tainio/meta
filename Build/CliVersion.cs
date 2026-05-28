using System.Reflection;
using Meta.Core.Presentation;

namespace Meta.Core.Presentation.Cli;

public static class CliVersion
{
    public static bool TryWriteVersion(
        ConsolePresenter presenter,
        string appName,
        string[] args,
        out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        ArgumentNullException.ThrowIfNull(args);

        exitCode = 0;
        if (args.Length != 1 ||
            !string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        presenter.WriteInfo($"{appName} {ResolveVersion()}");
        return true;
    }

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : version.ToString();
    }
}
