using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsImportSession
{
    private readonly MetaDocsModel model;
    private readonly DocumentationSource source;
    private readonly DocumentationImportBatch batch;
    private readonly HashSet<string> touchedSubjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> touchedFacts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> touchedRelationships = new(StringComparer.OrdinalIgnoreCase);

    public MetaDocsImportSession(
        MetaDocsModel model,
        string sourceId,
        string sourceKind,
        string displayName,
        string locator,
        string sourceFingerprint,
        string importerId,
        string importerVersion)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(importerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(importerVersion);

        this.model = model;
        MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(model);
        MetaDocsDefaults.EnsureDefaultView(model);

        var importedAt = DateTimeOffset.UtcNow.ToString("O");
        source = model.DocumentationSourceList.FirstOrDefault(row =>
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

        source.Kind = sourceKind;
        source.DisplayName = displayName;
        source.Locator = locator;
        source.SourceFingerprint = sourceFingerprint;
        source.ImporterId = importerId;
        source.ImportedAt = importedAt;
        source.Status = "Current";

        batch = new DocumentationImportBatch
        {
            Id = $"{source.Id}:batch:{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DocumentationSource = source,
            ImporterId = importerId,
            ImporterVersion = importerVersion,
            SourceFingerprint = sourceFingerprint,
            ImportedAt = importedAt,
            Status = "Current",
        };
        model.DocumentationImportBatchList.Add(batch);
    }

    public DocumentationSource Source => source;

    public DocumentationImportBatch Batch => batch;

    public DocumentationSubject UpsertSubject(
        string key,
        string kind,
        string nativeKind,
        string nativeId,
        string displayName,
        string displayPath,
        string summary,
        string parentKey,
        int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var existing = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, key, StringComparison.OrdinalIgnoreCase));
        var status = existing is null
            ? "New"
            : HasSubjectChanged(existing, kind, nativeKind, nativeId, displayName, displayPath, summary, parentKey)
                ? "Changed"
                : "Current";
        var subject = existing ?? new DocumentationSubject
        {
            Id = key,
        };
        if (existing is null)
        {
            model.DocumentationSubjectList.Add(subject);
        }

        var previousDisplayPath = subject.DisplayPath ?? string.Empty;
        subject.DocumentationSource = source;
        subject.Key = key;
        subject.Kind = kind;
        subject.NativeKind = nativeKind;
        subject.NativeId = nativeId;
        subject.DisplayName = displayName;
        subject.DisplayPath = displayPath;
        subject.Summary = summary;
        subject.ParentKey = parentKey;
        subject.Ordinal = ordinal.ToString("000");
        subject.Status = status;
        touchedSubjects.Add(subject.Id);

        if (!string.IsNullOrWhiteSpace(previousDisplayPath) &&
            !string.Equals(previousDisplayPath, displayPath, StringComparison.Ordinal))
        {
            EnsureAlias(subject, previousDisplayPath, "PreviousDisplayPath");
        }

        return subject;
    }

    public DocumentationFact UpsertFact(
        DocumentationSubject subject,
        string kind,
        string name,
        string value,
        string valueKind = "String")
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = $"{subject.Id}:fact:{NormalizeKey(kind)}:{NormalizeKey(name)}";
        var existing = model.DocumentationFactList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        var status = existing is null
            ? "New"
            : string.Equals(existing.Value ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal) &&
              string.Equals(existing.ValueKind, valueKind, StringComparison.Ordinal)
                ? "Current"
                : "Changed";
        var fact = existing ?? new DocumentationFact
        {
            Id = id,
        };
        if (existing is null)
        {
            model.DocumentationFactList.Add(fact);
        }

        fact.DocumentationSubject = subject;
        fact.DocumentationSource = source;
        fact.DocumentationImportBatch = batch;
        fact.SubjectKey = subject.Id;
        fact.Kind = kind;
        fact.Name = name;
        fact.Value = value;
        fact.ValueKind = valueKind;
        fact.SourceFingerprint = source.SourceFingerprint;
        fact.Status = status;
        touchedFacts.Add(fact.Id);
        return fact;
    }

    public DocumentationNarrative UpsertNarrative(
        DocumentationSubject subject,
        string slot,
        string title,
        string body,
        string origin,
        int ordinal,
        bool preserveExisting = true)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var id = $"{subject.Id}:narrative:{NormalizeKey(slot)}:{ordinal:000}";
        var narrative = model.DocumentationNarrativeList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (narrative is null)
        {
            narrative = new DocumentationNarrative
            {
                Id = id,
                DocumentationSubject = subject,
                SubjectKey = subject.Id,
                Slot = slot,
                Title = title,
                Body = body,
                BodyFormat = "PlainText",
                Origin = origin,
                LastReviewedImportBatchId = batch.Id,
                ReviewStatus = string.IsNullOrWhiteSpace(body) ? "NeedsAuthoring" : "Current",
                Ordinal = ordinal.ToString("000"),
            };
            model.DocumentationNarrativeList.Add(narrative);
            return narrative;
        }

        narrative.DocumentationSubject = subject;
        narrative.SubjectKey = subject.Id;
        narrative.LastReviewedImportBatchId = batch.Id;
        narrative.Ordinal = ordinal.ToString("000");
        if (!preserveExisting ||
            string.Equals(narrative.Origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(narrative.Body))
        {
            narrative.Slot = slot;
            narrative.Title = title;
            narrative.Body = body;
            narrative.BodyFormat = "PlainText";
            narrative.Origin = origin;
            narrative.ReviewStatus = string.IsNullOrWhiteSpace(body) ? "NeedsAuthoring" : "Current";
        }

        return narrative;
    }

    public DocumentationRelationship UpsertRelationship(
        string fromSubjectKey,
        string kind,
        string toSubjectKey,
        int ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSubjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSubjectKey);

        var id = $"{fromSubjectKey}:relationship:{NormalizeKey(kind)}:{NormalizeKey(toSubjectKey)}";
        var relationship = model.DocumentationRelationshipList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationRelationship
            {
                Id = id,
            };
        if (!model.DocumentationRelationshipList.Contains(relationship))
        {
            model.DocumentationRelationshipList.Add(relationship);
        }

        relationship.DocumentationSource = source;
        relationship.DocumentationImportBatch = batch;
        relationship.FromSubjectKey = fromSubjectKey;
        relationship.Kind = kind;
        relationship.ToSubjectKey = toSubjectKey;
        relationship.Ordinal = ordinal.ToString("000");
        touchedRelationships.Add(relationship.Id);
        return relationship;
    }

    public DocumentationViewNode EnsureViewNode(
        DocumentationSubject subject,
        string title,
        int ordinal,
        string parentNodeId = "")
    {
        var view = MetaDocsDefaults.EnsureDefaultView(model);
        var nodeId = $"view:default:node:{NormalizeKey(subject.Id)}";
        var node = model.DocumentationViewNodeList.FirstOrDefault(row =>
            string.Equals(row.Id, nodeId, StringComparison.OrdinalIgnoreCase))
            ?? new DocumentationViewNode
            {
                Id = nodeId,
            };
        if (!model.DocumentationViewNodeList.Contains(node))
        {
            model.DocumentationViewNodeList.Add(node);
        }

        node.DocumentationView = view;
        node.Title = title;
        node.ParentNodeId = parentNodeId;
        node.SubjectKey = subject.Id;
        node.Selection = string.Empty;
        node.Ordinal = ordinal.ToString("000");
        return node;
    }

    public void Complete()
    {
        foreach (var subject in model.DocumentationSubjectList.Where(row =>
                     ReferenceEquals(row.DocumentationSource, source) &&
                     !touchedSubjects.Contains(row.Id)))
        {
            subject.Status = "MissingFromSource";
        }

        foreach (var fact in model.DocumentationFactList.Where(row =>
                     ReferenceEquals(row.DocumentationSource, source) &&
                     !touchedFacts.Contains(row.Id)))
        {
            fact.Status = "MissingFromSource";
        }
    }

    private void EnsureAlias(DocumentationSubject subject, string aliasKey, string reason)
    {
        var id = $"{subject.Id}:alias:{NormalizeKey(aliasKey)}";
        if (model.DocumentationSubjectAliasList.Any(row =>
                string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        model.DocumentationSubjectAliasList.Add(new DocumentationSubjectAlias
        {
            Id = id,
            DocumentationSubject = subject,
            AliasKey = aliasKey,
            SubjectKey = subject.Id,
            Reason = reason,
        });
    }

    private static bool HasSubjectChanged(
        DocumentationSubject subject,
        string kind,
        string nativeKind,
        string nativeId,
        string displayName,
        string displayPath,
        string summary,
        string parentKey) =>
        !string.Equals(subject.Kind, kind, StringComparison.Ordinal) ||
        !string.Equals(subject.NativeKind ?? string.Empty, nativeKind ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.NativeId ?? string.Empty, nativeId ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.DisplayName, displayName, StringComparison.Ordinal) ||
        !string.Equals(subject.DisplayPath ?? string.Empty, displayPath ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.Summary ?? string.Empty, summary ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.ParentKey ?? string.Empty, parentKey ?? string.Empty, StringComparison.Ordinal);

    public static string NormalizeKey(string value)
    {
        var chars = value.Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var text = new string(chars);
        while (text.Contains("--", StringComparison.Ordinal))
        {
            text = text.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(text.Trim('-')) ? "item" : text.Trim('-');
    }
}
