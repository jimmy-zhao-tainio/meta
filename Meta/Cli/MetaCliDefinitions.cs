using Meta.Core.Presentation.Cli;

public static class MetaCliDefinitions
{
    public static CliAppDefinition CreateAppDefinition() => CliRuntime.CreateAppDefinition();
}

internal sealed partial class CliRuntime
{
    internal static CliAppDefinition CreateAppDefinition()
    {
        var runtime = new CliRuntime();
        var registry = runtime.BuildCommandRegistry();
        var commands = CreateCommandDefinitions(registry);

        return new CliAppDefinition(
            "meta",
            new[] { "meta <command> [options]" },
            commands,
            new[]
            {
                "meta is the generic metadata workspace CLI.",
                "Use model-specific CLIs when a sanctioned model family owns the operation."
            },
            "meta help");
    }

    private static IReadOnlyList<CliCommandDefinition> CreateCommandDefinitions(
        IReadOnlyDictionary<string, CliCommandRegistration> registry)
    {
        var commands = new List<CliCommandDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in registry
                     .OrderBy(item => item.Value.Domain, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddCommandDefinition(commands, seen, item.Key, item.Value.Description);
        }

        return commands;
    }

    private static void AddCommandDefinition(
        ICollection<CliCommandDefinition> commands,
        ISet<string> seen,
        string commandName,
        string description)
    {
        if (!seen.Add(commandName))
        {
            return;
        }

        var command = CreateCommandDefinition(commandName, description, out var document);
        commands.Add(command);
        if (document is null)
        {
            return;
        }

        foreach (var subcommand in SubcommandEntries(document.Value))
        {
            AddCommandDefinition(
                commands,
                seen,
                $"{commandName} {subcommand.Command}",
                subcommand.Description);
        }
    }

    private static CliCommandDefinition CreateCommandDefinition(
        string commandName,
        string description,
        out HelpDocument? document)
    {
        if (!HelpTopics.TryBuildHelpTopic(commandName, out var topic))
        {
            document = null;
            return new CliCommandDefinition(
                commandName,
                description,
                new[] { $"meta {commandName} [options]" });
        }

        document = topic;
        var notes = topic.Sections
            .Where(static section => !IsSubcommandsSection(section))
            .SelectMany(section => section.Entries.Select(entry => $"{section.Title} {entry.Command} - {entry.Description}"))
            .ToArray();
        return new CliCommandDefinition(
            commandName,
            string.IsNullOrWhiteSpace(topic.Header.Note) ? description : topic.Header.Note,
            new[] { topic.Usage },
            topic.Options.Select(option => new CliOptionDefinition(option.Option, option.Description)).ToArray(),
            notes,
            topic.Examples,
            topic.Next);
    }

    private static IEnumerable<(string Command, string Description)> SubcommandEntries(HelpDocument document) =>
        document.Sections
            .Where(IsSubcommandsSection)
            .SelectMany(section => section.Entries);

    private static bool IsSubcommandsSection(HelpSection section) =>
        section.Title.Trim().TrimEnd(':').Equals("Subcommands", StringComparison.OrdinalIgnoreCase);
}
