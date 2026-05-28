using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Services;

public static class BulkRelationshipResolver
{
    public static void ResolveRelationshipIds(Workspace workspace, WorkspaceOp operation)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (!string.Equals(operation.Type, WorkspaceOpTypes.BulkUpsertRows, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.EntityName))
        {
            throw new InvalidOperationException("Bulk upsert operation requires entity name.");
        }

        var entity = workspace.Model.FindEntity(operation.EntityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{operation.EntityName}' does not exist.");
        }

        var relationTargets = entity.Relationships
            .Select(relationship => relationship.GetColumnName())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rowPatch in operation.RowPatches)
        {
            var keys = rowPatch.RelationshipIds.Keys.ToList();
            foreach (var relationName in keys)
            {
                if (!relationTargets.Contains(relationName))
                {
                    throw new InvalidOperationException(
                        $"Entity '{entity.Name}' has no relationship '{relationName}'.");
                }

                var rawValue = rowPatch.RelationshipIds[relationName];
                var resolved = ResolveRelationshipValue(relationName, rowPatch.Id, rawValue);
                rowPatch.RelationshipIds[relationName] = resolved;
            }
        }
    }

    private static string ResolveRelationshipValue(
        string relationName,
        string rowId,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Row '{rowId}' relationship '{relationName}' is missing required target id.");
        }

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException(
                $"Row '{rowId}' relationship '{relationName}' has empty id reference.");
        }

        return trimmed;
    }
}
