using MetaCli;
using MetaCli.Core;

internal sealed partial class CliRuntime
{
    private const string CommandApplicationId = "app-meta";
    private MetaCliModel? commandSurfaceModel;

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, "meta.MetaCli");

    string BuildUsageHintForCurrentArgs()
    {
        var model = LoadCommandSurface();
        if (model is null)
        {
            return string.Empty;
        }

        var help = new MetaCliHelpService();
        return help.TryBuildUsage(model, CommandApplicationId, args, out var usage)
            ? usage
            : string.Empty;
    }

    string NormalizeUsageSyntax(string usage)
    {
        if (string.IsNullOrWhiteSpace(usage))
        {
            return string.Empty;
        }

        var trimmed = usage.Trim();
        const string Prefix = "Usage:";
        if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[Prefix.Length..].Trim();
        }

        if (!trimmed.StartsWith("meta ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "meta " + trimmed;
        }

        return trimmed;
    }

    string BuildNextHelpHintFromUsage(string usage)
    {
        var normalizedUsage = NormalizeUsageSyntax(usage);
        if (string.IsNullOrWhiteSpace(normalizedUsage))
        {
            return "meta help";
        }

        var tokens = normalizedUsage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1 || !string.Equals(tokens[0], "meta", StringComparison.OrdinalIgnoreCase))
        {
            return "meta help";
        }

        var topic = new List<string>();
        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("<", StringComparison.Ordinal) ||
                token.StartsWith("[", StringComparison.Ordinal) ||
                token.StartsWith("--", StringComparison.Ordinal) ||
                token.Contains('|', StringComparison.Ordinal))
            {
                break;
            }

            topic.Add(token);
        }

        return topic.Count == 0 ? "meta help" : $"meta help {string.Join(" ", topic)}";
    }

    string BuildNextHelpHintForCurrentArgs()
    {
        var usage = BuildUsageHintForCurrentArgs();
        return BuildNextHelpHintFromUsage(usage);
    }

    bool TryHandleHelpRequest(string[] commandArgs, out int exitCode)
    {
        exitCode = 0;
        if (commandArgs.Length == 0 ||
            !MetaCliHelpService.IsHelpToken(commandArgs[0]) &&
            !MetaCliHelpService.IsHelpToken(commandArgs[^1]))
        {
            return false;
        }

        var model = LoadCommandSurface();
        if (model is null)
        {
            return false;
        }

        var help = new MetaCliHelpService(Console.Out, Console.Error);
        return help.TryWriteHelp(model, CommandApplicationId, commandArgs, out exitCode);
    }

    static bool IsHelpToken(string value) =>
        MetaCliHelpService.IsHelpToken(value);

    int PrintCommandUnknownError(string command)
    {
        var suggestions = SuggestValues(command, GetCommandSuggestions());
        var hints = new List<string>();
        if (suggestions.Count > 0)
        {
            hints.Add("Did you mean: " + string.Join(", ", suggestions.Take(3)));
        }

        hints.Add("Next: meta help");

        return PrintFormattedError(
            "E_COMMAND_UNKNOWN",
            $"Unknown command '{command}'.",
            exitCode: 1,
            hints: hints);
    }

    private MetaCliModel? LoadCommandSurface()
    {
        if (commandSurfaceModel is not null)
        {
            return commandSurfaceModel;
        }

        try
        {
            commandSurfaceModel = MetaCliModel.LoadFromXmlWorkspace(CommandWorkspacePath);
            return commandSurfaceModel;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetCommandSuggestions()
    {
        var model = LoadCommandSurface();
        if (model is null)
        {
            return new[] { "help" };
        }

        var surface = new MetaCliCommandSurface(model, CommandApplicationId);
        if (!surface.TryResolveApplication(out var application, out _))
        {
            return new[] { "help" };
        }

        return new[] { "help" }
            .Concat(surface.RootCommands(application).Select(command =>
                string.IsNullOrWhiteSpace(command.Token) ? command.Name : command.Token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
