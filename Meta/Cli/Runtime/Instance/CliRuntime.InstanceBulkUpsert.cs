internal sealed partial class CliRuntime
{
    WorkspaceOp BuildUpsertOperationFromRows(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<string> keyFields,
        bool autoEnsure,
        bool autoId = false)
    {
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.BulkUpsertRows,
            EntityName = entity.Name,
        };

        var propertyNames = entity.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);

        foreach (var keyField in keyFields)
        {
            if (!string.Equals(keyField, "Id", StringComparison.OrdinalIgnoreCase) &&
                !propertyNames.Contains(keyField) &&
                !relationshipByAlias.ContainsKey(keyField))
            {
                throw new InvalidOperationException($"bulk-insert --key field '{keyField}' is not valid for entity '{entity.Name}'.");
            }
        }

        var reservedIds = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
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

        var createdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingOrPlannedIds = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                    id = ResolveIdByKeys(workspace, entity, keyFields, row, autoEnsure, createdByKey, reservedIds);
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

            var createsNewRow = !existingOrPlannedIds.Contains(id);
            existingOrPlannedIds.Add(id);

            var patch = new RowPatch
            {
                Id = id,
                Values =
                {
                    ["Id"] = id,
                },
            };

            foreach (var pair in row)
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
                    patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value, relationshipUsageName);
                    continue;
                }

                throw new InvalidOperationException($"Column '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
            }

            if (createsNewRow)
            {
                EnsureCreatePatchIncludesRequiredRelationships(entity, patch, operationName: "bulk-insert", rowNumber: rowIndex + 1);
            }

            operation.RowPatches.Add(patch);
        }

        return operation;
    }

    string ResolveIdByKeys(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyList<string> keyFields,
        IReadOnlyDictionary<string, string> row,
        bool autoEnsure,
        IDictionary<string, string> createdByKey,
        ISet<string> reservedIds)
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
        if (createdByKey.TryGetValue(signature, out var existingCreatedId))
        {
            return existingCreatedId;
        }

        var candidates = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
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

        if (!autoEnsure)
        {
            throw new InvalidOperationException(
                $"bulk-insert --key found no matching row in '{entity.Name}' for key '{signature}'.");
        }

        var createdId = GenerateNextIdFromReserved(reservedIds);
        reservedIds.Add(createdId);
        createdByKey[signature] = createdId;
        return createdId;
    }

    string GetRecordFieldValue(GenericRecord record, string field)
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
}
