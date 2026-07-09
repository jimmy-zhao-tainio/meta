namespace MetaDocs.Core;

public sealed class MetaDocsExampleAuthoringService
{
    private readonly MetaDocsQueryService query = new();

    public DocumentationExample UpsertExample(
        MetaDocsModel model,
        MetaDocsSubjectSelector selector,
        string id,
        string title,
        string summary,
        string sectionId,
        string body,
        string bodyFormat,
        string previousExampleId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var subject = query.ResolveSubject(model, selector);
        var example = model.DocumentationExampleList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (example is null)
        {
            example = new DocumentationExample
            {
                Id = id.Trim(),
                PreviousExample = ResolvePreviousExample(model, subject, previousExampleId)
                                  ?? OrderedExamples(model, subject).LastOrDefault(),
            };
            model.DocumentationExampleList.Add(example);
        }
        else if (!string.IsNullOrWhiteSpace(previousExampleId))
        {
            example.PreviousExample = ResolvePreviousExample(model, subject, previousExampleId)
                ?? throw new InvalidOperationException($"Could not resolve previous example '{previousExampleId}' for subject '{subject.DisplayName}'.");
        }

        if (example.DocumentationSubject is not null &&
            !ReferenceEquals(example.DocumentationSubject, subject))
        {
            throw new InvalidOperationException($"Example '{example.Id}' already belongs to '{example.DocumentationSubject.DisplayName}'.");
        }

        example.DocumentationSubject = subject;
        example.Title = title.Trim();
        example.Summary = summary?.Trim() ?? string.Empty;
        example.Origin = "Authored";
        example.ReviewStatus = "Current";

        UpsertSection(
            model,
            example.Id,
            sectionId,
            string.Empty,
            body,
            bodyFormat,
            previousSectionId: string.Empty);
        return example;
    }

    public DocumentationExampleSection UpsertSection(
        MetaDocsModel model,
        string exampleId,
        string id,
        string title,
        string body,
        string bodyFormat,
        string previousSectionId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(exampleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var example = ResolveExample(model, exampleId);
        var section = model.DocumentationExampleSectionList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (section is null)
        {
            section = new DocumentationExampleSection
            {
                Id = id.Trim(),
                PreviousSection = ResolvePreviousSection(model, example, previousSectionId)
                                  ?? OrderedSections(model, example).LastOrDefault(),
            };
            model.DocumentationExampleSectionList.Add(section);
        }
        else if (!string.IsNullOrWhiteSpace(previousSectionId))
        {
            section.PreviousSection = ResolvePreviousSection(model, example, previousSectionId)
                ?? throw new InvalidOperationException($"Could not resolve previous section '{previousSectionId}' for example '{example.Id}'.");
        }

        if (section.DocumentationExample is not null &&
            !ReferenceEquals(section.DocumentationExample, example))
        {
            throw new InvalidOperationException($"Example section '{section.Id}' already belongs to example '{section.DocumentationExample.Id}'.");
        }

        section.DocumentationExample = example;
        section.Title = title?.Trim() ?? string.Empty;
        section.Body = body.Trim();
        section.BodyFormat = string.IsNullOrWhiteSpace(bodyFormat) ? "PlainText" : bodyFormat.Trim();
        return section;
    }

    public DocumentationExampleCode UpsertCode(
        MetaDocsModel model,
        string sectionId,
        string id,
        string title,
        string language,
        string code,
        string previousCodeId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var section = ResolveSection(model, sectionId);
        var codeRow = model.DocumentationExampleCodeList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (codeRow is null)
        {
            codeRow = new DocumentationExampleCode
            {
                Id = id.Trim(),
                PreviousCode = ResolvePreviousCode(model, section, previousCodeId)
                               ?? OrderedCodes(model, section).LastOrDefault(),
            };
            model.DocumentationExampleCodeList.Add(codeRow);
        }
        else if (!string.IsNullOrWhiteSpace(previousCodeId))
        {
            codeRow.PreviousCode = ResolvePreviousCode(model, section, previousCodeId)
                ?? throw new InvalidOperationException($"Could not resolve previous code block '{previousCodeId}' for section '{section.Id}'.");
        }

        if (codeRow.DocumentationExampleSection is not null &&
            !ReferenceEquals(codeRow.DocumentationExampleSection, section))
        {
            throw new InvalidOperationException($"Example code block '{codeRow.Id}' already belongs to section '{codeRow.DocumentationExampleSection.Id}'.");
        }

        codeRow.DocumentationExampleSection = section;
        codeRow.Title = title?.Trim() ?? string.Empty;
        codeRow.Language = language?.Trim() ?? string.Empty;
        codeRow.Code = code.Trim();
        return codeRow;
    }

    private static DocumentationExample ResolveExample(MetaDocsModel model, string exampleId) =>
        model.DocumentationExampleList.FirstOrDefault(row =>
            string.Equals(row.Id, exampleId.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Could not resolve example '{exampleId}'.");

    private static DocumentationExampleSection ResolveSection(MetaDocsModel model, string sectionId) =>
        model.DocumentationExampleSectionList.FirstOrDefault(row =>
            string.Equals(row.Id, sectionId.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Could not resolve example section '{sectionId}'.");

    private static DocumentationExample? ResolvePreviousExample(
        MetaDocsModel model,
        DocumentationSubject subject,
        string previousExampleId)
    {
        if (string.IsNullOrWhiteSpace(previousExampleId))
        {
            return null;
        }

        var previous = model.DocumentationExampleList.FirstOrDefault(row =>
            string.Equals(row.Id, previousExampleId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (previous is null ||
            !ReferenceEquals(previous.DocumentationSubject, subject))
        {
            return null;
        }

        return previous;
    }

    private static DocumentationExampleSection? ResolvePreviousSection(
        MetaDocsModel model,
        DocumentationExample example,
        string previousSectionId)
    {
        if (string.IsNullOrWhiteSpace(previousSectionId))
        {
            return null;
        }

        var previous = model.DocumentationExampleSectionList.FirstOrDefault(row =>
            string.Equals(row.Id, previousSectionId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (previous is null ||
            !ReferenceEquals(previous.DocumentationExample, example))
        {
            return null;
        }

        return previous;
    }

    private static DocumentationExampleCode? ResolvePreviousCode(
        MetaDocsModel model,
        DocumentationExampleSection section,
        string previousCodeId)
    {
        if (string.IsNullOrWhiteSpace(previousCodeId))
        {
            return null;
        }

        var previous = model.DocumentationExampleCodeList.FirstOrDefault(row =>
            string.Equals(row.Id, previousCodeId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (previous is null ||
            !ReferenceEquals(previous.DocumentationExampleSection, section))
        {
            return null;
        }

        return previous;
    }

    private static IReadOnlyList<DocumentationExample> OrderedExamples(
        MetaDocsModel model,
        DocumentationSubject subject) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleList.Where(row => ReferenceEquals(row.DocumentationSubject, subject)),
                static row => row.PreviousExample,
                static row => row.Title)
            .ToArray();

    private static IReadOnlyList<DocumentationExampleSection> OrderedSections(
        MetaDocsModel model,
        DocumentationExample example) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleSectionList.Where(row => ReferenceEquals(row.DocumentationExample, example)),
                static row => row.PreviousSection,
                static row => row.Title ?? row.Id)
            .ToArray();

    private static IReadOnlyList<DocumentationExampleCode> OrderedCodes(
        MetaDocsModel model,
        DocumentationExampleSection section) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleCodeList.Where(row => ReferenceEquals(row.DocumentationExampleSection, section)),
                static row => row.PreviousCode,
                static row => row.Title ?? row.Id)
            .ToArray();
}
