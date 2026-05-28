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
    private static string UnescapeCanonical(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static void CopyRowsByEntityWithIdentity(
        Workspace source,
        Workspace destination,
        string entityName,
        IdentityAllocator identityAllocator,
        IDictionary<string, Dictionary<string, string>> idMapByEntity,
        IReadOnlyDictionary<string, string>? referenceFields = null,
        IReadOnlySet<string>? includeSourceRowIds = null)
    {
        var sourceRows = BuildRecordMap(source, entityName).Values
            .Where(row => includeSourceRowIds == null || includeSourceRowIds.Contains(row.Id))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceRow in sourceRows)
        {
            var newId = identityAllocator.NextId(entityName);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in sourceRow.Values)
            {
                fields[pair.Key] = pair.Value;
            }

            foreach (var pair in sourceRow.RelationshipIds)
            {
                fields[pair.Key] = pair.Value;
            }

            foreach (var pair in fields.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (pair.Value == null)
                {
                    continue;
                }

                if (referenceFields != null &&
                    referenceFields.TryGetValue(pair.Key, out var referencedEntityName))
                {
                    if (!idMapByEntity.TryGetValue(referencedEntityName, out var referencedIdMap) ||
                        !referencedIdMap.TryGetValue(pair.Value, out var remappedId))
                    {
                        throw new InvalidOperationException(
                            $"{entityName} row '{sourceRow.Id}' references missing '{referencedEntityName}' id '{pair.Value}' via '{pair.Key}'.");
                    }

                    values[pair.Key] = remappedId;
                    continue;
                }

                values[pair.Key] = pair.Value;
            }

            AddDiffRecord(
                destination,
                entityName,
                newId,
                values);
            idMap[sourceRow.Id] = newId;
        }

        idMapByEntity[entityName] = idMap;
    }

    private static AlignmentCatalog ParseAlignmentCatalog(
        Workspace alignmentWorkspace,
        string expectedModelName,
        string expectedModelSignature)
    {
        if (!string.Equals(alignmentWorkspace.Model.Name, expectedModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' is not an {expectedModelName} workspace.");
        }

        if (!IsModelContract(alignmentWorkspace.Model, expectedModelSignature))
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' does not match the fixed {expectedModelName} model contract.");
        }

        var alignmentRows = BuildRecordMap(alignmentWorkspace, AlignmentEntityName);
        if (alignmentRows.Count != 1)
        {
            throw new InvalidOperationException(
                $"workspace '{alignmentWorkspace.WorkspaceRootPath}' must contain exactly one '{AlignmentEntityName}' row.");
        }

        var alignmentRow = alignmentRows.Values.Single();
        var alignmentId = alignmentRow.Id;
        var alignmentName = RequireValue(alignmentRow, "Name", AlignmentEntityName);
        var modelLeftId = RequireValue(alignmentRow, "ModelLeftId", AlignmentEntityName);
        var modelRightId = RequireValue(alignmentRow, "ModelRightId", AlignmentEntityName);

        var modelRows = BuildRecordMap(alignmentWorkspace, ModelEntityName);
        var modelLeftRows = BuildRecordMap(alignmentWorkspace, ModelLeftEntityName);
        var modelRightRows = BuildRecordMap(alignmentWorkspace, ModelRightEntityName);
        var modelLeftRow = modelLeftRows.TryGetValue(modelLeftId, out var modelLeft)
            ? modelLeft
            : throw new InvalidOperationException($"Alignment references missing ModelLeft '{modelLeftId}'.");
        var modelRightRow = modelRightRows.TryGetValue(modelRightId, out var modelRight)
            ? modelRight
            : throw new InvalidOperationException($"Alignment references missing ModelRight '{modelRightId}'.");

        var modelLeftModelId = RequireValue(modelLeftRow, "ModelId", ModelLeftEntityName);
        var modelRightModelId = RequireValue(modelRightRow, "ModelId", ModelRightEntityName);
        var modelLeftName = modelRows.TryGetValue(modelLeftModelId, out var modelLeftModel)
            ? RequireValue(modelLeftModel, "Name", ModelEntityName)
            : throw new InvalidOperationException($"ModelLeft '{modelLeftId}' references missing Model '{modelLeftModelId}'.");
        var modelRightName = modelRows.TryGetValue(modelRightModelId, out var modelRightModel)
            ? RequireValue(modelRightModel, "Name", ModelEntityName)
            : throw new InvalidOperationException($"ModelRight '{modelRightId}' references missing Model '{modelRightModelId}'.");

        var leftEntityRows = BuildRecordMap(alignmentWorkspace, ModelLeftEntityEntityName);
        var rightEntityRows = BuildRecordMap(alignmentWorkspace, ModelRightEntityEntityName);
        var leftEntityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightEntityNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in leftEntityRows.Values)
        {
            var rowModelLeftId = RequireValue(row, "ModelLeftId", ModelLeftEntityEntityName);
            if (!string.Equals(rowModelLeftId, modelLeftId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ModelLeftEntityEntityName} row '{row.Id}' references ModelLeftId '{rowModelLeftId}', expected '{modelLeftId}'.");
            }

            leftEntityNameById[row.Id] = RequireValue(row, "Name", ModelLeftEntityEntityName);
        }

        foreach (var row in rightEntityRows.Values)
        {
            var rowModelRightId = RequireValue(row, "ModelRightId", ModelRightEntityEntityName);
            if (!string.Equals(rowModelRightId, modelRightId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ModelRightEntityEntityName} row '{row.Id}' references ModelRightId '{rowModelRightId}', expected '{modelRightId}'.");
            }

            rightEntityNameById[row.Id] = RequireValue(row, "Name", ModelRightEntityEntityName);
        }

        var leftPropertyRows = BuildRecordMap(alignmentWorkspace, ModelLeftPropertyEntityName);
        var rightPropertyRows = BuildRecordMap(alignmentWorkspace, ModelRightPropertyEntityName);
        var leftPropertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var leftPropertyEntityIdByPropertyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyEntityIdByPropertyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in leftPropertyRows.Values)
        {
            var entityId = RequireValue(row, "ModelLeftEntityId", ModelLeftPropertyEntityName);
            if (!leftEntityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"{ModelLeftPropertyEntityName} row '{row.Id}' references missing ModelLeftEntity '{entityId}'.");
            }

            leftPropertyNameById[row.Id] = RequireValue(row, "Name", ModelLeftPropertyEntityName);
            leftPropertyEntityIdByPropertyId[row.Id] = entityId;
        }

        foreach (var row in rightPropertyRows.Values)
        {
            var entityId = RequireValue(row, "ModelRightEntityId", ModelRightPropertyEntityName);
            if (!rightEntityRows.ContainsKey(entityId))
            {
                throw new InvalidOperationException(
                    $"{ModelRightPropertyEntityName} row '{row.Id}' references missing ModelRightEntity '{entityId}'.");
            }

            rightPropertyNameById[row.Id] = RequireValue(row, "Name", ModelRightPropertyEntityName);
            rightPropertyEntityIdByPropertyId[row.Id] = entityId;
        }

        var entityMapRows = BuildRecordMap(alignmentWorkspace, EntityMapEntityName);
        var entityMapById = new Dictionary<string, (string ModelLeftEntityId, string ModelRightEntityId)>(StringComparer.OrdinalIgnoreCase);
        var entityMapIdByLeftEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityMapIdByRightEntityId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in entityMapRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var leftEntityId = RequireValue(row, "ModelLeftEntityId", EntityMapEntityName);
            var rightEntityId = RequireValue(row, "ModelRightEntityId", EntityMapEntityName);
            if (!leftEntityRows.ContainsKey(leftEntityId))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} row '{row.Id}' references missing ModelLeftEntity '{leftEntityId}'.");
            }

            if (!rightEntityRows.ContainsKey(rightEntityId))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} row '{row.Id}' references missing ModelRightEntity '{rightEntityId}'.");
            }

            if (!entityMapIdByLeftEntityId.TryAdd(leftEntityId, row.Id))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} has multiple mappings for ModelLeftEntityId '{leftEntityId}'.");
            }

            if (!entityMapIdByRightEntityId.TryAdd(rightEntityId, row.Id))
            {
                throw new InvalidOperationException(
                    $"{EntityMapEntityName} has multiple mappings for ModelRightEntityId '{rightEntityId}'.");
            }

            entityMapById[row.Id] = (leftEntityId, rightEntityId);
        }

        var propertyMapRows = BuildRecordMap(alignmentWorkspace, PropertyMapEntityName);
        var propertyMapById = new Dictionary<string, (string ModelLeftPropertyId, string ModelRightPropertyId)>(StringComparer.OrdinalIgnoreCase);
        var propertyMapIdsByEntityMapId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var leftPropertyMapSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rightPropertyMapSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in propertyMapRows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var leftPropertyId = RequireValue(row, "ModelLeftPropertyId", PropertyMapEntityName);
            var rightPropertyId = RequireValue(row, "ModelRightPropertyId", PropertyMapEntityName);
            if (!leftPropertyRows.ContainsKey(leftPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' references missing ModelLeftProperty '{leftPropertyId}'.");
            }

            if (!rightPropertyRows.ContainsKey(rightPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' references missing ModelRightProperty '{rightPropertyId}'.");
            }

            var leftPropertyName = leftPropertyNameById[leftPropertyId];
            var rightPropertyName = rightPropertyNameById[rightPropertyId];
            if (string.Equals(leftPropertyName, "Id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rightPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!leftPropertyMapSeen.Add(leftPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} has multiple mappings for ModelLeftPropertyId '{leftPropertyId}'.");
            }

            if (!rightPropertyMapSeen.Add(rightPropertyId))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} has multiple mappings for ModelRightPropertyId '{rightPropertyId}'.");
            }

            var leftEntityId = leftPropertyEntityIdByPropertyId[leftPropertyId];
            var rightEntityId = rightPropertyEntityIdByPropertyId[rightPropertyId];
            if (!entityMapIdByLeftEntityId.TryGetValue(leftEntityId, out var entityMapId) ||
                !entityMapById.TryGetValue(entityMapId, out var mappedEntityIds) ||
                !string.Equals(mappedEntityIds.ModelRightEntityId, rightEntityId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{PropertyMapEntityName} row '{row.Id}' maps properties across entities without a matching EntityMap.");
            }

            propertyMapById[row.Id] = (leftPropertyId, rightPropertyId);
            if (!propertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var mapIds))
            {
                mapIds = new List<string>();
                propertyMapIdsByEntityMapId[entityMapId] = mapIds;
            }

            mapIds.Add(row.Id);
        }

        var normalizedPropertyMapIdsByEntityMapId = propertyMapIdsByEntityMapId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        return new AlignmentCatalog(
            AlignmentId: alignmentId,
            AlignmentName: alignmentName,
            ModelLeftId: modelLeftId,
            ModelRightId: modelRightId,
            ModelLeftName: modelLeftName,
            ModelRightName: modelRightName,
            LeftEntityNameById: leftEntityNameById,
            RightEntityNameById: rightEntityNameById,
            LeftPropertyNameById: leftPropertyNameById,
            RightPropertyNameById: rightPropertyNameById,
            LeftPropertyEntityIdByPropertyId: leftPropertyEntityIdByPropertyId,
            RightPropertyEntityIdByPropertyId: rightPropertyEntityIdByPropertyId,
            EntityMapById: entityMapById,
            PropertyMapById: propertyMapById,
            EntityMapIdByLeftEntityId: entityMapIdByLeftEntityId,
            EntityMapIdByRightEntityId: entityMapIdByRightEntityId,
            PropertyMapIdsByEntityMapId: normalizedPropertyMapIdsByEntityMapId);
    }

    private void ValidateWorkspaceMatchesAlignment(
        Workspace workspace,
        string expectedModelName,
        IReadOnlyDictionary<string, string> entityNameById,
        IReadOnlyDictionary<string, string> propertyNameById,
        IReadOnlyDictionary<string, string> propertyEntityIdByPropertyId)
    {
        if (!string.Equals(workspace.Model.Name, expectedModelName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace model '{workspace.Model.Name}' does not match expected model '{expectedModelName}'.");
        }

        foreach (var entityName in entityNameById.Values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            if (workspace.Model.FindEntity(entityName) == null)
            {
                throw new InvalidOperationException(
                    $"Workspace model '{workspace.Model.Name}' is missing aligned entity '{entityName}'.");
            }
        }

        foreach (var propertyId in propertyNameById.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            var propertyName = propertyNameById[propertyId];
            var entityId = propertyEntityIdByPropertyId[propertyId];
            if (!entityNameById.TryGetValue(entityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"Alignment property '{propertyId}' references missing entity '{entityId}'.");
            }

            var entity = workspace.Model.FindEntity(entityName)
                         ?? throw new InvalidOperationException(
                             $"Workspace model '{workspace.Model.Name}' is missing aligned entity '{entityName}'.");
            var hasDirectProperty = entity.Properties.Any(item =>
                string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            var hasRelationshipProperty = IsRelationshipProperty(entity, propertyName, out _);
            if (!hasDirectProperty && !hasRelationshipProperty)
            {
                throw new InvalidOperationException(
                    $"Workspace model '{workspace.Model.Name}' entity '{entityName}' is missing aligned property '{propertyName}'.");
            }
        }
    }

    private AlignedSideData BuildAlignedSideData(
        Workspace sourceWorkspace,
        Workspace diffWorkspace,
        AlignmentCatalog alignment,
        bool leftSide,
        IdentityAllocator identityAllocator)
    {
        var rowEntityName = leftSide ? ModelLeftEntityInstanceEntityName : ModelRightEntityInstanceEntityName;
        var propertyEntityName = leftSide ? ModelLeftPropertyInstanceEntityName : ModelRightPropertyInstanceEntityName;
        var entityNameById = leftSide ? alignment.LeftEntityNameById : alignment.RightEntityNameById;
        var propertyNameById = leftSide ? alignment.LeftPropertyNameById : alignment.RightPropertyNameById;

        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowCount = 0;
        var propertyCount = 0;

        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var modelEntityId = leftSide ? entityMap.Value.ModelLeftEntityId : entityMap.Value.ModelRightEntityId;
            if (!entityNameById.TryGetValue(modelEntityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"Alignment EntityMap '{entityMapId}' references missing {(leftSide ? "ModelLeftEntity" : "ModelRightEntity")} '{modelEntityId}'.");
            }

            var modelEntity = RequireEntity(sourceWorkspace, entityName);
            var rows = BuildRecordMap(sourceWorkspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                var rowInstanceId = identityAllocator.NextId(rowEntityName);
                AddDiffRecord(
                    diffWorkspace,
                    rowEntityName,
                    rowInstanceId,
                    new Dictionary<string, string?>
                    {
                        [leftSide ? "ModelLeftEntityId" : "ModelRightEntityId"] = modelEntityId,
                        ["EntityInstanceIdentifier"] = row.Id,
                    });
                rowCount++;
                var rowKey = CreateAlignedRowKey(entityMapId, row.Id);
                rowSet.Add(rowKey);
                entityInstanceIdByRowKey[rowKey] = rowInstanceId;

                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    var modelPropertyId = leftSide ? propertyMap.ModelLeftPropertyId : propertyMap.ModelRightPropertyId;
                    if (!propertyNameById.TryGetValue(modelPropertyId, out var propertyName))
                    {
                        throw new InvalidOperationException(
                            $"Alignment PropertyMap '{propertyMapId}' references missing property '{modelPropertyId}'.");
                    }

                    if (!TryGetPropertyLikeValue(modelEntity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    var propertyInstanceId = identityAllocator.NextId(propertyEntityName);
                    AddDiffRecord(
                        diffWorkspace,
                        propertyEntityName,
                        propertyInstanceId,
                        new Dictionary<string, string?>
                        {
                            [leftSide ? "ModelLeftEntityInstanceId" : "ModelRightEntityInstanceId"] = rowInstanceId,
                            [leftSide ? "ModelLeftPropertyId" : "ModelRightPropertyId"] = modelPropertyId,
                            ["Value"] = value,
                        });
                    propertyCount++;

                    var tupleKey = CreateAlignedPropertyTupleKey(entityMapId, row.Id, propertyMapId, value);
                    propertySet.Add(tupleKey);
                    propertyInstanceIdByTupleKey[tupleKey] = propertyInstanceId;
                    valueByEntityMapRowPropertyMapKey[CreateAlignedEntityRowPropertyMapKey(entityMapId, row.Id, propertyMapId)] = value;
                }
            }
        }

        return new AlignedSideData(
            RowSet: rowSet,
            EntityInstanceIdByRowKey: entityInstanceIdByRowKey,
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityMapRowPropertyMapKey: valueByEntityMapRowPropertyMapKey,
            RowCount: rowCount,
            PropertyCount: propertyCount);
    }

    private InstanceDiffBuildResult BuildAlignedInstanceDiffWorkspace(
        Workspace leftWorkspace,
        Workspace rightWorkspace,
        Workspace alignmentWorkspace,
        AlignmentCatalog alignment,
        string rightWorkspacePath)
    {
        var diffWorkspacePath = ResolveInstanceDiffOutputPath(rightWorkspacePath, "instance-diff-aligned");
        var diffWorkspace = CreateWorkspaceFromDefinition(InstanceDiffAlignedWorkspaceDefinition.Value, diffWorkspacePath);
        var identityAllocator = new IdentityAllocator();
        var idMapByEntity = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var includedPropertyMapIds = alignment.PropertyMapById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedLeftPropertyIds = alignment.PropertyMapById.Values
            .Select(item => item.ModelLeftPropertyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedRightPropertyIds = alignment.PropertyMapById.Values
            .Select(item => item.ModelRightPropertyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entityName in new[]
                 {
                     ModelEntityName,
                     ModelLeftEntityName,
                     ModelRightEntityName,
                     AlignmentEntityName,
                     ModelLeftEntityEntityName,
                     ModelRightEntityEntityName,
                     ModelLeftPropertyEntityName,
                     ModelRightPropertyEntityName,
                     EntityMapEntityName,
                     PropertyMapEntityName,
                 })
        {
            var referenceFields = AlignmentReferenceFieldsByEntity.TryGetValue(entityName, out var fields)
                ? fields
                : null;
            IReadOnlySet<string>? includeSourceRowIds = null;
            if (string.Equals(entityName, PropertyMapEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedPropertyMapIds;
            }
            else if (string.Equals(entityName, ModelLeftPropertyEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedLeftPropertyIds;
            }
            else if (string.Equals(entityName, ModelRightPropertyEntityName, StringComparison.OrdinalIgnoreCase))
            {
                includeSourceRowIds = includedRightPropertyIds;
            }

            CopyRowsByEntityWithIdentity(
                alignmentWorkspace,
                diffWorkspace,
                entityName,
                identityAllocator,
                idMapByEntity,
                referenceFields,
                includeSourceRowIds);
        }

        var diffAlignment = ParseAlignmentCatalog(
            diffWorkspace,
            InstanceDiffAlignedModelName,
            InstanceDiffAlignedModelSignature.Value);
        var leftSide = BuildAlignedSideData(
            leftWorkspace,
            diffWorkspace,
            diffAlignment,
            leftSide: true,
            identityAllocator);
        var rightSide = BuildAlignedSideData(
            rightWorkspace,
            diffWorkspace,
            diffAlignment,
            leftSide: false,
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

        static string ExtractAlignedEntityKeyFromPropertyTuple(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid aligned property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        var leftNotInRight = leftSide.PropertySet
            .Except(rightSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTuple(tupleKey)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        var rightNotInLeft = rightSide.PropertySet
            .Except(leftSide.PropertySet, StringComparer.Ordinal)
            .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTuple(tupleKey)))
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

    private AlignedDiffData ParseAlignedDiffWorkspace(Workspace diffWorkspace)
    {
        var alignment = ParseAlignmentCatalog(
            diffWorkspace,
            InstanceDiffAlignedModelName,
            InstanceDiffAlignedModelSignature.Value);

        var leftRows = ParseAlignedSideRows(
            diffWorkspace,
            ModelLeftEntityInstanceEntityName,
            "ModelLeftEntityId",
            alignment,
            leftSide: true);
        var rightRows = ParseAlignedSideRows(
            diffWorkspace,
            ModelRightEntityInstanceEntityName,
            "ModelRightEntityId",
            alignment,
            leftSide: false);
        var leftProperties = ParseAlignedSideProperties(
            diffWorkspace,
            ModelLeftPropertyInstanceEntityName,
            "ModelLeftEntityInstanceId",
            "ModelLeftPropertyId",
            leftRows.RowIdentityByRowInstanceId,
            alignment,
            leftSide: true);
        var rightProperties = ParseAlignedSideProperties(
            diffWorkspace,
            ModelRightPropertyInstanceEntityName,
            "ModelRightEntityInstanceId",
            "ModelRightPropertyId",
            rightRows.RowIdentityByRowInstanceId,
            alignment,
            leftSide: false);

        ValidateAlignedEntityNotInRows(
            diffWorkspace,
            ModelLeftEntityInstanceNotInRightEntityName,
            "ModelLeftEntityInstanceId",
            leftRows.EntityInstanceIdByRowKey,
            leftRows.RowSet.Except(rightRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        ValidateAlignedEntityNotInRows(
            diffWorkspace,
            ModelRightEntityInstanceNotInLeftEntityName,
            "ModelRightEntityInstanceId",
            rightRows.EntityInstanceIdByRowKey,
            rightRows.RowSet.Except(leftRows.RowSet, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));

        var sharedEntityKeys = leftRows.RowSet
            .Intersect(rightRows.RowSet, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        static string ExtractAlignedEntityKeyFromPropertyTupleForValidation(string tupleKey)
        {
            var parts = tupleKey.Split('\n');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException($"Invalid aligned property tuple key '{tupleKey}'.");
            }

            return string.Join("\n", parts[0], parts[1]);
        }

        ValidateAlignedNotInRows(
            diffWorkspace,
            ModelLeftPropertyInstanceNotInRightEntityName,
            "ModelLeftPropertyInstanceId",
            leftProperties.PropertyInstanceIdByTupleKey,
            leftProperties.PropertySet
                .Except(rightProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));
        ValidateAlignedNotInRows(
            diffWorkspace,
            ModelRightPropertyInstanceNotInLeftEntityName,
            "ModelRightPropertyInstanceId",
            rightProperties.PropertyInstanceIdByTupleKey,
            rightProperties.PropertySet
                .Except(leftProperties.PropertySet, StringComparer.Ordinal)
                .Where(tupleKey => sharedEntityKeys.Contains(ExtractAlignedEntityKeyFromPropertyTupleForValidation(tupleKey)))
                .ToHashSet(StringComparer.Ordinal));

        return new AlignedDiffData(
            Alignment: alignment,
            LeftRowSet: leftRows.RowSet,
            RightRowSet: rightRows.RowSet,
            LeftPropertySet: leftProperties.PropertySet,
            RightPropertySet: rightProperties.PropertySet,
            RightValueByEntityMapRowPropertyMapKey: rightProperties.ValueByEntityMapRowPropertyMapKey);
    }

    private sealed record ParsedAlignedSideRows(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyDictionary<string, (string EntityMapId, string EntityInstanceIdentifier)> RowIdentityByRowInstanceId);

    private static ParsedAlignedSideRows ParseAlignedSideRows(
        Workspace diffWorkspace,
        string rowEntityName,
        string entityIdPropertyName,
        AlignmentCatalog alignment,
        bool leftSide)
    {
        var rows = BuildRecordMap(diffWorkspace, rowEntityName);
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var entityInstanceIdByRowKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var rowIdentityByRowInstanceId =
            new Dictionary<string, (string EntityMapId, string EntityInstanceIdentifier)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var modelEntityId = RequireValue(row, entityIdPropertyName, rowEntityName);
            var entityMapId = leftSide
                ? alignment.EntityMapIdByLeftEntityId.TryGetValue(modelEntityId, out var mappedByLeft)
                    ? mappedByLeft
                    : string.Empty
                : alignment.EntityMapIdByRightEntityId.TryGetValue(modelEntityId, out var mappedByRight)
                    ? mappedByRight
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(entityMapId))
            {
                throw new InvalidOperationException(
                    $"{rowEntityName} row '{row.Id}' references unmapped entity id '{modelEntityId}'.");
            }

            var entityInstanceIdentifier = RequireValue(row, "EntityInstanceIdentifier", rowEntityName);
            var rowKey = CreateAlignedRowKey(entityMapId, entityInstanceIdentifier);
            rowSet.Add(rowKey);
            entityInstanceIdByRowKey[rowKey] = row.Id;
            rowIdentityByRowInstanceId[row.Id] = (entityMapId, entityInstanceIdentifier);
        }

        return new ParsedAlignedSideRows(rowSet, entityInstanceIdByRowKey, rowIdentityByRowInstanceId);
    }

    private sealed record ParsedAlignedSideProperties(
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityMapRowPropertyMapKey);

    private static ParsedAlignedSideProperties ParseAlignedSideProperties(
        Workspace diffWorkspace,
        string propertyEntityName,
        string rowInstanceIdPropertyName,
        string propertyIdPropertyName,
        IReadOnlyDictionary<string, (string EntityMapId, string EntityInstanceIdentifier)> rowIdentityByRowInstanceId,
        AlignmentCatalog alignment,
        bool leftSide)
    {
        var rows = BuildRecordMap(diffWorkspace, propertyEntityName);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var propertyInstanceIdByTupleKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityKeys = new HashSet<string>(StringComparer.Ordinal);

        var propertyMapIdByLeftPropertyId = alignment.PropertyMapById
            .ToDictionary(pair => pair.Value.ModelLeftPropertyId, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
        var propertyMapIdByRightPropertyId = alignment.PropertyMapById
            .ToDictionary(pair => pair.Value.ModelRightPropertyId, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var rowInstanceId = RequireValue(row, rowInstanceIdPropertyName, propertyEntityName);
            if (!rowIdentityByRowInstanceId.TryGetValue(rowInstanceId, out var rowIdentity))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references missing row instance '{rowInstanceId}'.");
            }

            var modelPropertyId = RequireValue(row, propertyIdPropertyName, propertyEntityName);
            var propertyMapId = leftSide
                ? propertyMapIdByLeftPropertyId.TryGetValue(modelPropertyId, out var mappedLeft)
                    ? mappedLeft
                    : string.Empty
                : propertyMapIdByRightPropertyId.TryGetValue(modelPropertyId, out var mappedRight)
                    ? mappedRight
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(propertyMapId))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} row '{row.Id}' references unmapped property id '{modelPropertyId}'.");
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
            var tupleKey = CreateAlignedPropertyTupleKey(
                rowIdentity.EntityMapId,
                rowIdentity.EntityInstanceIdentifier,
                propertyMapId,
                value);
            var identityKey = CreateAlignedEntityRowPropertyMapKey(
                rowIdentity.EntityMapId,
                rowIdentity.EntityInstanceIdentifier,
                propertyMapId);
            if (!identityKeys.Add(identityKey))
            {
                throw new InvalidOperationException(
                    $"{propertyEntityName} contains duplicate mapped property identity '{identityKey}'.");
            }

            propertySet.Add(tupleKey);
            propertyInstanceIdByTupleKey[tupleKey] = row.Id;
            valueByEntityMapRowPropertyMapKey[identityKey] = value;
        }

        return new ParsedAlignedSideProperties(
            PropertySet: propertySet,
            PropertyInstanceIdByTupleKey: propertyInstanceIdByTupleKey,
            ValueByEntityMapRowPropertyMapKey: valueByEntityMapRowPropertyMapKey);
    }

    private static void ValidateAlignedNotInRows(
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

    private static void ValidateAlignedEntityNotInRows(
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

    private (
        HashSet<string> RowSet,
        HashSet<string> PropertySet,
        Dictionary<string, string> ValueByEntityMapRowPropertyMapKey)
        BuildWorkspaceSnapshotForAlignedDiff(
            Workspace workspace,
            AlignmentCatalog alignment)
    {
        var rowSet = new HashSet<string>(StringComparer.Ordinal);
        var propertySet = new HashSet<string>(StringComparer.Ordinal);
        var valueByEntityMapRowPropertyMapKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var leftEntityId = entityMap.Value.ModelLeftEntityId;
            if (!alignment.LeftEntityNameById.TryGetValue(leftEntityId, out var entityName))
            {
                throw new InvalidOperationException(
                    $"EntityMap '{entityMapId}' references missing ModelLeftEntity '{leftEntityId}'.");
            }

            var entity = RequireEntity(workspace, entityName);
            var rows = BuildRecordMap(workspace, entityName);
            foreach (var row in rows.Values.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            {
                rowSet.Add(CreateAlignedRowKey(entityMapId, row.Id));
                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    if (!alignment.LeftPropertyNameById.TryGetValue(propertyMap.ModelLeftPropertyId, out var propertyName))
                    {
                        continue;
                    }

                    if (!TryGetPropertyLikeValue(entity, row, propertyName, out var value))
                    {
                        continue;
                    }

                    propertySet.Add(CreateAlignedPropertyTupleKey(entityMapId, row.Id, propertyMapId, value));
                    valueByEntityMapRowPropertyMapKey[CreateAlignedEntityRowPropertyMapKey(entityMapId, row.Id, propertyMapId)] = value;
                }
            }
        }

        return (rowSet, propertySet, valueByEntityMapRowPropertyMapKey);
    }

    private void ApplyAlignedRightSnapshotToWorkspace(
        Workspace targetWorkspace,
        AlignedDiffData diffData)
    {
        var alignment = diffData.Alignment;
        foreach (var entityMap in alignment.EntityMapById.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entityMapId = entityMap.Key;
            var leftEntityId = entityMap.Value.ModelLeftEntityId;
            if (!alignment.LeftEntityNameById.TryGetValue(leftEntityId, out var entityName))
            {
                continue;
            }

            var entity = RequireEntity(targetWorkspace, entityName);
            var rows = targetWorkspace.Instance.GetOrCreateEntityRecords(entityName);
            var rightRowIds = diffData.RightRowSet
                .Select(key =>
                {
                    var parts = key.Split('\n');
                    return (EntityMapId: UnescapeCanonical(parts[0]), RowId: UnescapeCanonical(parts[1]));
                })
                .Where(item => string.Equals(item.EntityMapId, entityMapId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.RowId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            rows.RemoveAll(row => !rightRowIds.Contains(row.Id));
            foreach (var rowId in rightRowIds.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var row = rows.FirstOrDefault(item => string.Equals(item.Id, rowId, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                {
                    row = new GenericRecord
                    {
                        Id = rowId,
                    };
                    rows.Add(row);
                }
                else
                {
                    row.Id = rowId;
                }

                if (!alignment.PropertyMapIdsByEntityMapId.TryGetValue(entityMapId, out var propertyMapIds))
                {
                    continue;
                }

                foreach (var propertyMapId in propertyMapIds)
                {
                    if (!alignment.PropertyMapById.TryGetValue(propertyMapId, out var propertyMap))
                    {
                        continue;
                    }

                    if (!alignment.LeftPropertyNameById.TryGetValue(propertyMap.ModelLeftPropertyId, out var propertyName))
                    {
                        continue;
                    }

                    var valueKey = CreateAlignedEntityRowPropertyMapKey(entityMapId, rowId, propertyMapId);
                    if (!diffData.RightValueByEntityMapRowPropertyMapKey.TryGetValue(valueKey, out var value))
                    {
                        if (IsRelationshipProperty(entity, propertyName, out var missingRelationshipEntity))
                        {
                            row.RelationshipIds.Remove(missingRelationshipEntity);
                        }
                        else
                        {
                            row.Values.Remove(propertyName);
                        }

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
            }
        }
    }
}
