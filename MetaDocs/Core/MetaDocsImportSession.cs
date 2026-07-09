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
        string sourceType,
        string displayName,
        string locator,
        string sourceFingerprint,
        string importerId,
        string importerVersion)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(importerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(importerVersion);

        this.model = model;
        MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(model);
        MetaDocsDefaults.EnsureDefaultView(model);

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

        var existingBatch = model.DocumentationImportBatchList
            .Where(row =>
                IsSource(row.DocumentationSource) &&
                string.Equals(row.SourceFingerprint ?? string.Empty, sourceFingerprint, StringComparison.Ordinal))
            .OrderByDescending(static row => row.ImportedAt, StringComparer.Ordinal)
            .FirstOrDefault();
        var importedAt = existingBatch?.ImportedAt ?? DateTimeOffset.UtcNow.ToString("O");

        source.DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, sourceType);
        source.DisplayName = displayName;
        source.Locator = locator;
        source.SourceFingerprint = sourceFingerprint;
        source.ImporterId = importerId;
        source.ImportedAt = importedAt;
        source.Status = "Current";

        batch = existingBatch ?? new DocumentationImportBatch
        {
            Id = $"{source.Id}:batch:{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DocumentationSource = source,
            ImporterId = importerId,
            ImporterVersion = importerVersion,
            SourceFingerprint = sourceFingerprint,
            ImportedAt = importedAt,
            Status = "Current",
        };
        batch.DocumentationSource = source;
        batch.ImporterId = importerId;
        batch.ImporterVersion = importerVersion;
        batch.SourceFingerprint = sourceFingerprint;
        batch.ImportedAt = importedAt;
        batch.Status = "Current";
        if (!model.DocumentationImportBatchList.Contains(batch))
        {
            model.DocumentationImportBatchList.Add(batch);
        }
    }

    public DocumentationSource Source => source;

    public DocumentationImportBatch Batch => batch;

    public DocumentationSubject? EnsureParentSubject(string parentSubjectId)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectId))
        {
            return null;
        }

        var id = parentSubjectId.Trim();
        var existing = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            touchedSubjects.Add(existing.Id);
            return existing;
        }

        var subject = new DocumentationSubject
        {
            Id = id,
            DocumentationSource = source,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "ReferenceSection"),
            SourceTypeName = "MetaDocs.ReferenceSection",
            NativeId = id,
            DisplayName = DisplayNameFromSubjectId(id),
            DisplayPath = DisplayNameFromSubjectId(id),
            Summary = string.Empty,
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(subject);
        touchedSubjects.Add(subject.Id);
        return subject;
    }

    public DocumentationSubject UpsertSubject(
        string subjectId,
        string subjectType,
        string sourceTypeName,
        string nativeId,
        string displayName,
        string displayPath,
        string summary,
        DocumentationSubject? parentSubject,
        DocumentationSubject? previousSubject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var resolvedSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, subjectType);
        var existing = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, subjectId, StringComparison.OrdinalIgnoreCase));
        var resolvedSummary = ResolveSubjectSummary(existing, subjectType, displayName, summary);
        var hasChanged = existing is not null &&
                         HasSubjectChanged(existing, subjectType, sourceTypeName, nativeId, displayName, displayPath, resolvedSummary, parentSubject);
        var status = existing is null
            ? "New"
            : ResolveExistingStatus(existing.Status, hasChanged);
        var subject = existing ?? new DocumentationSubject
        {
            Id = subjectId,
        };
        if (existing is null)
        {
            model.DocumentationSubjectList.Add(subject);
        }

        var previousDisplayPath = subject.DisplayPath ?? string.Empty;
        subject.DocumentationSource = source;
        subject.DocumentationSubjectType = resolvedSubjectType;
        subject.SourceTypeName = sourceTypeName;
        subject.NativeId = nativeId;
        subject.DisplayName = displayName;
        subject.DisplayPath = displayPath;
        subject.Summary = resolvedSummary;
        subject.ParentSubject = parentSubject;
        subject.PreviousSubject = previousSubject;
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
        string factType,
        string name,
        string value,
        string valueType = "String")
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(factType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var id = $"{subject.Id}:fact:{NormalizeKey(factType)}:{NormalizeKey(name)}";
        var resolvedFactType = MetaDocsVocabulary.EnsureFactType(model, factType);
        var resolvedValueType = MetaDocsVocabulary.EnsureValueType(model, valueType);
        var existing = model.DocumentationFactList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        var hasChanged = existing is not null &&
                         (!string.Equals(existing.Value ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal) ||
                          !string.Equals(MetaDocsVocabulary.ValueTypeName(existing), valueType, StringComparison.Ordinal));
        var status = existing is null
            ? "New"
            : ResolveExistingStatus(existing.Status, hasChanged);
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
        fact.DocumentationFactType = resolvedFactType;
        fact.DocumentationValueType = resolvedValueType;
        fact.Name = name;
        fact.Value = value;
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
        DocumentationNarrative? previousNarrative = null,
        bool preserveExisting = true)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var id = $"{subject.Id}:narrative:{NormalizeKey(slot)}:{NormalizeKey(string.IsNullOrWhiteSpace(title) ? slot : title)}";
        var narrative = model.DocumentationNarrativeList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (narrative is null)
        {
            narrative = new DocumentationNarrative
            {
                Id = id,
                DocumentationSubject = subject,
                Slot = slot,
                Title = title,
                Body = body,
                BodyFormat = "PlainText",
                Origin = origin,
                LastReviewedImportBatchId = batch.Id,
                ReviewStatus = string.IsNullOrWhiteSpace(body) ? "NeedsAuthoring" : "Current",
                PreviousNarrative = previousNarrative,
            };
            model.DocumentationNarrativeList.Add(narrative);
            return narrative;
        }

        narrative.DocumentationSubject = subject;
        narrative.LastReviewedImportBatchId = batch.Id;
        narrative.PreviousNarrative = previousNarrative;
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
        string relationshipType,
        string toSubjectKey,
        DocumentationRelationship? previousRelationship = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSubjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipType);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSubjectKey);

        var fromSubject = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, fromSubjectKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Relationship source subject '{fromSubjectKey}' was not found.");
        var toSubject = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, toSubjectKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Relationship target subject '{toSubjectKey}' was not found.");
        var id = $"{fromSubjectKey}:relationship:{NormalizeKey(relationshipType)}:{NormalizeKey(toSubjectKey)}";
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
        relationship.DocumentationRelationshipType = MetaDocsVocabulary.EnsureRelationshipType(model, relationshipType);
        relationship.FromSubject = fromSubject;
        relationship.ToSubject = toSubject;
        relationship.PreviousRelationship = previousRelationship;
        touchedRelationships.Add(relationship.Id);
        return relationship;
    }

    public DocumentationViewNode EnsureViewNode(
        DocumentationSubject subject,
        string title,
        DocumentationViewNode? previousNode = null,
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
        node.DocumentationSubject = subject;
        node.Selection = string.Empty;
        node.PreviousNode = previousNode;
        return node;
    }

    public void Complete(bool pruneMissingFromSource = false)
    {
        if (pruneMissingFromSource)
        {
            PruneUntouchedSourceRows();
            PruneAmbiguousGeneratedAliases();
            return;
        }

        MarkUntouchedSourceRowsMissing();
        PruneAmbiguousGeneratedAliases();
    }

    private void MarkUntouchedSourceRowsMissing()
    {
        foreach (var subject in model.DocumentationSubjectList.Where(row =>
                     IsSource(row.DocumentationSource) &&
                     !touchedSubjects.Contains(row.Id)))
        {
            subject.Status = "MissingFromSource";
        }

        foreach (var fact in model.DocumentationFactList.Where(row =>
                     IsSource(row.DocumentationSource) &&
                     !touchedFacts.Contains(row.Id)))
        {
            fact.Status = "MissingFromSource";
        }
    }

    private void PruneUntouchedSourceRows()
    {
        var staleSubjectIds = model.DocumentationSubjectList
            .Where(row => IsSource(row.DocumentationSource) &&
                          !touchedSubjects.Contains(row.Id))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        model.DocumentationFactList.RemoveAll(row =>
            (IsSource(row.DocumentationSource) && !touchedFacts.Contains(row.Id)) ||
            (row.DocumentationSubject is not null && staleSubjectIds.Contains(row.DocumentationSubject.Id)));
        model.DocumentationRelationshipList.RemoveAll(row =>
            (IsSource(row.DocumentationSource) && !touchedRelationships.Contains(row.Id)) ||
            (row.FromSubject is not null && staleSubjectIds.Contains(row.FromSubject.Id)) ||
            (row.ToSubject is not null && staleSubjectIds.Contains(row.ToSubject.Id)));
        model.DocumentationNarrativeList.RemoveAll(row =>
            (row.DocumentationSubject is not null && staleSubjectIds.Contains(row.DocumentationSubject.Id)));
        model.DocumentationSubjectAliasList.RemoveAll(row =>
            (row.DocumentationSubject is not null && staleSubjectIds.Contains(row.DocumentationSubject.Id)));
        model.DocumentationViewNodeList.RemoveAll(row =>
            row.DocumentationSubject is not null && staleSubjectIds.Contains(row.DocumentationSubject.Id));
        var staleExampleIds = model.DocumentationExampleList
            .Where(row => row.DocumentationSubject is not null && staleSubjectIds.Contains(row.DocumentationSubject.Id))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staleExampleSectionIds = model.DocumentationExampleSectionList
            .Where(row => row.DocumentationExample is not null && staleExampleIds.Contains(row.DocumentationExample.Id))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        model.DocumentationExampleCodeList.RemoveAll(row =>
            row.DocumentationExampleSection is not null && staleExampleSectionIds.Contains(row.DocumentationExampleSection.Id));
        model.DocumentationExampleSectionList.RemoveAll(row =>
            staleExampleSectionIds.Contains(row.Id));
        model.DocumentationExampleList.RemoveAll(row =>
            staleExampleIds.Contains(row.Id));
        model.DocumentationSubjectList.RemoveAll(row =>
            staleSubjectIds.Contains(row.Id));
    }

    private void PruneAmbiguousGeneratedAliases()
    {
        var ambiguousPreviousDisplayPathAliases = model.DocumentationSubjectAliasList
            .Where(row =>
                string.Equals(row.Reason, "PreviousDisplayPath", StringComparison.OrdinalIgnoreCase) &&
                row.DocumentationSubject is not null &&
                IsSource(row.DocumentationSubject.DocumentationSource))
            .GroupBy(row => row.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(group => group
                .Select(row => row.DocumentationSubject?.Id ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any())
            .SelectMany(group => group)
            .Select(row => row.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ambiguousPreviousDisplayPathAliases.Count == 0)
        {
            return;
        }

        model.DocumentationSubjectAliasList.RemoveAll(row => ambiguousPreviousDisplayPathAliases.Contains(row.Id));
    }

    private bool IsSource(DocumentationSource candidate) =>
        string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase);

    private static string ResolveExistingStatus(string? currentStatus, bool hasChanged)
    {
        if (hasChanged)
        {
            return "Changed";
        }

        if (string.Equals(currentStatus, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
        {
            return "Current";
        }

        return string.IsNullOrWhiteSpace(currentStatus) ? "Current" : currentStatus;
    }

    private static string ResolveSubjectSummary(
        DocumentationSubject? existing,
        string kind,
        string displayName,
        string importedSummary)
    {
        if (!string.IsNullOrWhiteSpace(importedSummary))
        {
            return importedSummary;
        }

        if (existing is null)
        {
            return string.Empty;
        }

        var existingSummary = existing.Summary ?? string.Empty;
        return IsGeneratedPlaceholderSummary(kind, displayName, existingSummary)
            ? string.Empty
            : existingSummary;
    }

    private static bool IsGeneratedPlaceholderSummary(string kind, string displayName, string summary)
    {
        var text = summary.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(text, "Metadata workspace.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, $"Model {displayName}.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, $"Entity {displayName}.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(kind, "Property", StringComparison.OrdinalIgnoreCase) &&
            (text.StartsWith("Required ", StringComparison.OrdinalIgnoreCase) ||
             text.StartsWith("Optional ", StringComparison.OrdinalIgnoreCase)) &&
            text.EndsWith(" property.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(kind, "Relationship", StringComparison.OrdinalIgnoreCase) &&
               (text.StartsWith("Required relationship to ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Optional relationship to ", StringComparison.OrdinalIgnoreCase)) &&
               text.EndsWith(".", StringComparison.OrdinalIgnoreCase);
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
            Alias = aliasKey,
            Reason = reason,
        });
    }

    private static bool HasSubjectChanged(
        DocumentationSubject subject,
        string subjectType,
        string sourceTypeName,
        string nativeId,
        string displayName,
        string displayPath,
        string summary,
        DocumentationSubject? parentSubject) =>
        !string.Equals(MetaDocsVocabulary.SubjectTypeName(subject), subjectType, StringComparison.Ordinal) ||
        !string.Equals(subject.SourceTypeName ?? string.Empty, sourceTypeName ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.NativeId ?? string.Empty, nativeId ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.DisplayName, displayName, StringComparison.Ordinal) ||
        !string.Equals(subject.DisplayPath ?? string.Empty, displayPath ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.Summary ?? string.Empty, summary ?? string.Empty, StringComparison.Ordinal) ||
        !string.Equals(subject.ParentSubject?.Id ?? string.Empty, parentSubject?.Id ?? string.Empty, StringComparison.Ordinal);

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

    private static string DisplayNameFromSubjectId(string id)
    {
        var lastSegment = id.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? id;
        return string.Equals(lastSegment, "cli", StringComparison.OrdinalIgnoreCase)
            ? "CLI"
            : string.Join(
                " ",
                lastSegment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
