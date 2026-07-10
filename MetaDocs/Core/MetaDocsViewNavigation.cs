namespace MetaDocs.Core;

internal static class MetaDocsViewNavigation
{
    public static string Title(MetaDocsModel model, DocumentationSubject subject)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(subject);

        var view = model.DocumentationViewList.FirstOrDefault(row =>
                       string.Equals(row.Id, "view:default", StringComparison.OrdinalIgnoreCase))
                   ?? model.DocumentationViewList.FirstOrDefault();
        if (view is null)
        {
            return subject.DisplayName;
        }

        var node = model.DocumentationViewNodeList.FirstOrDefault(row =>
            string.Equals(row.DocumentationView?.Id, view.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.DocumentationSubject?.Id, subject.Id, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(node?.Title) ? subject.DisplayName : node.Title;
    }
}
