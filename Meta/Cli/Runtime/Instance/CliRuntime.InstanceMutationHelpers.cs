internal sealed partial class CliRuntime
{
    IReadOnlyList<(string Key, string Value)> BuildRowPreviewDetails(
        GenericEntity entity,
        IReadOnlyDictionary<string, string> values)
    {
        var details = new List<(string Key, string Value)>();
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(previewProperty) &&
            values.TryGetValue(previewProperty, out var previewValue) &&
            !string.IsNullOrWhiteSpace(previewValue))
        {
            details.Add((previewProperty, previewValue));
        }

        return details;
    }

    IReadOnlyList<(string Key, string Value)> BuildUpsertSuccessDetails(
        IReadOnlySet<string> existingIds,
        IReadOnlyList<string> rowIds)
    {
        var inserted = rowIds.Count(id => !existingIds.Contains(id));
        var updated = rowIds.Count - inserted;
        return new[]
        {
            ("Inserted", inserted.ToString(CultureInfo.InvariantCulture)),
            ("Updated", updated.ToString(CultureInfo.InvariantCulture)),
            ("Total", rowIds.Count.ToString(CultureInfo.InvariantCulture)),
        };
    }

    GenericEntity RequireEntity(GenericModel model, string entityName)
    {
        var entity = model.FindEntity(entityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }

        return entity;
    }

    GenericRecord? TryFindRowById(InMemoryWorkspace workspace, string entityName, string id)
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

    GenericRecord ResolveRowById(InMemoryWorkspace workspace, string entityName, string id)
    {
        var row = TryFindRowById(workspace, entityName, id);
        if (row == null)
        {
            throw new InvalidOperationException($"Instance with Id '{id}' does not exist in entity '{entityName}'.");
        }

        return row;
    }

    IReadOnlyList<Operation> BuildUpdateOperations(
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

        var operations = new List<Operation>(setValues.Count);

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("instance update does not allow updating Id.");
            }

            if (propertyNames.Contains(pair.Key))
            {
                operations.Add(new Operation.SetProperty(
                    entity.Name,
                    id,
                    pair.Key,
                    pair.Value));
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                var targetId = NormalizeRelationshipInputValue(pair.Value);
                operations.Add(string.IsNullOrWhiteSpace(targetId)
                    ? new Operation.ClearRelationship(
                        entity.Name,
                        id,
                        relationshipUsageName)
                    : new Operation.SetRelationship(
                        entity.Name,
                        id,
                        relationshipUsageName,
                        targetId));
                continue;
            }

            throw new InvalidOperationException(
                $"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        return operations;
    }

    Operation.InsertRecord BuildInsertRecordOperation(
        GenericEntity entity,
        IReadOnlyDictionary<string, string> setValues,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Cannot create '{entity.Name}' with an empty Id.");
        }

        id = id.Trim();

        var propertyNames = entity.Properties.Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relationshipIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (propertyNames.Contains(pair.Key))
            {
                values[pair.Key] = pair.Value;
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                var targetId = NormalizeRelationshipInputValue(pair.Value);
                if (!string.IsNullOrWhiteSpace(targetId))
                {
                    relationshipIds[relationshipUsageName] = targetId;
                }
                continue;
            }

            throw new InvalidOperationException($"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        EnsureCreateIncludesRequiredRelationships(
            entity,
            relationshipIds,
            operationName: "insert",
            rowNumber: null);
        return new Operation.InsertRecord(
            entity.Name,
            id,
            values,
            relationshipIds);
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

    string NormalizeRelationshipInputValue(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    void EnsureCreateIncludesRequiredRelationships(
        GenericEntity entity,
        IReadOnlyDictionary<string, string> relationshipIds,
        string operationName,
        int? rowNumber)
    {
        foreach (var relationship in entity.Relationships
                     .Where(item => !item.IsNullable)
                     .Select(item => item.GetColumnName())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (relationshipIds.TryGetValue(relationship, out var relationshipId) &&
                !string.IsNullOrWhiteSpace(relationshipId))
            {
                continue;
            }

            if (string.Equals(operationName, "bulk-insert", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"bulk-insert row {rowNumber.GetValueOrDefault()} is missing required relationship '{relationship}'. Set column '{relationship}' to a target Id.");
            }

            throw new InvalidOperationException(
                $"insert is missing required relationship '{relationship}'. Set it with --set {relationship}=<Id>.");
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

}
