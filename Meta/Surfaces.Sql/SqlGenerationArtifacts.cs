using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Surfaces.Sql.Ddl;
using Meta.Operations.Domain;


namespace Meta.Surfaces.Sql;

internal static class SqlGenerationArtifacts
{
    private const string IdentityCollation = "Latin1_General_100_CI_AS_SC";

    public static string BuildSchema(InMemoryWorkspace state)
    {
        return DdlSqlServerRenderer.RenderSchema(BuildDdlDatabase(state));
    }

    public static string BuildData(InMemoryWorkspace state)
    {
        return DdlSqlServerRenderer.RenderData(BuildDdlDatabase(state));
    }

    private static DdlDatabase BuildDdlDatabase(InMemoryWorkspace state)
    {
        var database = new DdlDatabase();
        var entities = state.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entity in entities)
        {
            var table = new DdlTable
            {
                Schema = "dbo",
                Name = entity.Name,
                PrimaryKey = new DdlPrimaryKeyConstraint
                {
                    Name = SqlWorkspaceNames.PrimaryKey(entity.Name),
                    IsClustered = true,
                },
            };
            table.PrimaryKey.ColumnNames.Add("Id");
            table.Columns.Add(new DdlColumn
            {
                Name = "Id",
                DataType = $"NVARCHAR({MetaIdentity.MaximumLength}) COLLATE {IdentityCollation}",
                IsNullable = false,
            });

            foreach (var property in entity.Properties
                         .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
            {
                table.Columns.Add(new DdlColumn
                {
                    Name = property.Name,
                    DataType = "NVARCHAR(MAX)",
                    IsNullable = property.IsNullable,
                });
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase))
            {
                var relationshipName = relationship.GetColumnName();
                table.Columns.Add(new DdlColumn
                {
                    Name = relationshipName,
                    DataType = $"NVARCHAR({MetaIdentity.MaximumLength}) COLLATE {IdentityCollation}",
                    IsNullable = relationship.IsNullable,
                });

                var foreignKey = new DdlForeignKeyConstraint
                {
                    Name = SqlWorkspaceNames.ForeignKey(
                        entity.Name,
                        relationship.Entity,
                        relationshipName),
                    ReferencedSchema = "dbo",
                    ReferencedTableName = relationship.Entity,
                };
                foreignKey.ColumnNames.Add(relationshipName);
                foreignKey.ReferencedColumnNames.Add("Id");
                table.ForeignKeys.Add(foreignKey);
            }

            database.Tables.Add(table);
        }

        foreach (var entity in GetEntitiesInRequiredDependencyOrder(state.Model))
        {
            if (!state.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
            {
                continue;
            }

            foreach (var row in records.OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
            {
                var statement = new DdlInsertStatement
                {
                    Schema = "dbo",
                    TableName = entity.Name,
                };
                var deferredRelationships = new DdlUpdateStatement
                {
                    Schema = "dbo",
                    TableName = entity.Name,
                    WhereColumnName = "Id",
                    WhereSqlLiteral = ToSqlLiteral(row.Id),
                };
                statement.Values.Add(new DdlInsertValue
                {
                    ColumnName = "Id",
                    SqlLiteral = ToSqlLiteral(row.Id),
                });

                foreach (var property in entity.Properties
                             .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    statement.Values.Add(new DdlInsertValue
                    {
                        ColumnName = property.Name,
                        SqlLiteral = row.Values.TryGetValue(property.Name, out var propertyValue)
                            ? ToSqlLiteral(propertyValue)
                            : "NULL",
                    });
                }

                foreach (var relationship in entity.Relationships
                             .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase))
                {
                    var relationshipName = relationship.GetColumnName();
                    statement.Values.Add(new DdlInsertValue
                    {
                        ColumnName = relationshipName,
                        SqlLiteral = relationship.IsNullable
                            ? "NULL"
                            : row.RelationshipIds.TryGetValue(relationshipName, out var relationshipValue)
                                ? ToSqlLiteral(relationshipValue)
                                : "NULL",
                    });

                    if (relationship.IsNullable &&
                        row.RelationshipIds.TryGetValue(relationshipName, out var deferredRelationshipValue) &&
                        !string.IsNullOrWhiteSpace(deferredRelationshipValue))
                    {
                        deferredRelationships.Values.Add(new DdlInsertValue
                        {
                            ColumnName = relationshipName,
                            SqlLiteral = ToSqlLiteral(deferredRelationshipValue),
                        });
                    }
                }

                database.Inserts.Add(statement);
                if (deferredRelationships.Values.Count > 0)
                {
                    database.Updates.Add(deferredRelationships);
                }
            }
        }

        return database;
    }

    private static IReadOnlyList<GenericEntity> GetEntitiesInRequiredDependencyOrder(GenericModel model)
    {
        var lookup = model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .ToDictionary(entity => entity.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<GenericEntity>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = lookup.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var name in ordered)
        {
            Visit(name);
        }

        return result;

        void Visit(string entityName)
        {
            if (visited.Contains(entityName))
            {
                return;
            }

            if (visiting.Contains(entityName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate data script because required relationship cycle includes '{entityName}'.");
            }

            visiting.Add(entityName);
            var entity = lookup[entityName];
            foreach (var relationship in entity.Relationships
                         .Where(item => !item.IsNullable)
                         .OrderBy(item => item.Entity, StringComparer.OrdinalIgnoreCase))
            {
                if (lookup.ContainsKey(relationship.Entity))
                {
                    Visit(relationship.Entity);
                }
            }

            visiting.Remove(entityName);
            visited.Add(entityName);
            result.Add(entity);
        }
    }

    private static string ToSqlLiteral(string? value)
    {
        if (value == null)
        {
            return "NULL";
        }

        return "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

}
