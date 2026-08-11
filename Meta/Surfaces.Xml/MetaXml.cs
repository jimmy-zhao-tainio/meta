using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Xml;

public sealed record MetaXml(
    XDocument Model,
    XDocument Instance);

public static class MetaXmlCodec
{
    public static InMemoryWorkspace Read(
        XDocument modelDocument,
        params XDocument[] instanceDocuments)
    {
        ArgumentNullException.ThrowIfNull(modelDocument);
        ArgumentNullException.ThrowIfNull(instanceDocuments);

        var model = ModelXmlCodec.Load(modelDocument);
        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        foreach (var document in instanceDocuments)
        {
            ArgumentNullException.ThrowIfNull(document);
            InstanceXmlCodec.MergeDocument(
                instance,
                document,
                model);
        }

        var state = new InMemoryWorkspace(model, instance);
        EnsureValid(
            state,
            message => new InvalidDataException(message),
            "Cannot read XML as valid metadata.");
        return state;
    }

    public static MetaXml Write(InMemoryWorkspace state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureValid(
            state,
            message => new InvalidOperationException(message),
            "Cannot write XML for invalid metadata.");
        return new MetaXml(
            ModelXmlCodec.BuildDocument(state.Model),
            InstanceXmlCodec.BuildDocument(
                state.Model,
                state.Instance));
    }

    private static void EnsureValid(
        InMemoryWorkspace state,
        Func<string, Exception> createException,
        string message)
    {
        var diagnostics = WorkspaceValidator.Validate(
            state.Model,
            state.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        throw createException(
            message + " " + string.Join(" | ", errors));
    }
}
