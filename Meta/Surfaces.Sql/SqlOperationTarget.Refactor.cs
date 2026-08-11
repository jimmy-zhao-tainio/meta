using System.Globalization;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

internal sealed partial class SqlOperationTarget
{
    private const string TextEqualityCollation =
        "Latin1_General_100_BIN2";

    private PropertyToRelationshipResult Apply(
        Operation.PropertyToRelationship operation)
    {
        var sourceName = MetaName.Require(
            operation.SourceEntityName,
            "Source entity name.");
        var sourceProperty = MetaName.Require(
            operation.SourcePropertyName,
            "Source property name.");
        var targetName = MetaName.Require(
            operation.TargetEntityName,
            "Target entity name.");
        RequireProperty(sourceName, sourceProperty);
        LoadColumnNames(targetName);
        var usesTargetId = MetaName.Comparer.Equals(
            operation.LookupPropertyName,
            "Id");
        var targetLookup = usesTargetId
            ? "Id"
            : MetaName.Require(
                operation.LookupPropertyName,
                "Lookup property name.");
        if (!usesTargetId)
        {
            RequireProperty(targetName, targetLookup);
        }

        var relationshipRole = string.IsNullOrEmpty(operation.Role)
            ? targetName
            : operation.Role;
        var relationshipName = MetaName.Require(
            relationshipRole + "Id",
            "Relationship name.");
        var relationships = LoadRelationships(sourceEntityName: sourceName);
        var matchingRelationships = relationships.Where(relationship =>
                MetaName.Comparer.Equals(
                    relationship.TargetEntityName,
                    targetName) &&
                MetaName.Comparer.Equals(
                    RelationshipRole(relationship.ColumnName),
                    relationshipRole))
            .ToList();
        if (matchingRelationships.Count > 1)
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceName}.{relationshipName}' is ambiguous.");
        }

        var existingRelationship = matchingRelationships.SingleOrDefault();
        if (existingRelationship == null)
        {
            EnsureRefactorRelationshipNameAvailable(
                sourceName,
                sourceProperty,
                relationshipRole,
                relationshipName,
                operation.PreserveProperty);
        }

        var sourceIsRequired = !ColumnIsNullable(
            sourceName,
            sourceProperty);
        var sourceTable = Table(sourceName);
        var targetTable = Table(targetName);
        var sourceColumn = "sourceValue." + Quote(sourceProperty);
        var targetColumn = "targetValue." + Quote(targetLookup);
        var targetId = "targetValue.[Id]";
        var join = usesTargetId
            ? $"{sourceColumn} COLLATE {IdentityCollation} = {targetId}"
            : $"{sourceColumn} COLLATE {TextEqualityCollation} = {targetColumn} COLLATE {TextEqualityCollation}";
        var sourceHasValue =
            $"{sourceColumn} IS NOT NULL AND {sourceColumn} COLLATE {TextEqualityCollation} <> N'' COLLATE {TextEqualityCollation}";

        if (!usesTargetId && HasAny(
                $"SELECT TOP (1) 1 FROM {targetTable} AS targetValue " +
                $"WHERE {targetColumn} IS NULL OR {targetColumn} COLLATE {TextEqualityCollation} = N'' COLLATE {TextEqualityCollation};"))
        {
            throw new InvalidOperationException(
                $"Target lookup '{targetName}.{targetLookup}' contains a missing or empty value.");
        }

        if (!usesTargetId && HasAny(
                $"SELECT TOP (1) 1 FROM {targetTable} AS targetValue " +
                $"GROUP BY {targetColumn} COLLATE {TextEqualityCollation} HAVING COUNT_BIG(1) > 1;"))
        {
            throw new InvalidOperationException(
                $"Target lookup '{targetName}.{targetLookup}' contains duplicate values.");
        }

        if (sourceIsRequired && HasAny(
                $"SELECT TOP (1) 1 FROM {sourceTable} AS sourceValue " +
                $"WHERE {sourceColumn} IS NULL OR {sourceColumn} COLLATE {TextEqualityCollation} = N'' COLLATE {TextEqualityCollation};"))
        {
            throw new InvalidOperationException(
                $"Required property '{sourceName}.{sourceProperty}' contains a missing or empty value.");
        }

        if (HasAny(
                $"SELECT TOP (1) 1 FROM {sourceTable} AS sourceValue " +
                $"LEFT JOIN {targetTable} AS targetValue ON {join} " +
                $"WHERE {sourceHasValue} AND {targetId} IS NULL;"))
        {
            throw new InvalidOperationException(
                $"Some values from '{sourceName}.{sourceProperty}' do not resolve through '{targetName}.{targetLookup}'.");
        }

        if (existingRelationship != null && HasAny(
                $"SELECT TOP (1) 1 FROM {sourceTable} AS sourceValue " +
                $"INNER JOIN {targetTable} AS targetValue ON {join} " +
                $"WHERE {sourceHasValue} " +
                $"AND sourceValue.{Quote(existingRelationship.ColumnName)} IS NOT NULL " +
                $"AND sourceValue.{Quote(existingRelationship.ColumnName)} COLLATE {IdentityCollation} <> {targetId};"))
        {
            throw new InvalidOperationException(
                $"Existing relationship '{sourceName}.{relationshipName}' conflicts with the source property.");
        }

        if (sourceIsRequired)
        {
            EnsureNoRequiredRelationshipCycle(
                sourceName,
                targetName,
                existingRelationship);
        }

        var sourceRecordCount = Count(
            $"SELECT COUNT_BIG(1) FROM {sourceTable};");
        var relationshipValueCount = Count(
            $"SELECT COUNT_BIG(1) FROM {sourceTable} AS sourceValue WHERE {sourceHasValue};");

        if (existingRelationship != null)
        {
            Execute(
                $"UPDATE sourceValue SET {Quote(existingRelationship.ColumnName)} = {targetId} " +
                $"FROM {sourceTable} AS sourceValue " +
                $"INNER JOIN {targetTable} AS targetValue ON {join} " +
                $"WHERE {sourceHasValue};");
            Execute(
                $"ALTER TABLE {sourceTable} ALTER COLUMN {Quote(existingRelationship.ColumnName)} " +
                $"{IdentitySqlType} {(sourceIsRequired ? "NOT NULL" : "NULL")};");
            if (!operation.PreserveProperty)
            {
                Execute(
                    $"ALTER TABLE {sourceTable} DROP COLUMN {Quote(sourceProperty)};");
            }
        }
        else if (MetaName.Comparer.Equals(
                     sourceProperty,
                     relationshipName))
        {
            Execute(
                $"UPDATE sourceValue SET {Quote(sourceProperty)} = {targetId} " +
                $"FROM {sourceTable} AS sourceValue " +
                $"INNER JOIN {targetTable} AS targetValue ON {join} " +
                $"WHERE {sourceHasValue};");
            Execute(
                $"ALTER TABLE {sourceTable} ALTER COLUMN {Quote(sourceProperty)} " +
                $"{IdentitySqlType} {(sourceIsRequired ? "NOT NULL" : "NULL")};");
            AddForeignKey(sourceName, targetName, relationshipName);
        }
        else
        {
            Execute(
                $"ALTER TABLE {sourceTable} ADD {Quote(relationshipName)} {IdentitySqlType} NULL;");
            Execute(
                $"UPDATE sourceValue SET {Quote(relationshipName)} = {targetId} " +
                $"FROM {sourceTable} AS sourceValue " +
                $"INNER JOIN {targetTable} AS targetValue ON {join} " +
                $"WHERE {sourceHasValue};");
            if (sourceIsRequired)
            {
                Execute(
                    $"ALTER TABLE {sourceTable} ALTER COLUMN {Quote(relationshipName)} {IdentitySqlType} NOT NULL;");
            }

            AddForeignKey(sourceName, targetName, relationshipName);
            if (!operation.PreserveProperty)
            {
                Execute(
                    $"ALTER TABLE {sourceTable} DROP COLUMN {Quote(sourceProperty)};");
            }
        }

        return new PropertyToRelationshipResult(
            sourceRecordCount,
            relationshipValueCount,
            PropertyRemoved: !operation.PreserveProperty,
            relationshipName);
    }

    private RelationshipToPropertyResult Apply(
        Operation.RelationshipToProperty operation)
    {
        var sourceName = MetaName.Require(
            operation.SourceEntityName,
            "Source entity name.");
        var targetName = MetaName.Require(
            operation.TargetEntityName,
            "Target entity name.");
        LoadColumnNames(targetName);
        var expectedRole = string.IsNullOrEmpty(operation.Role)
            ? targetName
            : operation.Role;
        var matches = LoadRelationships(sourceEntityName: sourceName)
            .Where(relationship =>
                MetaName.Comparer.Equals(
                    relationship.TargetEntityName,
                    targetName) &&
                MetaName.Comparer.Equals(
                    RelationshipRole(relationship.ColumnName),
                    expectedRole))
            .ToList();
        var relationship = matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Relationship '{sourceName}->{targetName}' does not exist."),
            _ => throw new InvalidOperationException(
                $"Relationship '{sourceName}->{targetName}' is ambiguous."),
        };
        var propertyName = string.IsNullOrEmpty(operation.PropertyName)
            ? relationship.ColumnName
            : operation.PropertyName;
        var columns = LoadColumnNames(sourceName);
        if (MetaName.Comparer.Equals(propertyName, "Id") ||
            columns.Any(column =>
                !MetaName.Comparer.Equals(
                    column,
                    relationship.ColumnName) &&
                MetaName.Comparer.Equals(column, propertyName)))
        {
            throw new InvalidOperationException(
                $"Property '{sourceName}.{propertyName}' already exists.");
        }

        var sourceTable = Table(sourceName);
        var sourceRecordCount = Count(
            $"SELECT COUNT_BIG(1) FROM {sourceTable};");
        var propertyValueCount = Count(
            $"SELECT COUNT_BIG(1) FROM {sourceTable} WHERE {Quote(relationship.ColumnName)} IS NOT NULL;");
        Execute(
            $"ALTER TABLE {sourceTable} DROP CONSTRAINT {Quote(relationship.ConstraintName)};");
        if (!string.Equals(
                relationship.ColumnName,
                propertyName,
                StringComparison.Ordinal))
        {
            RenameColumn(
                sourceName,
                relationship.ColumnName,
                propertyName);
        }

        Execute(
            $"ALTER TABLE {sourceTable} ALTER COLUMN {Quote(propertyName)} {PropertySqlType} " +
            $"{(relationship.IsNullable ? "NULL" : "NOT NULL")};");

        return new RelationshipToPropertyResult(
            sourceRecordCount,
            propertyValueCount,
            IsRequired: !relationship.IsNullable,
            propertyName);
    }

    private void EnsureRefactorRelationshipNameAvailable(
        string sourceEntityName,
        string sourcePropertyName,
        string relationshipRole,
        string relationshipName,
        bool preserveProperty)
    {
        var columns = LoadColumnNames(sourceEntityName);
        var mayReplaceSourceProperty =
            !preserveProperty &&
            MetaName.Comparer.Equals(
                sourcePropertyName,
                relationshipName);
        if (MetaName.Comparer.Equals(
                relationshipRole,
                sourceEntityName) ||
            columns.Any(column =>
                (!mayReplaceSourceProperty ||
                 !MetaName.Comparer.Equals(column, sourcePropertyName)) &&
                (MetaName.Comparer.Equals(column, relationshipRole) ||
                 MetaName.Comparer.Equals(column, relationshipName))))
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceEntityName}.{relationshipName}' conflicts with an existing member.");
        }
    }

    private bool ColumnIsNullable(
        string entityName,
        string columnName)
    {
        var value = Scalar(
            """
            SELECT columnValue.is_nullable
            FROM sys.columns columnValue
            INNER JOIN sys.tables tableValue
                ON tableValue.object_id = columnValue.object_id
            INNER JOIN sys.schemas schemaValue
                ON schemaValue.schema_id = tableValue.schema_id
            WHERE schemaValue.name = @schema
              AND tableValue.name = @table
              AND columnValue.name = @column;
            """,
            NameParameter("@schema", _schema),
            NameParameter("@table", entityName),
            NameParameter("@column", columnName));
        return value is bool isNullable
            ? isNullable
            : throw new InvalidOperationException(
                $"Column '{entityName}.{columnName}' does not exist.");
    }

    private bool HasAny(string sql) => Scalar(sql) != null;

    private long Count(
        string sql,
        params SqlParameter[] parameters) => Convert.ToInt64(
        Scalar(sql, parameters),
        CultureInfo.InvariantCulture);

    private void EnsureNoRequiredRelationshipCycle(
        string sourceEntityName,
        string targetEntityName,
        SqlRelationship? existingRelationship)
    {
        var graph = new Dictionary<string, HashSet<string>>(
            MetaName.Comparer);
        foreach (var relationship in LoadRelationships()
                     .Where(relationship => !relationship.IsNullable))
        {
            if (!graph.TryGetValue(
                    relationship.SourceEntityName,
                    out var targets))
            {
                targets = new HashSet<string>(MetaName.Comparer);
                graph.Add(relationship.SourceEntityName, targets);
            }

            targets.Add(relationship.TargetEntityName);
        }

        if (existingRelationship == null || existingRelationship.IsNullable)
        {
            if (!graph.TryGetValue(sourceEntityName, out var targets))
            {
                targets = new HashSet<string>(MetaName.Comparer);
                graph.Add(sourceEntityName, targets);
            }

            targets.Add(targetEntityName);
        }

        var visited = new HashSet<string>(MetaName.Comparer);
        var active = new HashSet<string>(MetaName.Comparer);
        foreach (var entity in graph.Keys)
        {
            if (Visit(entity))
            {
                throw new InvalidOperationException(
                    $"Required relationship '{sourceEntityName}->{targetEntityName}' would create a cycle.");
            }
        }

        bool Visit(string entity)
        {
            if (active.Contains(entity))
            {
                return true;
            }

            if (!visited.Add(entity))
            {
                return false;
            }

            active.Add(entity);
            if (graph.TryGetValue(entity, out var targets))
            {
                foreach (var target in targets)
                {
                    if (Visit(target))
                    {
                        return true;
                    }
                }
            }

            active.Remove(entity);
            return false;
        }
    }
}
