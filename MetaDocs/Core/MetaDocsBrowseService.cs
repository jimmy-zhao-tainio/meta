using System.Text;

namespace MetaDocs.Core;

public sealed class MetaDocsBrowseService
{
    public MetaDocsBrowseResult Browse(MetaDocsModel model, string path = "")
    {
        ArgumentNullException.ThrowIfNull(model);

        var segments = NormalizePath(path);
        if (segments.Count == 0)
        {
            return Success(FormatRoot(model));
        }

        return segments[0].ToLowerInvariant() switch
        {
            "cli" => BrowseCli(model, segments),
            "model" or "models" => BrowseModel(model, segments),
            _ => Failure(FormatUnknownRoot(segments[0])),
        };
    }

    private static MetaDocsBrowseResult BrowseCli(MetaDocsModel model, IReadOnlyList<string> segments)
    {
        if (segments.Count == 1)
        {
            return Success(FormatCliIndex(model));
        }

        var appName = segments[1];
        var app = CliApplications(model)
            .FirstOrDefault(subject => NameEquals(subject, appName));
        if (app is null)
        {
            return Failure(FormatUnknownCli(model, appName));
        }

        if (segments.Count == 2)
        {
            return Success(FormatCliApplication(model, app));
        }

        var commandRoute = string.Join(" ", segments.Skip(2));
        var command = CliCommands(model, app)
            .FirstOrDefault(subject => CommandRouteEquals(model, app, subject, commandRoute));
        if (command is null)
        {
            return Failure(FormatUnknownCliCommand(model, app, commandRoute));
        }

        return Success(FormatCliCommand(model, app, command));
    }

    private static MetaDocsBrowseResult BrowseModel(MetaDocsModel model, IReadOnlyList<string> segments)
    {
        if (segments.Count == 1)
        {
            return Success(FormatModelIndex(model));
        }

        var modelName = segments[1];
        var modelSubject = Models(model)
            .FirstOrDefault(subject => NameEquals(subject, modelName));
        if (modelSubject is null)
        {
            return Failure(FormatUnknownModel(model, modelName));
        }

        if (segments.Count == 2)
        {
            return Success(FormatModel(model, modelSubject));
        }

        var entityName = string.Join(" ", segments.Skip(2));
        var entity = EntityChildren(model, modelSubject)
            .FirstOrDefault(subject => NameEquals(subject, entityName));
        if (entity is null)
        {
            return Failure(FormatUnknownEntity(model, modelSubject, entityName));
        }

        return Success(FormatEntity(model, modelSubject, entity));
    }

    private static string FormatRoot(MetaDocsModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MetaDocs");
        builder.AppendLine();
        builder.AppendLine(WorkspaceDescription(model));
        builder.AppendLine();
        builder.AppendLine("Browse:");
        builder.AppendLine("  CLI tools  meta-docs browse cli");
        builder.AppendLine("  Models     meta-docs browse model");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine("  meta-docs search <text>");

        var starts = UsefulStarts(model).ToArray();
        if (starts.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("Useful starts:");
            foreach (var start in starts)
            {
                builder.AppendLine($"  {start}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCliIndex(MetaDocsModel model)
    {
        var applications = CliApplications(model).ToArray();
        var grouped = applications
            .GroupBy(app => FirstNonEmpty(Fact(model, app, "Cli", "GroupName"), "Tools"), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => GroupRank(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("CLI tools");
        builder.AppendLine();
        builder.AppendLine("Command-line tools documented in this workspace.");

        if (applications.Length == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No CLI applications are documented here.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("Applications:");
            foreach (var group in grouped)
            {
                if (grouped.Length > 1)
                {
                    builder.AppendLine($"  {DisplayGroupName(group.Key)}");
                }

                foreach (var app in group.OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    AppendItem(builder, app.DisplayName, Description(model, app), $"meta-docs browse {CliPath(app)}", grouped.Length > 1 ? 2 : 1);
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine("  meta-docs search <text>");
        return builder.ToString().TrimEnd();
    }

    private static string FormatCliApplication(MetaDocsModel model, DocumentationSubject app)
    {
        var commands = CliCommands(model, app).ToArray();
        var builder = new StringBuilder();
        var description = Description(model, app);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description);
        }

        if (commands.Length == 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("No commands are documented for this CLI.");
        }
        else
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Commands:");
            foreach (var command in commands)
            {
                var route = RelativeCommandRoute(model, app, command);
                AppendItem(builder, route, Description(model, command), $"meta-docs browse {CliCommandPath(model, app, command)}", 1);
            }
        }

        AppendSectionBreak(builder);
        builder.AppendLine("Up:");
        builder.AppendLine("  meta-docs browse cli");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {app.DisplayName}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatCliCommand(MetaDocsModel model, DocumentationSubject app, DocumentationSubject command)
    {
        var commandRoute = RelativeCommandRoute(model, app, command);
        var commandLine = $"{app.DisplayName} {commandRoute}".Trim();
        var options = Children(model, command, "CliOption").ToArray();
        var arguments = Children(model, command, "CliArgument").ToArray();
        var usages = Facts(model, command, "Usage")
            .OrderBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .Select(fact => fact.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();

        var description = Description(model, command);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description);
        }

        if (usages.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Usage:");
            foreach (var usage in usages)
            {
                builder.AppendLine($"  {usage}");
            }
        }

        if (arguments.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Arguments:");
            foreach (var argument in arguments)
            {
                AppendParameter(builder, FirstNonEmpty(Fact(model, argument, "Cli", "ValueName"), argument.DisplayName), Description(model, argument));
            }
        }

        if (options.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Options:");
            foreach (var option in options)
            {
                AppendParameter(builder, FirstNonEmpty(Fact(model, option, "Cli", "Syntax"), option.DisplayName), Description(model, option));
            }
        }

        AppendSectionBreak(builder);
        builder.AppendLine("Up:");
        builder.AppendLine($"  meta-docs browse {CliPath(app)}");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {commandRoute}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatModelIndex(MetaDocsModel model)
    {
        var models = Models(model).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("Models");
        builder.AppendLine();
        builder.AppendLine("Workspace models documented here.");

        if (models.Length == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No workspace models are documented here.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("Models:");
            foreach (var subject in models)
            {
                AppendItem(builder, subject.DisplayName, NonPlaceholderDescription(model, subject), $"meta-docs browse {ModelPath(subject)}", 1);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine("  meta-docs search <text>");
        return builder.ToString().TrimEnd();
    }

    private static string FormatModel(MetaDocsModel model, DocumentationSubject modelSubject)
    {
        var entities = EntityChildren(model, modelSubject).ToArray();
        var builder = new StringBuilder();

        var description = NonPlaceholderDescription(model, modelSubject);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description);
        }

        if (entities.Length == 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("No entities are documented for this model.");
        }
        else
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Entities:");
            foreach (var entity in entities)
            {
                AppendItem(builder, entity.DisplayName, NonPlaceholderDescription(model, entity), $"meta-docs browse {EntityPath(modelSubject, entity)}", 1);
            }
        }

        AppendSectionBreak(builder);
        builder.AppendLine("Up:");
        builder.AppendLine("  meta-docs browse model");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {modelSubject.DisplayName}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatEntity(MetaDocsModel model, DocumentationSubject modelSubject, DocumentationSubject entity)
    {
        var properties = Children(model, entity, "Property").ToArray();
        var relationships = Children(model, entity, "Relationship").ToArray();
        var related = RelatedEntities(model, relationships).ToArray();

        var builder = new StringBuilder();

        var description = NonPlaceholderDescription(model, entity);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine(description);
        }

        if (properties.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Properties:");
            foreach (var property in properties)
            {
                AppendParameter(builder, property.DisplayName, Description(model, property));
            }
        }

        if (relationships.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Relationships:");
            foreach (var relationship in relationships)
            {
                var target = Fact(model, relationship, "Model", "TargetEntity");
                var label = string.IsNullOrWhiteSpace(target)
                    ? relationship.DisplayName
                    : $"{relationship.DisplayName} -> {target}";
                AppendParameter(builder, label, Description(model, relationship));
            }
        }

        if (related.Length != 0)
        {
            AppendSectionBreak(builder);
            builder.AppendLine("Related:");
            foreach (var subject in related)
            {
                builder.AppendLine($"  {subject.DisplayName}");
                builder.AppendLine($"    open: meta-docs browse {EntityPath(modelSubject, subject)}");
            }
        }

        AppendSectionBreak(builder);
        builder.AppendLine("Up:");
        builder.AppendLine($"  meta-docs browse {ModelPath(modelSubject)}");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {entity.DisplayName}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUnknownRoot(string segment)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Could not browse '{segment}'.");
        builder.AppendLine();
        builder.AppendLine("Browse from the top:");
        builder.AppendLine("  meta-docs browse");
        builder.AppendLine("  meta-docs browse cli");
        builder.AppendLine("  meta-docs browse model");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUnknownCli(MetaDocsModel model, string cli)
    {
        var suggestions = Closest(CliApplications(model), cli).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Could not find CLI application '{cli}'.");
        if (suggestions.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("Did you mean?");
            foreach (var subject in suggestions)
            {
                builder.AppendLine($"  {subject.DisplayName}");
                builder.AppendLine($"    open: meta-docs browse {CliPath(subject)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Browse all CLI apps:");
        builder.AppendLine("  meta-docs browse cli");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {cli}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUnknownCliCommand(MetaDocsModel model, DocumentationSubject app, string command)
    {
        var commands = CliCommands(model, app).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Could not find command '{command}' under CLI application '{app.DisplayName}'.");

        if (commands.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("Available commands:");
            foreach (var subject in commands)
            {
                var route = RelativeCommandRoute(model, app, subject);
                builder.AppendLine($"  {route}");
                builder.AppendLine($"    open: meta-docs browse {CliCommandPath(model, app, subject)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {command}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUnknownModel(MetaDocsModel model, string modelName)
    {
        var suggestions = Closest(Models(model), modelName).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Could not find model '{modelName}'.");
        if (suggestions.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("Did you mean?");
            foreach (var subject in suggestions)
            {
                builder.AppendLine($"  {subject.DisplayName}");
                builder.AppendLine($"    open: meta-docs browse {ModelPath(subject)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Browse all models:");
        builder.AppendLine("  meta-docs browse model");
        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {modelName}");
        return builder.ToString().TrimEnd();
    }

    private static string FormatUnknownEntity(MetaDocsModel model, DocumentationSubject modelSubject, string entityName)
    {
        var entities = EntityChildren(model, modelSubject).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Could not find entity '{entityName}' in model '{modelSubject.DisplayName}'.");
        if (entities.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("Available entities:");
            foreach (var subject in entities.Take(25))
            {
                builder.AppendLine($"  {subject.DisplayName}");
                builder.AppendLine($"    open: meta-docs browse {EntityPath(modelSubject, subject)}");
            }

            if (entities.Length > 25)
            {
                builder.AppendLine("  ...");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Search:");
        builder.AppendLine($"  meta-docs search {entityName}");
        return builder.ToString().TrimEnd();
    }

    private static void AppendItem(StringBuilder builder, string title, string description, string openCommand, int indent)
    {
        var prefix = new string(' ', indent * 2);
        builder.AppendLine($"{prefix}{title}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine($"{prefix}  {description}");
        }

        builder.AppendLine($"{prefix}  open: {openCommand}");
    }

    private static void AppendParameter(StringBuilder builder, string label, string description)
    {
        builder.AppendLine($"  {label}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine($"    {description}");
        }
    }

    private static void AppendSectionBreak(StringBuilder builder)
    {
        if (builder.Length != 0)
        {
            builder.AppendLine();
        }
    }

    private static IReadOnlyList<string> NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path.Trim(), "/", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return path
            .Replace('\\', '/')
            .Trim()
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<DocumentationSubject> CliApplications(MetaDocsModel model) =>
        CurrentSubjects(model)
            .Where(static subject => string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<DocumentationSubject> CliCommands(MetaDocsModel model, DocumentationSubject app) =>
        Children(model, app, "CliCommand");

    private static IEnumerable<DocumentationSubject> Models(MetaDocsModel model) =>
        CurrentSubjects(model)
            .Where(static subject => string.Equals(subject.Kind, "Model", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<DocumentationSubject> EntityChildren(MetaDocsModel model, DocumentationSubject modelSubject) =>
        Children(model, modelSubject, "Entity");

    private static IEnumerable<DocumentationSubject> Children(MetaDocsModel model, DocumentationSubject parent, string kind)
    {
        var children = CurrentSubjects(model)
            .Where(subject => string.Equals(subject.ParentKey ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase))
            .Where(subject => string.Equals(subject.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return MetaDocsOrdering.ByPrevious(
            children,
            static subject => subject.PreviousSubject,
            static subject => FirstNonEmpty(subject.DisplayPath, subject.DisplayName, subject.Id));
    }

    private static IEnumerable<DocumentationSubject> RelatedEntities(MetaDocsModel model, IEnumerable<DocumentationSubject> relationships)
    {
        var subjectsByKey = CurrentSubjects(model)
            .GroupBy(static subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relationships)
        {
            foreach (var reference in model.DocumentationRelationshipList
                         .Where(IsCurrent)
                         .Where(row => string.Equals(row.FromSubjectKey, relationship.Id, StringComparison.OrdinalIgnoreCase))
                         .Where(row => string.Equals(row.Kind, "ReferencesEntity", StringComparison.OrdinalIgnoreCase)))
            {
                if (subjectsByKey.TryGetValue(reference.ToSubjectKey, out var target) &&
                    seen.Add(target.Id))
                {
                    yield return target;
                }
            }
        }
    }

    private static IEnumerable<DocumentationSubject> Closest(IEnumerable<DocumentationSubject> subjects, string value) =>
        subjects
            .Select(subject => (Subject: subject, Score: SuggestionScore(subject.DisplayName, value)))
            .Where(item => item.Score < 100)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Subject.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(item => item.Subject);

    private static int SuggestionScore(string candidate, string value)
    {
        if (candidate.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (candidate.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var distance = Distance(candidate.ToLowerInvariant(), value.ToLowerInvariant());
        return distance <= 3 ? 10 + distance : 100;
    }

    private static int Distance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var index = 0; index <= right.Length; index++)
        {
            costs[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var previous = costs[0];
            costs[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var current = costs[rightIndex];
                costs[rightIndex] = Math.Min(
                    Math.Min(costs[rightIndex] + 1, costs[rightIndex - 1] + 1),
                    previous + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1));
                previous = current;
            }
        }

        return costs[right.Length];
    }

    private static string WorkspaceDescription(MetaDocsModel model)
    {
        var title = model.DocumentationViewList
            .Select(static view => FirstNonEmpty(view.Title, view.Name))
            .FirstOrDefault(static value => value.Contains("meta", StringComparison.OrdinalIgnoreCase) && value.Contains("meta-bi", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(title)
            ? "This workspace documents CLI tools and workspace models."
            : "This workspace documents the meta and meta-bi suite.";
    }

    private static IEnumerable<string> UsefulStarts(MetaDocsModel model)
    {
        foreach (var appName in new[] { "meta-docs", "meta-sql", "meta-mesh" })
        {
            var app = CliApplications(model).FirstOrDefault(subject => string.Equals(subject.DisplayName, appName, StringComparison.OrdinalIgnoreCase));
            if (app is not null)
            {
                yield return $"meta-docs browse {CliPath(app)}";
            }
        }

        foreach (var modelName in new[] { "MetaDocs", "MetaSql" })
        {
            var subject = Models(model).FirstOrDefault(model => string.Equals(model.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
            if (subject is not null)
            {
                yield return $"meta-docs browse {ModelPath(subject)}";
            }
        }
    }

    private static bool NameEquals(DocumentationSubject subject, string name)
    {
        var normalized = name.Trim();
        return string.Equals(subject.DisplayName, normalized, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.NativeId, normalized, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.Key, normalized, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.Id, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CommandRouteEquals(MetaDocsModel model, DocumentationSubject app, DocumentationSubject command, string route)
    {
        var normalized = route.Trim();
        var relative = RelativeCommandRoute(model, app, command);
        return string.Equals(relative, normalized, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(command.DisplayName, $"{app.DisplayName} {normalized}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Fact(model, command, "Cli", "Name"), normalized, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Fact(model, command, "Cli", "CommandPath"), $"{app.DisplayName} {normalized}", StringComparison.OrdinalIgnoreCase);
    }

    private static string RelativeCommandRoute(MetaDocsModel model, DocumentationSubject app, DocumentationSubject command)
    {
        var name = FirstNonEmpty(Fact(model, command, "Cli", "Name"), command.DisplayName);
        var prefix = app.DisplayName + " ";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..]
            : name;
    }

    private static string Description(MetaDocsModel model, DocumentationSubject subject) =>
        FirstNonEmpty(
            model.DocumentationNarrativeList
                .Where(IsCurrent)
                .Where(narrative => string.Equals(narrative.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase))
                .Where(narrative => string.Equals(narrative.Slot, "Summary", StringComparison.OrdinalIgnoreCase))
                .Select(static narrative => narrative.Body)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)),
            subject.Summary);

    private static string NonPlaceholderDescription(MetaDocsModel model, DocumentationSubject subject)
    {
        var description = Description(model, subject).Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        if (string.Equals(description, $"Model {subject.DisplayName}.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(description, $"Entity {subject.DisplayName}.", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return description;
    }

    private static IReadOnlyList<DocumentationFact> Facts(MetaDocsModel model, DocumentationSubject subject, string kind) =>
        model.DocumentationFactList
            .Where(IsCurrent)
            .Where(fact => string.Equals(fact.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase))
            .Where(fact => string.Equals(fact.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string Fact(MetaDocsModel model, DocumentationSubject subject, string kind, string name) =>
        Facts(model, subject, kind)
            .Where(fact => string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(static fact => fact.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string CliPath(DocumentationSubject app) =>
        "cli/" + app.DisplayName;

    private static string CliCommandPath(MetaDocsModel model, DocumentationSubject app, DocumentationSubject command) =>
        CliPath(app) + "/" + RelativeCommandRoute(model, app, command).Replace(' ', '/');

    private static string ModelPath(DocumentationSubject model) =>
        "model/" + model.DisplayName;

    private static string EntityPath(DocumentationSubject model, DocumentationSubject entity) =>
        ModelPath(model) + "/" + entity.DisplayName;

    private static int GroupRank(string group) =>
        group.ToLowerInvariant() switch
        {
            "meta" => 0,
            "meta-bi" => 1,
            _ => 2,
        };

    private static string DisplayGroupName(string group) =>
        string.Equals(group, "meta-bi", StringComparison.OrdinalIgnoreCase)
            ? "Meta-BI"
            : string.Equals(group, "meta", StringComparison.OrdinalIgnoreCase)
                ? "Meta"
                : group;

    private static IEnumerable<DocumentationSubject> CurrentSubjects(MetaDocsModel model) =>
        model.DocumentationSubjectList.Where(IsCurrent);

    private static bool IsCurrent(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrent(DocumentationNarrative narrative) =>
        !string.Equals(narrative.ReviewStatus, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(narrative.ReviewStatus, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(narrative.ReviewStatus, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrent(DocumentationFact fact) =>
        !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fact.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fact.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrent(DocumentationRelationship relationship) =>
        IsVisibleStatus(relationship.DocumentationSource?.Status) &&
        IsVisibleStatus(relationship.DocumentationImportBatch?.Status);

    private static bool IsVisibleStatus(string? status) =>
        !string.Equals(status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static MetaDocsBrowseResult Success(string text) =>
        new(true, text);

    private static MetaDocsBrowseResult Failure(string text) =>
        new(false, text);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed record MetaDocsBrowseResult(bool Succeeded, string Text);
