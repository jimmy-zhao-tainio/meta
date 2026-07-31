using System.Collections.Generic;
using Meta.Core.Domain;

namespace Meta.Core.Operations;

public static class WorkspaceOpTypes
{
    public const string AddEntity = "add_entity";
    public const string DeleteEntity = "delete_entity";
    public const string RenameEntity = "rename_entity";
    public const string AddProperty = "add_property";
    public const string DeleteProperty = "delete_property";
    public const string RenameProperty = "rename_property";
    public const string ChangeNullability = "change_nullability";
    public const string AddRelationship = "add_relationship";
    public const string DeleteRelationship = "delete_relationship";
    public const string BulkUpsertRows = "bulk_upsert_rows";
    public const string DeleteRows = "delete_rows";
    public const string TransformInstances = "transform_instances";
}

public sealed class WorkspaceOp
{
    public string Type { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string NewEntityName { get; set; } = string.Empty;
    public GenericProperty? Property { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string NewPropertyName { get; set; } = string.Empty;
    public string? PropertyDefaultValue { get; set; }
    public bool? IsNullable { get; set; }
    public string RelatedEntity { get; set; } = string.Empty;
    public string RelatedRole { get; set; } = string.Empty;
    public string RelatedDefaultId { get; set; } = string.Empty;
    public List<RowPatch> RowPatches { get; set; } = new();
    public List<string> Ids { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public sealed class RowPatch
{
    public string Id { get; set; } = string.Empty;
    public bool ReplaceExisting { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
    public Dictionary<string, string> RelationshipIds { get; set; } = new();
}

