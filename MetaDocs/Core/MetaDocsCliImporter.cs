using System.Security.Cryptography;
using System.Text;
using Meta.Core.Presentation.Cli;
using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsCliImporter
{
    public DocumentationSubject ImportApplication(
        MetaDocsModel model,
        CliAppDefinition app,
        CliDocumentationProfile? profile = null,
        string groupName = "",
        int ordinal = 100,
        string sourceId = "")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(app);
        profile ??= new CliDocumentationProfile();

        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? $"source:cli:{NormalizeKey(app.Name)}"
            : sourceId;
        var session = new MetaDocsImportSession(
            model,
            normalizedSourceId,
            "CliDefinition",
            app.Name,
            app.Name,
            ComputeCliFingerprint(app),
            "MetaDocs.CliDefinition",
            "1");

        var visibleCommands = app.Commands
            .Where(command =>
                command.ShowInCommandCatalog &&
                !string.Equals(command.Name, "help", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasAuthoredApplicationSummary = !string.IsNullOrWhiteSpace(profile.ApplicationSummary);
        var applicationSummary = FirstNonEmpty(
            profile.ApplicationSummary,
            FindNarrative(model, $"{normalizedSourceId}:app", "Summary")?.Body,
            app.Notes.FirstOrDefault(),
            visibleCommands.FirstOrDefault()?.Description);
        var application = session.UpsertSubject(
            $"{normalizedSourceId}:app",
            "CliApplication",
            "CliAppDefinition",
            app.Name,
            app.Name,
            app.Name,
            applicationSummary,
            string.Empty,
            ordinal);
        session.UpsertFact(application, "Cli", "CommandCount", visibleCommands.Length.ToString(), "Number");
        session.UpsertFact(application, "Cli", "GroupName", groupName, "String");
        session.UpsertNarrative(
            application,
            "Summary",
            "Summary",
            applicationSummary,
            hasAuthoredApplicationSummary ? "Authored" : "Generated",
            10);
        session.EnsureViewNode(application, app.Name, ordinal);

        for (var i = 0; i < visibleCommands.Length; i++)
        {
            AddCommand(model, session, application, visibleCommands[i], i + 1, profile);
        }

        session.Complete();
        return application;
    }

    private static void AddCommand(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject application,
        CliCommandDefinition command,
        int ordinal,
        CliDocumentationProfile profile)
    {
        var commandCommentary = profile.FindCommand(command.Name);
        var commandKey = $"{application.Id}:command:{NormalizeKey(command.Name)}";
        var commandPath = $"{application.NativeId} {command.Name}".Trim();
        var commandSummary = command.Description;
        var commandSubject = session.UpsertSubject(
            commandKey,
            "CliCommand",
            "CliCommandDefinition",
            command.Name,
            commandPath,
            commandPath,
            commandSummary,
            application.Id,
            ordinal);
        session.UpsertFact(commandSubject, "Cli", "Name", command.Name);
        session.UpsertFact(commandSubject, "Cli", "CommandPath", commandPath);
        session.UpsertFact(commandSubject, "Cli", "UsageCount", command.Usages.Count.ToString(), "Number");
        session.UpsertFact(commandSubject, "Cli", "OptionCount", command.Options.Count.ToString(), "Number");
        session.UpsertNarrative(
            commandSubject,
            "Summary",
            "Summary",
            FirstNonEmpty(commandCommentary?.Purpose, FindNarrative(model, commandKey, "Summary")?.Body, command.Description),
            commandCommentary is null ? "Generated" : "Authored",
            10);
        UpsertNarrativeIfPresent(
            model,
            session,
            commandSubject,
            commandKey,
            "Usage",
            "Usage",
            commandCommentary?.WhenToUse,
            commandCommentary is null ? "Generated" : "Authored",
            20);
        UpsertNarrativeIfPresent(
            model,
            session,
            commandSubject,
            commandKey,
            "ImplementationNote",
            "How it works",
            commandCommentary?.HowItWorks,
            commandCommentary is null ? "Generated" : "Authored",
            30);

        for (var i = 0; i < command.Usages.Count; i++)
        {
            session.UpsertFact(
                commandSubject,
                "Usage",
                (i + 1).ToString("000"),
                command.Usages[i]);
        }

        for (var i = 0; i < command.Options.Count; i++)
        {
            AddOption(model, session, commandSubject, command, command.Options[i], i + 1, profile);
        }

        for (var i = 0; i < command.Notes.Count; i++)
        {
            session.UpsertFact(
                commandSubject,
                "Note",
                (i + 1).ToString("000"),
                command.Notes[i]);
        }

        for (var i = 0; i < command.Examples.Count; i++)
        {
            var example = command.Examples[i];
            var exampleSubject = session.UpsertSubject(
                $"{commandSubject.Id}:example:{i + 1:000}",
                "CliArgument",
                "CliExample",
                example,
                $"Example {i + 1}",
                $"{commandSubject.DisplayPath} example {i + 1}",
                profile.FindExampleExplanation(command.Name, example),
                commandSubject.Id,
                1000 + i);
            session.UpsertFact(exampleSubject, "Cli", "CommandText", example);
            var exampleExplanation = profile.FindExampleExplanation(command.Name, example);
            if (!string.IsNullOrWhiteSpace(exampleExplanation))
            {
                session.UpsertNarrative(
                    exampleSubject,
                    "Example",
                    $"Example {i + 1}",
                    exampleExplanation,
                    "Authored",
                    10);
            }
            else
            {
                RemoveEmptyGeneratedNarratives(model, exampleSubject.Id, "Example");
            }
        }
    }

    private static void AddOption(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject commandSubject,
        CliCommandDefinition command,
        CliOptionDefinition option,
        int ordinal,
        CliDocumentationProfile profile)
    {
        var (optionName, valueName) = SplitOptionSyntax(option.Syntax);
        var optionCommentary = profile.FindOption(command.Name, optionName);
        var optionKey = $"{commandSubject.Id}:option:{NormalizeKey(optionName)}";
        var optionSubject = session.UpsertSubject(
            optionKey,
            "CliOption",
            "CliOptionDefinition",
            optionName,
            optionName,
            $"{commandSubject.DisplayPath} {optionName}",
            option.Description,
            commandSubject.Id,
            ordinal);
        session.UpsertFact(optionSubject, "Cli", "Name", optionName);
        session.UpsertFact(optionSubject, "Cli", "Syntax", option.Syntax);
        session.UpsertFact(optionSubject, "Cli", "ValueName", valueName);
        session.UpsertFact(optionSubject, "Cli", "Description", option.Description);
        session.UpsertNarrative(
            optionSubject,
            "Summary",
            "Summary",
            FirstNonEmpty(optionCommentary?.Explanation, FindNarrative(model, optionKey, "Summary")?.Body, option.Description),
            optionCommentary is null ? "Generated" : "Authored",
            10);
        UpsertNarrativeIfPresent(
            model,
            session,
            optionSubject,
            optionKey,
            "Usage",
            "When to use",
            optionCommentary?.WhenToUse,
            optionCommentary is null ? "Generated" : "Authored",
            20);
        session.UpsertFact(
            optionSubject,
            "Cli",
            "ExampleValue",
            FirstNonEmpty(optionCommentary?.ExampleValue, FindFact(model, optionKey, "Cli", "ExampleValue")?.Value));
    }

    private static void UpsertNarrativeIfPresent(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject subject,
        string subjectKey,
        string slot,
        string title,
        string? body,
        string origin,
        int ordinal)
    {
        var existing = FindNarrative(model, subjectKey, slot)?.Body;
        var resolvedBody = FirstNonEmpty(body, existing);
        if (string.IsNullOrWhiteSpace(resolvedBody))
        {
            RemoveEmptyGeneratedNarratives(model, subjectKey, slot);
            return;
        }

        session.UpsertNarrative(subject, slot, title, resolvedBody, origin, ordinal);
    }

    private static void RemoveEmptyGeneratedNarratives(MetaDocsModel model, string subjectKey, string slot)
    {
        for (var i = model.DocumentationNarrativeList.Count - 1; i >= 0; i--)
        {
            var narrative = model.DocumentationNarrativeList[i];
            if (string.Equals(narrative.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(narrative.Slot, slot, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(narrative.Body) &&
                (string.Equals(narrative.Origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(narrative.ReviewStatus, "NeedsAuthoring", StringComparison.OrdinalIgnoreCase)))
            {
                model.DocumentationNarrativeList.RemoveAt(i);
            }
        }
    }

    private static DocumentationNarrative? FindNarrative(MetaDocsModel model, string subjectKey, string slot) =>
        model.DocumentationNarrativeList.FirstOrDefault(row =>
            string.Equals(row.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Slot, slot, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Origin, "Authored", StringComparison.OrdinalIgnoreCase));

    private static DocumentationFact? FindFact(MetaDocsModel model, string subjectKey, string kind, string name) =>
        model.DocumentationFactList.FirstOrDefault(row =>
            string.Equals(row.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase));

    private static (string OptionName, string ValueName) SplitOptionSyntax(string syntax)
    {
        var trimmed = syntax.Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
    }

    private static string ComputeCliFingerprint(CliAppDefinition app)
    {
        var builder = new StringBuilder();
        builder.AppendLine(app.Name);
        foreach (var command in app.Commands.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(command.Name);
            builder.AppendLine(command.Description);
            foreach (var usage in command.Usages)
            {
                builder.AppendLine("usage:" + usage);
            }

            foreach (var option in command.Options)
            {
                builder.AppendLine("option:" + option.Syntax + ":" + option.Description);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeKey(string value) => MetaDocsImportSession.NormalizeKey(value);
}
