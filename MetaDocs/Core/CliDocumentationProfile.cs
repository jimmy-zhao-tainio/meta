namespace MetaDocs.Core;

public sealed class CliDocumentationProfile
{
    private readonly Dictionary<string, CliCommandCommentary> commands;
    private readonly Dictionary<string, Dictionary<string, CliOptionCommentary>> optionsByCommand;
    private readonly Dictionary<string, string> exampleExplanations;

    public CliDocumentationProfile(
        string? applicationSummary = null,
        IEnumerable<CliCommandCommentary>? commands = null,
        IEnumerable<CliOptionCommentary>? options = null,
        IEnumerable<CliExampleCommentary>? examples = null)
    {
        ApplicationSummary = applicationSummary ?? string.Empty;
        this.commands = (commands ?? [])
            .ToDictionary(row => row.CommandName, StringComparer.OrdinalIgnoreCase);
        optionsByCommand = new Dictionary<string, Dictionary<string, CliOptionCommentary>>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options ?? [])
        {
            if (!optionsByCommand.TryGetValue(option.CommandName, out var commandOptions))
            {
                commandOptions = new Dictionary<string, CliOptionCommentary>(StringComparer.OrdinalIgnoreCase);
                optionsByCommand.Add(option.CommandName, commandOptions);
            }

            commandOptions[option.OptionName] = option;
        }

        exampleExplanations = (examples ?? [])
            .ToDictionary(row => BuildExampleKey(row.CommandName, row.CommandText), row => row.Explanation, StringComparer.OrdinalIgnoreCase);
    }

    public string ApplicationSummary { get; }

    public CliCommandCommentary? FindCommand(string commandName) =>
        commands.TryGetValue(commandName, out var commentary)
            ? commentary
            : null;

    public CliOptionCommentary? FindOption(string commandName, string optionName) =>
        optionsByCommand.TryGetValue(commandName, out var commandOptions) &&
        commandOptions.TryGetValue(optionName, out var commentary)
            ? commentary
            : null;

    public string FindExampleExplanation(string commandName, string commandText) =>
        exampleExplanations.TryGetValue(BuildExampleKey(commandName, commandText), out var explanation)
            ? explanation
            : string.Empty;

    private static string BuildExampleKey(string commandName, string commandText) =>
        $"{commandName.Trim()}::{commandText.Trim()}";
}

public sealed record CliCommandCommentary(
    string CommandName,
    string Purpose,
    string WhenToUse,
    string HowItWorks);

public sealed record CliOptionCommentary(
    string CommandName,
    string OptionName,
    string Explanation,
    string WhenToUse = "",
    string ExampleValue = "");

public sealed record CliExampleCommentary(
    string CommandName,
    string CommandText,
    string Explanation);
