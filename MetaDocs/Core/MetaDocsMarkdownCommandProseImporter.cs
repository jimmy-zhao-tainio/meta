using System.Security.Cryptography;
using System.Text;
using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsMarkdownCommandProseImporter
{
    private static readonly string[] CommandNarrativeLabels =
    [
        "Purpose",
        "Behavior summary",
        "Notes",
        "Caveats",
        "Troubleshooting",
    ];

    public async Task<MetaDocsMarkdownCommandProseImportResult> ImportCommandProseAsync(
        MetaDocsModel model,
        string sourceRoot,
        string sourceId = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);

        var files = ResolveMarkdownFiles(sourceRoot);
        var documents = new List<MarkdownDocument>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            documents.Add(MarkdownDocument.Parse(file, text));
        }

        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? $"source:markdown-command-prose:{MetaDocsImportSession.NormalizeKey(Path.GetFileNameWithoutExtension(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}"
            : sourceId;
        var batch = UpsertSource(model, normalizedSourceId, sourceRoot, ComputeFingerprint(documents));

        var matchedApplications = 0;
        var matchedCommands = 0;
        var importedNarratives = 0;

        foreach (var application in CurrentSubjects(model, "CliApplication"))
        {
            var appSection = FindBestSection(documents, application);
            if (appSection is null)
            {
                continue;
            }

            matchedApplications++;
            var importedApplicationNarrative = UpsertNarrative(
                model,
                batch,
                application,
                "Summary",
                "Summary",
                BuildApplicationSummary(appSection),
                null);
            importedNarratives += importedApplicationNarrative is null ? 0 : 1;

            var commands = CurrentCliCommands(model, application).ToArray();
            foreach (var command in commands)
            {
                var commandSection = FindBestCommandSection(documents, appSection, application, command, commands.Length);
                if (commandSection is null)
                {
                    continue;
                }

                matchedCommands++;
                var previousCommandNarrative = LastNarrative(model, command);
                var importedCommandNarrative = UpsertNarrative(
                    model,
                    batch,
                    command,
                    "Guidance",
                    "Guidance",
                    BuildCommandGuidance(commandSection),
                    previousCommandNarrative);
                importedNarratives += importedCommandNarrative is null ? 0 : 1;
                previousCommandNarrative = importedCommandNarrative ?? previousCommandNarrative;

                importedCommandNarrative = UpsertNarrative(
                    model,
                    batch,
                    command,
                    "Example",
                    "Examples",
                    BuildExamples(commandSection),
                    previousCommandNarrative);
                importedNarratives += importedCommandNarrative is null ? 0 : 1;
                previousCommandNarrative = importedCommandNarrative ?? previousCommandNarrative;

                importedCommandNarrative = UpsertNarrative(
                    model,
                    batch,
                    command,
                    "Reference",
                    "See also",
                    BuildLabeledNarrative(commandSection, "See also"),
                    previousCommandNarrative);
                importedNarratives += importedCommandNarrative is null ? 0 : 1;

                foreach (var option in CurrentChildren(model, command, "CliOption"))
                {
                    var optionName = FirstNonEmpty(
                        FindFact(model, option, "Cli", "Name"),
                        option.NativeId,
                        option.DisplayName);
                    var importedOptionNarrative = UpsertNarrative(
                        model,
                        batch,
                        option,
                        "Usage",
                        "Usage",
                        BuildOptionNarrative(commandSection, optionName),
                        LastNarrative(model, option));
                    importedNarratives += importedOptionNarrative is null ? 0 : 1;
                }
            }
        }

        return new MetaDocsMarkdownCommandProseImportResult(
            documents.Count,
            matchedApplications,
            matchedCommands,
            importedNarratives);
    }

    private static DocumentationImportBatch UpsertSource(
        MetaDocsModel model,
        string sourceId,
        string sourceRoot,
        string fingerprint)
    {
        MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(model);
        MetaDocsDefaults.EnsureDefaultView(model);

        var importedAt = DateTimeOffset.UtcNow.ToString("O");
        var source = model.DocumentationSourceList.FirstOrDefault(row =>
            string.Equals(row.Id, sourceId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationSource
            {
                Id = sourceId,
                DocumentationWorkspace = model.DocumentationWorkspaceList.First(),
            };
        if (!model.DocumentationSourceList.Contains(source))
        {
            model.DocumentationSourceList.Add(source);
        }

        source.Kind = "MarkdownCommandProse";
        source.DisplayName = "Markdown command prose";
        source.Locator = Path.GetFullPath(sourceRoot);
        source.SourceFingerprint = fingerprint;
        source.ImporterId = "MetaDocs.MarkdownCommandProse";
        source.ImportedAt = importedAt;
        source.Status = "Current";

        var batch = new DocumentationImportBatch
        {
            Id = $"{source.Id}:batch:{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DocumentationSource = source,
            ImporterId = "MetaDocs.MarkdownCommandProse",
            ImporterVersion = "1",
            SourceFingerprint = fingerprint,
            ImportedAt = importedAt,
            Status = "Current",
        };
        model.DocumentationImportBatchList.Add(batch);
        return batch;
    }

    private static DocumentationNarrative? UpsertNarrative(
        MetaDocsModel model,
        DocumentationImportBatch batch,
        DocumentationSubject subject,
        string slot,
        string title,
        string body,
        DocumentationNarrative? previousNarrative)
    {
        if (model.DocumentationNarrativeList.Any(row =>
                string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Slot, slot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Origin, "Authored", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.Body)))
        {
            RemoveImportedNarratives(model, subject.Id, slot);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            RemoveImportedNarratives(model, subject.Id, slot);
            return null;
        }

        var id = $"{subject.Id}:narrative:{MetaDocsImportSession.NormalizeKey(slot)}:{MetaDocsImportSession.NormalizeKey(title)}";
        var narrative = model.DocumentationNarrativeList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (narrative is null)
        {
            narrative = new DocumentationNarrative
            {
                Id = id,
            };
            model.DocumentationNarrativeList.Add(narrative);
        }
        else if (string.Equals(narrative.Origin, "Authored", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(narrative.Body))
        {
            return null;
        }

        narrative.DocumentationSubject = subject;
        narrative.SubjectKey = subject.Id;
        narrative.Slot = slot;
        narrative.Title = title;
        narrative.Body = body;
        narrative.BodyFormat = "MarkdownSubset";
        narrative.Origin = "ImportedMarkdown";
        narrative.LastReviewedImportBatchId = batch.Id;
        narrative.ReviewStatus = "Current";
        narrative.PreviousNarrative = previousNarrative;
        return narrative;
    }

    private static void RemoveImportedNarratives(MetaDocsModel model, string subjectKey, string slot)
    {
        for (var index = model.DocumentationNarrativeList.Count - 1; index >= 0; index--)
        {
            var narrative = model.DocumentationNarrativeList[index];
            if (string.Equals(narrative.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(narrative.Slot, slot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(narrative.Origin, "ImportedMarkdown", StringComparison.OrdinalIgnoreCase))
            {
                model.DocumentationNarrativeList.RemoveAt(index);
            }
        }
    }

    private static string BuildApplicationSummary(MarkdownSection section)
    {
        var builder = new StringBuilder();
        AppendIfPresent(builder, section.IntroBody());
        AppendIfPresent(builder, BuildLabeledNarrative(section, "Purpose"));
        return NormalizeBody(builder.ToString());
    }

    private static string BuildCommandGuidance(MarkdownSection section)
    {
        var builder = new StringBuilder();
        foreach (var label in CommandNarrativeLabels)
        {
            AppendIfPresent(builder, BuildLabeledNarrative(section, label));
        }

        return NormalizeBody(builder.ToString());
    }

    private static string BuildExamples(MarkdownSection section)
    {
        var examples = BuildLabeledNarrative(section, "Examples");
        if (!string.IsNullOrWhiteSpace(examples))
        {
            return examples;
        }

        var fences = section.ContentLines
            .Where(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            .Take(1)
            .Any()
            ? section.ContentBody()
            : string.Empty;
        return NormalizeBody(fences);
    }

    private static string BuildOptionNarrative(MarkdownSection section, string optionName)
    {
        if (string.IsNullOrWhiteSpace(optionName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var label in CommandNarrativeLabels.Concat(["Options"]))
        {
            foreach (var line in section.LabeledBlockLines(label))
            {
                if (line.Contains(optionName, StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine(line);
                }
            }
        }

        return NormalizeBody(builder.ToString());
    }

    private static string BuildLabeledNarrative(MarkdownSection section, string label) =>
        NormalizeBody(string.Join(Environment.NewLine, section.LabeledBlockLines(label)));

    private static IEnumerable<DocumentationSubject> CurrentSubjects(MetaDocsModel model, string kind) =>
        MetaDocsOrdering.ByPrevious(
            model.DocumentationSubjectList
                .Where(subject => string.Equals(subject.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Where(IsCurrent),
            static subject => subject.PreviousSubject,
            static subject => subject.DisplayName);

    private static IEnumerable<DocumentationSubject> CurrentChildren(
        MetaDocsModel model,
        DocumentationSubject parent,
        string kind) =>
        MetaDocsOrdering.ByPrevious(
            model.DocumentationSubjectList
                .Where(subject => string.Equals(subject.ParentKey ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase))
                .Where(subject => string.Equals(subject.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Where(IsCurrent),
            static subject => subject.PreviousSubject,
            static subject => subject.DisplayName);

    private static IEnumerable<DocumentationSubject> CurrentCliCommands(
        MetaDocsModel model,
        DocumentationSubject application)
    {
        var childrenByParent = model.DocumentationSubjectList
            .Where(IsCurrent)
            .Where(subject => !string.IsNullOrWhiteSpace(subject.ParentKey))
            .GroupBy(subject => subject.ParentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var commands = new List<DocumentationSubject>();
        AddCommandDescendants(application.Id);
        return commands;

        void AddCommandDescendants(string parentKey)
        {
            if (!childrenByParent.TryGetValue(parentKey, out var children))
            {
                return;
            }

            foreach (var command in MetaDocsOrdering.ByPrevious(
                         children.Where(subject => string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase)),
                         static subject => subject.PreviousSubject,
                         static subject => subject.DisplayName))
            {
                commands.Add(command);
                AddCommandDescendants(command.Id);
            }
        }
    }

    private static DocumentationNarrative? LastNarrative(MetaDocsModel model, DocumentationSubject subject) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationNarrativeList
                    .Where(row => string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase)),
                static row => row.PreviousNarrative,
                static row => $"{row.Slot}:{row.Title}:{row.Id}")
            .LastOrDefault();

    private static MarkdownSection? FindBestSection(IEnumerable<MarkdownDocument> documents, DocumentationSubject subject)
    {
        var names = SubjectNames(subject).ToArray();
        return documents
            .SelectMany(document => document.Sections)
            .Where(section => names.Any(name => MatchesHeading(section.Title, name)))
            .OrderBy(section => section.Level)
            .ThenBy(section => section.Ordinal)
            .FirstOrDefault();
    }

    private static MarkdownSection? FindBestCommandSection(
        IEnumerable<MarkdownDocument> documents,
        MarkdownSection appSection,
        DocumentationSubject application,
        DocumentationSubject command,
        int commandCount)
    {
        var commandPath = FirstNonEmpty(
            command.DisplayName,
            $"{application.DisplayName} {command.NativeId}".Trim());
        var names = SubjectNames(command)
            .Concat([commandPath])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        var scoped = appSection.Descendants()
            .Where(section => names.Any(name => MatchesHeading(section.Title, name)))
            .OrderBy(section => section.Level)
            .ThenBy(section => section.Ordinal)
            .FirstOrDefault();
        if (scoped is not null)
        {
            return scoped;
        }

        var global = documents
            .SelectMany(document => document.Sections)
            .Where(section => names.Any(name => MatchesHeading(section.Title, name)))
            .OrderBy(section => section.Level)
            .ThenBy(section => section.Ordinal)
            .FirstOrDefault();
        if (global is not null)
        {
            return global;
        }

        return commandCount == 1 && SectionMentionsCommand(appSection, command.NativeId)
            ? appSection
            : null;
    }

    private static IEnumerable<string> SubjectNames(DocumentationSubject subject)
    {
        yield return subject.NativeId ?? string.Empty;
        yield return subject.DisplayName;
        yield return subject.DisplayPath ?? string.Empty;
    }

    private static bool SectionMentionsCommand(MarkdownSection section, string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        return section.ContentLines.Any(line =>
            line.Contains($" {commandName} ", StringComparison.OrdinalIgnoreCase) ||
            line.Contains($"`{commandName}`", StringComparison.OrdinalIgnoreCase) ||
            line.Contains($"{commandName} --", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesHeading(string heading, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedHeading = NormalizeHeading(heading);
        var normalizedCandidate = NormalizeHeading(candidate);
        return string.Equals(normalizedHeading, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeading(string value)
    {
        var text = value.Trim().Trim('`');
        if (text.StartsWith("### ", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        return text.Trim().TrimEnd(':');
    }

    private static string FindFact(MetaDocsModel model, DocumentationSubject subject, string kind, string name) =>
        model.DocumentationFactList
            .Where(row =>
                string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static IReadOnlyList<string> ResolveMarkdownFiles(string sourceRoot)
    {
        var fullPath = Path.GetFullPath(sourceRoot);
        if (File.Exists(fullPath))
        {
            return string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase)
                ? [fullPath]
                : [];
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Markdown source path '{sourceRoot}' does not exist.");
        }

        return Directory.EnumerateFiles(fullPath, "*.md", SearchOption.AllDirectories)
            .Where(IsUsableMarkdownPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUsableMarkdownPath(string path)
    {
        var parts = Path.GetFullPath(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(static part => !string.IsNullOrWhiteSpace(part));
        foreach (var part in parts)
        {
            if (string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, ".vs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "test-results", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string ComputeFingerprint(IEnumerable<MarkdownDocument> documents)
    {
        var builder = new StringBuilder();
        foreach (var document in documents.OrderBy(document => document.Path, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(document.Path);
            builder.AppendLine(document.SourceText);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AppendIfPresent(StringBuilder builder, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine(body.Trim());
    }

    private static string NormalizeBody(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Where(static line => !ContainsSamplesDemoReference(line))
            .ToArray();
        var start = 0;
        var end = lines.Length - 1;
        while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        return start > end
            ? string.Empty
            : string.Join(Environment.NewLine, lines[start..(end + 1)]).Trim();
    }

    private static bool ContainsSamplesDemoReference(string line) =>
        line.Contains("Samples\\Demos", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Samples/Demos", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrent(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record MarkdownDocument(string Path, string SourceText, IReadOnlyList<MarkdownSection> Sections)
    {
        public static MarkdownDocument Parse(string path, string text)
        {
            var roots = new List<MarkdownSection>();
            var stack = new Stack<MarkdownSection>();
            var ordinal = 0;
            foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (TryParseHeading(line, out var level, out var title))
                {
                    var section = new MarkdownSection(level, title, ++ordinal);
                    while (stack.Count > 0 && stack.Peek().Level >= level)
                    {
                        stack.Pop();
                    }

                    if (stack.Count == 0)
                    {
                        roots.Add(section);
                    }
                    else
                    {
                        stack.Peek().Children.Add(section);
                    }

                    stack.Push(section);
                    continue;
                }

                if (stack.Count > 0)
                {
                    stack.Peek().ContentLines.Add(line);
                }
            }

            return new MarkdownDocument(path, text, roots.SelectMany(static root => root.SelfAndDescendants()).ToArray());
        }

        private static bool TryParseHeading(string line, out int level, out string title)
        {
            level = 0;
            title = string.Empty;
            var count = 0;
            while (count < line.Length && line[count] == '#')
            {
                count++;
            }

            if (count == 0 || count > 6 || count >= line.Length || line[count] != ' ')
            {
                return false;
            }

            level = count;
            title = line[(count + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(title);
        }
    }

    private sealed class MarkdownSection
    {
        public MarkdownSection(int level, string title, int ordinal)
        {
            Level = level;
            Title = title;
            Ordinal = ordinal;
        }

        public int Level { get; }

        public string Title { get; }

        public int Ordinal { get; }

        public List<string> ContentLines { get; } = new();

        public List<MarkdownSection> Children { get; } = new();

        public IEnumerable<MarkdownSection> SelfAndDescendants()
        {
            yield return this;
            foreach (var child in Children.SelectMany(static child => child.SelfAndDescendants()))
            {
                yield return child;
            }
        }

        public IEnumerable<MarkdownSection> Descendants() => Children.SelectMany(static child => child.SelfAndDescendants());

        public string ContentBody() => NormalizeBody(string.Join(Environment.NewLine, ContentLines));

        public string IntroBody()
        {
            var lines = new List<string>();
            foreach (var line in ContentLines)
            {
                if (IsLabel(line))
                {
                    break;
                }

                lines.Add(line);
            }

            return NormalizeBody(string.Join(Environment.NewLine, lines));
        }

        public IEnumerable<string> LabeledBlockLines(string label)
        {
            var inBlock = false;
            foreach (var line in ContentLines)
            {
                if (IsLabel(line, label))
                {
                    inBlock = true;
                    continue;
                }

                if (inBlock && IsLabel(line))
                {
                    yield break;
                }

                if (inBlock)
                {
                    yield return line;
                }
            }
        }

        private static bool IsLabel(string line, string? label = null)
        {
            var trimmed = line.Trim();
            if (!trimmed.EndsWith(':'))
            {
                return false;
            }

            var value = trimmed.TrimEnd(':');
            if (string.IsNullOrWhiteSpace(label))
            {
                return value.All(character => char.IsLetter(character) || char.IsWhiteSpace(character));
            }

            return string.Equals(value, label, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed record MetaDocsMarkdownCommandProseImportResult(
    int SourceFileCount,
    int MatchedApplicationCount,
    int MatchedCommandCount,
    int ImportedNarrativeCount);
