using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Ddl;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Adapters;

public sealed partial class SqlServerMetaOperationSession
{
    private async Task AddEntityAsync(
        AddEntityOperation operation,
        CancellationToken cancellationToken)
    {
        var entityName = RequireIdentifier(
            operation.EntityName,
            nameof(operation.EntityName));
        if (_model.FindEntity(entityName) != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entityName}' already exists.");
        }

        var constraintName = NormalizeIdentifier($"PK_{entityName}");
        var identityConstraintName =
            MetaSqlStorageContract.GetIdentityCheckConstraintName(
                entityName,
                "Id");
        var sql =
            $"CREATE TABLE {QualifiedTable(entityName)} (" +
            $"[Id] NVARCHAR(128) COLLATE {MetaSqlStorageContract.IdentityCollation} NOT NULL, " +
            $"CONSTRAINT {QuoteIdentifier(constraintName)} " +
            "PRIMARY KEY CLUSTERED ([Id] ASC), " +
            $"CONSTRAINT {QuoteIdentifier(identityConstraintName)} CHECK (" +
            $"{MetaSqlStorageContract.GetIdentityCheckExpression("Id")}));";
        await ExecuteNonQueryAsync(sql, cancellationToken).ConfigureAwait(false);

        _model.Entities.Add(new GenericEntity
        {
            Name = entityName,
        });
    }

    private async Task RemoveEntityAsync(
        RemoveEntityOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        if (await HasRowsAsync(entity.Name, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has records and cannot be removed.");
        }

        var inbound = _model.Entities.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, entity) &&
            candidate.Relationships.Any(relationship =>
                string.Equals(
                    relationship.Entity,
                    entity.Name,
                    StringComparison.OrdinalIgnoreCase)));
        if (inbound != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' is referenced by entity '{inbound.Name}' and cannot be removed.");
        }

        await ExecuteNonQueryAsync(
                $"DROP TABLE {QualifiedTable(entity.Name)};",
                cancellationToken)
            .ConfigureAwait(false);
        _model.Entities.Remove(entity);
    }

    private async Task AddPropertyAsync(
        AddPropertyOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var propertyName = RequireIdentifier(
            operation.PropertyName,
            nameof(operation.PropertyName));
        EnsureMemberNameAvailable(entity, propertyName);

        var hasRows = await HasRowsAsync(entity.Name, cancellationToken)
            .ConfigureAwait(false);
        if (operation.IsRequired &&
            hasRows &&
            operation.ExistingRecordValue == null)
        {
            throw new InvalidOperationException(
                $"Required property '{entity.Name}.{propertyName}' needs a value for existing records.");
        }

        var addAsNullable = operation.ExistingRecordValue != null;
        await ExecuteNonQueryAsync(
                $"ALTER TABLE {QualifiedTable(entity.Name)} ADD " +
                $"{QuoteIdentifier(propertyName)} NVARCHAR(MAX) " +
                $"{(addAsNullable || !operation.IsRequired ? "NULL" : "NOT NULL")};",
                cancellationToken)
            .ConfigureAwait(false);

        if (operation.ExistingRecordValue != null)
        {
            await using var command = CreateCommand(
                $"UPDATE {QualifiedTable(entity.Name)} " +
                $"SET {QuoteIdentifier(propertyName)} = @value;");
            command.Parameters.Add(
                new SqlParameter("@value", SqlDbType.NVarChar, -1)
                {
                    Value = operation.ExistingRecordValue,
                });
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (operation.IsRequired)
            {
                await AlterPropertyNullabilityAsync(
                        entity.Name,
                        propertyName,
                        isRequired: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        entity.Properties.Add(new GenericProperty
        {
            Name = propertyName,
            IsNullable = !operation.IsRequired,
        });
    }

    private async Task RemovePropertyAsync(
        RemovePropertyOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);

        await ExecuteNonQueryAsync(
                $"ALTER TABLE {QualifiedTable(entity.Name)} DROP COLUMN " +
                $"{QuoteIdentifier(property.Name)};",
                cancellationToken)
            .ConfigureAwait(false);
        entity.Properties.Remove(property);
    }

    private async Task RenamePropertyAsync(
        RenamePropertyOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        var newPropertyName = RequireIdentifier(
            operation.NewPropertyName,
            nameof(operation.NewPropertyName));

        if (string.Equals(
                property.Name,
                newPropertyName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Property '{entity.Name}.{property.Name}' already has that name.");
        }

        if (entity.Properties.Any(candidate =>
                string.Equals(
                    candidate.Name,
                    newPropertyName,
                    StringComparison.OrdinalIgnoreCase)) ||
            entity.Relationships.Any(relationship =>
                string.Equals(
                    relationship.GetColumnName(),
                    newPropertyName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Member '{entity.Name}.{newPropertyName}' already exists.");
        }

        await using var command = CreateCommand(
            "EXEC sys.sp_rename @objectName, @newName, N'COLUMN';");
        command.Parameters.Add(
            new SqlParameter("@objectName", SqlDbType.NVarChar, 776)
            {
                Value =
                    $"{QuoteIdentifier(_schema)}." +
                    $"{QuoteIdentifier(entity.Name)}." +
                    $"{QuoteIdentifier(property.Name)}",
            });
        command.Parameters.Add(
            new SqlParameter("@newName", SqlDbType.NVarChar, 128)
            {
                Value = newPropertyName,
            });
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        property.Name = newPropertyName;
    }

    private async Task SetPropertyRequiredAsync(
        SetPropertyRequiredOperation operation,
        CancellationToken cancellationToken)
    {
        var entity = RequireEntity(operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);

        if (!operation.IsRequired && operation.MissingRecordValue != null)
        {
            throw new InvalidOperationException(
                "A value for missing records is only valid when making a property required.");
        }

        if (operation.IsRequired)
        {
            var missingCount = await CountNullsAsync(
                    entity.Name,
                    property.Name,
                    cancellationToken)
                .ConfigureAwait(false);
            if (missingCount > 0 && operation.MissingRecordValue == null)
            {
                throw new InvalidOperationException(
                    $"Property '{entity.Name}.{property.Name}' needs a value for {missingCount} existing record(s).");
            }

            if (missingCount > 0)
            {
                await using var command = CreateCommand(
                    $"UPDATE {QualifiedTable(entity.Name)} " +
                    $"SET {QuoteIdentifier(property.Name)} = @value " +
                    $"WHERE {QuoteIdentifier(property.Name)} IS NULL;");
                command.Parameters.Add(
                    new SqlParameter("@value", SqlDbType.NVarChar, -1)
                    {
                        Value = operation.MissingRecordValue!,
                    });
                await command.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await AlterPropertyNullabilityAsync(
                entity.Name,
                property.Name,
                operation.IsRequired,
                cancellationToken)
            .ConfigureAwait(false);
        property.IsNullable = !operation.IsRequired;
    }

    private async Task AddRelationshipAsync(
        AddRelationshipOperation operation,
        CancellationToken cancellationToken)
    {
        var sourceEntity = RequireEntity(operation.SourceEntityName);
        var targetEntity = RequireEntity(operation.TargetEntityName);
        var role = RequireOptionalIdentifier(
            operation.Role,
            nameof(operation.Role));
        var relationship = new GenericRelationship
        {
            Entity = targetEntity.Name,
            Role = role,
            IsNullable = !operation.IsRequired,
        };
        var columnName = RequireIdentifier(
            relationship.GetColumnName(),
            "Relationship column name");
        EnsureMemberNameAvailable(sourceEntity, columnName);

        string? targetId = null;
        if (operation.ExistingRecordTargetId != null)
        {
            targetId = await RequireActualIdAsync(
                    targetEntity.Name,
                    operation.ExistingRecordTargetId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var hasRows = await HasRowsAsync(
                sourceEntity.Name,
                cancellationToken)
            .ConfigureAwait(false);
        if (operation.IsRequired && hasRows && targetId == null)
        {
            throw new InvalidOperationException(
                $"Required relationship '{sourceEntity.Name}.{columnName}' needs a target for existing records.");
        }

        var addAsNullable = targetId != null;
        var identityConstraintName =
            MetaSqlStorageContract.GetIdentityCheckConstraintName(
                sourceEntity.Name,
                columnName);
        await ExecuteNonQueryAsync(
                $"ALTER TABLE {QualifiedTable(sourceEntity.Name)} ADD " +
                $"{QuoteIdentifier(columnName)} NVARCHAR(128) COLLATE " +
                $"{MetaSqlStorageContract.IdentityCollation} " +
                $"{(addAsNullable || !operation.IsRequired ? "NULL" : "NOT NULL")}, " +
                $"CONSTRAINT {QuoteIdentifier(identityConstraintName)} CHECK (" +
                $"{MetaSqlStorageContract.GetIdentityCheckExpression(columnName)});",
                cancellationToken)
            .ConfigureAwait(false);

        if (targetId != null)
        {
            await using var command = CreateCommand(
                $"UPDATE {QualifiedTable(sourceEntity.Name)} " +
                $"SET {QuoteIdentifier(columnName)} = @targetId;");
            command.Parameters.Add(
                new SqlParameter("@targetId", SqlDbType.NVarChar, 128)
                {
                    Value = targetId,
                });
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);

            if (operation.IsRequired)
            {
                await AlterRelationshipNullabilityAsync(
                        sourceEntity.Name,
                        columnName,
                        isRequired: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var constraintName = NormalizeIdentifier(
            $"FK_{sourceEntity.Name}_{targetEntity.Name}_{columnName}");
        await ExecuteNonQueryAsync(
                $"ALTER TABLE {QualifiedTable(sourceEntity.Name)} WITH CHECK " +
                $"ADD CONSTRAINT {QuoteIdentifier(constraintName)} " +
                $"FOREIGN KEY ({QuoteIdentifier(columnName)}) " +
                $"REFERENCES {QualifiedTable(targetEntity.Name)} ([Id]);",
                cancellationToken)
            .ConfigureAwait(false);

        sourceEntity.Relationships.Add(relationship);
    }

    private async Task RemoveRelationshipAsync(
        RemoveRelationshipOperation operation,
        CancellationToken cancellationToken)
    {
        var sourceEntity = RequireEntity(operation.SourceEntityName);
        var relationship = ResolveRelationship(
            sourceEntity,
            operation.RelationshipName);
        var columnName = relationship.GetColumnName();
        var constraintName = await RequireForeignKeyConstraintNameAsync(
                sourceEntity.Name,
                columnName,
                relationship.Entity,
                cancellationToken)
            .ConfigureAwait(false);
        var identityConstraintName =
            MetaSqlStorageContract.GetIdentityCheckConstraintName(
                sourceEntity.Name,
                columnName);

        await ExecuteNonQueryAsync(
                $"ALTER TABLE {QualifiedTable(sourceEntity.Name)} " +
                $"DROP CONSTRAINT {QuoteIdentifier(constraintName)}; " +
                $"ALTER TABLE {QualifiedTable(sourceEntity.Name)} " +
                $"DROP CONSTRAINT {QuoteIdentifier(identityConstraintName)}; " +
                $"ALTER TABLE {QualifiedTable(sourceEntity.Name)} " +
                $"DROP COLUMN {QuoteIdentifier(columnName)};",
                cancellationToken)
            .ConfigureAwait(false);
        sourceEntity.Relationships.Remove(relationship);
    }
}
