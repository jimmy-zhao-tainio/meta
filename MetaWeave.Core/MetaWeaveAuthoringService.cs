using Meta.Core.Domain;

namespace MetaWeave.Core;

public interface IMetaWeaveAuthoringService
{
    void AddModelReference(Workspace weaveWorkspace, string alias, string modelName, string workspacePath);
    void AddPropertyBinding(
        Workspace weaveWorkspace,
        string name,
        string sourceModelAlias,
        string sourceEntity,
        string sourceProperty,
        string targetModelAlias,
        string targetEntity,
        string targetProperty);
}

public sealed class MetaWeaveAuthoringService : IMetaWeaveAuthoringService
{
    public void AddModelReference(Workspace weaveWorkspace, string alias, string modelName, string workspacePath)
    {
        ArgumentNullException.ThrowIfNull(weaveWorkspace);
        RequireNonEmpty(alias, nameof(alias));
        RequireNonEmpty(modelName, nameof(modelName));
        RequireNonEmpty(workspacePath, nameof(workspacePath));
        if (string.IsNullOrWhiteSpace(weaveWorkspace.WorkspaceRootPath))
        {
            throw new InvalidOperationException("Weave workspace root path is required.");
        }

        var records = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference");
        if (records.Any(record => string.Equals(GetRequiredValue(record, "Alias"), alias, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"ModelReference alias '{alias}' already exists.");
        }

        var normalizedWorkspacePath = NormalizeWorkspacePathForStorage(
            weaveWorkspace.WorkspaceRootPath,
            workspacePath);

        records.Add(new GenericRecord
        {
            Id = AllocateNumericId(records),
            Values =
            {
                ["Alias"] = alias,
                ["ModelName"] = modelName,
                ["WorkspacePath"] = normalizedWorkspacePath
            }
        });
    }

    public void AddPropertyBinding(
        Workspace weaveWorkspace,
        string name,
        string sourceModelAlias,
        string sourceEntity,
        string sourceProperty,
        string targetModelAlias,
        string targetEntity,
        string targetProperty)
    {
        ArgumentNullException.ThrowIfNull(weaveWorkspace);
        RequireNonEmpty(name, nameof(name));
        RequireNonEmpty(sourceModelAlias, nameof(sourceModelAlias));
        RequireNonEmpty(sourceEntity, nameof(sourceEntity));
        RequireNonEmpty(sourceProperty, nameof(sourceProperty));
        RequireNonEmpty(targetModelAlias, nameof(targetModelAlias));
        RequireNonEmpty(targetEntity, nameof(targetEntity));
        RequireNonEmpty(targetProperty, nameof(targetProperty));

        var modelReferences = weaveWorkspace.Instance.GetOrCreateEntityRecords("ModelReference");
        var sourceModel = modelReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Alias"), sourceModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{sourceModelAlias}' was not found.");
        var targetModel = modelReferences.SingleOrDefault(record => string.Equals(GetRequiredValue(record, "Alias"), targetModelAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"ModelReference alias '{targetModelAlias}' was not found.");

        var bindings = weaveWorkspace.Instance.GetOrCreateEntityRecords("PropertyBinding");
        if (bindings.Any(record => string.Equals(GetRequiredValue(record, "Name"), name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"PropertyBinding '{name}' already exists.");
        }

        if (bindings.Any(record =>
                string.Equals(GetRequiredValue(record, "SourceEntity"), sourceEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "SourceProperty"), sourceProperty, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetEntity"), targetEntity, StringComparison.Ordinal) &&
                string.Equals(GetRequiredValue(record, "TargetProperty"), targetProperty, StringComparison.Ordinal) &&
                string.Equals(record.RelationshipIds["SourceModelId"], sourceModel.Id, StringComparison.Ordinal) &&
                string.Equals(record.RelationshipIds["TargetModelId"], targetModel.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("An equivalent PropertyBinding already exists.");
        }

        bindings.Add(new GenericRecord
        {
            Id = AllocateNumericId(bindings),
            Values =
            {
                ["Name"] = name,
                ["SourceEntity"] = sourceEntity,
                ["SourceProperty"] = sourceProperty,
                ["TargetEntity"] = targetEntity,
                ["TargetProperty"] = targetProperty
            },
            RelationshipIds =
            {
                ["SourceModelId"] = sourceModel.Id,
                ["TargetModelId"] = targetModel.Id
            }
        });
    }

    private static void RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' must not be empty.", name);
        }
    }

    private static string GetRequiredValue(GenericRecord record, string propertyName)
    {
        if (!record.Values.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Record '{record.Id}' is missing required property '{propertyName}'.");
        }

        return value;
    }

    private static string AllocateNumericId(IReadOnlyCollection<GenericRecord> records)
    {
        var next = records
            .Select(record => int.TryParse(record.Id, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NormalizeWorkspacePathForStorage(string weaveWorkspaceRootPath, string workspacePath)
    {
        var absoluteTargetPath = Path.GetFullPath(workspacePath);
        var absoluteWeaveRootPath = Path.GetFullPath(weaveWorkspaceRootPath);
        var relativePath = Path.GetRelativePath(absoluteWeaveRootPath, absoluteTargetPath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ".";
        }

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }
}
