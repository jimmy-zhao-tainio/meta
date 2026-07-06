using System.Text;

namespace MetaDocs.Core;

public sealed class MetaDocsQueryService
{
    public DocumentationSubject ResolveSubject(MetaDocsModel model, MetaDocsSubjectSelector selector)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selector);

        if (!string.IsNullOrWhiteSpace(selector.Subject))
        {
            return ResolveBySubjectKey(model, selector.Subject);
        }

        if (!string.IsNullOrWhiteSpace(selector.Model))
        {
            return ResolveModel(model, selector.Model);
        }

        if (string.IsNullOrWhiteSpace(selector.Cli))
        {
            throw new InvalidOperationException("Provide --subject <id>, --model <name>, or --cli <name>.");
        }

        var application = ResolveCliApplication(model, selector.Cli);
        if (string.IsNullOrWhiteSpace(selector.Command))
        {
            return string.IsNullOrWhiteSpace(selector.Option)
                ? application
                : ResolveCliOption(model, application, selector.Option);
        }

        var command = ResolveCliCommand(model, application, selector.Command);
        if (string.IsNullOrWhiteSpace(selector.Option))
        {
            return command;
        }

        return ResolveCliOption(model, command, selector.Option);
    }

    public IReadOnlyList<MetaDocsSearchMatch> Search(
        MetaDocsModel model,
        string query,
        string kind = "",
        int limit = 25)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var normalizedLimit = limit <= 0 ? 25 : limit;
        var matches = new List<MetaDocsSearchMatch>();
        var matchedSubjectCount = 0;
        var narrativesBySubject = model.DocumentationNarrativeList
            .Where(IsCurrent)
            .Where(static narrative => !string.IsNullOrWhiteSpace(narrative.SubjectKey))
            .GroupBy(static narrative => narrative.SubjectKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var factsBySubject = model.DocumentationFactList
            .Where(IsCurrent)
            .Where(static fact => !string.IsNullOrWhiteSpace(fact.SubjectKey))
            .GroupBy(static fact => fact.SubjectKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static fact => fact.Kind, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static fact => fact.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var subject in model.DocumentationSubjectList
                     .Where(IsCurrent)
                     .Where(subject => string.IsNullOrWhiteSpace(kind) || string.Equals(subject.Kind, kind, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(subject => SearchRank(subject, query))
                     .ThenBy(subject => subject.DisplayPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var countBeforeSubject = matches.Count;
            AddSubjectMatches(matches, subject, query);
            if (narrativesBySubject.TryGetValue(subject.Id, out var subjectNarratives))
            {
                foreach (var narrative in OrderedNarratives(subjectNarratives))
                {
                    AddIfContains(
                        matches,
                        subject,
                        "narrative",
                        $"{narrative.Slot}: {FirstNonEmpty(narrative.Title, narrative.Body)}",
                        query,
                        narrative.Body);
                }
            }

            if (factsBySubject.TryGetValue(subject.Id, out var subjectFacts))
            {
                foreach (var fact in subjectFacts)
                {
                    AddIfContains(
                        matches,
                        subject,
                        "fact",
                        $"{fact.Kind}.{fact.Name}: {fact.Value}",
                        query,
                        fact.Value);
                }
            }

            if (matches.Count > countBeforeSubject)
            {
                matchedSubjectCount++;
                if (matchedSubjectCount >= normalizedLimit)
                {
                    break;
                }
            }
        }

        return matches
            .GroupBy(match => match.Subject.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(normalizedLimit)
            .ToArray();
    }

    public DocumentationNarrative UpsertDescription(
        MetaDocsModel model,
        MetaDocsSubjectSelector selector,
        string slot,
        string title,
        string body,
        string bodyFormat = "PlainText")
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var subject = ResolveSubject(model, selector);
        var normalizedSlot = slot.Trim();
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? normalizedSlot : title.Trim();
        var id = $"{subject.Id}:narrative:{MetaDocsImportSession.NormalizeKey(normalizedSlot)}:{MetaDocsImportSession.NormalizeKey(normalizedTitle)}";

        var narrative = model.DocumentationNarrativeList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (narrative is null)
        {
            narrative = new DocumentationNarrative
            {
                Id = id,
                PreviousNarrative = OrderedNarratives(model, subject).LastOrDefault(),
            };
            model.DocumentationNarrativeList.Add(narrative);
        }

        narrative.DocumentationSubject = subject;
        narrative.SubjectKey = subject.Id;
        narrative.Slot = normalizedSlot;
        narrative.Title = normalizedTitle;
        narrative.Body = body.Trim();
        narrative.BodyFormat = string.IsNullOrWhiteSpace(bodyFormat) ? "PlainText" : bodyFormat.Trim();
        narrative.Origin = "Authored";
        narrative.LastReviewedImportBatchId = string.Empty;
        narrative.ReviewStatus = "Current";
        return narrative;
    }

    public static string FormatSearchResults(string query, IReadOnlyList<MetaDocsSearchMatch> matches)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Search \"{query}\"");
        builder.AppendLine($"{matches.Count} matches");
        foreach (var match in matches)
        {
            builder.AppendLine();
            builder.AppendLine(match.Subject.DisplayName);
            builder.AppendLine($"  {DisplayKind(match.Subject.Kind)}");
            var browseCommand = BuildBrowseCommand(match.Subject);
            if (!string.IsNullOrWhiteSpace(browseCommand))
            {
                builder.AppendLine($"  open: {browseCommand}");
            }

            if (!string.Equals(match.MatchKind, "subject", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(match.Snippet) &&
                !match.Snippet.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"  {DisplayMatchKind(match.MatchKind)} {match.Snippet}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static DocumentationSubject ResolveBySubjectKey(MetaDocsModel model, string subject)
    {
        var key = subject.Trim();
        var direct = CurrentSubjects(model).FirstOrDefault(row =>
            string.Equals(row.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct;
        }

        var alias = model.DocumentationSubjectAliasList.FirstOrDefault(row =>
            string.Equals(row.AliasKey, key, StringComparison.OrdinalIgnoreCase));
        if (alias?.DocumentationSubject is not null && IsCurrent(alias.DocumentationSubject))
        {
            return alias.DocumentationSubject;
        }

        throw new InvalidOperationException($"Could not resolve documentation subject '{subject}'.");
    }

    private static DocumentationSubject ResolveCliApplication(MetaDocsModel model, string cli)
    {
        var matches = CurrentSubjects(model)
            .Where(subject => string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase))
            .Where(subject => MatchesSubjectName(subject, cli))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve CLI '{cli}'."),
            _ => throw new InvalidOperationException($"CLI selector '{cli}' matched more than one subject."),
        };
    }

    private static DocumentationSubject ResolveModel(MetaDocsModel docs, string model)
    {
        var matches = CurrentSubjects(docs)
            .Where(subject => string.Equals(subject.Kind, "Model", StringComparison.OrdinalIgnoreCase))
            .Where(subject => MatchesSubjectName(subject, model))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve model '{model}'."),
            _ => throw new InvalidOperationException($"Model selector '{model}' matched more than one subject."),
        };
    }

    private static DocumentationSubject ResolveCliCommand(
        MetaDocsModel model,
        DocumentationSubject application,
        string command)
    {
        var appCommands = CommandDescendants(model, application).ToArray();
        var matches = appCommands
            .Where(subject => MatchesSubjectName(subject, command) || MatchesCommandPath(model, subject, application, command))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve command '{command}' for CLI '{application.DisplayName}'."),
            _ => throw new InvalidOperationException($"Command selector '{command}' matched more than one command for CLI '{application.DisplayName}'."),
        };
    }

    private static DocumentationSubject ResolveCliOption(
        MetaDocsModel model,
        DocumentationSubject command,
        string option)
    {
        var matches = CurrentSubjects(model)
            .Where(subject => string.Equals(subject.ParentKey ?? string.Empty, command.Id, StringComparison.OrdinalIgnoreCase))
            .Where(subject => string.Equals(subject.Kind, "CliOption", StringComparison.OrdinalIgnoreCase))
            .Where(subject => MatchesSubjectName(subject, option) || string.Equals(FindFact(model, subject, "Cli", "Name"), option, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve option '{option}' for command '{command.DisplayName}'."),
            _ => throw new InvalidOperationException($"Option selector '{option}' matched more than one option for command '{command.DisplayName}'."),
        };
    }

    private static IEnumerable<DocumentationSubject> CommandDescendants(MetaDocsModel model, DocumentationSubject root)
    {
        var childrenByParent = CurrentSubjects(model)
            .Where(subject => !string.IsNullOrWhiteSpace(subject.ParentKey))
            .GroupBy(subject => subject.ParentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var command in Descend(root.Id))
        {
            yield return command;
        }

        IEnumerable<DocumentationSubject> Descend(string parentKey)
        {
            if (!childrenByParent.TryGetValue(parentKey, out var children))
            {
                yield break;
            }

            foreach (var command in MetaDocsOrdering.ByPrevious(
                         children.Where(subject => string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase)),
                         static subject => subject.PreviousSubject,
                         static subject => subject.DisplayName))
            {
                yield return command;
                foreach (var child in Descend(command.Id))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<DocumentationSubject> CurrentSubjects(MetaDocsModel model) =>
        model.DocumentationSubjectList.Where(IsCurrent);

    private static bool MatchesSubjectName(DocumentationSubject subject, string value)
    {
        var trimmed = value.Trim();
        return string.Equals(subject.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.Key, trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.NativeId, trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(subject.DisplayPath, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCommandPath(MetaDocsModel model, DocumentationSubject subject, DocumentationSubject application, string value)
    {
        var trimmed = value.Trim();
        if (string.Equals(subject.DisplayName, $"{application.DisplayName} {trimmed}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var commandPath = FindFact(model, subject, "Cli", "CommandPath");
        return string.Equals(commandPath, $"{application.DisplayName} {trimmed}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(commandPath, trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindFact(MetaDocsModel model, DocumentationSubject subject, string kind, string name) =>
        model.DocumentationFactList
            .Where(IsCurrent)
            .Where(fact =>
                string.Equals(fact.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fact.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyList<DocumentationNarrative> OrderedNarratives(MetaDocsModel model, DocumentationSubject subject) =>
        OrderedNarratives(model.DocumentationNarrativeList
            .Where(IsCurrent)
            .Where(narrative => string.Equals(narrative.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<DocumentationNarrative> OrderedNarratives(IEnumerable<DocumentationNarrative> narratives) =>
        MetaDocsOrdering.ByPrevious(
                narratives,
                static narrative => narrative.PreviousNarrative,
                static narrative => $"{narrative.Slot}:{narrative.Title}:{narrative.Id}")
            .ToArray();

    private static void AddSubjectMatches(List<MetaDocsSearchMatch> matches, DocumentationSubject subject, string query)
    {
        AddIfContains(matches, subject, "subject", subject.DisplayName, query);
        AddIfContains(matches, subject, "subject", subject.DisplayPath, query);
        AddIfContains(matches, subject, "summary", subject.Summary, query);
        AddIfContains(matches, subject, "subject", subject.Kind, query);
        AddIfContains(matches, subject, "subject", subject.NativeId, query);
        AddIfContains(matches, subject, "subject", subject.Id, query);
    }

    private static int SearchRank(DocumentationSubject subject, string query)
    {
        var trimmed = query.Trim();
        var textRank = 3;
        if (EqualsText(subject.DisplayName, trimmed) ||
            EqualsText(subject.DisplayPath, trimmed) ||
            EqualsText(subject.NativeId, trimmed) ||
            EqualsText(subject.Key, trimmed))
        {
            textRank = 0;
        }
        else if (StartsWithText(subject.DisplayName, trimmed) ||
                 StartsWithText(subject.DisplayPath, trimmed) ||
                 StartsWithText(subject.NativeId, trimmed))
        {
            textRank = 1;
        }
        else if (ContainsText(subject.DisplayName, trimmed) ||
                 ContainsText(subject.DisplayPath, trimmed) ||
                 ContainsText(subject.NativeId, trimmed))
        {
            textRank = 2;
        }

        return SubjectKindRank(subject.Kind) + textRank;
    }

    private static int SubjectKindRank(string? kind) =>
        kind?.Trim() switch
        {
            "CliApplication" => 0,
            "CliCommand" => 0,
            "CliOption" => 10,
            "Model" => 20,
            "Entity" => 20,
            "CliArgument" => 30,
            "CliParameterGroup" => 30,
            "CliAllowedValue" => 30,
            "Property" => 50,
            "Relationship" => 50,
            _ => 70,
        };

    private static bool EqualsText(string? value, string query) =>
        string.Equals(value?.Trim(), query, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithText(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().StartsWith(query, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void AddIfContains(
        List<MetaDocsSearchMatch> matches,
        DocumentationSubject subject,
        string matchKind,
        string? value,
        string query,
        string? snippetSource = null)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        matches.Add(new MetaDocsSearchMatch(subject, matchKind, Snippet(snippetSource ?? value, query)));
    }

    private static string Snippet(string value, string query)
    {
        var text = NormalizeWhitespace(value);
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || text.Length <= 140)
        {
            return text;
        }

        var start = Math.Max(0, index - 45);
        var length = Math.Min(text.Length - start, 140);
        var snippet = text.Substring(start, length);
        if (start > 0)
        {
            snippet = "..." + snippet;
        }

        if (start + length < text.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(" ", value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').Select(static line => line.Trim()).Where(static line => line.Length > 0));

    private static string DisplayKind(string? kind) =>
        kind?.Trim() switch
        {
            "CliApplication" => "CLI application",
            "CliCommand" => "CLI command",
            "CliOption" => "CLI option",
            null or "" => "Documentation subject",
            var value when string.Equals(value, "CliApplication", StringComparison.OrdinalIgnoreCase) => "CLI application",
            var value when string.Equals(value, "CliCommand", StringComparison.OrdinalIgnoreCase) => "CLI command",
            var value when string.Equals(value, "CliOption", StringComparison.OrdinalIgnoreCase) => "CLI option",
            var value => SplitPascalCase(value),
        };

    private static string SplitPascalCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string DisplayMatchKind(string matchKind) =>
        matchKind switch
        {
            "subject" => "matched",
            "summary" => "summary",
            "fact" => "detail",
            "narrative" => "description",
            _ => matchKind,
        };

    private static string BuildBrowseCommand(DocumentationSubject subject)
    {
        if (string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase))
        {
            return $"meta-docs browse cli/{subject.DisplayName}";
        }

        if (string.Equals(subject.Kind, "Model", StringComparison.OrdinalIgnoreCase))
        {
            return $"meta-docs browse model/{subject.DisplayName}";
        }

        if (string.Equals(subject.Kind, "Entity", StringComparison.OrdinalIgnoreCase) &&
            TrySplitModelEntityPath(subject, out var model, out var entity))
        {
            return $"meta-docs browse model/{model}/{entity}";
        }

        if (string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase) &&
            TrySplitCliPath(subject.DisplayName, out var cli, out var command))
        {
            return $"meta-docs browse cli/{cli}/{command.Replace(' ', '/')}";
        }

        if (string.Equals(subject.Kind, "CliOption", StringComparison.OrdinalIgnoreCase) &&
            TrySplitOptionPath(subject, out cli, out command, out _))
        {
            return string.IsNullOrWhiteSpace(command)
                ? $"meta-docs browse cli/{cli}"
                : $"meta-docs browse cli/{cli}/{command.Replace(' ', '/')}";
        }

        return string.Empty;
    }

    private static bool TrySplitOptionPath(DocumentationSubject subject, out string cli, out string command, out string option)
    {
        var path = FirstNonEmpty(subject.DisplayPath, subject.DisplayName).Trim();
        var lastSpace = path.LastIndexOf(' ');
        if (lastSpace <= 0 || lastSpace >= path.Length - 1)
        {
            cli = string.Empty;
            command = string.Empty;
            option = string.Empty;
            return false;
        }

        option = path[(lastSpace + 1)..];
        var ownerPath = path[..lastSpace];
        if (TrySplitCliPath(ownerPath, out cli, out command))
        {
            return true;
        }

        cli = ownerPath;
        command = string.Empty;
        return !string.IsNullOrWhiteSpace(cli);
    }

    private static bool TrySplitCliPath(string displayName, out string cli, out string command)
    {
        var trimmed = displayName.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0 || firstSpace >= trimmed.Length - 1)
        {
            cli = string.Empty;
            command = string.Empty;
            return false;
        }

        cli = trimmed[..firstSpace];
        command = trimmed[(firstSpace + 1)..];
        return true;
    }

    private static bool TrySplitModelEntityPath(DocumentationSubject subject, out string model, out string entity)
    {
        var path = FirstNonEmpty(subject.DisplayPath, subject.DisplayName).Trim();
        var dot = path.IndexOf('.');
        if (dot <= 0 || dot >= path.Length - 1)
        {
            model = string.Empty;
            entity = string.Empty;
            return false;
        }

        model = path[..dot];
        entity = path[(dot + 1)..];
        return !string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(entity);
    }

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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed record MetaDocsSubjectSelector(
    string Subject = "",
    string Model = "",
    string Cli = "",
    string Command = "",
    string Option = "");

public sealed record MetaDocsSearchMatch(
    DocumentationSubject Subject,
    string MatchKind,
    string Snippet);
