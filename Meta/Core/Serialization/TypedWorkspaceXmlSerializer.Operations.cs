using System.Reflection;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Serialization;

public static partial class TypedWorkspaceXmlSerializer
{
    internal static GenericMetadataState CaptureOperationState<TModel>(TModel model)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);

        var modelMap = GetModelMap(typeof(TModel));
        ValidateForSave(model, modelMap);

        var instance = new GenericInstance
        {
            ModelName = modelMap.RootElementName,
        };

        foreach (var entityMap in modelMap.EntityMaps)
        {
            var records = instance.GetOrCreateEntityRecords(entityMap.EntityName);
            foreach (var row in entityMap.ShardProperty.GetList(model))
            {
                if (row == null)
                {
                    throw new InvalidOperationException(
                        $"Entity '{entityMap.EntityName}' contains a null row.");
                }

                var record = new GenericRecord
                {
                    Id = GetRequiredId(
                        entityMap,
                        row,
                        $"Entity '{entityMap.EntityName}' contains a row with empty Id."),
                };

                foreach (var scalar in entityMap.ScalarProperties)
                {
                    if (scalar.Property.GetValue(row) is string value)
                    {
                        record.Values[scalar.XmlElementName] = value;
                    }
                }

                foreach (var relationship in entityMap.RelationshipProperties)
                {
                    var target = relationship.Property.GetValue(row);
                    if (target == null)
                    {
                        continue;
                    }

                    var targetEntity = modelMap.EntityMapsByName[relationship.TargetEntityName];
                    record.RelationshipIds[relationship.Name] = GetRequiredId(
                        targetEntity,
                        target,
                        $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{record.Id}' references a target with empty Id.");
                }

                records.Add(record);
            }
        }

        return new GenericMetadataState(BuildGenericModel(modelMap), instance);
    }

    internal static InsertRecordOperation CaptureInsertOperation<TModel, TEntity>(
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);

        var modelMap = GetModelMap(typeof(TModel));
        var entityMap = RequireEntityMap(modelMap, typeof(TEntity));
        var id = GetRequiredId(
            entityMap,
            row,
            $"Entity '{entityMap.EntityName}' contains a row with empty Id.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scalar in entityMap.ScalarProperties)
        {
            if (scalar.Property.GetValue(row) is string value)
            {
                values.Add(scalar.XmlElementName, value);
            }
        }

        var relationshipIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in entityMap.RelationshipProperties)
        {
            var target = relationship.Property.GetValue(row);
            if (target == null)
            {
                continue;
            }

            var targetEntity = modelMap.EntityMapsByName[relationship.TargetEntityName];
            var targetId = GetRequiredId(
                targetEntity,
                target,
                $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{id}' references a target with empty Id.");
            relationshipIds.Add(relationship.Name, targetId);
        }

        return new InsertRecordOperation(
            entityMap.EntityName,
            id,
            values,
            relationshipIds);
    }

    internal static void RequireOperationInsert<TModel, TEntity>(
        TModel model,
        TEntity row,
        InsertRecordOperation operation)
        where TModel : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(operation);

        var modelMap = GetModelMap(typeof(TModel));
        var entityMap = RequireEntityMap(modelMap, typeof(TEntity));
        var rows = entityMap.ShardProperty.GetList(model);
        if (rows.Cast<object>().Any(candidate => ReferenceEquals(candidate, row)))
        {
            throw new InvalidOperationException(
                $"Entity '{entityMap.EntityName}' already contains the supplied row object.");
        }

        var currentOperation = CaptureInsertOperation<TModel, TEntity>(row);
        if (!InsertOperationsAreEqual(operation, currentOperation))
        {
            throw new InvalidOperationException(
                $"Entity '{entityMap.EntityName}' row '{operation.Id}' changed after its typed operation plan was created.");
        }

        foreach (var relationship in entityMap.RelationshipProperties)
        {
            var target = relationship.Property.GetValue(row);
            if (target == null)
            {
                continue;
            }

            var targetEntity = modelMap.EntityMapsByName[relationship.TargetEntityName];
            var targetId = GetRequiredId(
                targetEntity,
                target,
                $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{operation.Id}' references a target with empty Id.");
            var targetRows = targetEntity.ShardProperty.GetList(model);
            if (!targetRows.Cast<object>().Any(candidate =>
                    ReferenceEquals(candidate, target)))
            {
                throw new InvalidOperationException(
                    $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{operation.Id}' references an object that is not the canonical row for Id '{targetId}'.");
            }
        }
    }

    internal static (string EntityName, string Id) RequireOperationRow<TModel, TEntity>(
        TModel model,
        TEntity row,
        (string EntityName, string Id)? expectedAddress = null)
        where TModel : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(row);

        var modelMap = GetModelMap(typeof(TModel));
        var entityMap = RequireEntityMap(modelMap, typeof(TEntity));
        var id = GetRequiredId(
            entityMap,
            row,
            $"Entity '{entityMap.EntityName}' contains a row with empty Id.");
        if (expectedAddress is { } expected &&
            (!string.Equals(
                 expected.EntityName,
                 entityMap.EntityName,
                 StringComparison.Ordinal) ||
             !string.Equals(expected.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Entity '{expected.EntityName}' row '{expected.Id}' changed identity after its typed operation plan was created.");
        }

        var rows = entityMap.ShardProperty.GetList(model);
        if (!rows.Cast<object>().Any(candidate => ReferenceEquals(candidate, row)))
        {
            throw new InvalidOperationException(
                $"Entity '{entityMap.EntityName}' row '{id}' is not the canonical row object in this model.");
        }

        return (entityMap.EntityName, id);
    }

    internal static (string EntityName, string Id) CaptureOperationRow<TModel, TEntity>(
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);

        var entityMap = RequireEntityMap(
            GetModelMap(typeof(TModel)),
            typeof(TEntity));
        return (
            entityMap.EntityName,
            GetRequiredId(
                entityMap,
                row,
                $"Entity '{entityMap.EntityName}' contains a row with empty Id."));
    }

    internal static string RequireOperationScalar<TModel, TEntity>(PropertyInfo property)
        where TModel : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(property);

        var modelMap = GetModelMap(typeof(TModel));
        var entityMap = RequireEntityMap(modelMap, typeof(TEntity));
        var scalar = entityMap.ScalarProperties.FirstOrDefault(
            item => IsSameProperty(item.Property, property));
        return scalar?.XmlElementName
               ?? throw new InvalidOperationException(
                   $"Property '{typeof(TEntity).FullName}.{property.Name}' is not a modeled scalar.");
    }

    internal static string RequireOperationRelationship<TModel, TEntity, TTarget>(
        PropertyInfo property)
        where TModel : class
        where TEntity : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(property);

        var modelMap = GetModelMap(typeof(TModel));
        var entityMap = RequireEntityMap(modelMap, typeof(TEntity));
        var relationship = entityMap.RelationshipProperties.FirstOrDefault(
            item =>
                IsSameProperty(item.Property, property) &&
                item.Property.PropertyType == typeof(TTarget));
        return relationship?.Name
               ?? throw new InvalidOperationException(
                   $"Property '{typeof(TEntity).FullName}.{property.Name}' is not a modeled relationship to '{typeof(TTarget).FullName}'.");
    }

    internal static void AddOperationRow<TModel, TEntity>(
        TModel model,
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        var entityMap = RequireEntityMap(GetModelMap(typeof(TModel)), typeof(TEntity));
        entityMap.ShardProperty.GetList(model).Add(row);
    }

    internal static void RemoveOperationRow<TModel, TEntity>(
        TModel model,
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        var entityMap = RequireEntityMap(GetModelMap(typeof(TModel)), typeof(TEntity));
        var rows = entityMap.ShardProperty.GetList(model);
        if (!rows.Contains(row))
        {
            throw new InvalidOperationException(
                $"Entity '{entityMap.EntityName}' does not contain the supplied row object.");
        }

        rows.Remove(row);
    }

    internal static int IndexOfOperationRow<TModel, TEntity>(
        TModel model,
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        var entityMap = RequireEntityMap(GetModelMap(typeof(TModel)), typeof(TEntity));
        var index = entityMap.ShardProperty.GetList(model).IndexOf(row);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Entity '{entityMap.EntityName}' does not contain the supplied row object.");
        }

        return index;
    }

    internal static void InsertOperationRow<TModel, TEntity>(
        TModel model,
        int index,
        TEntity row)
        where TModel : class
        where TEntity : class
    {
        var entityMap = RequireEntityMap(GetModelMap(typeof(TModel)), typeof(TEntity));
        entityMap.ShardProperty.GetList(model).Insert(index, row);
    }

    internal static void RestoreOperationState<TModel>(
        TModel target,
        GenericMetadataState state)
        where TModel : class, IMetaWorkspaceModel<TModel>
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(state);

        var modelMap = GetModelMap(typeof(TModel));
        var expectedModel = BuildGenericModel(modelMap);
        if (!string.Equals(
                expectedModel.ComputeContractSignature(),
                state.Model.ComputeContractSignature(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Metadata state model '{state.Model.Name}' does not match typed model '{modelMap.RootElementName}'.");
        }

        if (!string.Equals(
                state.Instance.ModelName,
                modelMap.RootElementName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Metadata instance '{state.Instance.ModelName}' does not match typed model '{modelMap.RootElementName}'.");
        }

        var knownEntities = modelMap.EntityMaps
            .Select(item => item.EntityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownEntity = state.Instance.RecordsByEntity.Keys.FirstOrDefault(
            name => !knownEntities.Contains(name));
        if (unknownEntity != null)
        {
            throw new InvalidOperationException(
                $"Metadata state contains unknown entity '{unknownEntity}'.");
        }

        var staged = TModel.CreateEmpty();
        var rowsByEntity = new Dictionary<string, List<(GenericRecord Record, object Row)>>(
            StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, Dictionary<string, object>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entityMap in modelMap.EntityMaps)
        {
            var stagedRows = entityMap.ShardProperty.GetList(staged);
            var rowPairs = new List<(GenericRecord Record, object Row)>();
            var rowsById = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var records = state.Instance.RecordsByEntity.TryGetValue(
                entityMap.EntityName,
                out var entityRecords)
                ? entityRecords
                : [];
            foreach (var record in records
                         .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                var row = Activator.CreateInstance(entityMap.ItemType)
                          ?? throw new InvalidOperationException(
                              $"Could not create entity '{entityMap.ItemType.FullName}'.");
                entityMap.IdProperty.SetValue(row, record.Id);

                var knownScalars = entityMap.ScalarProperties
                    .ToDictionary(item => item.XmlElementName, StringComparer.OrdinalIgnoreCase);
                foreach (var value in record.Values)
                {
                    if (!knownScalars.TryGetValue(value.Key, out var scalar))
                    {
                        throw new InvalidOperationException(
                            $"Metadata state contains unknown property '{entityMap.EntityName}.{value.Key}'.");
                    }

                    scalar.Property.SetValue(row, value.Value);
                }

                foreach (var scalar in entityMap.ScalarProperties)
                {
                    if (!record.Values.ContainsKey(scalar.XmlElementName))
                    {
                        scalar.Property.SetValue(row, null);
                    }
                }

                var id = GetRequiredId(
                    entityMap,
                    row,
                    $"Entity '{entityMap.EntityName}' contains a row with empty Id.");
                if (!rowsById.TryAdd(id, row))
                {
                    throw new InvalidOperationException(
                        $"Entity '{entityMap.EntityName}' contains duplicate Id '{id}'.");
                }

                stagedRows.Add(row);
                rowPairs.Add((record, row));
            }

            rowsByEntity.Add(entityMap.EntityName, rowPairs);
            indexes.Add(entityMap.EntityName, rowsById);
        }

        foreach (var entityMap in modelMap.EntityMaps)
        {
            var knownRelationships = entityMap.RelationshipProperties
                .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var (record, row) in rowsByEntity[entityMap.EntityName])
            {
                foreach (var relationshipValue in record.RelationshipIds)
                {
                    if (!knownRelationships.TryGetValue(
                            relationshipValue.Key,
                            out var relationship))
                    {
                        throw new InvalidOperationException(
                            $"Metadata state contains unknown relationship '{entityMap.EntityName}.{relationshipValue.Key}'.");
                    }

                    if (!indexes[relationship.TargetEntityName].TryGetValue(
                            relationshipValue.Value,
                            out var targetRow))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entityMap.EntityName}.{relationship.Name}' on row '{entityMap.EntityName}:{record.Id}' points to missing Id '{relationshipValue.Value}'.");
                    }

                    relationship.Property.SetValue(row, targetRow);
                }

                foreach (var relationship in entityMap.RelationshipProperties)
                {
                    if (!record.RelationshipIds.ContainsKey(relationship.Name))
                    {
                        relationship.Property.SetValue(row, null);
                    }
                }
            }
        }

        ValidateForSave(staged, modelMap);
        foreach (var shardProperty in modelMap.ShardProperties)
        {
            var targetRows = shardProperty.GetList(target);
            var stagedRows = shardProperty.GetList(staged);
            targetRows.Clear();
            foreach (var row in stagedRows)
            {
                targetRows.Add(row);
            }
        }
    }

    private static EntityMap RequireEntityMap(ModelMap modelMap, Type entityType)
    {
        return modelMap.EntityMaps.FirstOrDefault(item => item.ItemType == entityType)
               ?? throw new InvalidOperationException(
                   $"Type '{entityType.FullName}' is not an entity in typed model '{modelMap.RootElementName}'.");
    }

    private static bool IsSameProperty(PropertyInfo left, PropertyInfo right)
    {
        return left == right ||
               (left.DeclaringType == right.DeclaringType &&
                string.Equals(left.Name, right.Name, StringComparison.Ordinal));
    }

    private static bool InsertOperationsAreEqual(
        InsertRecordOperation left,
        InsertRecordOperation right)
    {
        return string.Equals(
                   left.EntityName,
                   right.EntityName,
                   StringComparison.Ordinal) &&
               string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               DictionariesAreEqual(left.Values, right.Values) &&
               DictionariesAreEqual(
                   left.RelationshipIds,
                   right.RelationshipIds);
    }

    private static bool DictionariesAreEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.All(item =>
            right.TryGetValue(item.Key, out var rightValue) &&
            string.Equals(item.Value, rightValue, StringComparison.Ordinal));
    }
}
