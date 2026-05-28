using System.Reflection;
using System.Globalization;
using System.Xml.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService : IInstanceDiffService
{
    private (string ModelId, IReadOnlyDictionary<string, EqualEntityCatalog> CatalogByName) BuildEqualEntityCatalog(
        GenericModel model,
        Workspace diffWorkspace,
        IdentityAllocator identityAllocator)
    {
        var modelId = identityAllocator.NextId(ModelEntityName);
        AddDiffRecord(
            diffWorkspace,
            ModelEntityName,
            modelId,
            new Dictionary<string, string?>
            {
                ["Name"] = model.Name,
            });

        var catalogByName = new Dictionary<string, EqualEntityCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = identityAllocator.NextId(EntityEntityName);
            AddDiffRecord(
                diffWorkspace,
                EntityEntityName,
                entityId,
                new Dictionary<string, string?>
                {
                    ["Name"] = entity.Name,
                    ["ModelId"] = modelId,
                });

            var propertyNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entity.Properties)
            {
                if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                propertyNames.Add(property.Name);
            }

            foreach (var relationship in entity.Relationships)
            {
                propertyNames.Add(relationship.GetColumnName());
            }

            var propertyIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in propertyNames.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var propertyId = identityAllocator.NextId(PropertyEntityName);
                propertyIdByName[propertyName] = propertyId;
                AddDiffRecord(
                    diffWorkspace,
                    PropertyEntityName,
                    propertyId,
                    new Dictionary<string, string?>
                    {
                        ["Name"] = propertyName,
                        ["EntityId"] = entityId,
                    });
            }

            catalogByName[entity.Name] = new EqualEntityCatalog(
                EntityId: entityId,
                EntityName: entity.Name,
                ModelEntity: entity,
                PropertyIdByName: propertyIdByName,
                OrderedPropertyNames: propertyIdByName.Keys
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        return (modelId, catalogByName);
    }

    private EqualSideData BuildEqualSideData(
        Workspace sourceWorkspace,
        Workspace diffWorkspace,
        IReadOnlyDictionary<string, EqualEntityCatalog> catalogByEntityName,
        bool leftSide,
        string diffId,
        IdentityAllocator identityAllocator)
    {
        var rowEntityName = leftSide ? ModelLeftEntityInstanceEntityName : ModelRightEntityInstanceEntityName;
        var propertyEntityName = leftSide ? ModelLeftPropertyInstanceEntityName : ModelRightPropertyInstanceEntityName;

        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowCount = 0;
        var propertyCount = 0;

        foreach (var catalog in catalogByEntityName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var rows = BuildRecordMap(sourceWorkspace, catalog.EntityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var rowInstanceId = identityAllocator.NextId(rowEntityName);
                AddDiffRecord(
                    diffWorkspace,
                    rowEntityName,
                    rowInstanceId,
                    new Dictionary<string, string?>
                    {
                        ["DiffId"] = diffId,
                        ["EntityId"] = catalog.EntityId,
                        ["EntityInstanceIdentifier"] = row.Id,
                    });
                rowCount++;
                var rowKey = CreateEntityInstanceKey(catalog.EntityId, row.Id);
                rowSet.Add(rowKey);
                entityInstanceIdByRowKey[rowKey] = rowInstanceId;

                foreach (var propertyName in catalog.OrderedPropertyNames)
                {
                    if (!TryGetPropertyLikeValue(catalog.ModelEntity, row, propertyName, out var propertyValue))
                    {
                        continue;
                    }

                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Property catalog for entity '{catalog.EntityName}' is missing '{propertyName}'.");
                    }

                    var propertyInstanceId = identityAllocator.NextId(propertyEntityName);
                    AddDiffRecord(
                        diffWorkspace,
                        propertyEntityName,
                        propertyInstanceId,
                        new Dictionary<string, string?>
                        {
                            [leftSide ? "ModelLeftEntityInstanceId" : "ModelRightEntityInstanceId"] = rowInstanceId,
                            ["PropertyId"] = propertyId,
                            ["Value"] = propertyValue,
                        });
                    propertyCount++;

                    var tupleKey = CreatePropertyTupleKey(catalog.EntityId, row.Id, propertyId, propertyValue);
                    propertySet.Add(tupleKey);
                    propertyInstanceIdByTupleKey[tupleKey] = propertyInstanceId;
                    valueByEntityRowPropertyKey[CreateEntityPropertyIdentityKey(catalog.EntityId, row.Id, propertyId)] = propertyValue;
                }
            }
        }

        return new EqualSideData(
            RowSet: rowSet,
            EntityInstanceIdByRowKey: entityInstanceIdByRowKey,
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityRowPropertyKey: valueByEntityRowPropertyKey,
            RowCount: rowCount,
            PropertyCount: propertyCount);
    }

    private InstanceDiffBuildResult BuildEqualInstanceDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        string rightWorkspacePath)
    {
        var diffWorkspacePath = ResolveInstanceDiffOutputPath(rightWorkspacePath, "instance-diff");
        var diffWorkspace = CreateWorkspaceFromDefinition(InstanceDiffEqualWorkspaceDefinition.Value, diffWorkspacePath);
        var identityAllocator = new IdentityAllocator();

        var (modelId, catalogByEntityName) = BuildEqualEntityCatalog(leftWorkspace.Model, diffWorkspace, identityAllocator);
        var diffId = identityAllocator.NextId(DiffEntityName);
        AddDiffRecord(
            diffWorkspace,
            DiffEntityName,
            diffId,
            new Dictionary<string, string?>
            {
                ["Name"] = "instance-diff",
                ["ModelId"] = modelId,
                ["DiffModelVersion"] = "1.0",
            });

        var leftSide = BuildEqualSideData(
            leftWorkspace,
            diffWorkspace,
            catalogByEntityName,
            leftSide: true,
            diffId,
            identityAllocator);
        var rightSide = BuildEqualSideData(
            rightWorkspace,
            diffWorkspace,
            catalogByEntityName,
            leftSide: false,
            diffId,
            identityAllocator);

        var leftEntityNotInRight = leftSide.RowSet
            .Except(rightSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightEntityNotInLeft = rightSide.RowSet
            .Except(leftSide.RowSet, StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var sharedEntityKeys = leftSide.RowSet
            .Intersect(rightSide.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractEntityInstanceKeyFromPropertyTuple(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        var leftNotInRight = leftSide.PropertySet
            .Except(rightSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightNotInLeft = rightSide.PropertySet
            .Except(leftSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        foreach (var rowKey in leftEntityNotInRight)
        {
            if (!leftSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftEntityInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftEntityInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var rowKey in rightEntityNotInLeft)
        {
            if (!rightSide.EntityInstanceIdByRowKey.TryGetValue(rowKey, out var entityInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightEntityInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightEntityInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightEntityInstanceId"] = entityInstanceId,
                });
        }

        foreach (var tupleKey in leftNotInRight)
        {
            if (!leftSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelLeftPropertyInstanceNotInRightEntityName,
                identityAllocator.NextId(ModelLeftPropertyInstanceNotInRightEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelLeftPropertyInstanceId"] = propertyInstanceId,
                });
        }

        foreach (var tupleKey in rightNotInLeft)
        {
            if (!rightSide.PropertyInstanceIdByTupleKey.TryGetValue(tupleKey, out var propertyInstanceId))
            {
                continue;
            }

            AddDiffRecord(
                diffWorkspace,
                ModelRightPropertyInstanceNotInLeftEntityName,
                identityAllocator.NextId(ModelRightPropertyInstanceNotInLeftEntityName),
                new Dictionary<string, string?>
                {
                    ["ModelRightPropertyInstanceId"] = propertyInstanceId,
                });
        }

        var hasDifferences = leftEntityNotInRight.Count > 0 ||
                             rightEntityNotInLeft.Count > 0 ||
                             leftNotInRight.Count > 0 ||
                             rightNotInLeft.Count > 0 ||
                             !new HashSet<string>(leftSide.RowSet, StringComparer.Ordinal).SetEquals(rightSide.RowSet);

        return new InstanceDiffBuildResult(
            DiffWorkspace: diffWorkspace,
            DiffWorkspacePath: diffWorkspacePath,
            HasDifferences: hasDifferences,
            LeftRowCount: leftSide.RowCount,
            RightRowCount: rightSide.RowCount,
            LeftPropertyCount: leftSide.PropertyCount,
            RightPropertyCount: rightSide.PropertyCount,
            LeftNotInRightCount: leftNotInRight.Count,
            RightNotInLeftCount: rightNotInLeft.Count);
    }

    private static EqualDiffData ParseEqualDiffWorkspace(Workspace diffWorkspace)
    {
        if (!string.Equals(diffWorkspace.Model.Name, InstanceDiffEqualModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' is not an {InstanceDiffEqualModelName} workspace.");
        }

        if (!IsModelContract(diffWorkspace.Model, InstanceDiffEqualModelSignature.Value))
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' does not match the fixed {InstanceDiffEqualModelName} model contract.");
        }

        var diffRows = BuildRecordMap(diffWorkspace, DiffEntityName);
        if (diffRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"workspace '{diffWorkspace.WorkspaceRootPath}' must contain exactly one '{DiffEntityName}' row.");
        }

        var diffRow = diffRows.Values.Single();
        var diffId = diffRow.Id;
        var modelId = RequireValue(diffRow, "ModelId", DiffEntityName);
        var diffModelVersion = RequireValue(diffRow, "DiffModelVersion", DiffEntityName);
        if (!string.Equals(diffModelVersion, "1.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Diff row '{diffId}' uses unsupported DiffModelVersion '{diffModelVersion}'.");
        }

        var modelRows = BuildRecordMap(diffWorkspace, ModelEntityName);
        if (!modelRows.ContainsKey(modelId))
        {
            throw new InvalidOperationException($"Diff row '{diffId}' references missing Model '{modelId}'.");
        }

        var entityRows = BuildRecordMap(diffWorkspace, EntityEntityName);
        var entityCatalogByName = new Dictionary<string, EqualEntityCatalog>(StringComparer.OrdinalIgnoreCase);
        var entityIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityRow in entityRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = entityRow.Id;
            var entityName = RequireValue(entityRow, "Name", EntityEntityName);
            var rowModelId = RequireValue(entityRow, "ModelId", EntityEntityName);
            if (!string.Equals(rowModelId, modelId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' references ModelId '{rowModelId}', expected '{modelId}'.");
            }

            if (!entityIdByName.TryAdd(entityName, entityId))
            {
                throw new InvalidOperationException($"Entity table contains duplicate Name '{entityName}'.");
            }

            entityNameById[entityId] = entityName;
        }

        var propertyRows = BuildRecordMap(diffWorkspace, PropertyEntityName);
        var propertyNamesByEntityId = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var propertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var propertyIdByEntityAndName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyRow in propertyRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var propertyId = propertyRow.Id;
            var propertyName = RequireValue(propertyRow, "Name", PropertyEntityName);
            if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Property table must not include special identity property 'Id' (row '{propertyId}').");
            }

            var entityId = RequireValue(propertyRow, "EntityId", PropertyEntityName);
            if (!entityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"Property '{propertyId}' references missing Entity '{entityId}'.");
            }

            if (!propertyNamesByEntityId.TryGetValue(entityId, out var names))
            {
                names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                propertyNamesByEntityId[entityId] = names;
            }

            if (!names.Add(propertyName))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityId}' has duplicate Property Name '{propertyName}' in diff workspace.");
            }

            propertyNameById[propertyId] = propertyName;
            propertyIdByEntityAndName[entityId + "\n" + propertyName] = propertyId;
        }

        var orderedPropertiesByEntity = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityName in entityIdByName.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var entityId = entityIdByName[entityName];
            var ordered = propertyNamesByEntityId.TryGetValue(entityId, out var names)
                ? names.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            orderedPropertiesByEntity[entityName] = ordered;

            var propertyIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in ordered)
            {
                propertyIdByName[propertyName] = propertyIdByEntityAndName[entityId + "\n" + propertyName];
            }

            entityCatalogByName[entityName] = new EqualEntityCatalog(
                EntityId: entityId,
                EntityName: entityName,
                ModelEntity: new GenericEntity { Name = entityName },
                PropertyIdByName: propertyIdByName,
                OrderedPropertyNames: ordered);
        }

        var leftRows = ParseSideRows(
            diffWorkspace,
            ModelLeftEntityInstanceEntityName,
            "EntityId",
            entityRows,
            expectedDiffId: diffId);
        var rightRows = ParseSideRows(
            diffWorkspace,
            ModelRightEntityInstanceEntityName,
            "EntityId",
            entityRows,
            expectedDiffId: diffId);
        var leftProperties = ParseEqualSideProperties(
            diffWorkspace,
            ModelLeftPropertyInstanceEntityName,
            "ModelLeftEntityInstanceId",
            "PropertyId",
            leftRows.RowIdentityByRowInstanceId,
            propertyRows);
        var rightProperties = ParseEqualSideProperties(
            diffWorkspace,
            ModelRightPropertyInstanceEntityName,
            "ModelRightEntityInstanceId",
            "PropertyId",
            rightRows.RowIdentityByRowInstanceId,
            propertyRows);

        ValidateEqualEntityNotInRows(
            diffWorkspace,
            ModelLeftEntityInstanceNotInRightEntityName,
            "ModelLeftEntityInstanceId",
            leftRows.EntityInstanceIdByRowKey,
            leftRows.RowSet.Except(rightRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        ValidateEqualEntityNotInRows(
            diffWorkspace,
            ModelRightEntityInstanceNotInLeftEntityName,
            "ModelRightEntityInstanceId",
            rightRows.EntityInstanceIdByRowKey,
            rightRows.RowSet.Except(leftRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));

        var sharedEntityKeys = leftRows.RowSet
            .Intersect(rightRows.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractEntityInstanceKeyFromPropertyTupleForValidation(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        ValidateEqualNotInRows(
            diffWorkspace,
            ModelLeftPropertyInstanceNotInRightEntityName,
            "ModelLeftPropertyInstanceId",
            leftProperties.PropertyInstanceIdByTupleKey,
            leftProperties.PropertySet
                .Except(rightProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));
        ValidateEqualNotInRows(
            diffWorkspace,
            ModelRightPropertyInstanceNotInLeftEntityName,
            "ModelRightPropertyInstanceId",
            rightProperties.PropertyInstanceIdByTupleKey,
            rightProperties.PropertySet
                .Except(leftProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractEntityInstanceKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));

        return new EqualDiffData(
            EntityCatalogByName: entityCatalogByName,
            OrderedPropertiesByEntity: orderedPropertiesByEntity,
            LeftRowSet: leftRows.RowSet,
            RightRowSet: rightRows.RowSet,
            LeftPropertySet: leftProperties.PropertySet,
            RightPropertySet: rightProperties.PropertySet,
            RightValueByEntityRowPropertyKey: rightProperties.ValueByEntityRowPropertyKey,
            DiffId: diffId);
    }

    private sealed record ParsedSideRows(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyDictionary<string, (string EntityId, string EntityInstanceIdentifier)> RowIdentityByRowInstanceId);

    private static ParsedSideRows ParseSideRows(
        Workspace diffWorkspace,
        string rowEntityName,
        string entityIdPropertyName,
        IReadOnlyDictionary<string, GenericRecord> entityRowsById,
        string expectedDiffId)
    {
        var rows = BuildRecordMap(diffWorkspace, rowEntityName);
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowIdentityByRowInstanceId = new Dictionary<string, (string EntityId, string EntityInstanceIdentifier)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var diffId = RequireValue(row, "DiffId", rowEntityName);
            if (!string.Equals(diffId, expectedDiffId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references DiffId '{diffId}', expected '{expectedDiffId}'.");
            }

            var entityId = RequireValue(row, entityIdPropertyName, rowEntityName);
            if (!entityRowsById.TryGetValue(entityId, out var entityRow))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references missing Entity '{entityId}'.");
            }

            _ = entityRow;
            var entityInstanceIdentifier = RequireValue(row, "EntityInstanceIdentifier", rowEntityName);
            var rowKey = CreateEntityInstanceKey(entityId, entityInstanceIdentifier);
            rowIdentityByRowInstanceId[row.Id] = (entityId, entityInstanceIdentifier);
            rowSet.Add(rowKey);
            entityInstanceIdByRowKey[rowKey] = row.Id;
        }

        return new ParsedSideRows(rowSet, entityInstanceIdByRowKey, rowIdentityByRowInstanceId);
    }

    private sealed record ParsedEqualSideProperties(
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityRowPropertyKey);

    private static ParsedEqualSideProperties ParseEqualSideProperties(
        Workspace diffWorkspace,
        string propertyEntityName,
        string rowInstanceIdPropertyName,
        string propertyIdPropertyName,
        IReadOnlyDictionary<string, (string EntityId, string EntityInstanceIdentifier)> rowIdentityByRowInstanceId,
        IReadOnlyDictionary<string, GenericRecord> propertyRowsById)
    {
        var rows = BuildRecordMap(diffWorkspace, propertyEntityName);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityKeySet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var rowInstanceId = RequireValue(row, rowInstanceIdPropertyName, propertyEntityName);
            if (!rowIdentityByRowInstanceId.TryGetValue(rowInstanceId, out var rowIdentity))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing row instance '{rowInstanceId}'.");
            }

            var propertyId = RequireValue(row, propertyIdPropertyName, propertyEntityName);
            if (!propertyRowsById.ContainsKey(propertyId))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing Property '{propertyId}'.");
            }

            if (!row.Values.TryGetValue("Value", out var storedValue))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' is missing required value 'Value'.");
            }

            if (storedValue == null)
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' contains null for required value 'Value'.");
            }

            var value = storedValue;
            var tupleKey = CreatePropertyTupleKey(
                rowIdentity.EntityId,
                rowIdentity.EntityInstanceIdentifier,
                propertyId,
                value);
            var identityKey = CreateEntityPropertyIdentityKey(
                rowIdentity.EntityId,
                rowIdentity.EntityInstanceIdentifier,
                propertyId);
            if (!identityKeySet.Add(identityKey))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} contains duplicate property identity '{identityKey}'.");
            }

            propertySet.Add(tupleKey);
            propertyInstanceIdByTupleKey[tupleKey] = row.Id;
            valueByEntityRowPropertyKey[identityKey] = value;
        }

        return new ParsedEqualSideProperties(
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityRowPropertyKey: valueByEntityRowPropertyKey);
    }

    private static void ValidateEqualEntityNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string entityInstanceIdFieldName,
        IReadOnlyDictionary<string, string> entityInstanceIdByRowKey,
        IReadOnlySet<string> expectedRowKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualRowKeys = new HashSet<string>(StringComparer.Ordinal);
        var rowKeyByEntityInstanceId = entityInstanceIdByRowKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var entityInstanceId = RequireValue(row, entityInstanceIdFieldName, notInEntityName);
            if (!rowKeyByEntityInstanceId.TryGetValue(entityInstanceId, out var rowKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing entity instance '{entityInstanceId}'.");
            }

            actualRowKeys.Add(rowKey);
        }

        if (!actualRowKeys.SetEquals(expectedRowKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private static void ValidateEqualNotInRows(
        Workspace diffWorkspace,
        string notInEntityName,
        string propertyInstanceIdFieldName,
        IReadOnlyDictionary<string, string> propertyInstanceIdByTupleKey,
        IReadOnlySet<string> expectedTupleKeys)
    {
        var rows = BuildRecordMap(diffWorkspace, notInEntityName);
        var actualTupleKeys = new HashSet<string>(StringComparer.Ordinal);
        var tupleKeyByPropertyInstanceId = propertyInstanceIdByTupleKey
            .ToDictionary(item => item.Value, item => item.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var propertyInstanceId = RequireValue(row, propertyInstanceIdFieldName, notInEntityName);
            if (!tupleKeyByPropertyInstanceId.TryGetValue(propertyInstanceId, out var tupleKey))
            {
                throw new InvalidOperationException(
                    $"{notInEntityName} row '{row.Id}' references missing property instance '{propertyInstanceId}'.");
            }

            actualTupleKeys.Add(tupleKey);
        }

        if (!actualTupleKeys.SetEquals(expectedTupleKeys))
        {
            throw new InvalidOperationException($"{notInEntityName} does not match computed set difference.");
        }
    }

    private static string RequireValue(GenericRecord row, string key, string entityName)
    {
        if (!TryGetRecordFieldValue(row, key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Entity '{entityName}' row '{row.Id}' is missing required value '{key}'.");
        }

        return value;
    }

    private (HashSet<string> RowSet, HashSet<string> PropertySet, Dictionary<string, string> ValueByEntityRowPropertyKey)
        BuildWorkspaceSnapshotForEqualDiff(
            Workspace workspace,
            EqualDiffData diffData)
    {
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var valueByEntityRowPropertyKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var catalog in diffData.EntityCatalogByName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var entityName = catalog.EntityName;
            var entity = RequireEntity(workspace, entityName);
            var rows = BuildRecordMap(workspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                rowSet.Add(CreateEntityInstanceKey(catalog.EntityId, row.Id));
                foreach (var propertyName in diffData.OrderedPropertiesByEntity[entityName])
                {
                    if (!TryGetPropertyLikeValue(entity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Diff catalog for entity '{entityName}' is missing PropertyId for '{propertyName}'.");
                    }

                    propertySet.Add(CreatePropertyTupleKey(catalog.EntityId, row.Id, propertyId, value));
                    valueByEntityRowPropertyKey[CreateEntityPropertyIdentityKey(catalog.EntityId, row.Id, propertyId)] = value;
                }
            }
        }

        return (rowSet, propertySet, valueByEntityRowPropertyKey);
    }

    private void ApplyEqualRightSnapshotToWorkspace(
        Workspace targetWorkspace,
        EqualDiffData diffData)
    {
        foreach (var catalog in diffData.EntityCatalogByName.Values.OrderBy(item => item.EntityName, StringComparer.OrdinalIgnoreCase))
        {
            var entityName = catalog.EntityName;
            var entity = RequireEntity(targetWorkspace, entityName);
            var rightRowsForEntity = diffData.RightRowSet
                .Select(key =>
                {
                    var parts = key.Split('\n');
                    return (EntityId: UnescapeCanonical(parts[0]), EntityInstanceIdentifier: UnescapeCanonical(parts[1]));
                })
                .Where(item => string.Equals(item.EntityId, catalog.EntityId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.EntityInstanceIdentifier)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rows = targetWorkspace.Instance.GetOrCreateEntityRecords(entityName);
            rows.Clear();

            foreach (var rowId in rightRowsForEntity)
            {
                var row = new GenericRecord
                {
                    Id = rowId,
                };

                foreach (var propertyName in diffData.OrderedPropertiesByEntity[entityName])
                {
                    if (!catalog.PropertyIdByName.TryGetValue(propertyName, out var propertyId))
                    {
                        throw new InvalidOperationException(
                            $"Diff catalog for entity '{entityName}' is missing PropertyId for '{propertyName}'.");
                    }

                    var key = CreateEntityPropertyIdentityKey(catalog.EntityId, rowId, propertyId);
                    if (!diffData.RightValueByEntityRowPropertyKey.TryGetValue(key, out var value))
                    {
                        continue;
                    }

                    if (IsRelationshipProperty(entity, propertyName, out var relationshipEntity))
                    {
                        row.RelationshipIds[relationshipEntity] = value;
                    }
                    else
                    {
                        row.Values[propertyName] = value;
                    }
                }

                rows.Add(row);
            }
        }
    }

}
