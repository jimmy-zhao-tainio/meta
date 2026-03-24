using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Services;

public static class DataBatchParser
{
    public static WorkspaceOp ParseBulkUpsert(string entityName, GenericEntity entity, string input)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new InvalidOperationException("Entity name is required.");
        }

        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Bulk upsert input is empty.");
        }

        var lines = SplitNonEmptyLines(input);
        if (lines.Count < 2)
        {
            throw new InvalidOperationException("Bulk upsert requires a header row and at least one data row.");
        }

        var delimiter = DetectDelimiter(lines[0]);
        var headers = ParseDelimitedLine(lines[0], delimiter)
            .Select(value => NormalizeColumnName(value))
            .ToList();

        if (headers.Count == 0)
        {
            throw new InvalidOperationException("Header row is empty.");
        }

        var duplicateHeader = headers
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHeader != null)
        {
            throw new InvalidOperationException($"Header column '{duplicateHeader.Key}' is duplicated.");
        }

        var idIndex = headers.FindIndex(value => string.Equals(value, "Id", StringComparison.OrdinalIgnoreCase));
        if (idIndex < 0)
        {
            throw new InvalidOperationException("Bulk upsert header must include 'Id'.");
        }

        var propertyMap = entity.Properties.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var relationshipHeaderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in entity.Relationships)
        {
            var relationshipName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(relationshipName))
            {
                continue;
            }

            TryAddRelationshipHeaderAlias(entityName, relationshipHeaderMap, relationshipName, relationshipName);
        }

        var columnKinds = new List<ColumnKind>(headers.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (string.Equals(header, "Id", StringComparison.OrdinalIgnoreCase))
            {
                columnKinds.Add(new ColumnKind(ColumnType.Id, "Id"));
                continue;
            }

            if (propertyMap.ContainsKey(header))
            {
                columnKinds.Add(new ColumnKind(ColumnType.Property, propertyMap[header].Name));
                continue;
            }

            if (relationshipHeaderMap.TryGetValue(header, out var relationshipName))
            {
                columnKinds.Add(new ColumnKind(ColumnType.Relationship, relationshipName));
                continue;
            }

            throw new InvalidOperationException(
                $"Column '{header}' is not a property or relationship on entity '{entityName}'.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patches = new List<RowPatch>();
        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var values = ParseDelimitedLine(line, delimiter);
            if (values.Count > headers.Count)
            {
                throw new InvalidOperationException(
                    $"Row {lineIndex + 1} has {values.Count} values but header has {headers.Count} columns.");
            }

            while (values.Count < headers.Count)
            {
                values.Add(string.Empty);
            }

            var id = values[idIndex].Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"Row {lineIndex + 1} is missing Id.");
            }

            if (!seenIds.Add(id))
            {
                throw new InvalidOperationException($"Row {lineIndex + 1} duplicates Id '{id}' in input batch.");
            }

            var patch = new RowPatch
            {
                Id = id,
            };

            for (var columnIndex = 0; columnIndex < columnKinds.Count; columnIndex++)
            {
                if (columnIndex == idIndex)
                {
                    continue;
                }

                var value = values[columnIndex];
                var kind = columnKinds[columnIndex];
                if (kind.Type == ColumnType.Property)
                {
                    patch.Values[kind.Name] = value;
                }
                else if (kind.Type == ColumnType.Relationship)
                {
                    patch.RelationshipIds[kind.Name] = value;
                }
            }

            patches.Add(patch);
        }

        return new WorkspaceOp
        {
            Type = WorkspaceOpTypes.BulkUpsertRows,
            EntityName = entityName,
            RowPatches = patches,
        };
    }

    public static WorkspaceOp ParseDeleteRows(string entityName, string input)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new InvalidOperationException("Entity name is required.");
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Delete rows input is empty.");
        }

        var tokens = input
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(new[] { '\n', ',', ';', '\t' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException("No Id values found.");
        }

        if (string.Equals(tokens[0], "Id", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        var ids = new HashSet<string>(tokens.Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("No Id values found.");
        }

        return new WorkspaceOp
        {
            Type = WorkspaceOpTypes.DeleteRows,
            EntityName = entityName,
            Ids = ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static List<string> SplitNonEmptyLines(string input)
    {
        return input
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static char DetectDelimiter(string line)
    {
        if (line.Contains('\t'))
        {
            return '\t';
        }

        if (line.Contains(','))
        {
            return ',';
        }

        throw new InvalidOperationException("Header must use tab or comma delimiters.");
    }

    private static List<string> ParseDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    buffer.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                values.Add(buffer.ToString().Trim());
                buffer.Clear();
                continue;
            }

            buffer.Append(ch);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("Unclosed quote in delimited input.");
        }

        values.Add(buffer.ToString().Trim());
        return values;
    }

    private static string NormalizeColumnName(string value)
    {
        return value.Trim().TrimStart('\uFEFF');
    }

    private static void TryAddRelationshipHeaderAlias(
        string entityName,
        IDictionary<string, string> relationshipHeaderMap,
        string alias,
        string usageName)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        if (relationshipHeaderMap.TryGetValue(alias, out var existingUsageName) &&
            !string.Equals(existingUsageName, usageName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Entity '{entityName}' has ambiguous relationship header alias '{alias}'.");
        }

        relationshipHeaderMap[alias] = usageName;
    }

    private enum ColumnType
    {
        Id,
        Property,
        Relationship,
    }

    private readonly record struct ColumnKind(ColumnType Type, string Name);
}

