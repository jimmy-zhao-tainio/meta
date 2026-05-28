namespace Meta.Core.Presentation.Cli;

public sealed record CliOptionDefinition(
    string Syntax,
    string Description);

public sealed record CliCommandDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> Usages,
    IReadOnlyList<CliOptionDefinition>? Options = null,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<string>? Examples = null,
    string? Next = null,
    bool ShowInCommandCatalog = true,
    IReadOnlyList<string>? AdditionalNext = null)
{
    public IReadOnlyList<CliOptionDefinition> Options { get; } = Options ?? Array.Empty<CliOptionDefinition>();

    public IReadOnlyList<string> Notes { get; } = Notes ?? Array.Empty<string>();

    public IReadOnlyList<string> Examples { get; } = Examples ?? Array.Empty<string>();

    public IReadOnlyList<string> AdditionalNext { get; } = AdditionalNext ?? Array.Empty<string>();

    public string HelpCommand(string appName) => $"{appName} {Name} --help";
}

public sealed record CliCommandRoute(
    CliCommandDefinition Definition,
    Func<string[], Task<int>> ExecuteAsync);

public sealed record CliAppDefinition(
    string Name,
    IReadOnlyList<string> Usages,
    IReadOnlyList<CliCommandDefinition> Commands,
    IReadOnlyList<string>? Notes = null,
    string? Next = null,
    IReadOnlyList<string>? Examples = null)
{
    public IReadOnlyList<string> Notes { get; } = Notes ?? Array.Empty<string>();

    public IReadOnlyList<string> Examples { get; } = Examples ?? Array.Empty<string>();

    public bool TryGetCommand(string name, out CliCommandDefinition command)
    {
        command = Commands.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))!;
        return command != null;
    }

    public CliCommandDefinition GetCommand(string name)
    {
        return TryGetCommand(name, out var command)
            ? command
            : throw new InvalidOperationException($"CLI '{Name}' does not define command '{name}'.");
    }
}

public static class CliHelpRenderer
{
    public static void WriteAppHelp(ConsolePresenter presenter, CliAppDefinition app)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(app);

        WriteUsages(presenter, app.Usages);
        var commands = app.Commands
            .Where(command => command.ShowInCommandCatalog)
            .Select(command => (command.Name, command.Description))
            .ToArray();
        if (commands.Length > 0)
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteCommandCatalog("Commands:", commands);
        }

        WriteNotes(presenter, app.Notes);
        if (app.Examples.Count > 0)
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteExamples(app.Examples);
        }

        if (!string.IsNullOrWhiteSpace(app.Next))
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteNext(app.Next);
        }
    }

    public static void WriteCommandHelp(ConsolePresenter presenter, CliAppDefinition app, CliCommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(command);

        presenter.WriteInfo($"Command: {command.Name}");
        WriteUsages(presenter, command.Usages);

        if (command.Options.Count > 0)
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteOptionCatalog(command.Options.Select(option => (option.Syntax, option.Description)).ToArray());
        }

        WriteNotes(presenter, command.Notes);

        if (command.Examples.Count > 0)
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteExamples(command.Examples);
        }

        var wroteNextHeader = false;
        if (!string.IsNullOrWhiteSpace(command.Next))
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteNext(command.Next);
            wroteNextHeader = true;
        }

        foreach (var next in command.AdditionalNext.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!wroteNextHeader)
            {
                presenter.WriteInfo(string.Empty);
                wroteNextHeader = true;
            }

            presenter.WriteNext(next);
        }
    }

    private static void WriteUsages(ConsolePresenter presenter, IReadOnlyList<string> usages)
    {
        foreach (var usage in usages.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            presenter.WriteUsage(usage);
        }
    }

    private static void WriteNotes(ConsolePresenter presenter, IReadOnlyList<string> notes)
    {
        var normalizedNotes = notes
            .Where(static note => !string.IsNullOrWhiteSpace(note))
            .ToArray();
        if (normalizedNotes.Length == 0)
        {
            return;
        }

        presenter.WriteInfo(string.Empty);
        presenter.WriteInfo("Notes:");
        foreach (var note in normalizedNotes)
        {
            presenter.WriteInfo($"  {note}");
        }
    }
}
