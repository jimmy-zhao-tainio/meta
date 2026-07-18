internal sealed partial class CliRuntime
{
    IReadOnlyList<(string Key, string Value)> BuildRowPreviewDetails(GenericEntity entity, RowPatch rowPatch)
    {
        var details = new List<(string Key, string Value)>();
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(previewProperty) &&
            rowPatch.Values.TryGetValue(previewProperty, out var previewValue) &&
            !string.IsNullOrWhiteSpace(previewValue))
        {
            details.Add((previewProperty, previewValue));
        }

        return details;
    }

    IReadOnlyList<(string Key, string Value)> BuildUpsertSuccessDetails(
        Workspace workspace,
        string entityName,
        IReadOnlyList<string> rowIds)
    {
        var existingIds = workspace.Instance.GetOrCreateEntityRecords(entityName)
            .Select(record => record.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inserted = rowIds.Count(id => !existingIds.Contains(id));
        var updated = rowIds.Count - inserted;
        return new[]
        {
            ("Inserted", inserted.ToString(CultureInfo.InvariantCulture)),
            ("Updated", updated.ToString(CultureInfo.InvariantCulture)),
            ("Total", rowIds.Count.ToString(CultureInfo.InvariantCulture)),
        };
    }

    GenericEntity RequireEntity(Workspace workspace, string entityName)
    {
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }

        return entity;
    }

    GenericRecord? TryFindRowById(Workspace workspace, string entityName, string id)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new InvalidOperationException("Entity name is required.");
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
        return rows.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    GenericRecord ResolveRowById(Workspace workspace, string entityName, string id)
    {
        var row = TryFindRowById(workspace, entityName, id);
        if (row == null)
        {
            throw new InvalidOperationException($"Instance with Id '{id}' does not exist in entity '{entityName}'.");
        }

        return row;
    }

    RowPatch BuildRowPatchForUpdate(
        GenericEntity entity,
        string id,
        IReadOnlyDictionary<string, string> setValues)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Cannot update '{entity.Name}' instance with empty Id.");
        }

        var propertyNames = entity.Properties.Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);

        var patch = new RowPatch
        {
            Id = id,
        };

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("instance update does not allow updating Id.");
            }

            if (propertyNames.Contains(pair.Key))
            {
                patch.Values[pair.Key] = pair.Value;
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value);
                continue;
            }

            throw new InvalidOperationException(
                $"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        return patch;
    }

    RowPatch BuildRowPatchForCreate(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyDictionary<string, string> setValues,
        string? explicitId)
    {
        var id = !string.IsNullOrWhiteSpace(explicitId)
            ? explicitId.Trim()
            : GenerateNextId(workspace, entity.Name);

        if (workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Cannot create '{entity.Name}' with Id '{id}' because it already exists.");
        }

        var propertyNames = entity.Properties.Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);

        var patch = new RowPatch
        {
            Id = id,
            Values =
            {
                ["Id"] = id,
            },
        };

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (propertyNames.Contains(pair.Key))
            {
                patch.Values[pair.Key] = pair.Value;
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value);
                continue;
            }

            throw new InvalidOperationException($"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        EnsureCreatePatchIncludesRequiredRelationships(entity, patch, operationName: "insert", rowNumber: null);
        return patch;
    }

    bool ContainsIdSetAssignment(IReadOnlyDictionary<string, string> setValues)
    {
        if (setValues == null || setValues.Count == 0)
        {
            return false;
        }

        return setValues.Keys.Any(key => string.Equals(key, "Id", StringComparison.OrdinalIgnoreCase));
    }

    string ResolveRelationshipName(GenericEntity entity, string candidateToEntityName)
    {
        return ResolveRelationshipDefinition(entity, candidateToEntityName, out _)
            ?.GetColumnName() ?? string.Empty;
    }

    GenericRelationship? ResolveRelationshipDefinition(
        GenericEntity entity,
        string candidateToEntityName,
        out bool isAmbiguous)
    {
        isAmbiguous = false;
        var selector = candidateToEntityName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var byRoleOrColumn = entity.Relationships
            .Where(item =>
                string.Equals(item.GetRoleOrDefault(), selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.GetColumnName(), selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byRoleOrColumn.Count == 1)
        {
            return byRoleOrColumn[0];
        }

        if (byRoleOrColumn.Count > 1)
        {
            isAmbiguous = true;
            return null;
        }

        var byTarget = entity.Relationships
            .Where(item => string.Equals(item.Entity, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byTarget.Count == 1)
        {
            return byTarget[0];
        }

        if (byTarget.Count > 1)
        {
            isAmbiguous = true;
        }

        return null;
    }

    string TryGetDisplayValue(GenericEntity entity, GenericRecord row)
    {
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(previewProperty))
        {
            return string.Empty;
        }

        return row.Values.TryGetValue(previewProperty, out var value) ? value : string.Empty;
    }

    int CountRelationshipUsages(GenericRecord row, string relationshipUsageName)
    {
        return row.RelationshipIds.Count(item =>
            string.Equals(item.Key, relationshipUsageName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Value));
    }

    RowPatch BuildRelationshipUsageRewritePatch(
        GenericRecord sourceRow,
        string relationshipUsageName,
        string? targetId)
    {
        var patch = new RowPatch
        {
            Id = sourceRow.Id,
            ReplaceExisting = true,
        };
        foreach (var value in sourceRow.Values)
        {
            patch.Values[value.Key] = value.Value;
        }

        foreach (var relationship in sourceRow.RelationshipIds
                     .Where(item => !string.Equals(item.Key, relationshipUsageName, StringComparison.OrdinalIgnoreCase)))
        {
            patch.RelationshipIds[relationship.Key] = relationship.Value;
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            patch.RelationshipIds[relationshipUsageName] = targetId;
        }

        return patch;
    }

    string NormalizeRelationshipInputValue(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    void EnsureCreatePatchIncludesRequiredRelationships(
        GenericEntity entity,
        RowPatch patch,
        string operationName,
        int? rowNumber)
    {
        foreach (var relationship in entity.Relationships
                     .Where(item => !item.IsNullable)
                     .Select(item => item.GetColumnName())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!patch.RelationshipIds.TryGetValue(relationship, out var relationshipId) ||
                string.IsNullOrWhiteSpace(relationshipId))
            {
                if (string.Equals(operationName, "bulk-insert", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"bulk-insert row {rowNumber.GetValueOrDefault()} is missing required relationship '{relationship}'. Set column '{relationship}' to a target Id.");
                }

                throw new InvalidOperationException(
                    $"insert is missing required relationship '{relationship}'. Set it with --set {relationship}=<Id>.");
            }
        }
    }

    Dictionary<string, string> BuildRelationshipAliasMap(GenericEntity entity)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in entity.Relationships)
        {
            var relationshipName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(relationshipName))
            {
                continue;
            }

            aliases[relationshipName] = relationshipName;
            aliases[relationship.GetRoleOrDefault()] = relationshipName;
        }

        return aliases;
    }

    string GenerateNextId(Workspace workspace, string entityName)
    {
        var records = workspace.Instance.GetOrCreateEntityRecords(entityName);
        var ids = records
            .Select(row => row.Id)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var numericIds = ids
            .Select(value => long.TryParse(value, out var parsed) ? parsed : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (numericIds.Count > 0)
        {
            var next = numericIds.Max() + 1;
            while (ids.Contains(next.ToString()))
            {
                next++;
            }

            return next.ToString();
        }

        var candidate = 1L;
        while (ids.Contains(candidate.ToString()))
        {
            candidate++;
        }

        return candidate.ToString();
    }
}
