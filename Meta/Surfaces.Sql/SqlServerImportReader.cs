using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

internal static class SqlServerImportReader
{
    private const string TextComparisonCollation =
        SqlWorkspaceContract.CaseInsensitiveCollation;
    private const string DeterministicOrderCollation =
        "Latin1_General_100_BIN2";

    public static async IAsyncEnumerable<RecordData> StreamRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericEntity entity,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var record in StreamRowsCoreAsync(
                           connection,
                           transaction,
                           schema,
                           entity,
                           filterId: null,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    public static async Task<RecordData?> ReadRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericEntity entity,
        string id,
        CancellationToken cancellationToken)
    {
        await foreach (var record in StreamRowsCoreAsync(
                           connection,
                           transaction,
                           schema,
                           entity,
                           id,
                           cancellationToken).ConfigureAwait(false))
        {
            return record;
        }

        return null;
    }

    public static async Task<long> CountRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericEntity entity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT COUNT_BIG(1) FROM [{EscapeSqlIdentifier(schema)}].[{EscapeSqlIdentifier(entity.Name)}];";
        var count = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture);
    }

    public static async Task<RecordQueryResult> QueryRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericEntity entity,
        RecordQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var predicate = BuildQueryPredicate(entity, query, command);
        command.Parameters.Add(new SqlParameter(
            "@maximumRecords",
            SqlDbType.Int)
        {
            Value = query.MaximumRecords,
        });
        command.CommandText =
            $"SELECT TOP (@maximumRecords) COUNT_BIG(1) OVER() AS [__MetaTotalCount], {string.Join(", ", SelectedColumns(entity))} " +
            $"FROM [{EscapeSqlIdentifier(schema)}].[{EscapeSqlIdentifier(entity.Name)}]{predicate} " +
            $"ORDER BY [Id] COLLATE {DeterministicOrderCollation};";

        await using var reader = await command.ExecuteReaderAsync(
                cancellationToken)
            .ConfigureAwait(false);
        var columnOrdinals = ReadColumnOrdinals(reader, firstDataOrdinal: 1);
        var records = new List<RecordData>();
        long totalCount = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (records.Count == 0)
            {
                totalCount = reader.GetInt64(0);
            }

            records.Add(ReadCurrentRecord(
                reader,
                schema,
                entity,
                columnOrdinals));
        }

        return new RecordQueryResult(totalCount, records);
    }

    private static async IAsyncEnumerable<RecordData> StreamRowsCoreAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericEntity entity,
        string? filterId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tableName = EscapeSqlIdentifier(entity.Name);
        var schemaName = EscapeSqlIdentifier(schema);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = filterId == null
            ? $"SELECT {string.Join(", ", SelectedColumns(entity))} " +
              $"FROM [{schemaName}].[{tableName}] " +
              $"ORDER BY [Id] COLLATE {DeterministicOrderCollation};"
            : $"SELECT {string.Join(", ", SelectedColumns(entity))} " +
              $"FROM [{schemaName}].[{tableName}] WHERE [Id] = @id;";
        if (filterId != null)
        {
            command.Parameters.Add(new SqlParameter(
                "@id",
                SqlDbType.NVarChar,
                MetaIdentity.MaximumLength)
            {
                Value = filterId,
            });
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columnOrdinals = ReadColumnOrdinals(reader, firstDataOrdinal: 0);

        if (!columnOrdinals.ContainsKey("Id"))
        {
            throw new InvalidOperationException(
                $"Table '{schema}.{entity.Name}' does not include required column 'Id'.");
        }

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return ReadCurrentRecord(
                reader,
                schema,
                entity,
                columnOrdinals);
        }
    }

    private static string BuildQueryPredicate(
        GenericEntity entity,
        RecordQuery query,
        SqlCommand command)
    {
        if (query.Conditions.Count == 0)
        {
            return string.Empty;
        }

        var predicates = new List<string>();
        for (var index = 0; index < query.Conditions.Count; index++)
        {
            var condition = query.Conditions[index];
            var column = $"[{EscapeSqlIdentifier(ResolveField(entity, condition.FieldName))}]";
            var parameterName = $"@condition{index}";
            command.Parameters.Add(new SqlParameter(
                parameterName,
                SqlDbType.NVarChar,
                -1)
            {
                Value = condition.Value,
            });
            predicates.Add(condition switch
            {
                RecordCondition.Equal =>
                    $"COALESCE({column}, N'') COLLATE {TextComparisonCollation} = {parameterName} COLLATE {TextComparisonCollation}",
                RecordCondition.Contains =>
                    $"CHARINDEX({parameterName} COLLATE {TextComparisonCollation}, COALESCE({column}, N'') COLLATE {TextComparisonCollation}) > 0",
                _ => throw new InvalidOperationException(
                    $"Unsupported record condition '{condition.GetType().Name}'."),
            });
        }

        return " WHERE " + string.Join(" AND ", predicates);
    }

    private static string ResolveField(
        GenericEntity entity,
        string fieldName)
    {
        if (MetaName.Comparer.Equals(fieldName, "Id"))
        {
            return "Id";
        }

        var property = entity.Properties.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(candidate.Name, fieldName));
        if (property != null)
        {
            return property.Name;
        }

        var relationship = entity.Relationships.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(
                candidate.GetRoleOrDefault(),
                fieldName) ||
            MetaName.Comparer.Equals(candidate.GetColumnName(), fieldName));
        return relationship?.GetColumnName() ??
               throw new InvalidOperationException(
                   $"Field '{fieldName}' does not exist on entity '{entity.Name}'.");
    }

    private static IEnumerable<string> SelectedColumns(GenericEntity entity) =>
        new[] { "Id" }
            .Concat(entity.Properties.Select(property => property.Name))
            .Concat(entity.Relationships.Select(
                relationship => relationship.GetColumnName()))
            .Distinct(MetaName.Comparer)
            .Select(name => $"[{EscapeSqlIdentifier(name)}]");

    private static IReadOnlyDictionary<string, int> ReadColumnOrdinals(
        SqlDataReader reader,
        int firstDataOrdinal)
    {
        var columnOrdinals = new Dictionary<string, int>(MetaName.Comparer);
        for (var index = firstDataOrdinal; index < reader.FieldCount; index++)
        {
            var name = reader.GetName(index);
            if (!columnOrdinals.TryAdd(name, index))
            {
                throw new InvalidOperationException(
                    $"SQL result contains duplicate data column '{name}'.");
            }
        }

        return columnOrdinals;
    }

    private static RecordData ReadCurrentRecord(
        SqlDataReader reader,
        string schema,
        GenericEntity entity,
        IReadOnlyDictionary<string, int> columnOrdinals)
    {
        if (!columnOrdinals.TryGetValue("Id", out var idOrdinal))
        {
            throw new InvalidOperationException(
                $"Table '{schema}.{entity.Name}' does not include required column 'Id'.");
        }

        if (reader.IsDBNull(idOrdinal))
        {
            throw new InvalidOperationException(
                $"Table '{schema}.{entity.Name}' contains null Id values.");
        }

        var id = Convert.ToString(
            reader.GetValue(idOrdinal),
            CultureInfo.InvariantCulture);
        if (!MetaIdentity.TryValidate(id, out var identityError))
        {
            throw new InvalidOperationException(
                $"Table '{schema}.{entity.Name}' contains invalid Id '{id}'. {identityError}");
        }

        var values = new Dictionary<string, string>(MetaName.Comparer);
        var relationshipIds = new Dictionary<string, string>(MetaName.Comparer);
        foreach (var property in entity.Properties)
        {
            if (!columnOrdinals.TryGetValue(
                    property.Name,
                    out var propertyOrdinal) ||
                reader.IsDBNull(propertyOrdinal))
            {
                continue;
            }

            var textValue = Convert.ToString(
                reader.GetValue(propertyOrdinal),
                CultureInfo.InvariantCulture);
            if (textValue != null)
            {
                values[property.Name] = textValue;
            }
        }

        foreach (var relationship in entity.Relationships)
        {
            var columnName = relationship.GetColumnName();
            if (!columnOrdinals.TryGetValue(
                    columnName,
                    out var relationshipOrdinal))
            {
                throw new InvalidOperationException(
                    $"Table '{schema}.{entity.Name}' is missing relationship column '{columnName}'.");
            }

            if (reader.IsDBNull(relationshipOrdinal))
            {
                if (relationship.IsNullable)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Table '{schema}.{entity.Name}' has null relationship value for '{columnName}' on row '{id}'.");
            }

            var relationshipId = Convert.ToString(
                reader.GetValue(relationshipOrdinal),
                CultureInfo.InvariantCulture);
            if (!MetaIdentity.TryValidate(
                    relationshipId,
                    out var relationshipIdentityError))
            {
                throw new InvalidOperationException(
                    $"Table '{schema}.{entity.Name}' has invalid relationship value '{relationshipId}' for '{columnName}' on row '{id}'. {relationshipIdentityError}");
            }

            relationshipIds[columnName] = relationshipId!;
        }

        return new RecordData(id!, values, relationshipIds);
    }

    private static string EscapeSqlIdentifier(string value)
    {
        return value.Replace("]", "]]", StringComparison.Ordinal);
    }

}
