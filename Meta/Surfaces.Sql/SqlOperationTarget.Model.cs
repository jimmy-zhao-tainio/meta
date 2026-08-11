using Meta.Operations.Domain;
using Meta.Operations;


namespace Meta.Surfaces.Sql;

internal sealed partial class SqlOperationTarget
{
    private void Apply(Operation.AddEntity operation)
    {
        var name = MetaName.Require(operation.Name, "Entity name.");
        Execute(
            $"""
            CREATE TABLE {Table(name)}
            (
                [Id] {IdentitySqlType} NOT NULL,
                CONSTRAINT {Quote(SqlWorkspaceNames.PrimaryKey(name))}
                    PRIMARY KEY CLUSTERED ([Id])
            );
            """);
    }

    private void Apply(Operation.RemoveEntity operation)
    {
        var name = MetaName.Require(operation.Name, "Entity name.");
        LoadColumnNames(name);
        if (HasRows(name))
        {
            throw new InvalidOperationException(
                $"Entity '{name}' has records and cannot be removed.");
        }

        Execute($"DROP TABLE {Table(name)};");
    }

    private RenameEntityResult Apply(Operation.RenameEntity operation)
    {
        var name = MetaName.Require(operation.Name, "Entity name.");
        var newName = MetaName.Require(
            operation.NewName,
            "New entity name.");
        LoadColumnNames(name);
        if (string.Equals(name, newName, StringComparison.Ordinal))
        {
            return new RenameEntityResult(
                name,
                newName,
                Count($"SELECT COUNT_BIG(1) FROM {Table(name)};"),
                LoadRelationships(targetEntityName: name).Count,
                0);
        }

        if (!MetaName.Comparer.Equals(name, newName) &&
            TableExists(newName))
        {
            throw new InvalidOperationException(
                $"Entity '{newName}' already exists.");
        }

        var allInbound = LoadRelationships(targetEntityName: name)
            .ToList();
        var renamedInbound = allInbound
            .Where(relationship => MetaName.Comparer.Equals(
                relationship.ColumnName,
                name + "Id"))
            .ToList();
        foreach (var relationship in renamedInbound)
        {
            var collision = LoadColumnNames(relationship.SourceEntityName)
                .Any(column =>
                    !MetaName.Comparer.Equals(
                        column,
                        relationship.ColumnName) &&
                    MetaName.Comparer.Equals(
                        column,
                        newName + "Id"));
            if (collision)
            {
                throw new InvalidOperationException(
                    $"Relationship '{relationship.SourceEntityName}.{newName}' conflicts with an existing member.");
            }
        }

        var affectedRelationships = LoadRelationships()
            .Where(relationship =>
                MetaName.Comparer.Equals(
                    relationship.SourceEntityName,
                    name) ||
                MetaName.Comparer.Equals(
                    relationship.TargetEntityName,
                    name))
            .ToList();
        var affectedPrimaryKey = RequirePrimaryKeyConstraint(name);
        var newImplicitColumnName = renamedInbound.Any()
            ? MetaName.Require(newName + "Id", "Relationship name.")
            : null;
        var relationshipPlans = affectedRelationships
            .Select(relationship =>
            {
                var sourceName = MetaName.Comparer.Equals(
                    relationship.SourceEntityName,
                    name)
                    ? newName
                    : relationship.SourceEntityName;
                var targetName = MetaName.Comparer.Equals(
                    relationship.TargetEntityName,
                    name)
                    ? newName
                    : relationship.TargetEntityName;
                var columnName = relationship.ColumnName;
                if (newImplicitColumnName != null &&
                    MetaName.Comparer.Equals(
                        relationship.TargetEntityName,
                        name) &&
                    MetaName.Comparer.Equals(
                        relationship.ColumnName,
                        name + "Id"))
                {
                    columnName = newImplicitColumnName;
                }

                return new SqlRelationshipPlan(
                    relationship,
                    sourceName,
                    targetName,
                    columnName,
                    SqlWorkspaceNames.ForeignKey(
                        sourceName,
                        targetName,
                        columnName));
            })
            .ToList();
        var constraintRenames = PlanConstraintRenames(
            [
                (
                    affectedPrimaryKey,
                    SqlWorkspaceNames.PrimaryKey(newName)),
                .. relationshipPlans.Select(plan => (
                    plan.Relationship.ConstraintName,
                    plan.NewConstraintName)),
            ]);
        foreach (var constraint in constraintRenames)
        {
            RenameConstraint(
                constraint.CurrentName,
                constraint.TemporaryName);
        }

        var recordCount = Count(
            $"SELECT COUNT_BIG(1) FROM {Table(name)};");
        var relationshipValueCount = renamedInbound.Sum(relationship =>
            Count(
                $"SELECT COUNT_BIG(1) FROM {Table(relationship.SourceEntityName)} " +
                $"WHERE {Quote(relationship.ColumnName)} IS NOT NULL;"));

        Execute(
            "EXEC sys.sp_rename @objectName, @newName, N'OBJECT';",
            TextParameter("@objectName", Quote(_schema) + "." + Quote(name)),
            NameParameter("@newName", newName));

        foreach (var plan in relationshipPlans)
        {
            if (string.Equals(
                    plan.Relationship.ColumnName,
                    plan.NewColumnName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            RenameColumn(
                plan.NewSourceName,
                plan.Relationship.ColumnName,
                plan.NewColumnName);
        }

        foreach (var constraint in constraintRenames)
        {
            RenameConstraint(
                constraint.TemporaryName,
                constraint.FinalName);
        }

        return new RenameEntityResult(
            name,
            newName,
            recordCount,
            allInbound.Count,
            relationshipValueCount);
    }

    private void Apply(Operation.AddProperty operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var propertyName = RequireNewMemberName(
            entityName,
            operation.Name,
            "Property name.");

        if (operation.IsRequired && operation.ExistingRecordValue == null)
        {
            Execute(
                $"ALTER TABLE {Table(entityName)} ADD {Quote(propertyName)} {PropertySqlType} NOT NULL;");
            return;
        }

        Execute(
            $"ALTER TABLE {Table(entityName)} ADD {Quote(propertyName)} {PropertySqlType} NULL;");
        if (operation.ExistingRecordValue != null)
        {
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(propertyName)} = @value;",
                TextParameter("@value", operation.ExistingRecordValue));
        }

        if (operation.IsRequired)
        {
            Execute(
                $"ALTER TABLE {Table(entityName)} ALTER COLUMN {Quote(propertyName)} {PropertySqlType} NOT NULL;");
        }
    }

    private void Apply(Operation.RemoveProperty operation)
    {
        RequireProperty(operation.EntityName, operation.Name);
        Execute(
            $"ALTER TABLE {Table(operation.EntityName)} DROP COLUMN {Quote(operation.Name)};");
    }

    private void Apply(Operation.RenameProperty operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var propertyName = MetaName.Require(
            operation.Name,
            "Property name.");
        RequireProperty(entityName, propertyName);
        var newName = RequireNewMemberName(
            entityName,
            operation.NewName,
            "New property name.");
        RenameColumn(entityName, propertyName, newName);
    }

    private void Apply(Operation.SetPropertyRequired operation)
    {
        var entityName = MetaName.Require(
            operation.EntityName,
            "Entity name.");
        var propertyName = MetaName.Require(
            operation.Name,
            "Property name.");
        RequireProperty(entityName, propertyName);
        if (!operation.IsRequired && operation.MissingRecordValue != null)
        {
            throw new InvalidOperationException(
                "A value for missing records is only valid when making a property required.");
        }

        if (operation.IsRequired && operation.MissingRecordValue != null)
        {
            Execute(
                $"UPDATE {Table(entityName)} SET {Quote(propertyName)} = @value WHERE {Quote(propertyName)} IS NULL;",
                TextParameter("@value", operation.MissingRecordValue));
        }

        Execute(
            $"ALTER TABLE {Table(entityName)} ALTER COLUMN {Quote(propertyName)} {PropertySqlType} " +
            (operation.IsRequired ? "NOT NULL;" : "NULL;"));
    }

    private void Apply(Operation.AddRelationship operation)
    {
        var sourceName = MetaName.Require(
            operation.SourceEntityName,
            "Source entity name.");
        var targetName = MetaName.Require(
            operation.TargetEntityName,
            "Target entity name.");
        LoadColumnNames(sourceName);
        LoadColumnNames(targetName);
        var role = string.IsNullOrEmpty(operation.Role)
            ? targetName
            : MetaName.Require(operation.Role, "Relationship role.");
        var relationshipName = RequireNewMemberName(
            sourceName,
            role + "Id",
            "Relationship name.");

        if (operation.ExistingRecordTargetId != null &&
            !RecordExists(targetName, operation.ExistingRecordTargetId))
        {
            throw new InvalidOperationException(
                $"Record '{targetName}:{operation.ExistingRecordTargetId}' does not exist.");
        }

        if (operation.IsRequired && operation.ExistingRecordTargetId == null)
        {
            Execute(
                $"ALTER TABLE {Table(sourceName)} ADD {Quote(relationshipName)} {IdentitySqlType} NOT NULL;");
        }
        else
        {
            Execute(
                $"ALTER TABLE {Table(sourceName)} ADD {Quote(relationshipName)} {IdentitySqlType} NULL;");
            if (operation.ExistingRecordTargetId != null)
            {
                Execute(
                    $"UPDATE {Table(sourceName)} SET {Quote(relationshipName)} = @targetId;",
                    IdentityParameter(
                        "@targetId",
                        operation.ExistingRecordTargetId));
            }

            if (operation.IsRequired)
            {
                Execute(
                    $"ALTER TABLE {Table(sourceName)} ALTER COLUMN {Quote(relationshipName)} {IdentitySqlType} NOT NULL;");
            }
        }

        AddForeignKey(sourceName, targetName, relationshipName);
    }

    private void Apply(Operation.RemoveRelationship operation)
    {
        var relationship = RequireRelationship(
            operation.SourceEntityName,
            operation.Name);
        Execute(
            $"ALTER TABLE {Table(relationship.SourceEntityName)} DROP CONSTRAINT {Quote(relationship.ConstraintName)};");
        Execute(
            $"ALTER TABLE {Table(relationship.SourceEntityName)} DROP COLUMN {Quote(relationship.ColumnName)};");
    }

    private RenameRelationshipResult Apply(Operation.RenameRelationship operation)
    {
        var sourceName = MetaName.Require(
            operation.SourceEntityName,
            "Source entity name.");
        var relationship = RequireRelationship(sourceName, operation.Name);
        var newRole = string.IsNullOrWhiteSpace(operation.NewRole) ||
                      MetaName.Comparer.Equals(
                          operation.NewRole.Trim(),
                          relationship.TargetEntityName)
            ? relationship.TargetEntityName
            : MetaName.Require(
                operation.NewRole.Trim(),
                "New relationship role.");
        var newColumnName = MetaName.Require(
            newRole + "Id",
            "New relationship name.");
        if (string.Equals(
                relationship.ColumnName,
                newColumnName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceName}.{relationship.ColumnName}' already uses the requested role.");
        }

        var columns = LoadColumnNames(sourceName);
        if (MetaName.Comparer.Equals(newRole, sourceName) ||
            columns.Any(column =>
                !MetaName.Comparer.Equals(
                    column,
                    relationship.ColumnName) &&
                (MetaName.Comparer.Equals(column, newRole) ||
                 MetaName.Comparer.Equals(column, newColumnName))))
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceName}.{newRole}' conflicts with an existing member.");
        }

        var relationshipValueCount = Count(
            $"SELECT COUNT_BIG(1) FROM {Table(sourceName)} " +
                $"WHERE {Quote(relationship.ColumnName)} IS NOT NULL;");
        var newConstraintName = SqlWorkspaceNames.ForeignKey(
            sourceName,
            relationship.TargetEntityName,
            newColumnName);
        var constraintRename = PlanConstraintRenames(
                (relationship.ConstraintName, newConstraintName))
            .Single();
        var oldColumnName = relationship.ColumnName;
        RenameConstraint(
            constraintRename.CurrentName,
            constraintRename.TemporaryName);
        RenameColumn(
            sourceName,
            oldColumnName,
            newColumnName);
        RenameConstraint(
            constraintRename.TemporaryName,
            constraintRename.FinalName);
        return new RenameRelationshipResult(
            sourceName,
            relationship.TargetEntityName,
            oldColumnName,
            newColumnName,
            relationshipValueCount);
    }

    private void Apply(Operation.RetargetRelationship operation)
    {
        var sourceName = MetaName.Require(
            operation.SourceEntityName,
            "Source entity name.");
        var relationship = RequireRelationship(sourceName, operation.Name);
        var targetName = MetaName.Require(
            operation.TargetEntityName,
            "Target entity name.");
        LoadColumnNames(targetName);

        var oldDefaultName = relationship.TargetEntityName + "Id";
        var hasExplicitRole = !MetaName.Comparer.Equals(
            relationship.ColumnName,
            oldDefaultName);
        var newColumnName = hasExplicitRole
            ? relationship.ColumnName
            : MetaName.Require(
                targetName + "Id",
                "New relationship name.");
        var navigationNameCollision =
            !hasExplicitRole &&
            (MetaName.Comparer.Equals(targetName, sourceName) ||
             LoadColumnNames(sourceName).Any(column =>
                 MetaName.Comparer.Equals(column, targetName)));
        var columnNameCollision =
            !MetaName.Comparer.Equals(
                relationship.ColumnName,
                newColumnName) &&
            LoadColumnNames(sourceName).Any(column =>
                MetaName.Comparer.Equals(column, newColumnName));
        if (navigationNameCollision || columnNameCollision)
        {
            throw new InvalidOperationException(
                $"Relationship '{sourceName}.{newColumnName}' conflicts with an existing member.");
        }

        Execute(
            $"ALTER TABLE {Table(sourceName)} DROP CONSTRAINT {Quote(relationship.ConstraintName)};");
        if (!string.Equals(
                relationship.ColumnName,
                newColumnName,
                StringComparison.Ordinal))
        {
            RenameColumn(
                sourceName,
                relationship.ColumnName,
                newColumnName);
        }

        AddForeignKey(
            sourceName,
            targetName,
            newColumnName);
    }

    private void Apply(Operation.SetRelationshipRequired operation)
    {
        var relationship = RequireRelationship(
            operation.SourceEntityName,
            operation.Name);
        if (!operation.IsRequired &&
            operation.MissingRecordTargetId != null)
        {
            throw new InvalidOperationException(
                "A target for missing records is only valid when making a relationship required.");
        }

        if (operation.MissingRecordTargetId != null)
        {
            var targetId = MetaIdentity.Require(
                operation.MissingRecordTargetId,
                "Target record Id.");
            if (!RecordExists(
                    relationship.TargetEntityName,
                    targetId))
            {
                throw new InvalidOperationException(
                    $"Record '{relationship.TargetEntityName}:{targetId}' does not exist.");
            }

            Execute(
                $"UPDATE {Table(relationship.SourceEntityName)} " +
                $"SET {Quote(relationship.ColumnName)} = @targetId " +
                $"WHERE {Quote(relationship.ColumnName)} IS NULL;",
                IdentityParameter("@targetId", targetId));
        }

        Execute(
            $"ALTER TABLE {Table(relationship.SourceEntityName)} " +
            $"ALTER COLUMN {Quote(relationship.ColumnName)} {IdentitySqlType} " +
            (operation.IsRequired ? "NOT NULL;" : "NULL;"));
    }

    private void AddForeignKey(
        string sourceName,
        string targetName,
        string relationshipName)
    {
        var constraintName = SqlWorkspaceNames.ForeignKey(
            sourceName,
            targetName,
            relationshipName);
        Execute(
            $"""
            ALTER TABLE {Table(sourceName)} WITH CHECK
            ADD CONSTRAINT {Quote(constraintName)}
                FOREIGN KEY ({Quote(relationshipName)})
                REFERENCES {Table(targetName)} ([Id]);
            """);
    }

    private RenameModelResult Apply(Operation.RenameModel operation)
    {
        var oldName = MetaName.Require(
            SqlWorkspaceModelMetadata.Read(_connection, _transaction),
            "Model name.");
        var expectedName = MetaName.Require(operation.Name, "Model name.");
        if (!MetaName.Comparer.Equals(oldName, expectedName))
        {
            throw new InvalidOperationException(
                $"SQL workspace model is '{oldName}', not '{expectedName}'.");
        }

        var newName = MetaName.Require(operation.NewName, "New model name.");
        SqlWorkspaceModelMetadata.Write(
            _connection,
            _transaction,
            newName);
        return new RenameModelResult(oldName, newName);
    }

    private string RequireNewMemberName(
        string entityName,
        string name,
        string description)
    {
        var requiredEntity = MetaName.Require(entityName, "Entity name.");
        var requiredName = MetaName.Require(name, description);
        var columns = LoadColumnNames(requiredEntity);
        if (MetaName.Comparer.Equals(requiredName, "Id") ||
            MetaName.Comparer.Equals(requiredName, requiredEntity) ||
            columns.Any(column => MetaName.Comparer.Equals(column, requiredName)))
        {
            throw new InvalidOperationException(
                $"Member '{requiredEntity}.{requiredName}' already exists.");
        }

        return requiredName;
    }

    private void RenameColumn(
        string entityName,
        string name,
        string newName)
    {
        MetaName.Require(newName, "New column name.");
        Execute(
            "EXEC sys.sp_rename @objectName, @newName, N'COLUMN';",
            TextParameter(
                "@objectName",
                Quote(_schema) + "." + Quote(entityName) + "." + Quote(name)),
            NameParameter("@newName", newName));
    }

    private sealed record SqlRelationshipPlan(
        SqlRelationship Relationship,
        string NewSourceName,
        string NewTargetName,
        string NewColumnName,
        string NewConstraintName);
}
