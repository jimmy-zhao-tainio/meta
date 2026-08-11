using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Xml;

public static partial class TypedWorkspaceXmlSerializer
{
    internal static TModel FromInMemoryWorkspace<TModel>(
        InMemoryWorkspace workspace,
        Func<TModel> createModel)
        where TModel : class
    {
        EnsureValid(workspace, "Cannot map invalid metadata to a typed model.");
        var modelMap = GetModelMap(typeof(TModel));
        EnsureMatchingModel(workspace.Model, modelMap);

        var model = createModel() ??
            throw new InvalidOperationException(
                $"Could not create typed model '{typeof(TModel).FullName}'.");
        var typedRowsByEntity = new Dictionary<string, Dictionary<string, object>>(
            MetaName.Comparer);
        var pendingRelationships = new List<TypedPendingRelationship>();

        foreach (var entityMap in modelMap.EntityMaps)
        {
            var rowsById = new Dictionary<string, object>(MetaIdentity.Comparer);
            typedRowsByEntity.Add(entityMap.EntityName, rowsById);
            var targetList = entityMap.ShardProperty.GetList(model);
            var sourceRows = workspace.Instance.RecordsByEntity.TryGetValue(
                entityMap.EntityName,
                out var records)
                ? records
                : [];

            foreach (var sourceRow in sourceRows
                         .OrderBy(row => row.Id, MetaIdentity.Comparer)
                         .ThenBy(row => row.Id, StringComparer.Ordinal))
            {
                var typedRow = Activator.CreateInstance(entityMap.ItemType) ??
                    throw new InvalidOperationException(
                        $"Could not create entity '{entityMap.ItemType.FullName}'.");
                entityMap.IdProperty.SetValue(typedRow, sourceRow.Id);
                foreach (var scalar in entityMap.ScalarProperties)
                {
                    scalar.Property.SetValue(
                        typedRow,
                        sourceRow.Values.TryGetValue(
                            scalar.XmlElementName,
                            out var value)
                            ? value
                            : null);
                }

                targetList.Add(typedRow);
                rowsById.Add(sourceRow.Id, typedRow);
                pendingRelationships.Add(new TypedPendingRelationship(
                    entityMap,
                    typedRow,
                    sourceRow));
            }
        }

        foreach (var pending in pendingRelationships)
        {
            foreach (var relationship in pending.EntityMap.RelationshipProperties)
            {
                if (!pending.Source.RelationshipIds.TryGetValue(
                        relationship.Name,
                        out var targetId))
                {
                    relationship.Property.SetValue(pending.Target, null);
                    continue;
                }

                if (!typedRowsByEntity[relationship.TargetEntityName]
                        .TryGetValue(targetId, out var target))
                {
                    throw new InvalidOperationException(
                        $"Relationship '{pending.EntityMap.EntityName}.{relationship.Name}' on row '{pending.Source.Id}' points to missing Id '{targetId}'.");
                }

                relationship.Property.SetValue(pending.Target, target);
            }
        }

        ValidateForSave(model, modelMap);
        return model;
    }

    internal static InMemoryWorkspace ToInMemoryWorkspace<TModel>(TModel model)
        where TModel : class
    {
        var modelMap = GetModelMap(typeof(TModel));
        var indexes = ValidateForSave(model, modelMap);
        var genericModel = BuildGenericModel(modelMap);
        var instance = new GenericInstance
        {
            ModelName = genericModel.Name,
        };

        foreach (var entityMap in modelMap.EntityMaps)
        {
            var targetRows = instance.GetOrCreateEntityRecords(
                entityMap.EntityName);
            foreach (var typedRow in entityMap.ShardProperty.GetList(model)
                         .Cast<object>()
                         .OrderBy(row => GetId(entityMap, row), MetaIdentity.Comparer)
                         .ThenBy(row => GetId(entityMap, row), StringComparer.Ordinal))
            {
                var row = new GenericRecord
                {
                    Id = GetRequiredId(
                        entityMap,
                        typedRow,
                        $"Entity '{entityMap.EntityName}' contains a row with empty Id."),
                };

                foreach (var scalar in entityMap.ScalarProperties)
                {
                    if (scalar.Property.GetValue(typedRow) is string value)
                    {
                        row.Values.Add(scalar.XmlElementName, value);
                    }
                }

                foreach (var relationship in entityMap.RelationshipProperties)
                {
                    var target = relationship.Property.GetValue(typedRow);
                    if (target == null)
                    {
                        continue;
                    }

                    var targetEntity = modelMap.EntityMapsByName[
                        relationship.TargetEntityName];
                    var targetId = GetRequiredId(
                        targetEntity,
                        target,
                        $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{row.Id}' references a target with empty Id.");
                    if (!indexes[relationship.TargetEntityName]
                            .TryGetValue(targetId, out var canonical) ||
                        !ReferenceEquals(canonical, target))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{row.Id}' does not reference its canonical target.");
                    }

                    row.RelationshipIds.Add(relationship.Name, targetId);
                }

                targetRows.Add(row);
            }
        }

        var workspace = new InMemoryWorkspace(genericModel, instance);
        EnsureValid(workspace, "Typed model produced invalid metadata.");
        return workspace;
    }

    private static void EnsureMatchingModel(
        GenericModel actual,
        ModelMap modelMap)
    {
        var expected = BuildGenericModel(modelMap);
        var actualWorkspace = new InMemoryWorkspace(
            actual,
            new GenericInstance { ModelName = actual.Name });
        var expectedWorkspace = new InMemoryWorkspace(
            expected,
            new GenericInstance { ModelName = expected.Name });
        var difference = InMemoryWorkspaceComparer.FindDifference(
            expectedWorkspace,
            actualWorkspace);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"Typed model '{modelMap.RootElementName}' does not match the workspace model. {difference}");
        }
    }

    private static void EnsureValid(
        InMemoryWorkspace workspace,
        string message)
    {
        var diagnostics = WorkspaceValidator.Validate(
            workspace.Model,
            workspace.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue =>
                $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(
            message + " " + string.Join(" | ", errors));
    }

    private sealed record TypedPendingRelationship(
        EntityMap EntityMap,
        object Target,
        GenericRecord Source);
}
