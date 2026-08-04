internal sealed partial class CliRuntime
{
    BulkUpsertPlan BuildUpsertOperationsFromRows(
        GenericEntity entity,
        IReadOnlyList<RecordData> existingRecords,
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<string> keyFields,
        bool autoId = false)
    {
        var propertyNames = entity.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
        var relationshipByName = entity.Relationships
            .ToDictionary(
                relationship => relationship.GetColumnName(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var keyField in keyFields)
        {
            if (!string.Equals(keyField, "Id", StringComparison.OrdinalIgnoreCase) &&
                !propertyNames.Contains(keyField) &&
                !relationshipByAlias.ContainsKey(keyField))
            {
                throw new InvalidOperationException($"bulk-insert --key field '{keyField}' is not valid for entity '{entity.Name}'.");
            }
        }

        var reservedIds = existingRecords
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (autoId)
        {
            var nonNumericId = reservedIds.FirstOrDefault(id => !long.TryParse(id, out _));
            if (!string.IsNullOrWhiteSpace(nonNumericId))
            {
                throw new InvalidOperationException(
                    $"Cannot auto-generate Id for entity '{entity.Name}' because existing Id '{nonNumericId}' is not numeric. Use explicit Id values in input.");
            }
        }

        var existingIds = existingRecords
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plansById = new Dictionary<string, BulkUpsertRowPlan>(StringComparer.OrdinalIgnoreCase);
        var plansInInputOrder = new List<BulkUpsertRowPlan>();
        var rowIds = new List<string>(rows.Count);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            row.TryGetValue("Id", out var providedId);
            var id = providedId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                if (autoId)
                {
                    id = GenerateNextIdFromReserved(reservedIds);
                    reservedIds.Add(id);
                }
                else if (keyFields.Count > 0)
                {
                    id = ResolveIdByKeys(existingRecords, entity, keyFields, row);
                }
                else
                {
                    throw new InvalidOperationException("bulk-insert row is missing Id and no --key fields were provided.");
                }
            }
            else
            {
                reservedIds.Add(id);
            }

            rowIds.Add(id);
            if (!plansById.TryGetValue(id, out var plan))
            {
                plan = new BulkUpsertRowPlan(
                    id,
                    isNew: !existingIds.Contains(id),
                    firstInputRow: rowIndex + 1);
                plansById.Add(id, plan);
                plansInInputOrder.Add(plan);
            }

            foreach (var pair in row)
            {
                if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (propertyNames.Contains(pair.Key))
                {
                    plan.Values[pair.Key] = pair.Value;
                    continue;
                }

                if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
                {
                    plan.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value);
                    continue;
                }

                throw new InvalidOperationException($"Column '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
            }

        }

        foreach (var plan in plansInInputOrder.Where(plan => plan.IsNew))
        {
            EnsureCreateIncludesRequiredRelationships(
                entity,
                plan.RelationshipIds,
                operationName: "bulk-insert",
                rowNumber: plan.FirstInputRow);
        }

        var operations = new List<Operation>();
        var newPlans = plansInInputOrder
            .Where(plan => plan.IsNew)
            .ToDictionary(plan => plan.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var plan in OrderNewRowsByRequiredDependencies(
                     entity,
                     plansInInputOrder.Where(plan => plan.IsNew),
                     newPlans,
                     relationshipByName))
        {
            var immediateRelationships = plan.RelationshipIds
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Where(item =>
                {
                    var relationship = relationshipByName[item.Key];
                    return !relationship.IsNullable ||
                           !string.Equals(relationship.Entity, entity.Name, StringComparison.OrdinalIgnoreCase) ||
                           !newPlans.ContainsKey(item.Value);
                })
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            operations.Add(new Operation.InsertRecord(
                entity.Name,
                plan.Id,
                plan.Values,
                immediateRelationships));
        }

        foreach (var plan in plansInInputOrder.Where(plan => plan.IsNew))
        {
            foreach (var relationship in plan.RelationshipIds
                         .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                         .Where(item =>
                         {
                             var definition = relationshipByName[item.Key];
                             return definition.IsNullable &&
                                    string.Equals(definition.Entity, entity.Name, StringComparison.OrdinalIgnoreCase) &&
                                    newPlans.ContainsKey(item.Value);
                         }))
            {
                operations.Add(new Operation.SetRelationship(
                    entity.Name,
                    plan.Id,
                    relationship.Key,
                    relationship.Value));
            }
        }

        foreach (var plan in plansInInputOrder.Where(plan => !plan.IsNew))
        {
            foreach (var value in plan.Values)
            {
                operations.Add(new Operation.SetProperty(
                    entity.Name,
                    plan.Id,
                    value.Key,
                    value.Value));
            }

            foreach (var relationship in plan.RelationshipIds)
            {
                operations.Add(string.IsNullOrWhiteSpace(relationship.Value)
                    ? new Operation.ClearRelationship(
                        entity.Name,
                        plan.Id,
                        relationship.Key)
                    : new Operation.SetRelationship(
                        entity.Name,
                        plan.Id,
                        relationship.Key,
                        relationship.Value));
            }
        }

        return new BulkUpsertPlan(operations, rowIds);
    }

    IReadOnlyList<BulkUpsertRowPlan> OrderNewRowsByRequiredDependencies(
        GenericEntity entity,
        IEnumerable<BulkUpsertRowPlan> plans,
        IReadOnlyDictionary<string, BulkUpsertRowPlan> plansById,
        IReadOnlyDictionary<string, GenericRelationship> relationshipsByName)
    {
        var ordered = new List<BulkUpsertRowPlan>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(BulkUpsertRowPlan plan)
        {
            if (visited.Contains(plan.Id))
            {
                return;
            }

            if (!visiting.Add(plan.Id))
            {
                throw new InvalidOperationException(
                    $"bulk-insert cannot order required relationships among new '{entity.Name}' rows because they form a cycle at Id '{plan.Id}'.");
            }

            foreach (var relationship in plan.RelationshipIds)
            {
                var definition = relationshipsByName[relationship.Key];
                if (definition.IsNullable ||
                    !string.Equals(definition.Entity, entity.Name, StringComparison.OrdinalIgnoreCase) ||
                    !plansById.TryGetValue(relationship.Value, out var dependency))
                {
                    continue;
                }

                Visit(dependency);
            }

            visiting.Remove(plan.Id);
            visited.Add(plan.Id);
            ordered.Add(plan);
        }

        foreach (var plan in plans)
        {
            Visit(plan);
        }

        return ordered;
    }

    string ResolveIdByKeys(
        IReadOnlyList<RecordData> existingRecords,
        GenericEntity entity,
        IReadOnlyList<string> keyFields,
        IReadOnlyDictionary<string, string> row)
    {
        var keyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keyFields)
        {
            if (!row.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"bulk-insert --key field '{key}' is missing or empty in input row.");
            }

            var resolvedKey = ResolveQueryField(entity, key);
            keyValues[resolvedKey] = value.Trim();
        }

        var signature = string.Join(
            "\u001f",
            keyValues
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key.ToLowerInvariant()}={pair.Value.ToLowerInvariant()}"));
        var candidates = existingRecords
            .Where(record => keyValues.All(pair =>
                string.Equals(GetRecordFieldValue(record, pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"bulk-insert --key matched multiple rows in '{entity.Name}' for key '{signature}'.");
        }

        throw new InvalidOperationException(
            $"bulk-insert --key found no matching row in '{entity.Name}' for key '{signature}'.");
    }

    static string ResolveQueryField(GenericEntity entity, string fieldName)
    {
        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return "Id";
        }

        var property = entity.Properties.FirstOrDefault(item =>
            string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            return property.Name;
        }

        var relationship = entity.Relationships.FirstOrDefault(item =>
            string.Equals(item.GetRoleOrDefault(), fieldName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.GetColumnName(), fieldName, StringComparison.OrdinalIgnoreCase));
        return relationship?.GetColumnName() ??
               throw new InvalidOperationException(
                   $"Field '{fieldName}' does not exist on entity '{entity.Name}'.");
    }

    string GetRecordFieldValue(RecordData record, string field)
    {
        if (string.Equals(field, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return record.Id ?? string.Empty;
        }

        if (record.Values.TryGetValue(field, out var value))
        {
            return value ?? string.Empty;
        }

        if (record.RelationshipIds.TryGetValue(field, out var relationshipValue))
        {
            return relationshipValue ?? string.Empty;
        }

        return string.Empty;
    }

    string GenerateNextIdFromReserved(ISet<string> reservedIds)
    {
        var numericIds = reservedIds
            .Select(value => long.TryParse(value, out var parsed) ? parsed : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (numericIds.Count > 0)
        {
            var next = numericIds.Max() + 1;
            while (reservedIds.Contains(next.ToString()))
            {
                next++;
            }

            return next.ToString();
        }

        var candidate = 1L;
        while (reservedIds.Contains(candidate.ToString()))
        {
            candidate++;
        }

        return candidate.ToString();
    }

    IReadOnlyList<Dictionary<string, string>> ParseBulkInputRows(string input, string format)
    {
        var effectiveFormat = string.IsNullOrWhiteSpace(format)
            ? DetectBulkFormat(input)
            : format.Trim().ToLowerInvariant();

        return effectiveFormat switch
        {
            "tsv" => ParseDelimitedRows(input, '\t'),
            "csv" => ParseDelimitedRows(input, ','),
            _ => throw new InvalidOperationException($"Unsupported input format '{effectiveFormat}'."),
        };
    }

    string DetectBulkFormat(string input)
    {
        var firstLine = (input ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Contains('\t') ? "tsv" : "csv";
    }

    IReadOnlyList<Dictionary<string, string>> ParseDelimitedRows(string input, char delimiter)
    {
        var lines = (input ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (lines.Count == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var header = lines[0].Split(delimiter).Select(item => item.Trim()).ToArray();
        if (header.Length == 0 || header.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Input header is empty or invalid.");
        }

        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Count; i++)
        {
            var parts = lines[i].Split(delimiter);
            if (parts.Length != header.Length)
            {
                throw new InvalidOperationException(
                    $"Input row {i + 1} column count ({parts.Length}) does not match header ({header.Length}).");
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < header.Length; c++)
            {
                row[header[c]] = parts[c].Trim();
            }

            rows.Add(row);
        }

        return rows;
    }

    sealed record BulkUpsertPlan(
        IReadOnlyList<Operation> Operations,
        IReadOnlyList<string> RowIds);

    sealed class BulkUpsertRowPlan
    {
        public BulkUpsertRowPlan(
            string id,
            bool isNew,
            int firstInputRow)
        {
            Id = id;
            IsNew = isNew;
            FirstInputRow = firstInputRow;
        }

        public string Id { get; }
        public bool IsNew { get; }
        public int FirstInputRow { get; }
        public Dictionary<string, string> Values { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> RelationshipIds { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
