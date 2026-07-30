using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;

namespace Meta.Adapters;

internal static class SqlServerMetaModelReader
{
    private const int MaxIdentifierLength = 128;
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string NormalizeSchema(string? schema)
    {
        var effectiveSchema = string.IsNullOrWhiteSpace(schema)
            ? "dbo"
            : schema.Trim();
        ValidateIdentifier(effectiveSchema, "Schema name");
        return effectiveSchema;
    }

    public static async Task<GenericModel> LoadAsync(
        SqlConnection connection,
        string schema,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var effectiveSchema = NormalizeSchema(schema);
        var model = new GenericModel
        {
            Name = connection.Database ?? "MetadataModel",
        };
        ValidateIdentifier(model.Name, "Database name");

        var entityLookup = new Dictionary<string, GenericEntity>(
            StringComparer.OrdinalIgnoreCase);
        var tableNames = await SqlServerImportReader.LoadTableNamesAsync(
                connection,
                effectiveSchema,
                cancellationToken,
                transaction)
            .ConfigureAwait(false);
        foreach (var tableName in tableNames)
        {
            ValidateIdentifier(tableName, "Table name");
            if (entityLookup.ContainsKey(tableName))
            {
                throw new InvalidOperationException(
                    $"Duplicate table name '{tableName}' in schema '{effectiveSchema}'.");
            }

            var entity = new GenericEntity
            {
                Name = tableName,
            };
            model.Entities.Add(entity);
            entityLookup.Add(tableName, entity);
        }

        foreach (var entity in model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = await SqlServerImportReader.LoadColumnsAsync(
                    connection,
                    effectiveSchema,
                    entity.Name,
                    cancellationToken,
                    transaction)
                .ConfigureAwait(false);
            ApplyEntityColumns(entity, columns);
        }

        var relationships = await SqlServerImportReader.LoadRelationshipsAsync(
                connection,
                effectiveSchema,
                cancellationToken,
                transaction)
            .ConfigureAwait(false);
        foreach (var relationship in relationships)
        {
            if (!entityLookup.TryGetValue(
                    relationship.SourceTable,
                    out var sourceEntity) ||
                !entityLookup.TryGetValue(
                    relationship.TargetTable,
                    out var targetEntity))
            {
                continue;
            }

            if (!string.Equals(
                    relationship.TargetColumn,
                    "Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Foreign key '{relationship.ConstraintName}' on '{sourceEntity.Name}.{relationship.SourceColumn}' must reference '{targetEntity.Name}.Id'.");
            }

            var sourceColumnName = relationship.SourceColumn.Trim();
            ValidateIdentifier(
                sourceColumnName,
                $"Foreign key column on table '{sourceEntity.Name}'");
            if (!sourceColumnName.EndsWith(
                    "Id",
                    StringComparison.OrdinalIgnoreCase) ||
                sourceColumnName.Length <= 2)
            {
                throw new InvalidOperationException(
                    $"Foreign key '{relationship.ConstraintName}' on '{sourceEntity.Name}.{relationship.SourceColumn}' must use an '<Role>Id' column name.");
            }

            var role = sourceColumnName[..^2];
            if (sourceEntity.Relationships.Any(item =>
                    string.Equals(
                        item.GetRoleOrDefault(),
                        role,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Table '{sourceEntity.Name}' has duplicate relationship role '{role}'.");
            }

            sourceEntity.Relationships.Add(new GenericRelationship
            {
                Entity = targetEntity.Name,
                Role = string.Equals(
                    role,
                    targetEntity.Name,
                    StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : role,
                IsNullable = relationship.IsNullable,
            });
        }

        foreach (var entity in model.Entities)
        {
            NormalizeRelationshipProperties(entity);
        }

        return model;
    }

    public static void ValidateIdentifier(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (value.Length > MaxIdentifierLength)
        {
            throw new InvalidOperationException(
                $"{label} '{value}' exceeds max length {MaxIdentifierLength}.");
        }

        if (!IdentifierPattern.IsMatch(value))
        {
            throw new InvalidOperationException(
                $"{label} '{value}' is invalid. Use [A-Za-z_][A-Za-z0-9_]* and max length {MaxIdentifierLength}.");
        }
    }

    private static void ApplyEntityColumns(
        GenericEntity entity,
        IReadOnlyCollection<SqlServerColumnRow> columns)
    {
        var properties = new List<GenericProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            ValidateIdentifier(
                column.Name,
                $"Column name on table '{entity.Name}'");
            if (!seen.Add(column.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate column '{column.Name}' on table '{entity.Name}'.");
            }

            if (string.Equals(
                    column.Name,
                    "Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            properties.Add(new GenericProperty
            {
                Name = column.Name,
                IsNullable = column.IsNullable,
            });
        }

        if (!seen.Contains("Id"))
        {
            throw new InvalidOperationException(
                $"Table '{entity.Name}' must contain required column 'Id'.");
        }

        entity.Properties.Clear();
        entity.Properties.AddRange(properties);
    }

    private static void NormalizeRelationshipProperties(GenericEntity entity)
    {
        if (entity.Relationships.Count == 0 ||
            entity.Properties.Count == 0)
        {
            return;
        }

        var relationshipColumns = entity.Relationships
            .Where(item => !string.IsNullOrWhiteSpace(item.Entity))
            .Select(item => item.GetColumnName())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        entity.Properties.RemoveAll(property =>
            relationshipColumns.Contains(property.Name));
    }
}
