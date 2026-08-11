using System.Text;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

internal sealed partial class SqlOperationTarget
{
    private void Apply(Operation.InsertRecord operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var columns = LoadColumnNames(entityName);
        var relationships = LoadRelationships(sourceEntityName: entityName);
        var insertColumns = new List<string> { "Id" };
        var parameterNames = new List<string> { "@id" };
        var parameters = new List<SqlParameter>
        {
            IdentityParameter("@id", id),
        };

        foreach (var value in operation.Values)
        {
            var propertyName = MetaName.Require(
                value.Key,
                "Property name.");
            if (MetaName.Comparer.Equals(propertyName, "Id") ||
                !columns.Any(column => MetaName.Comparer.Equals(
                    column,
                    propertyName)) ||
                relationships.Any(relationship => MetaName.Comparer.Equals(
                    relationship.ColumnName,
                    propertyName)))
            {
                throw new InvalidOperationException(
                    $"Property '{entityName}.{propertyName}' does not exist.");
            }

            var parameterName = "@value" + parameters.Count;
            insertColumns.Add(propertyName);
            parameterNames.Add(parameterName);
            parameters.Add(TextParameter(parameterName, value.Value));
        }

        foreach (var value in operation.RelationshipIds)
        {
            var relationship = RequireRelationship(entityName, value.Key);
            var parameterName = "@relationship" + parameters.Count;
            insertColumns.Add(relationship.ColumnName);
            parameterNames.Add(parameterName);
            parameters.Add(IdentityParameter(parameterName, value.Value));
        }

        Execute(
            $"INSERT INTO {Table(entityName)} " +
            $"({string.Join(", ", insertColumns.Select(Quote))}) " +
            $"VALUES ({string.Join(", ", parameterNames)});",
            parameters.ToArray());
    }

    private void Apply(Operation.DeleteRecord operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        RequireOneRow(
            Execute(
                $"DELETE FROM {Table(entityName)} WHERE [Id] = @id;",
                IdentityParameter("@id", id)),
            entityName,
            id);
    }

    private RenameRecordResult Apply(Operation.RenameRecord operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var newId = MetaIdentity.Require(operation.NewId, "New record Id.");
        var columns = LoadColumnNames(entityName);
        var inbound = LoadRelationships(targetEntityName: entityName)
            .ToList();
        var relationshipValueCount = inbound.Sum(relationship =>
            Count(
                $"SELECT COUNT_BIG(1) FROM {Table(relationship.SourceEntityName)} " +
                $"WHERE {Quote(relationship.ColumnName)} = @id;",
                IdentityParameter("@id", id)));

        if (MetaIdentity.Comparer.Equals(id, newId))
        {
            RequireOneRow(
                Execute(
                    $"UPDATE {Table(entityName)} SET [Id] = @newId WHERE [Id] = @id;",
                    IdentityParameter("@newId", newId),
                    IdentityParameter("@id", id)),
                entityName,
                id);
            return new RenameRecordResult(
                entityName,
                id,
                newId,
                relationshipValueCount);
        }

        var projectedColumns = columns
            .Where(column => !MetaName.Comparer.Equals(column, "Id"))
            .ToList();
        var targetColumns = new[] { "Id" }
            .Concat(projectedColumns)
            .Select(Quote);
        var sourceValues = new[] { "@newId" }
            .Concat(projectedColumns.Select(Quote));
        var inserted = Execute(
            $"""
            INSERT INTO {Table(entityName)} ({string.Join(", ", targetColumns)})
            SELECT {string.Join(", ", sourceValues)}
            FROM {Table(entityName)}
            WHERE [Id] = @id;
            """,
            IdentityParameter("@newId", newId),
            IdentityParameter("@id", id));
        RequireOneRow(inserted, entityName, id);

        foreach (var relationship in inbound)
        {
            Execute(
                $"UPDATE {Table(relationship.SourceEntityName)} " +
                $"SET {Quote(relationship.ColumnName)} = @newId " +
                $"WHERE {Quote(relationship.ColumnName)} = @id;",
                IdentityParameter("@newId", newId),
                IdentityParameter("@id", id));
        }

        RequireOneRow(
            Execute(
                $"DELETE FROM {Table(entityName)} WHERE [Id] = @id;",
                IdentityParameter("@id", id)),
            entityName,
            id);
        return new RenameRecordResult(
            entityName,
            id,
            newId,
            relationshipValueCount);
    }

    private void Apply(Operation.SetProperty operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var propertyName = MetaName.Require(
            operation.PropertyName,
            "Property name.");
        RequireProperty(entityName, propertyName);
        RequireOneRow(
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(propertyName)} = @value WHERE [Id] = @id;",
                TextParameter(
                    "@value",
                    operation.Value ?? throw new InvalidOperationException(
                        "Property value is required.")),
                IdentityParameter("@id", id)),
            entityName,
            id);
    }

    private void Apply(Operation.ClearProperty operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var propertyName = MetaName.Require(
            operation.PropertyName,
            "Property name.");
        RequireProperty(entityName, propertyName);
        RequireOneRow(
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(propertyName)} = NULL WHERE [Id] = @id;",
                IdentityParameter("@id", id)),
            entityName,
            id);
    }

    private void Apply(Operation.SetRelationship operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var relationship = RequireRelationship(
            entityName,
            operation.RelationshipName);
        var targetId = MetaIdentity.Require(
            operation.TargetId,
            "Target record Id.");
        if (!RecordExists(relationship.TargetEntityName, targetId))
        {
            throw new InvalidOperationException(
                $"Record '{relationship.TargetEntityName}:{targetId}' does not exist.");
        }

        RequireOneRow(
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(relationship.ColumnName)} = @targetId WHERE [Id] = @id;",
                IdentityParameter("@targetId", targetId),
                IdentityParameter("@id", id)),
            entityName,
            id);
    }

    private void Apply(Operation.ClearRelationship operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var id = MetaIdentity.Require(operation.Id, "Record Id.");
        var relationship = RequireRelationship(
            entityName,
            operation.RelationshipName);
        RequireOneRow(
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(relationship.ColumnName)} = NULL WHERE [Id] = @id;",
                IdentityParameter("@id", id)),
            entityName,
            id);
    }

    private static void RequireOneRow(
        int affectedRows,
        string entityName,
        string id)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Record '{entityName}:{id}' does not exist.");
        }
    }
}
