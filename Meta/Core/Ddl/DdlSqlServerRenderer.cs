using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Meta.Core.Ddl;

public static class DdlSqlServerRenderer
{
    public static string RenderSchema(DdlDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var builder = new StringBuilder();
        builder.AppendLine("-- Deterministic schema script");
        builder.AppendLine();

        foreach (var table in database.Tables
                     .OrderBy(item => item.Schema, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"CREATE TABLE [{EscapeIdentifier(table.Schema)}].[{EscapeIdentifier(table.Name)}] (");
            var lines = table.Columns
                .Select(column =>
                    $"    [{EscapeIdentifier(column.Name)}] {column.DataType} {(column.IsNullable ? "NULL" : "NOT NULL")}")
                .ToList();

            if (table.PrimaryKey != null)
            {
                var pkColumns = string.Join(", ", table.PrimaryKey.ColumnNames.Select(name => $"[{EscapeIdentifier(name)}] ASC"));
                var clustered = table.PrimaryKey.IsClustered ? "CLUSTERED" : "NONCLUSTERED";
                lines.Add($"    CONSTRAINT [{EscapeIdentifier(NormalizeIdentifier(table.PrimaryKey.Name))}] PRIMARY KEY {clustered} ({pkColumns})");
            }

            foreach (var constraint in table.UniqueConstraints
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var uniqueColumns = string.Join(", ", constraint.ColumnNames.Select(name => $"[{EscapeIdentifier(name)}] ASC"));
                lines.Add($"    CONSTRAINT [{EscapeIdentifier(NormalizeIdentifier(constraint.Name))}] UNIQUE ({uniqueColumns})");
            }

            builder.AppendLine(string.Join(",\n", lines));
            builder.AppendLine(");");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        foreach (var table in database.Tables
                     .OrderBy(item => item.Schema, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var constraint in table.ForeignKeys
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var localColumns = string.Join(", ", constraint.ColumnNames.Select(name => $"[{EscapeIdentifier(name)}]"));
                var referencedColumns = string.Join(", ", constraint.ReferencedColumnNames.Select(name => $"[{EscapeIdentifier(name)}]"));
                builder.AppendLine(
                    $"ALTER TABLE [{EscapeIdentifier(table.Schema)}].[{EscapeIdentifier(table.Name)}] WITH CHECK ADD CONSTRAINT [{EscapeIdentifier(NormalizeIdentifier(constraint.Name))}] FOREIGN KEY({localColumns}) REFERENCES [{EscapeIdentifier(constraint.ReferencedSchema)}].[{EscapeIdentifier(constraint.ReferencedTableName)}]({referencedColumns});");
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            foreach (var index in table.Indexes
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var uniqueness = index.IsUnique ? "UNIQUE " : string.Empty;
                var clustering = index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
                var keyColumns = string.Join(", ", index.KeyColumns.Select(column => $"[{EscapeIdentifier(column.Name)}] {(column.IsDescending ? "DESC" : "ASC")}"));
                builder.Append($"CREATE {uniqueness}{clustering}INDEX [{EscapeIdentifier(NormalizeIdentifier(index.Name))}] ON [{EscapeIdentifier(table.Schema)}].[{EscapeIdentifier(table.Name)}] ({keyColumns})");
                if (index.IncludedColumnNames.Count > 0)
                {
                    var includedColumns = string.Join(", ", index.IncludedColumnNames.Select(name => $"[{EscapeIdentifier(name)}]"));
                    builder.Append($" INCLUDE ({includedColumns})");
                }

                builder.AppendLine(";");
                builder.AppendLine("GO");
                builder.AppendLine();
            }
        }

        return NormalizeNewlines(builder.ToString());
    }

    public static string RenderData(DdlDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var builder = new StringBuilder();
        builder.AppendLine("-- Deterministic data script");
        builder.AppendLine();

        foreach (var statement in database.Inserts)
        {
            var columns = string.Join(", ", statement.Values.Select(item => $"[{EscapeIdentifier(item.ColumnName)}]"));
            var values = string.Join(", ", statement.Values.Select(item => item.SqlLiteral));
            builder.AppendLine(
                $"INSERT INTO [{EscapeIdentifier(statement.Schema)}].[{EscapeIdentifier(statement.TableName)}] ({columns}) VALUES ({values});");
        }

        if (database.Inserts.Count > 0)
        {
            builder.AppendLine();
        }

        return NormalizeNewlines(builder.ToString());
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("]", "]]", StringComparison.Ordinal);
    }

    private static string NormalizeIdentifier(string identifier)
    {
        const int maxIdentifierLength = 128;
        if (identifier.Length <= maxIdentifierLength)
        {
            return identifier;
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(identifier));
        var hash = Convert.ToHexString(hashBytes).Substring(0, 16);
        var prefixLength = maxIdentifierLength - 1 - hash.Length;
        return identifier.Substring(0, prefixLength) + "_" + hash;
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }
}
