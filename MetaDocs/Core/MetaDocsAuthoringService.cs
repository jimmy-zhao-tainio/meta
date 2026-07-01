using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsAuthoringService
{
    public DocumentationSubject UpsertPage(MetaDocsModel model, MetaDocsAuthoredPage page)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(page.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(page.Title);

        var workspace = MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(model);
        MetaDocsDefaults.EnsureDefaultView(model);

        var sourceId = string.IsNullOrWhiteSpace(page.SourceId)
            ? "source:authored:metametabi-docs"
            : page.SourceId.Trim();
        var source = model.DocumentationSourceList.FirstOrDefault(row =>
            string.Equals(row.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            source = new DocumentationSource
            {
                Id = sourceId,
            };
            model.DocumentationSourceList.Add(source);
        }

        source.DocumentationWorkspace = workspace;
        source.DisplayName = string.IsNullOrWhiteSpace(page.SourceDisplayName)
            ? "Authored MetaDocs pages"
            : page.SourceDisplayName.Trim();
        source.Kind = "AuthoredDocumentation";
        source.Locator = "modeled";
        source.SourceFingerprint = string.Empty;
        source.ImporterId = "MetaDocs.Authoring";
        source.ImportedAt = DateTimeOffset.UtcNow.ToString("O");
        source.Status = "Current";

        var subject = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, page.Id, StringComparison.OrdinalIgnoreCase));
        if (subject is null)
        {
            subject = new DocumentationSubject
            {
                Id = page.Id,
            };
            model.DocumentationSubjectList.Add(subject);
        }

        subject.DocumentationSource = source;
        subject.Key = page.Id;
        subject.Kind = string.IsNullOrWhiteSpace(page.Kind) ? "Guide" : page.Kind.Trim();
        subject.NativeKind = "AuthoredPage";
        subject.NativeId = page.Id;
        subject.DisplayName = page.Title.Trim();
        subject.DisplayPath = string.IsNullOrWhiteSpace(page.DisplayPath) ? page.Title.Trim() : page.DisplayPath.Trim();
        subject.Summary = page.Summary?.Trim() ?? string.Empty;
        subject.ParentKey = page.ParentKey?.Trim() ?? string.Empty;
        subject.PreviousSubject = null;
        subject.Status = "Current";

        UpsertNarrative(model, subject, page);
        EnsureViewNode(model, subject, page);
        return subject;
    }

    private static void UpsertNarrative(MetaDocsModel model, DocumentationSubject subject, MetaDocsAuthoredPage page)
    {
        var slot = string.IsNullOrWhiteSpace(page.Slot) ? "Summary" : page.Slot.Trim();
        var title = string.IsNullOrWhiteSpace(page.NarrativeTitle) ? page.Title.Trim() : page.NarrativeTitle.Trim();
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

        narrative.DocumentationSubject = subject;
        narrative.SubjectKey = subject.Id;
        narrative.Slot = slot;
        narrative.Title = title;
        narrative.Body = page.Body?.Trim() ?? string.Empty;
        narrative.BodyFormat = "PlainText";
        narrative.Origin = "Authored";
        narrative.LastReviewedImportBatchId = string.Empty;
        narrative.ReviewStatus = string.IsNullOrWhiteSpace(narrative.Body) ? "NeedsAuthoring" : "Current";
        narrative.PreviousNarrative = null;
    }

    private static void EnsureViewNode(MetaDocsModel model, DocumentationSubject subject, MetaDocsAuthoredPage page)
    {
        var view = MetaDocsDefaults.EnsureDefaultView(model);
        var nodeId = $"view:default:node:{MetaDocsImportSession.NormalizeKey(subject.Id)}";
        var node = model.DocumentationViewNodeList.FirstOrDefault(row =>
            string.Equals(row.Id, nodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            node = new DocumentationViewNode
            {
                Id = nodeId,
            };
            model.DocumentationViewNodeList.Add(node);
        }

        node.DocumentationView = view;
        node.SubjectKey = subject.Id;
        node.Title = subject.DisplayName;
        node.ParentNodeId = string.IsNullOrWhiteSpace(page.ParentKey)
            ? string.Empty
            : $"view:default:node:{MetaDocsImportSession.NormalizeKey(page.ParentKey)}";
        node.Selection = string.Empty;
        node.PreviousNode = null;
    }
}

public sealed record MetaDocsAuthoredPage(
    string Id,
    string Title,
    string Summary,
    string Body,
    string Kind = "Guide",
    string DisplayPath = "",
    string ParentKey = "",
    string Slot = "Summary",
    string NarrativeTitle = "",
    string SourceId = "source:authored:metametabi-docs",
    string SourceDisplayName = "Authored MetaDocs pages");
