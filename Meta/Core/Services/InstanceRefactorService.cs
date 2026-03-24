using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public readonly record struct RenameInstanceIdRefactorOptions(
    string EntityName,
    string OldId,
    string NewId);

public readonly record struct RenameInstanceIdRefactorResult(
    string EntityName,
    string OldId,
    string NewId,
    int RelationshipsUpdated,
    int RowsTouched);

public sealed class InstanceRefactorService : IInstanceRefactorService
{
    public RenameInstanceIdRefactorResult RenameInstanceId(
        Workspace workspace,
        RenameInstanceIdRefactorOptions options)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(options.EntityName))
        {
            throw new InvalidOperationException("Entity name is required.");
        }

        if (string.IsNullOrWhiteSpace(options.OldId))
        {
            throw new InvalidOperationException("Old Id is required.");
        }

        if (string.IsNullOrWhiteSpace(options.NewId))
        {
            throw new InvalidOperationException("New Id is required.");
        }

        if (string.Equals(options.OldId, options.NewId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Old Id and new Id must differ.");
        }

        var entity = workspace.Model.FindEntity(options.EntityName)
                     ?? throw new InvalidOperationException($"Entity '{options.EntityName}' does not exist.");
        var rows = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
        var targetRow = rows.FirstOrDefault(row => string.Equals(row.Id, options.OldId, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Instance with Id '{options.OldId}' does not exist in entity '{entity.Name}'.");
        if (rows.Any(row =>
                !ReferenceEquals(row, targetRow) &&
                string.Equals(row.Id, options.NewId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Cannot rename '{entity.Name}' Id '{options.OldId}' to '{options.NewId}' because it already exists.");
        }

        var touchedRows = new HashSet<GenericRecord>(ReferenceEqualityComparer.Instance) { targetRow };
        targetRow.Id = options.NewId;

        var relationshipsUpdated = 0;
        foreach (var sourceEntity in workspace.Model.Entities)
        {
            foreach (var relationship in sourceEntity.Relationships
                         .Where(item => string.Equals(item.Entity, entity.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var usageName = relationship.GetColumnName();
                foreach (var row in workspace.Instance.GetOrCreateEntityRecords(sourceEntity.Name))
                {
                    if (!row.RelationshipIds.TryGetValue(usageName, out var relationshipId) ||
                        !string.Equals(relationshipId, options.OldId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    row.RelationshipIds[usageName] = options.NewId;
                    relationshipsUpdated++;
                    touchedRows.Add(row);
                }
            }
        }

        workspace.IsDirty = true;

        return new RenameInstanceIdRefactorResult(
            EntityName: entity.Name,
            OldId: options.OldId,
            NewId: options.NewId,
            RelationshipsUpdated: relationshipsUpdated,
            RowsTouched: touchedRows.Count);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<GenericRecord>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(GenericRecord? x, GenericRecord? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(GenericRecord obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
