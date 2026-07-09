namespace MetaDocs.Core;

public sealed class MetaDocsNavigationService
{
    public MetaDocsWorkspaceOverview Describe(MetaDocsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subjects = CurrentSubjects(model).ToArray();
        var narratives = model.DocumentationNarrativeList.Where(IsCurrent).ToArray();
        var facts = model.DocumentationFactList.Where(IsCurrent).ToArray();
        var views = model.DocumentationViewList
            .OrderBy(static view => FirstNonEmpty(view.Title, view.Name, view.Id), StringComparer.OrdinalIgnoreCase)
            .Select(view => new MetaDocsViewSummary(
                FirstNonEmpty(view.Title, view.Name, view.Id),
                model.DocumentationViewNodeList.Count(node => ReferenceEquals(node.DocumentationView, view))))
            .ToArray();
        var subjectTypes = subjects
            .GroupBy(MetaDocsVocabulary.SubjectTypeName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MetaDocsSubjectTypeCount(group.Key, group.Count()))
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.SubjectType, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readTargets = subjects
            .Where(subject =>
                MetaDocsVocabulary.IsSubjectType(subject, "CliApplication") ||
                MetaDocsVocabulary.IsSubjectType(subject, "Model"))
            .OrderBy(static subject => FirstNonEmpty(subject.DisplayPath, subject.DisplayName, subject.Id), StringComparer.OrdinalIgnoreCase)
            .Select(subject => new MetaDocsReadTarget(MetaDocsVocabulary.SubjectTypeName(subject), subject.DisplayName))
            .ToArray();

        var previewDepth = model.DocumentationViewNodeList.Count <= 10 ? 2 : 3;
        return new MetaDocsWorkspaceOverview(
            subjects.Length,
            narratives.Length,
            facts.Length,
            model.DocumentationSourceList.Count,
            model.DocumentationViewList.Count,
            subjectTypes,
            views,
            readTargets,
            Contents(model, maxDepth: previewDepth));
    }

    public MetaDocsContentsResult Contents(MetaDocsModel model, string viewSelector = "", int maxDepth = 4)
    {
        ArgumentNullException.ThrowIfNull(model);
        var depth = Math.Max(1, maxDepth);
        var view = ResolveView(model, viewSelector);
        var subjectsByKey = CurrentSubjects(model)
            .GroupBy(static subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var subjectChildrenByParent = CurrentSubjects(model)
            .Where(static subject => subject.ParentSubject is not null)
            .GroupBy(static subject => subject.ParentSubject!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        if (view is null)
        {
            var rootSubjects = CurrentSubjects(model)
                .Where(static subject => subject.ParentSubject is null)
                .ToArray();
            return new MetaDocsContentsResult(
                "Contents",
                OrderedSubjects(rootSubjects)
                    .Select(subject => BuildSubjectNode(subject, subjectChildrenByParent, depth, 1, string.Empty))
                    .ToArray());
        }

        if (view.RootSubject is not null)
        {
            return new MetaDocsContentsResult(
                FirstNonEmpty(view.Title, view.Name, view.Id),
                [BuildSubjectNode(view.RootSubject, subjectChildrenByParent, depth, 1, string.Empty)]);
        }

        var viewNodes = model.DocumentationViewNodeList
            .Where(node => ReferenceEquals(node.DocumentationView, view))
            .ToArray();
        var viewChildrenByParent = viewNodes
            .Where(static node => !string.IsNullOrWhiteSpace(node.ParentNodeId))
            .GroupBy(static node => node.ParentNodeId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var rootNodes = viewNodes
            .Where(static node => string.IsNullOrWhiteSpace(node.ParentNodeId))
            .ToArray();
        var expandSubjectChildren = viewNodes.Length <= 10;

        return new MetaDocsContentsResult(
            FirstNonEmpty(view.Title, view.Name, view.Id),
            OrderedViewNodes(rootNodes)
                .Select(node => BuildViewNode(
                    node,
                    subjectsByKey,
                    subjectChildrenByParent,
                    viewChildrenByParent,
                    expandSubjectChildren,
                    depth,
                    1))
                .ToArray());
    }

    private static MetaDocsContentNode BuildViewNode(
        DocumentationViewNode node,
        IReadOnlyDictionary<string, DocumentationSubject> subjectsByKey,
        IReadOnlyDictionary<string, DocumentationSubject[]> subjectChildrenByParent,
        IReadOnlyDictionary<string, DocumentationViewNode[]> viewChildrenByParent,
        bool expandSubjectChildren,
        int maxDepth,
        int currentDepth)
    {
        var subject = node.DocumentationSubject is not null && subjectsByKey.ContainsKey(node.DocumentationSubject.Id)
            ? node.DocumentationSubject
            : null;
        var title = FirstNonEmpty(node.Title, subject?.DisplayName, node.Selection, node.Id);
        var children = new List<MetaDocsContentNode>();
        var childDepth = currentDepth + 1;
        var hasMore = false;

        if (childDepth <= maxDepth && viewChildrenByParent.TryGetValue(node.Id, out var viewChildren))
        {
            children.AddRange(OrderedViewNodes(viewChildren)
                .Select(child => BuildViewNode(
                    child,
                    subjectsByKey,
                    subjectChildrenByParent,
                    viewChildrenByParent,
                    expandSubjectChildren,
                    maxDepth,
                    childDepth)));
        }
        else if (viewChildrenByParent.ContainsKey(node.Id))
        {
            hasMore = true;
        }

        if (subject is not null && expandSubjectChildren)
        {
            if (childDepth <= maxDepth)
            {
                children.AddRange(SubjectChildren(subject, subjectChildrenByParent)
                    .Select(child => BuildSubjectNode(child, subjectChildrenByParent, maxDepth, childDepth, subject.DisplayName)));
            }
            else if (SubjectChildren(subject, subjectChildrenByParent).Any())
            {
                hasMore = true;
            }
        }

        return new MetaDocsContentNode(title, subject, children, hasMore);
    }

    private static MetaDocsContentNode BuildSubjectNode(
        DocumentationSubject subject,
        IReadOnlyDictionary<string, DocumentationSubject[]> subjectChildrenByParent,
        int maxDepth,
        int currentDepth,
        string parentTitle)
    {
        var childDepth = currentDepth + 1;
        var children = childDepth > maxDepth
            ? []
            : SubjectChildren(subject, subjectChildrenByParent)
                .Select(child => BuildSubjectNode(child, subjectChildrenByParent, maxDepth, childDepth, subject.DisplayName))
                .ToArray();
        var hasMore = childDepth > maxDepth && SubjectChildren(subject, subjectChildrenByParent).Any();
        return new MetaDocsContentNode(DisplayTitle(subject.DisplayName, parentTitle), subject, children, hasMore);
    }

    private static string DisplayTitle(string displayName, string parentTitle) =>
        !string.IsNullOrWhiteSpace(parentTitle) &&
        displayName.StartsWith(parentTitle + " ", StringComparison.OrdinalIgnoreCase)
            ? displayName[(parentTitle.Length + 1)..]
            : displayName;

    private static IReadOnlyList<DocumentationSubject> SubjectChildren(
        DocumentationSubject subject,
        IReadOnlyDictionary<string, DocumentationSubject[]> subjectChildrenByParent) =>
        subjectChildrenByParent.TryGetValue(subject.Id, out var children)
            ? OrderedSubjects(children)
            : [];

    private static IReadOnlyList<DocumentationSubject> OrderedSubjects(IEnumerable<DocumentationSubject> subjects) =>
        MetaDocsOrdering.ByPrevious(
            subjects,
            static subject => subject.PreviousSubject,
            static subject => FirstNonEmpty(subject.DisplayPath, subject.DisplayName, subject.Id));

    private static IReadOnlyList<DocumentationViewNode> OrderedViewNodes(IEnumerable<DocumentationViewNode> nodes) =>
        MetaDocsOrdering.ByPrevious(
            nodes,
            static node => node.PreviousNode,
            static node => FirstNonEmpty(node.Title, node.Selection, node.Id));

    private static DocumentationView? ResolveView(MetaDocsModel model, string selector)
    {
        if (model.DocumentationViewList.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            return model.DocumentationViewList.FirstOrDefault(static view => string.Equals(view.Id, "view:default", StringComparison.OrdinalIgnoreCase)) ??
                   model.DocumentationViewList
                       .OrderBy(static view => FirstNonEmpty(view.Title, view.Name, view.Id), StringComparer.OrdinalIgnoreCase)
                       .First();
        }

        var trimmed = selector.Trim();
        var matches = model.DocumentationViewList
            .Where(view =>
                string.Equals(view.Id, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(view.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(view.Title, trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve documentation view '{selector}'."),
            _ => throw new InvalidOperationException($"Documentation view selector '{selector}' matched more than one view."),
        };
    }

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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed record MetaDocsWorkspaceOverview(
    int SubjectCount,
    int NarrativeCount,
    int FactCount,
    int SourceCount,
    int ViewCount,
    IReadOnlyList<MetaDocsSubjectTypeCount> SubjectTypes,
    IReadOnlyList<MetaDocsViewSummary> Views,
    IReadOnlyList<MetaDocsReadTarget> ReadTargets,
    MetaDocsContentsResult Contents);

public sealed record MetaDocsSubjectTypeCount(string SubjectType, int Count);

public sealed record MetaDocsViewSummary(string Title, int NodeCount);

public sealed record MetaDocsReadTarget(string SubjectType, string Name);

public sealed record MetaDocsContentsResult(
    string Title,
    IReadOnlyList<MetaDocsContentNode> Nodes);

public sealed record MetaDocsContentNode(
    string Title,
    DocumentationSubject? Subject,
    IReadOnlyList<MetaDocsContentNode> Children,
    bool HasMoreChildren);
