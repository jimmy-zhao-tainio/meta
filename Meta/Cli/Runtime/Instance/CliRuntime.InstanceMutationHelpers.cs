internal sealed partial class CliRuntime
{
    IReadOnlyList<(string Key, string Value)> BuildRowPreviewDetails(
        GenericEntity entity,
        InsertRecordOperation operation)
    {
        var details = new List<(string Key, string Value)>();
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(previewProperty) &&
            operation.Values.TryGetValue(previewProperty, out var previewValue) &&
            !string.IsNullOrWhiteSpace(previewValue))
        {
            details.Add((previewProperty, previewValue));
        }

        return details;
    }

    IReadOnlyList<(string Key, string Value)> BuildBulkInsertSuccessDetails(int inserted)
    {
        var value = inserted.ToString(CultureInfo.InvariantCulture);
        return new[]
        {
            ("Inserted", value),
            ("Total", value),
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

    MetaOperationPlan BuildRecordUpdatePlan(
        GenericEntity entity,
        string id,
        IReadOnlyDictionary<string, string> setValues)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Cannot update '{entity.Name}' instance with empty Id.");
        }

        var propertyByName = entity.Properties.ToDictionary(
            property => property.Name,
            StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
        var operations = new List<MetaOperation>();

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("instance update does not allow updating Id.");
            }

            if (propertyByName.TryGetValue(pair.Key, out var property))
            {
                operations.Add(new SetPropertyOperation(
                    entity.Name,
                    id,
                    property.Name,
                    pair.Value));
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationship))
            {
                if (relationship == null)
                {
                    throw new InvalidOperationException(
                        $"Relationship selector '{entity.Name}.{pair.Key}' is ambiguous in the model.");
                }

                var targetId = NormalizeRelationshipInputValue(pair.Value);
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    if (!relationship.IsNullable)
                    {
                        throw new InvalidOperationException(
                            $"Required relationship '{entity.Name}.{relationship.GetColumnName()}' cannot be cleared.");
                    }

                    operations.Add(new ClearRelationshipOperation(
                        entity.Name,
                        id,
                        relationship.GetColumnName()));
                    continue;
                }

                operations.Add(new SetRelationshipOperation(
                    entity.Name,
                    id,
                    relationship.GetColumnName(),
                    targetId));
                continue;
            }

            throw new InvalidOperationException(
                $"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        return new MetaOperationPlan(operations);
    }

    InsertRecordOperation BuildInsertOperation(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyDictionary<string, string> setValues,
        string explicitId)
    {
        var id = explicitId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Cannot create '{entity.Name}' with an empty Id.");
        }

        if (workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Cannot create '{entity.Name}' with Id '{id}' because it already exists.");
        }

        var propertyByName = entity.Properties.ToDictionary(
            property => property.Name,
            StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relationshipIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (propertyByName.TryGetValue(pair.Key, out var property))
            {
                values[property.Name] = pair.Value;
                continue;
            }

            if (relationshipByAlias.TryGetValue(pair.Key, out var relationship))
            {
                if (relationship == null)
                {
                    throw new InvalidOperationException(
                        $"Relationship selector '{entity.Name}.{pair.Key}' is ambiguous in the model.");
                }

                var targetId = NormalizeRelationshipInputValue(pair.Value);
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    if (!relationship.IsNullable)
                    {
                        throw new InvalidOperationException(
                            $"insert is missing required relationship '{relationship.GetColumnName()}'. Set it with --set {relationship.GetColumnName()}=<Id>.");
                    }

                    continue;
                }

                relationshipIds[relationship.GetColumnName()] = targetId;
                continue;
            }

            throw new InvalidOperationException(
                $"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }

        EnsureInsertIncludesRequiredRelationships(
            entity,
            relationshipIds,
            operationName: "insert",
            rowNumber: null);
        return new InsertRecordOperation(entity.Name, id, values, relationshipIds);
    }

    bool ContainsIdSetAssignment(IReadOnlyDictionary<string, string> setValues)
    {
        if (setValues == null || setValues.Count == 0)
        {
            return false;
        }

        return setValues.Keys.Any(key => string.Equals(key, "Id", StringComparison.OrdinalIgnoreCase));
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

    string NormalizeRelationshipInputValue(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    void EnsureInsertIncludesRequiredRelationships(
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

    Dictionary<string, GenericRelationship?> BuildRelationshipAliasMap(
        GenericEntity entity)
    {
        var aliases = new Dictionary<string, GenericRelationship?>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in entity.Relationships)
        {
            var relationshipName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(relationshipName))
            {
                continue;
            }

            AddAlias(relationshipName, relationship);
            AddAlias(relationship.GetRoleOrDefault(), relationship);
        }

        return aliases;

        void AddAlias(
            string alias,
            GenericRelationship relationship)
        {
            if (aliases.TryGetValue(alias, out var existing) &&
                !ReferenceEquals(existing, relationship))
            {
                aliases[alias] = null;
                return;
            }

            aliases[alias] = relationship;
        }
    }
}
