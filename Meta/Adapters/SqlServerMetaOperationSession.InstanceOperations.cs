using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Operations;

namespace Meta.Adapters;

public sealed partial class SqlServerMetaOperationSession
{
    private async Task InsertRecordAsync(
        InsertRecordOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var id = RequireIdentity(operation.Id, nameof(operation.Id));
        if (await FindActualIdAsync(
                entity.Name,
                id,
                cancellationToken)
            .ConfigureAwait(false) != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' already contains record '{id}'.");
        }

        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var supplied in operation.Values)
        {
            var property = RequireProperty(entity, supplied.Key);
            values.Add(
                property.Name,
                supplied.Value ??
                throw new InvalidOperationException(
                    $"Property '{entity.Name}.{property.Name}' cannot be null."));
        }

        foreach (var requiredProperty in entity.Properties.Where(
                     property => !property.IsNullable))
        {
            if (!values.ContainsKey(requiredProperty.Name))
            {
                throw new InvalidOperationException(
                    $"Record '{entity.Name}.{id}' is missing required property '{requiredProperty.Name}'.");
            }
        }

        var relationshipIds = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var supplied in operation.RelationshipIds)
        {
            var relationship = ResolveRelationship(entity, supplied.Key);
            var targetId = await RequireActualIdAsync(
                    relationship.Entity,
                    supplied.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            relationshipIds.Add(relationship.GetColumnName(), targetId);
        }

        foreach (var requiredRelationship in entity.Relationships.Where(
                     relationship => !relationship.IsNullable))
        {
            if (!relationshipIds.ContainsKey(
                    requiredRelationship.GetColumnName()))
            {
                throw new InvalidOperationException(
                    $"Record '{entity.Name}.{id}' is missing required relationship '{requiredRelationship.GetColumnName()}'.");
            }
        }

        var columns = new List<string>
        {
            "Id",
        };
        var parameters = new List<SqlParameter>
        {
            new("@value0", SqlDbType.NVarChar, 128)
            {
                Value = id,
            },
        };

        foreach (var value in values.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = $"@value{parameters.Count}";
            columns.Add(value.Key);
            parameters.Add(
                new SqlParameter(parameterName, SqlDbType.NVarChar, -1)
                {
                    Value = value.Value,
                });
        }

        foreach (var relationship in relationshipIds.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = $"@value{parameters.Count}";
            columns.Add(relationship.Key);
            parameters.Add(
                new SqlParameter(parameterName, SqlDbType.NVarChar, 128)
                {
                    Value = relationship.Value,
                });
        }

        var columnSql = string.Join(
            ", ",
            columns.Select(QuoteIdentifier));
        var valueSql = string.Join(
            ", ",
            parameters.Select(parameter => parameter.ParameterName));
        await using var command = CreateCommand(
            $"INSERT INTO {QualifiedTable(entity.Name)} " +
            $"({columnSql}) VALUES ({valueSql});");
        command.Parameters.AddRange(parameters.ToArray());
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, id);
    }

    private async Task SetPropertyAsync(
        SetPropertyOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        var actualId = await RequireActualIdAsync(
                entity.Name,
                operation.Id,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateCommand(
            $"UPDATE {QualifiedTable(entity.Name)} " +
            $"SET {QuoteIdentifier(property.Name)} = @value " +
            "WHERE [Id] = @id;");
        command.Parameters.Add(
            new SqlParameter("@value", SqlDbType.NVarChar, -1)
            {
                Value = operation.Value ??
                        throw new InvalidOperationException(
                            $"Property '{entity.Name}.{property.Name}' cannot be null."),
            });
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = actualId,
            });
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, actualId);
    }

    private async Task ClearPropertyAsync(
        ClearPropertyOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        if (!property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required property '{entity.Name}.{property.Name}' cannot be cleared.");
        }

        var actualId = await RequireActualIdAsync(
                entity.Name,
                operation.Id,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(
            $"UPDATE {QualifiedTable(entity.Name)} " +
            $"SET {QuoteIdentifier(property.Name)} = NULL " +
            "WHERE [Id] = @id;");
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = actualId,
            });
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, actualId);
    }

    private async Task SetRelationshipAsync(
        SetRelationshipOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var relationship = ResolveRelationship(
            entity,
            operation.RelationshipName);
        var actualId = await RequireActualIdAsync(
                entity.Name,
                operation.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var targetId = await RequireActualIdAsync(
                relationship.Entity,
                operation.TargetId,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateCommand(
            $"UPDATE {QualifiedTable(entity.Name)} " +
            $"SET {QuoteIdentifier(relationship.GetColumnName())} = @targetId " +
            "WHERE [Id] = @id;");
        command.Parameters.Add(
            new SqlParameter("@targetId", SqlDbType.NVarChar, 128)
            {
                Value = targetId,
            });
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = actualId,
            });
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, actualId);
    }

    private async Task ClearRelationshipAsync(
        ClearRelationshipOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var relationship = ResolveRelationship(
            entity,
            operation.RelationshipName);
        if (!relationship.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required relationship '{entity.Name}.{relationship.GetColumnName()}' cannot be cleared.");
        }

        var actualId = await RequireActualIdAsync(
                entity.Name,
                operation.Id,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = CreateCommand(
            $"UPDATE {QualifiedTable(entity.Name)} " +
            $"SET {QuoteIdentifier(relationship.GetColumnName())} = NULL " +
            "WHERE [Id] = @id;");
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = actualId,
            });
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, actualId);
    }

    private async Task DeleteRecordAsync(
        DeleteRecordOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var actualId = await RequireActualIdAsync(
                entity.Name,
                operation.Id,
                cancellationToken)
            .ConfigureAwait(false);

        await using var command = CreateCommand(
            $"DELETE FROM {QualifiedTable(entity.Name)} WHERE [Id] = @id;");
        command.Parameters.Add(
            new SqlParameter("@id", SqlDbType.NVarChar, 128)
            {
                Value = actualId,
            });
        var affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureSingleAffectedRow(affected, entity.Name, actualId);
    }
}
