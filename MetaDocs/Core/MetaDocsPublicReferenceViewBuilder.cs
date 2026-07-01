using MetaDocs;

namespace MetaDocs.Core;

public static class MetaDocsPublicReferenceViewBuilder
{
    public static DocumentationView EnsurePublicReferenceView(MetaDocsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var view = MetaDocsDefaults.EnsureDefaultView(model);
        view.Title = "meta + meta-bi reference";
        view.Summary = "Command-line and model references for the current public MetaDocs suite.";

        model.DocumentationViewNodeList.RemoveAll(node =>
            node.DocumentationView is not null &&
            string.Equals(node.DocumentationView.Id, view.Id, StringComparison.OrdinalIgnoreCase));

        var classified = model.DocumentationSubjectList
            .Select(subject => new ClassifiedSubject(subject, TryClassify(model, subject, out var classification), classification))
            .Where(item => item.IsPublic)
            .OrderBy(item => item.Classification.ProductFamily)
            .ThenBy(item => item.Classification.Surface)
            .ThenBy(item => item.Classification.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Subject.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        DocumentationViewNode? previousFamilyNode = null;
        foreach (var familyGroup in classified.GroupBy(item => item.Classification.ProductFamily).OrderBy(group => group.Key))
        {
            var familyKey = MetaDocsPublicReferenceClassifier.ProductFamilyKey(familyGroup.Key);
            var familyNode = AddNode(
                model,
                view,
                $"view:default:public:{familyKey}",
                null,
                null,
                $"ProductFamily:{familyKey}",
                MetaDocsPublicReferenceClassifier.FormatProductFamily(familyGroup.Key),
                previousFamilyNode);
            previousFamilyNode = familyNode;

            DocumentationViewNode? previousSurfaceNode = null;
            foreach (var surfaceGroup in familyGroup.GroupBy(item => item.Classification.Surface).OrderBy(group => group.Key))
            {
                var surfaceKey = MetaDocsPublicReferenceClassifier.ReferenceSurfaceKey(surfaceGroup.Key);
                var surfaceNode = AddNode(
                    model,
                    view,
                    $"{familyNode.Id}:{surfaceKey}",
                    familyNode.Id,
                    null,
                    $"ReferenceSurface:{familyKey}:{surfaceKey}",
                    MetaDocsPublicReferenceClassifier.FormatReferenceSurface(surfaceGroup.Key),
                    previousSurfaceNode);
                previousSurfaceNode = surfaceNode;

                DocumentationViewNode? previousSubjectNode = null;
                foreach (var item in surfaceGroup)
                {
                    previousSubjectNode = AddNode(
                        model,
                        view,
                        $"{surfaceNode.Id}:subject:{MetaDocsImportSession.NormalizeKey(item.Subject.Id)}",
                        surfaceNode.Id,
                        item.Subject.Id,
                        $"PublicReference:{familyKey}:{surfaceKey}",
                        item.Subject.DisplayName,
                        previousSubjectNode);
                }
            }
        }

        return view;
    }

    private static bool TryClassify(
        MetaDocsModel model,
        DocumentationSubject subject,
        out MetaDocsPublicReferenceClassification classification) =>
        MetaDocsPublicReferenceClassifier.TryClassify(model, subject, out classification);

    private static DocumentationViewNode AddNode(
        MetaDocsModel model,
        DocumentationView view,
        string id,
        string? parentNodeId,
        string? subjectKey,
        string selection,
        string title,
        DocumentationViewNode? previousNode)
    {
        var node = new DocumentationViewNode
        {
            Id = id,
            DocumentationView = view,
            ParentNodeId = parentNodeId,
            SubjectKey = subjectKey,
            Selection = selection,
            Title = title,
            PreviousNode = previousNode,
        };
        model.DocumentationViewNodeList.Add(node);
        return node;
    }

    private sealed record ClassifiedSubject(
        DocumentationSubject Subject,
        bool IsPublic,
        MetaDocsPublicReferenceClassification Classification);
}
