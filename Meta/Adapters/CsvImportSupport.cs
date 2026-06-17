using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Meta.Adapters;

internal static class CsvImportSupport
{
    private const int MaxIdentifierLength = 128;
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int ResolveIdColumnIndex(IReadOnlyList<string> headerRow, string idColumn)
    {
        var matches = new List<int>();
        for (var index = 0; index < headerRow.Count; index++)
        {
            if (string.Equals(headerRow[index]?.Trim(), idColumn.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(index);
            }
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"CSV file must include Id column '{idColumn}'.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"CSV file contains duplicate Id column '{idColumn}'.");
        }

        return matches[0];
    }

    public static List<CsvColumnPlan> BuildColumnPlans(IReadOnlyList<string> headerRow, int idColumnIndex)
    {
        var usedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<CsvColumnPlan>();
        for (var index = 0; index < headerRow.Count; index++)
        {
            if (index == idColumnIndex)
            {
                continue;
            }

            var rawHeader = index < headerRow.Count ? headerRow[index] : string.Empty;
            var fallback = "Column" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var normalized = NormalizeIdentifier(rawHeader, fallback);
            if (string.Equals(normalized, "Id", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "IdValue";
            }

            var unique = MakeUniqueIdentifier(normalized, usedPropertyNames);
            plans.Add(new CsvColumnPlan(index, unique));
        }

        return plans;
    }

    public static List<List<string>> ParseRows(string csvText)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csvText.Length; index++)
        {
            var ch = csvText[index];
            if (ch == '"')
            {
                if (inQuotes && index + 1 < csvText.Length && csvText[index + 1] == '"')
                {
                    currentCell.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && ch == ',')
            {
                AppendCell(currentRow, currentCell);
                continue;
            }

            if (!inQuotes && (ch == '\r' || ch == '\n'))
            {
                AppendCell(currentRow, currentCell);
                if (!IsRowCompletelyEmpty(currentRow))
                {
                    rows.Add(currentRow);
                }

                currentRow = new List<string>();
                if (ch == '\r' && index + 1 < csvText.Length && csvText[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            currentCell.Append(ch);
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("CSV contains an unclosed quoted field.");
        }

        AppendCell(currentRow, currentCell);
        if (!IsRowCompletelyEmpty(currentRow) || rows.Count == 0)
        {
            rows.Add(currentRow);
        }

        return rows;
    }

    public static string GetCellValue(IReadOnlyList<string> row, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= row.Count)
        {
            return string.Empty;
        }

        return row[columnIndex];
    }

    public static bool IsRowCompletelyEmpty(IReadOnlyCollection<string> row)
    {
        return row.Count == 0 || row.All(cell => string.IsNullOrWhiteSpace(cell));
    }

    public static string InferDataType(IReadOnlyCollection<string> values)
    {
        var nonEmptyValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (nonEmptyValues.Count == 0)
        {
            return "string";
        }

        if (nonEmptyValues.All(value => bool.TryParse(value, out _)))
        {
            return "bool";
        }

        if (nonEmptyValues.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "int";
        }

        if (nonEmptyValues.All(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "long";
        }

        if (nonEmptyValues.All(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            return "decimal";
        }

        if (nonEmptyValues.All(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out _)))
        {
            return "datetime";
        }

        return "string";
    }

    public static string NormalizeIdentifier(string value, string fallback)
    {
        var input = (value ?? string.Empty).Trim().TrimStart('\uFEFF');
        if (input.Length == 0)
        {
            input = fallback;
        }

        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        var normalized = builder.ToString().Trim('_');
        if (normalized.Length == 0)
        {
            normalized = fallback;
        }

        if (!char.IsLetter(normalized[0]) && normalized[0] != '_')
        {
            normalized = "_" + normalized;
        }

        normalized = CollapseUnderscores(normalized);
        if (normalized.Length > MaxIdentifierLength)
        {
            normalized = normalized[..MaxIdentifierLength];
        }

        if (!IdentifierPattern.IsMatch(normalized))
        {
            normalized = "_" + normalized.TrimStart('_');
            if (normalized.Length > MaxIdentifierLength)
            {
                normalized = normalized[..MaxIdentifierLength];
            }
        }

        ValidateIdentifier(normalized, "Identifier");
        return normalized;
    }

    private static void AppendCell(ICollection<string> row, StringBuilder currentCell)
    {
        var value = currentCell
            .ToString()
            .Trim()
            .TrimStart('\uFEFF');
        row.Add(value);
        currentCell.Clear();
    }

    private static string CollapseUnderscores(string value)
    {
        if (!value.Contains('_', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;
        foreach (var ch in value)
        {
            if (ch == '_')
            {
                if (previousWasUnderscore)
                {
                    continue;
                }

                previousWasUnderscore = true;
                builder.Append(ch);
                continue;
            }

            previousWasUnderscore = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string MakeUniqueIdentifier(string normalizedBase, ISet<string> usedNames)
    {
        if (usedNames.Add(normalizedBase))
        {
            return normalizedBase;
        }

        var suffix = 2;
        while (true)
        {
            var suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var maxBaseLength = MaxIdentifierLength - suffixText.Length;
            var baseName = normalizedBase.Length <= maxBaseLength
                ? normalizedBase
                : normalizedBase[..maxBaseLength];
            var candidate = baseName + suffixText;
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static void ValidateIdentifier(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (value.Length > MaxIdentifierLength)
        {
            throw new InvalidOperationException($"{label} '{value}' exceeds {MaxIdentifierLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        if (!IdentifierPattern.IsMatch(value))
        {
            throw new InvalidOperationException($"{label} '{value}' is not a valid SQL identifier.");
        }
    }
}

internal sealed class CsvColumnPlan
{
    public CsvColumnPlan(int columnIndex, string propertyName)
    {
        ColumnIndex = columnIndex;
        PropertyName = propertyName;
    }

    public int ColumnIndex { get; }
    public string PropertyName { get; }
}
