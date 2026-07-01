using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using MetaCli;
using MetaCli.Core;

namespace MetaDocs.Core;

public sealed class MetaDocsCliImporter
{
    public DocumentationSubject ImportApplication(
        MetaDocsModel model,
        MetaCliModel cli,
        CliDocumentationProfile? profile = null,
        string applicationId = "",
        string groupName = "",
        string sourceId = "")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cli);
        profile ??= new CliDocumentationProfile();

        var app = ResolveApplication(cli, applicationId);
        var surface = new MetaCliCommandSurface(cli, app.Id);
        var appName = ApplicationCommandName(app);
        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? $"source:cli:{NormalizeKey(appName)}"
            : sourceId;
        var session = new MetaDocsImportSession(
            model,
            normalizedSourceId,
            "MetaCliWorkspace",
            appName,
            appName,
            ComputeCliFingerprint(cli, app),
            "MetaDocs.MetaCliWorkspace",
            "1");

        var executables = cli.ExecutableCommandList
            .Where(executable => ReferenceEquals(executable.Command.Application, app))
            .OrderBy(executable => surface.Route(executable.Command), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasAuthoredApplicationSummary = !string.IsNullOrWhiteSpace(profile.ApplicationSummary);
        var applicationSummary = FirstNonEmpty(
            profile.ApplicationSummary,
            FindNarrative(model, $"{normalizedSourceId}:app", "Summary")?.Body,
            app.Description);
        var application = session.UpsertSubject(
            $"{normalizedSourceId}:app",
            "CliApplication",
            "MetaCli.Application",
            app.Id,
            appName,
            appName,
            applicationSummary,
            string.Empty,
            null);
        session.UpsertFact(application, "Cli", "ApplicationId", app.Id);
        session.UpsertFact(application, "Cli", "ExecutableName", appName);
        session.UpsertFact(application, "Cli", "CommandCount", executables.Length.ToString(CultureInfo.InvariantCulture), "Number");
        session.UpsertFact(application, "Cli", "ApplicationOptionCount", ApplicationOptions(cli, surface, app).Count().ToString(CultureInfo.InvariantCulture), "Number");
        session.UpsertFact(application, "Cli", "GroupName", groupName, "String");
        session.UpsertNarrative(
            application,
            "Summary",
            "Summary",
            applicationSummary,
            hasAuthoredApplicationSummary ? "Authored" : "Generated");
        session.EnsureViewNode(application, appName);

        DocumentationSubject? previousOption = null;
        var appOptions = ApplicationOptions(cli, surface, app).ToArray();
        foreach (var option in appOptions)
        {
            previousOption = AddOption(model, session, application, cli, surface, option, previousOption, profile, string.Empty);
        }

        DocumentationSubject? previousCommand = null;
        foreach (var executable in executables)
        {
            previousCommand = AddCommand(model, session, application, cli, surface, executable, previousCommand, profile);
        }

        session.Complete(pruneMissingFromSource: true);
        return application;
    }

    private static DocumentationSubject AddCommand(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject application,
        MetaCliModel cli,
        MetaCliCommandSurface surface,
        ExecutableCommand executable,
        DocumentationSubject? previousCommand,
        CliDocumentationProfile profile)
    {
        var commandRoute = surface.Route(executable.Command);
        var commandPath = $"{application.DisplayName} {commandRoute}".Trim();
        var commandCommentary = profile.FindCommand(commandRoute);
        var commandKey = $"{application.Id}:command:{NormalizeKey(commandRoute)}";
        var commandSummary = executable.Command.Description ?? string.Empty;
        var commandSubject = session.UpsertSubject(
            commandKey,
            "CliCommand",
            "MetaCli.ExecutableCommand",
            executable.Id,
            commandPath,
            commandPath,
            commandSummary,
            application.Id,
            previousCommand);
        session.UpsertFact(commandSubject, "Cli", "ExecutableCommandId", executable.Id);
        session.UpsertFact(commandSubject, "Cli", "CommandId", executable.Command.Id);
        session.UpsertFact(commandSubject, "Cli", "Name", commandRoute);
        session.UpsertFact(commandSubject, "Cli", "CommandPath", commandPath);
        session.UpsertFact(commandSubject, "Cli", "UsageCount", "1", "Number");

        var commandOptions = ApplicationOptions(cli, surface, executable.Command.Application)
            .Concat(CommandOptions(cli, surface, executable))
            .GroupBy(static option => option.Parameter, ReferenceComparer<Parameter>.Instance)
            .Select(static group => group.First())
            .OrderBy(option => surface.OptionTokens(option.Parameter).FirstOrDefault()?.Token ?? option.Parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var commandPositionals = surface.OrderedPositionals(executable).ToArray();
        var parameterGroups = cli.ParameterGroupList
            .Where(group => ReferenceEquals(group.ExecutableCommand, executable))
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        session.UpsertFact(commandSubject, "Cli", "OptionCount", commandOptions.Length.ToString(CultureInfo.InvariantCulture), "Number");
        session.UpsertFact(commandSubject, "Cli", "PositionalCount", commandPositionals.Length.ToString(CultureInfo.InvariantCulture), "Number");
        session.UpsertFact(commandSubject, "Cli", "ParameterGroupCount", parameterGroups.Length.ToString(CultureInfo.InvariantCulture), "Number");
        var previousNarrative = session.UpsertNarrative(
            commandSubject,
            "Summary",
            "Summary",
            FirstNonEmpty(commandCommentary?.Purpose, FindNarrative(model, commandKey, "Summary")?.Body, commandSummary),
            commandCommentary is null ? "Generated" : "Authored");
        previousNarrative = UpsertNarrativeIfPresent(
            model,
            session,
            commandSubject,
            commandKey,
            "Usage",
            "Usage",
            commandCommentary?.WhenToUse,
            commandCommentary is null ? "Generated" : "Authored",
            previousNarrative);
        previousNarrative = UpsertNarrativeIfPresent(
            model,
            session,
            commandSubject,
            commandKey,
            "ImplementationNote",
            "How it works",
            commandCommentary?.HowItWorks,
            commandCommentary is null ? "Generated" : "Authored",
            previousNarrative);

        session.UpsertFact(commandSubject, "Usage", "001", BuildUsage(commandPath, commandOptions, commandPositionals, surface));

        DocumentationSubject? previousOption = null;
        foreach (var option in commandOptions)
        {
            previousOption = AddOption(model, session, commandSubject, cli, surface, option, previousOption, profile, commandRoute);
        }

        DocumentationSubject? previousPositional = null;
        foreach (var positional in commandPositionals)
        {
            previousPositional = AddPositional(session, cli, commandSubject, positional, previousPositional);
        }

        DocumentationSubject? previousGroup = null;
        foreach (var group in parameterGroups)
        {
            previousGroup = AddParameterGroup(session, cli, commandSubject, group, previousGroup);
        }

        return commandSubject;
    }

    private static DocumentationSubject AddOption(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject parent,
        MetaCliModel cli,
        MetaCliCommandSurface surface,
        Option option,
        DocumentationSubject? previousOption,
        CliDocumentationProfile profile,
        string commandRoute)
    {
        var tokens = surface.OptionTokens(option.Parameter).ToArray();
        var optionName = tokens.FirstOrDefault()?.Token ?? option.Parameter.Name;
        var optionSyntax = BuildOptionSyntax(optionName, option.Parameter);
        var optionCommentary = string.IsNullOrWhiteSpace(commandRoute)
            ? null
            : profile.FindOption(commandRoute, optionName);
        var optionKey = $"{parent.Id}:option:{NormalizeKey(optionName)}";
        var optionSubject = session.UpsertSubject(
            optionKey,
            "CliOption",
            "MetaCli.Option",
            option.Id,
            optionName,
            $"{parent.DisplayPath} {optionName}",
            option.Parameter.Description ?? string.Empty,
            parent.Id,
            previousOption);
        session.UpsertFact(optionSubject, "Cli", "OptionId", option.Id);
        session.UpsertFact(optionSubject, "Cli", "ParameterId", option.Parameter.Id);
        session.UpsertFact(optionSubject, "Cli", "Name", optionName);
        session.UpsertFact(optionSubject, "Cli", "Syntax", optionSyntax);
        session.UpsertFact(optionSubject, "Cli", "ValueName", ValueName(option.Parameter));
        AddParameterShapeFacts(session, optionSubject, cli, option.Parameter);
        session.UpsertFact(optionSubject, "Cli", "Description", option.Parameter.Description ?? string.Empty);
        session.UpsertFact(optionSubject, "Cli", "Required", option.Parameter.IsRequired ?? string.Empty);
        session.UpsertFact(optionSubject, "Cli", "Repeatable", option.Parameter.IsRepeatable ?? string.Empty);
        session.UpsertFact(optionSubject, "Cli", "DefaultValue", option.Parameter.DefaultValue ?? string.Empty);
        session.UpsertFact(optionSubject, "Cli", "Aliases", string.Join(" ", tokens.Skip(1).Select(static token => token.Token)));
        var previousNarrative = session.UpsertNarrative(
            optionSubject,
            "Summary",
            "Summary",
            FirstNonEmpty(optionCommentary?.Explanation, FindNarrative(model, optionKey, "Summary")?.Body, option.Parameter.Description),
            optionCommentary is null ? "Generated" : "Authored");
        UpsertNarrativeIfPresent(
            model,
            session,
            optionSubject,
            optionKey,
            "Usage",
            "When to use",
            optionCommentary?.WhenToUse,
            optionCommentary is null ? "Generated" : "Authored",
            previousNarrative);
        session.UpsertFact(
            optionSubject,
            "Cli",
            "ExampleValue",
            FirstNonEmpty(optionCommentary?.ExampleValue, FindFact(model, optionKey, "Cli", "ExampleValue")?.Value));
        return optionSubject;
    }

    private static DocumentationSubject AddPositional(
        MetaDocsImportSession session,
        MetaCliModel cli,
        DocumentationSubject commandSubject,
        PositionalArgument positional,
        DocumentationSubject? previousPositional)
    {
        var parameter = positional.Parameter;
        var name = $"<{parameter.Name}>";
        var subject = session.UpsertSubject(
            $"{commandSubject.Id}:argument:{NormalizeKey(parameter.Name)}",
            "CliArgument",
            "MetaCli.PositionalArgument",
            positional.Id,
            name,
            $"{commandSubject.DisplayPath} {name}",
            parameter.Description ?? string.Empty,
            commandSubject.Id,
            previousPositional);
        session.UpsertFact(subject, "Cli", "PositionalArgumentId", positional.Id);
        session.UpsertFact(subject, "Cli", "ParameterId", parameter.Id);
        session.UpsertFact(subject, "Cli", "Name", parameter.Name);
        session.UpsertFact(subject, "Cli", "ValueName", ValueName(parameter));
        AddParameterShapeFacts(session, subject, cli, parameter);
        session.UpsertFact(subject, "Cli", "Required", parameter.IsRequired ?? string.Empty);
        session.UpsertFact(subject, "Cli", "Repeatable", parameter.IsRepeatable ?? string.Empty);
        session.UpsertFact(subject, "Cli", "DefaultValue", parameter.DefaultValue ?? string.Empty);
        return subject;
    }

    private static DocumentationSubject AddParameterGroup(
        MetaDocsImportSession session,
        MetaCliModel cli,
        DocumentationSubject commandSubject,
        ParameterGroup group,
        DocumentationSubject? previousGroup)
    {
        var members = cli.ParameterGroupMemberList
            .Where(member => ReferenceEquals(member.ParameterGroup, group))
            .OrderBy(member => member.Parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var memberNames = members
            .Select(member => member.Parameter.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var displayName = group.Name;
        var summary = FirstNonEmpty(
            group.Description,
            memberNames.Length == 0
                ? string.Empty
                : $"Choose {string.Join(" or ", memberNames)}.");
        var subject = session.UpsertSubject(
            $"{commandSubject.Id}:parameter-group:{NormalizeKey(group.Name)}",
            "CliParameterGroup",
            "MetaCli.ParameterGroup",
            group.Id,
            displayName,
            $"{commandSubject.DisplayPath} group {displayName}",
            summary,
            commandSubject.Id,
            previousGroup);
        session.UpsertFact(subject, "Cli", "ParameterGroupId", group.Id);
        session.UpsertFact(subject, "Cli", "Name", group.Name);
        session.UpsertFact(subject, "Cli", "Required", group.IsRequired ?? string.Empty);
        session.UpsertFact(subject, "Cli", "AllowsMultiple", group.AllowsMultiple ?? string.Empty);
        session.UpsertFact(subject, "Cli", "MemberCount", members.Length.ToString(CultureInfo.InvariantCulture), "Number");
        session.UpsertFact(subject, "Cli", "Members", string.Join(", ", memberNames));
        return subject;
    }

    private static void AddParameterShapeFacts(
        MetaDocsImportSession session,
        DocumentationSubject subject,
        MetaCliModel? cli,
        Parameter parameter)
    {
        var shape = parameter.ValueShape;
        session.UpsertFact(subject, "Cli", "ValueShapeId", shape.Id);
        session.UpsertFact(subject, "Cli", "ValueShape", shape.Name);
        session.UpsertFact(subject, "Cli", "ValueArity", shape.ValueArity.Name);
        session.UpsertFact(subject, "Cli", "MinValueCount", shape.ValueArity.MinValueCount, "Number");
        session.UpsertFact(subject, "Cli", "MaxValueCount", shape.ValueArity.MaxValueCount ?? string.Empty, "Number");
        session.UpsertFact(subject, "Cli", "AllowsOptionLikeValue", shape.AllowsOptionLikeValue ?? string.Empty);

        if (cli is null)
        {
            return;
        }

        var allowedValues = cli.AllowedValueList
            .Where(value => ReferenceEquals(value.ValueShape, shape))
            .OrderBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        session.UpsertFact(subject, "Cli", "AllowedValues", string.Join(", ", allowedValues.Select(value => value.Value)));
        DocumentationSubject? previousValue = null;
        foreach (var allowedValue in allowedValues)
        {
            previousValue = AddAllowedValue(session, subject, allowedValue, previousValue);
        }
    }

    private static DocumentationSubject AddAllowedValue(
        MetaDocsImportSession session,
        DocumentationSubject parent,
        AllowedValue allowedValue,
        DocumentationSubject? previousValue)
    {
        var subject = session.UpsertSubject(
            $"{parent.Id}:allowed-value:{NormalizeKey(allowedValue.Value)}",
            "CliAllowedValue",
            "MetaCli.AllowedValue",
            allowedValue.Id,
            allowedValue.Value,
            $"{parent.DisplayPath} value {allowedValue.Value}",
            allowedValue.Description ?? string.Empty,
            parent.Id,
            previousValue);
        session.UpsertFact(subject, "Cli", "AllowedValueId", allowedValue.Id);
        session.UpsertFact(subject, "Cli", "Value", allowedValue.Value);
        session.UpsertFact(subject, "Cli", "Description", allowedValue.Description ?? string.Empty);
        session.UpsertFact(subject, "Cli", "ValueShapeId", allowedValue.ValueShape.Id);
        session.UpsertFact(subject, "Cli", "ValueShape", allowedValue.ValueShape.Name);
        return subject;
    }

    private static Application ResolveApplication(MetaCliModel cli, string applicationId)
    {
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            return cli.ApplicationList.FirstOrDefault(application =>
                       string.Equals(application.Id, applicationId, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"MetaCli application '{applicationId}' was not found.");
        }

        return cli.ApplicationList.Count switch
        {
            1 => cli.ApplicationList[0],
            0 => throw new InvalidOperationException("MetaCli workspace contains no applications."),
            _ => throw new InvalidOperationException("MetaCli workspace contains more than one application; provide --application <id>.")
        };
    }

    private static IEnumerable<Option> ApplicationOptions(
        MetaCliModel cli,
        MetaCliCommandSurface surface,
        Application app)
    {
        var parameters = cli.ApplicationParameterList
            .Where(item => ReferenceEquals(item.Application, app))
            .Select(static item => item.Parameter)
            .ToHashSet(ReferenceComparer<Parameter>.Instance);
        return cli.OptionList
            .Where(option => parameters.Contains(option.Parameter))
            .OrderBy(option => surface.OptionTokens(option.Parameter).FirstOrDefault()?.Token ?? option.Parameter.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<Option> CommandOptions(
        MetaCliModel cli,
        MetaCliCommandSurface surface,
        ExecutableCommand executable)
    {
        var parameters = surface.CommandParameters(executable)
            .ToHashSet(ReferenceComparer<Parameter>.Instance);
        return cli.OptionList
            .Where(option => parameters.Contains(option.Parameter))
            .OrderBy(option => surface.OptionTokens(option.Parameter).FirstOrDefault()?.Token ?? option.Parameter.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildUsage(
        string commandPath,
        IReadOnlyList<Option> options,
        IReadOnlyList<PositionalArgument> positionals,
        MetaCliCommandSurface surface)
    {
        var parts = new List<string> { commandPath };
        parts.AddRange(positionals.Select(static positional => $"<{positional.Parameter.Name}>"));
        parts.AddRange(options.Select(option =>
        {
            var token = surface.OptionTokens(option.Parameter).FirstOrDefault()?.Token ?? option.Parameter.Name;
            var syntax = BuildOptionSyntax(token, option.Parameter);
            return IsRequired(option.Parameter) ? syntax : $"[{syntax}]";
        }));
        return string.Join(" ", parts);
    }

    private static string BuildOptionSyntax(string token, Parameter parameter)
    {
        var valueLabel = ValueName(parameter);
        return string.IsNullOrWhiteSpace(valueLabel) ? token : $"{token} {valueLabel}";
    }

    private static string ValueName(Parameter parameter) =>
        MetaCliCommandSurface.ValueName(parameter, $"<{parameter.Name}>");

    private static string ApplicationCommandName(Application app) =>
        FirstNonEmpty(app.ExecutableName, app.Name, app.Id);

    private static bool IsRequired(Parameter parameter) =>
        bool.TryParse(parameter.IsRequired, out var parsed) && parsed;

    private static DocumentationNarrative UpsertNarrativeIfPresent(
        MetaDocsModel model,
        MetaDocsImportSession session,
        DocumentationSubject subject,
        string subjectKey,
        string slot,
        string title,
        string? body,
        string origin,
        DocumentationNarrative previousNarrative)
    {
        var existing = FindNarrative(model, subjectKey, slot)?.Body;
        var resolvedBody = FirstNonEmpty(body, existing);
        if (string.IsNullOrWhiteSpace(resolvedBody))
        {
            RemoveEmptyGeneratedNarratives(model, subjectKey, slot);
            return previousNarrative;
        }

        return session.UpsertNarrative(subject, slot, title, resolvedBody, origin, previousNarrative);
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

    private static string ComputeCliFingerprint(MetaCliModel cli, Application app)
    {
        var surface = new MetaCliCommandSurface(cli, app.Id);
        var builder = new StringBuilder();
        builder.AppendLine(app.Id);
        builder.AppendLine(app.Name);
        builder.AppendLine(app.ExecutableName);
        foreach (var executable in cli.ExecutableCommandList
                     .Where(executable => ReferenceEquals(executable.Command.Application, app))
                     .OrderBy(executable => surface.Route(executable.Command), StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(executable.Id);
            builder.AppendLine(surface.Route(executable.Command));
            builder.AppendLine(executable.Command.Description);
            foreach (var option in ApplicationOptions(cli, surface, app).Concat(CommandOptions(cli, surface, executable)))
            {
                builder.AppendLine("option:" + option.Id);
                builder.AppendLine("parameter:" + option.Parameter.Id + ":" + option.Parameter.Name);
                foreach (var token in surface.OptionTokens(option.Parameter))
                {
                    builder.AppendLine("token:" + token.Id + ":" + token.Token);
                }
            }

            foreach (var positional in surface.OrderedPositionals(executable))
            {
                builder.AppendLine("positional:" + positional.Id + ":" + positional.Parameter.Id + ":" + positional.Parameter.Name);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeKey(string value) => MetaDocsImportSession.NormalizeKey(value);

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class?
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
