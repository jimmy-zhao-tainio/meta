using System.Globalization;
using Microsoft.Data.SqlClient;
using Meta.Core.Ddl;
using Meta.Core.Domain;

namespace Meta.Adapters;

internal static class SqlServerMetaStorageValidator
{
    public static async Task ValidateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericModel model,
        CancellationToken cancellationToken)
    {
        var idColumns = new Dictionary<string, SqlServerColumnRow>(
            StringComparer.OrdinalIgnoreCase);
        var columnsByEntity = new Dictionary<
            string,
            Dictionary<string, SqlServerColumnRow>>(
            StringComparer.OrdinalIgnoreCase);
        var checksByEntity = new Dictionary<
            string,
            Dictionary<string, SqlServerCheckConstraintRow>>(
            StringComparer.OrdinalIgnoreCase);
        var storedRelationships =
            (await SqlServerImportReader.LoadRelationshipsAsync(
                    connection,
                    schema,
                    cancellationToken,
                    transaction)
                .ConfigureAwait(false))
            .ToDictionary(
                relationship =>
                    RelationshipKey(
                        relationship.SourceTable,
                        relationship.SourceColumn),
                StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var columns = await SqlServerImportReader.LoadColumnsAsync(
                    connection,
                    schema,
                    entity.Name,
                    cancellationToken,
                    transaction)
                .ConfigureAwait(false);
            var byName = columns.ToDictionary(
                column => column.Name,
                StringComparer.OrdinalIgnoreCase);
            columnsByEntity.Add(entity.Name, byName);
            var checks =
                await SqlServerImportReader.LoadCheckConstraintsAsync(
                        connection,
                        schema,
                        entity.Name,
                        cancellationToken,
                        transaction)
                    .ConfigureAwait(false);
            checksByEntity.Add(
                entity.Name,
                checks.ToDictionary(
                    constraint => constraint.Name,
                    StringComparer.OrdinalIgnoreCase));

            var idColumn = RequireColumn(
                byName,
                schema,
                entity.Name,
                "Id");
            RequireNvarchar(
                idColumn,
                schema,
                entity.Name,
                expectedLength: 128,
                role: "identity");
            if (idColumn.IsNullable)
            {
                throw InvalidStorage(
                    schema,
                    entity.Name,
                    "Id",
                    "the identity column must be required");
            }

            if (!string.Equals(
                    idColumn.CollationName,
                    MetaSqlStorageContract.IdentityCollation,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidStorage(
                    schema,
                    entity.Name,
                    "Id",
                    $"the identity collation must be {MetaSqlStorageContract.IdentityCollation}");
            }
            RequireIdentityConstraint(
                checksByEntity[entity.Name],
                schema,
                entity.Name,
                "Id");

            var primaryKeyColumns =
                await SqlServerImportReader.LoadPrimaryKeyColumnsAsync(
                        connection,
                        schema,
                        entity.Name,
                        cancellationToken,
                        transaction)
                    .ConfigureAwait(false);
            if (primaryKeyColumns.Count != 1 ||
                !string.Equals(
                    primaryKeyColumns[0],
                    "Id",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"SQL operation sessions require an encoded Meta workspace. Table '{schema}.{entity.Name}' must have a single-column primary key on Id.");
            }

            foreach (var property in entity.Properties)
            {
                var column = RequireColumn(
                    byName,
                    schema,
                    entity.Name,
                    property.Name);
                RequireNvarchar(
                    column,
                    schema,
                    entity.Name,
                    expectedLength: -1,
                    role: "property");
            }

            idColumns.Add(entity.Name, idColumn);
        }

        foreach (var entity in model.Entities)
        {
            var byName = columnsByEntity[entity.Name];
            foreach (var relationship in entity.Relationships)
            {
                var columnName = relationship.GetColumnName();
                if (!storedRelationships.TryGetValue(
                        RelationshipKey(entity.Name, columnName),
                        out var storedRelationship) ||
                    !string.Equals(
                        storedRelationship.TargetTable,
                        relationship.Entity,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidStorage(
                        schema,
                        entity.Name,
                        columnName,
                        $"the foreign key to '{relationship.Entity}.Id' is missing");
                }

                if (storedRelationship.IsDisabled ||
                    storedRelationship.IsNotTrusted)
                {
                    throw InvalidStorage(
                        schema,
                        entity.Name,
                        columnName,
                        "the foreign key must be enabled and trusted");
                }

                var column = RequireColumn(
                    byName,
                    schema,
                    entity.Name,
                    columnName);
                RequireNvarchar(
                    column,
                    schema,
                    entity.Name,
                    expectedLength: 128,
                    role: "relationship");
                if (!string.Equals(
                        column.CollationName,
                        MetaSqlStorageContract.IdentityCollation,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidStorage(
                        schema,
                        entity.Name,
                        columnName,
                        $"the relationship collation must be {MetaSqlStorageContract.IdentityCollation}");
                }
                RequireIdentityConstraint(
                    checksByEntity[entity.Name],
                    schema,
                    entity.Name,
                    columnName);

                var targetIdColumn = idColumns[relationship.Entity];
                if (!string.Equals(
                        column.CollationName,
                        targetIdColumn.CollationName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw InvalidStorage(
                        schema,
                        entity.Name,
                        columnName,
                        $"the relationship collation must match '{schema}.{relationship.Entity}.Id'");
                }
            }
        }
    }

    private static string RelationshipKey(
        string entityName,
        string columnName)
    {
        return entityName + "\0" + columnName;
    }

    private static void RequireIdentityConstraint(
        IReadOnlyDictionary<string, SqlServerCheckConstraintRow> constraints,
        string schema,
        string entityName,
        string columnName)
    {
        var name =
            MetaSqlStorageContract.GetIdentityCheckConstraintName(
                entityName,
                columnName);
        if (!constraints.TryGetValue(name, out var constraint))
        {
            throw InvalidStorage(
                schema,
                entityName,
                columnName,
                $"required identity constraint '{name}' is missing");
        }

        if (constraint.IsDisabled || constraint.IsNotTrusted)
        {
            throw InvalidStorage(
                schema,
                entityName,
                columnName,
                $"identity constraint '{name}' must be enabled and trusted");
        }

        var quotedColumn =
            $"[{columnName.Replace("]", "]]", StringComparison.Ordinal)}]";
        var requiredDefinitionParts = new[]
        {
            $"datalength({quotedColumn})",
            $"left({quotedColumn}",
            $"right({quotedColumn}",
            MetaSqlStorageContract.IdentityCharacterCollation,
            "%[^ -~]%",
        };
        if (requiredDefinitionParts.Any(part =>
                !constraint.Definition.Contains(
                    part,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw InvalidStorage(
                schema,
                entityName,
                columnName,
                $"identity constraint '{name}' does not enforce the Meta SQL identity repertoire");
        }
    }

    private static SqlServerColumnRow RequireColumn(
        IReadOnlyDictionary<string, SqlServerColumnRow> columns,
        string schema,
        string entityName,
        string columnName)
    {
        return columns.TryGetValue(columnName, out var column)
            ? column
            : throw InvalidStorage(
                schema,
                entityName,
                columnName,
                "the column is missing");
    }

    private static void RequireNvarchar(
        SqlServerColumnRow column,
        string schema,
        string entityName,
        int expectedLength,
        string role)
    {
        if (string.Equals(
                column.DataType,
                "nvarchar",
                StringComparison.OrdinalIgnoreCase) &&
            column.CharacterMaximumLength == expectedLength)
        {
            return;
        }

        var expected = expectedLength == -1
            ? "nvarchar(max)"
            : $"nvarchar({expectedLength.ToString(CultureInfo.InvariantCulture)})";
        throw InvalidStorage(
            schema,
            entityName,
            column.Name,
            $"the {role} column must use {expected}");
    }

    private static InvalidOperationException InvalidStorage(
        string schema,
        string entityName,
        string columnName,
        string detail)
    {
        return new InvalidOperationException(
            $"SQL operation sessions require an encoded Meta workspace. Column '{schema}.{entityName}.{columnName}' is not compatible: {detail}.");
    }
}
