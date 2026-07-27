using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Ddl;
using Meta.Core.Domain;

namespace Meta.Core.Services;

internal static class SqlGenerationArtifacts
{
    public static string BuildSchema(Workspace workspace)
    {
        return DdlSqlServerRenderer.RenderSchema(BuildDdlDatabase(workspace));
    }

    public static string BuildData(Workspace workspace)
    {
        return DdlSqlServerRenderer.RenderData(BuildDdlDatabase(workspace));
    }

    public static string BuildPostDeployScript()
    {
        return NormalizeNewlines(
            "-- Deterministic post-deploy script\n" +
            ":r .\\Data.sql\n");
    }

    public static string BuildSqlProjectFile(Workspace workspace)
    {
        var projectName = string.IsNullOrWhiteSpace(workspace.Model.Name)
            ? "MetadataModel"
            : workspace.Model.Name;
        var xml =
            "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n" +
            "  <PropertyGroup>\n" +
            $"    <Name>{EscapeXml(projectName)}</Name>\n" +
            "    <DSP>Microsoft.Data.Tools.Schema.Sql.Sql150DatabaseSchemaProvider</DSP>\n" +
            "    <ModelCollation>1033,CI</ModelCollation>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <Build Include=\"Schema.sql\" />\n" +
            "    <PostDeploy Include=\"PostDeploy.sql\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n";
        return NormalizeNewlines(xml);
    }

    private static DdlDatabase BuildDdlDatabase(Workspace workspace)
    {
        var database = new DdlDatabase();
        var entities = workspace.Model.Entities
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
                    Name = $"PK_{entity.Name}",
                    IsClustered = true,
                },
            };
            table.PrimaryKey.ColumnNames.Add("Id");
            table.Columns.Add(new DdlColumn
            {
                Name = "Id",
                DataType = "NVARCHAR(128)",
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
                    DataType = "NVARCHAR(128)",
                    IsNullable = relationship.IsNullable,
                });

                var foreignKey = new DdlForeignKeyConstraint
                {
                    Name = $"FK_{entity.Name}_{relationship.Entity}_{relationshipName}",
                    ReferencedSchema = "dbo",
                    ReferencedTableName = relationship.Entity,
                };
                foreignKey.ColumnNames.Add(relationshipName);
                foreignKey.ReferencedColumnNames.Add("Id");
                table.ForeignKeys.Add(foreignKey);
            }

            database.Tables.Add(table);
        }

        foreach (var entity in GetEntitiesInRequiredDependencyOrder(workspace.Model))
        {
            if (!workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
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

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
    }
}
