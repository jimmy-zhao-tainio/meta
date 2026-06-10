using MetaDocs;

namespace MetaDocs.Core;

public static class MetaDocsPublicReferenceClassifier
{
    private static readonly HashSet<string> MetaCliApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "meta",
        "meta-docs",
        "meta-weave",
    };

    private static readonly HashSet<string> MetaModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "MetaDocs",
        "MetaWeave",
    };

    public static bool TryClassify(
        MetaDocsModel model,
        DocumentationSubject subject,
        out MetaDocsPublicReferenceClassification classification)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(subject);

        classification = default;
        if (!IsActive(subject))
        {
            return false;
        }

        if (string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase))
        {
            var appName = FirstNonEmpty(FindFact(model, subject, "Cli", "ApplicationName"), subject.DisplayName, subject.NativeId);
            var family = ClassifyCliFamily(model, subject, appName);
            classification = new MetaDocsPublicReferenceClassification(
                family,
                MetaDocsReferenceSurface.Cli,
                MetaDocsImportSession.NormalizeKey(appName));
            return true;
        }

        if (string.Equals(subject.Kind, "Model", StringComparison.OrdinalIgnoreCase))
        {
            var modelName = FirstNonEmpty(FindFact(model, subject, "Model", "Name"), subject.DisplayName, subject.NativeId);
            var family = MetaModels.Contains(modelName)
                ? MetaDocsProductFamily.Meta
                : MetaDocsProductFamily.MetaBi;
            classification = new MetaDocsPublicReferenceClassification(
                family,
                MetaDocsReferenceSurface.Models,
                MetaDocsImportSession.NormalizeKey(modelName));
            return true;
        }

        return false;
    }

    public static string FormatProductFamily(MetaDocsProductFamily family) =>
        family == MetaDocsProductFamily.Meta ? "Meta" : "Meta-BI";

    public static string FormatReferenceSurface(MetaDocsReferenceSurface surface) =>
        surface == MetaDocsReferenceSurface.Cli ? "CLI" : "Models";

    public static string ProductFamilyKey(MetaDocsProductFamily family) =>
        family == MetaDocsProductFamily.Meta ? "meta" : "meta-bi";

    public static string ReferenceSurfaceKey(MetaDocsReferenceSurface surface) =>
        surface == MetaDocsReferenceSurface.Cli ? "cli" : "models";

    public static bool IsActive(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static MetaDocsProductFamily ClassifyCliFamily(
        MetaDocsModel model,
        DocumentationSubject subject,
        string appName)
    {
        if (MetaCliApplications.Contains(appName))
        {
            return MetaDocsProductFamily.Meta;
        }

        if (appName.StartsWith("meta-data-type", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(appName, "meta-convert", StringComparison.OrdinalIgnoreCase))
        {
            return MetaDocsProductFamily.MetaBi;
        }

        var groupName = FindFact(model, subject, "Cli", "GroupName");
        if (string.Equals(groupName, "meta", StringComparison.OrdinalIgnoreCase) &&
            MetaCliApplications.Contains(appName))
        {
            return MetaDocsProductFamily.Meta;
        }

        return MetaDocsProductFamily.MetaBi;
    }

    private static string FindFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind,
        string name) =>
        model.DocumentationFactList
            .Where(row =>
                string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public readonly record struct MetaDocsPublicReferenceClassification(
    MetaDocsProductFamily ProductFamily,
    MetaDocsReferenceSurface Surface,
    string SortKey);

public enum MetaDocsProductFamily
{
    Meta = 0,
    MetaBi = 1,
}

public enum MetaDocsReferenceSurface
{
    Cli = 0,
    Models = 1,
}
