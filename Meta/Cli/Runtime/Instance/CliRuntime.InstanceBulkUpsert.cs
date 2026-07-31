internal sealed partial class CliRuntime
{
    (MetaOperationPlan Plan, IReadOnlyList<string> Ids) BuildBulkInsertPlan(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyList<Dictionary<string, string>> rows,
        bool autoId)
    {
        var propertyByName = entity.Properties.ToDictionary(
            property => property.Name,
            StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
        var existingIds = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reservedIds = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

        if (autoId)
        {
            var nonNumericId = reservedIds.FirstOrDefault(id => !long.TryParse(id, out _));
            if (!string.IsNullOrWhiteSpace(nonNumericId))
            {
                throw new InvalidOperationException(
                    $"Cannot auto-generate Id for entity '{entity.Name}' because existing Id '{nonNumericId}' is not numeric. Use explicit Id values in input.");
            }
        }

        var operations = new List<MetaOperation>();
        var ids = new List<string>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            var row = rows[rowIndex];
            row.TryGetValue("Id", out var providedId);
            var id = providedId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                if (!autoId)
                {
                    throw new InvalidOperationException(
                        $"bulk-insert row {rowNumber} is missing Id. Add an Id column value or use --auto-id.");
                }

                id = GenerateNextIdFromReserved(reservedIds);
            }

            if (!reservedIds.Add(id))
            {
                if (existingIds.Contains(id))
                {
                    throw new InvalidOperationException(
                        $"bulk-insert row {rowNumber} cannot insert '{entity.Name}.{id}' because it already exists.");
                }

                throw new InvalidOperationException(
                    $"bulk-insert row {rowNumber} repeats Id '{id}' from an earlier input row.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relationshipIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in row)
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
                                $"bulk-insert row {rowNumber} is missing required relationship '{relationship.GetColumnName()}'. Set column '{relationship.GetColumnName()}' to a target Id.");
                        }

                        continue;
                    }

                    relationshipIds[relationship.GetColumnName()] = targetId;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Column '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
            }

            EnsureInsertIncludesRequiredRelationships(
                entity,
                relationshipIds,
                operationName: "bulk-insert",
                rowNumber);
            operations.Add(new InsertRecordOperation(entity.Name, id, values, relationshipIds));
            ids.Add(id);
        }

        return (new MetaOperationPlan(operations), ids);
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
            while (reservedIds.Contains(next.ToString(CultureInfo.InvariantCulture)))
            {
                next++;
            }

            return next.ToString(CultureInfo.InvariantCulture);
        }

        var candidate = 1L;
        while (reservedIds.Contains(candidate.ToString(CultureInfo.InvariantCulture)))
        {
            candidate++;
        }

        return candidate.ToString(CultureInfo.InvariantCulture);
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
        var records = ParseDelimitedRecords(input ?? string.Empty, delimiter);
        if (records.Count == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var header = records[0]
            .Select(item => item.Trim().TrimStart('\uFEFF'))
            .ToArray();
        if (header.Length == 0 || header.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Input header is empty or invalid.");
        }

        var duplicateHeader = header
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader != null)
        {
            throw new InvalidOperationException(
                $"Input header contains duplicate column '{duplicateHeader.Key}'.");
        }

        var rows = new List<Dictionary<string, string>>();
        for (var rowIndex = 1; rowIndex < records.Count; rowIndex++)
        {
            var record = records[rowIndex];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (record.Count != header.Length)
            {
                throw new InvalidOperationException(
                    $"Input row {rowIndex + 1} column count ({record.Count}) does not match header ({header.Length}).");
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < header.Length; columnIndex++)
            {
                row.Add(header[columnIndex], record[columnIndex].Trim());
            }

            rows.Add(row);
        }

        return rows;
    }

    IReadOnlyList<IReadOnlyList<string>> ParseDelimitedRecords(string input, char delimiter)
    {
        var records = new List<IReadOnlyList<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < input.Length && input[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                record.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!inQuotes && (character == '\r' || character == '\n'))
            {
                record.Add(field.ToString());
                field.Clear();
                if (record.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    records.Add(record);
                }

                record = new List<string>();
                if (character == '\r' &&
                    index + 1 < input.Length &&
                    input[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            field.Append(character);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("Input contains an unclosed quoted field.");
        }

        record.Add(field.ToString());
        if (record.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            records.Add(record);
        }

        return records;
    }
}
