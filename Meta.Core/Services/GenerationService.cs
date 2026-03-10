using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Meta.Core.Ddl;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public sealed class GenerationManifest
{
    public string RootPath { get; set; } = string.Empty;
    public Dictionary<string, string> FileHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CombinedHash { get; set; } = string.Empty;
}

public static class GenerationService
{
    public static GenerationManifest GenerateSql(Workspace workspace, string outputDirectory)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var outputRoot = PrepareOutputDirectory(outputDirectory);
        WriteText(Path.Combine(outputRoot, "schema.sql"), BuildSqlSchema(workspace));
        WriteText(Path.Combine(outputRoot, "data.sql"), BuildSqlData(workspace));
        return BuildManifest(outputRoot);
    }

    public static GenerationManifest GenerateCSharp(
        Workspace workspace,
        string outputDirectory,
        bool includeTooling = false)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var outputRoot = PrepareOutputDirectory(outputDirectory);
        var namespaceName = ResolveModelNamespaceName(workspace.Model.Name);
        var modelTypeName = includeTooling
            ? ResolveToolingModelTypeName(workspace.Model)
            : ResolveConsumerModelTypeName(workspace.Model);
        var modelFileName = modelTypeName + ".cs";
        WriteText(
            Path.Combine(outputRoot, modelFileName),
            includeTooling
                ? BuildCSharpToolingModel(workspace, modelTypeName, namespaceName)
                : BuildCSharpConsumerModel(workspace, modelTypeName, namespaceName));
        var emittedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modelFileName,
        };

        if (includeTooling)
        {
            var toolingFileName = namespaceName + ".Tooling.cs";
            if (!emittedFiles.Add(toolingFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# tooling output because file name collides on '{toolingFileName}'.");
            }

            WriteText(Path.Combine(outputRoot, toolingFileName), BuildCSharpTooling(modelTypeName, namespaceName));
        }

        foreach (var entity in workspace.Model.Entities
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entityFileName = entity.Name + ".cs";
            if (!emittedFiles.Add(entityFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# output because model and entity file names collide on '{entityFileName}'.");
            }

            WriteText(Path.Combine(outputRoot, entityFileName), BuildCSharpEntity(entity, namespaceName));
        }

        return BuildManifest(outputRoot);
    }

    private static string BuildCSharpTooling(string modelTypeName, string namespaceName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using Meta.Adapters;");
        builder.AppendLine("using Meta.Core.Domain;");
        builder.AppendLine("using Meta.Core.Services;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public static class {namespaceName}Tooling");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static async Task<{modelTypeName}> LoadAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var workspace = await LoadWorkspaceAsync(workspacePath, searchUpward, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine($"            return {modelTypeName}Factory.CreateFromWorkspace(workspace);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static Task<Workspace> LoadWorkspaceAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var services = new ServiceCollection();");
        builder.AppendLine("            return services.WorkspaceService.LoadAsync(workspacePath, searchUpward, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static Task SaveWorkspaceAsync(");
        builder.AppendLine("            Workspace workspace,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var services = new ServiceCollection();");
        builder.AppendLine("            return services.WorkspaceService.SaveAsync(workspace, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static Task<Workspace> ImportSqlAsync(");
        builder.AppendLine("            string connectionString,");
        builder.AppendLine("            string schema = \"dbo\",");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var services = new ServiceCollection();");
        builder.AppendLine("            return services.ImportService.ImportSqlAsync(connectionString, schema, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    public sealed partial class {modelTypeName}");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static {modelTypeName} LoadFromXmlWorkspace(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true)");
        builder.AppendLine("        {");
        builder.AppendLine("            return LoadFromXmlWorkspaceAsync(workspacePath, searchUpward).GetAwaiter().GetResult();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task<{modelTypeName}> LoadFromXmlWorkspaceAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return {namespaceName}Tooling.LoadAsync(workspacePath, searchUpward, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    public static GenerationManifest GenerateSsdt(Workspace workspace, string outputDirectory)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var outputRoot = PrepareOutputDirectory(outputDirectory);
        var schema = BuildSqlSchema(workspace);
        var data = BuildSqlData(workspace);
        WriteText(Path.Combine(outputRoot, "Schema.sql"), schema);
        WriteText(Path.Combine(outputRoot, "Data.sql"), data);
        WriteText(Path.Combine(outputRoot, "PostDeploy.sql"), BuildPostDeployScript());
        WriteText(Path.Combine(outputRoot, "Metadata.sqlproj"), BuildSqlProjectFile(workspace));
        return BuildManifest(outputRoot);
    }

    public static GenerationManifest BuildManifest(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory is required.", nameof(rootDirectory));
        }

        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Directory '{root}' was not found.");
        }

        var manifest = new GenerationManifest
        {
            RootPath = root,
        };

        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var path in files)
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var hash = ComputeFileHash(path);
            manifest.FileHashes[relativePath] = hash;
        }

        manifest.CombinedHash = ComputeCombinedHash(manifest.FileHashes);
        return manifest;
    }

    public static bool AreEquivalent(GenerationManifest left, GenerationManifest right, out string message)
    {
        if (left.FileHashes.Count != right.FileHashes.Count)
        {
            message = $"File count mismatch: left={left.FileHashes.Count}, right={right.FileHashes.Count}.";
            return false;
        }

        foreach (var file in left.FileHashes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!right.FileHashes.TryGetValue(file.Key, out var otherHash))
            {
                message = $"Missing file in right manifest: {file.Key}.";
                return false;
            }

            if (!string.Equals(file.Value, otherHash, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Content hash mismatch for '{file.Key}'.";
                return false;
            }
        }

        if (!string.Equals(left.CombinedHash, right.CombinedHash, StringComparison.OrdinalIgnoreCase))
        {
            message = "Combined hash mismatch.";
            return false;
        }

        message = "Equivalent.";
        return true;
    }

    private static string PrepareOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            Directory.Delete(dir, recursive: false);
        }

        return root;
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildSqlSchema(Workspace workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-- Deterministic schema script");
        builder.AppendLine();

        var entities = workspace.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var relationships = new List<(string Source, string Target, string RelationshipName)>();
        foreach (var entity in entities)
        {
            var columns = new List<string>
            {
                "    [Id] NVARCHAR(128) NOT NULL",
            };

            foreach (var property in entity.Properties
                         .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
            {
                var nullable = property.IsNullable ? "NULL" : "NOT NULL";
                columns.Add($"    [{EscapeSqlIdentifier(property.Name)}] NVARCHAR(MAX) {nullable}");
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase))
            {
                var relationshipName = relationship.GetColumnName();
                columns.Add($"    [{EscapeSqlIdentifier(relationshipName)}] NVARCHAR(128) NOT NULL");
                relationships.Add((entity.Name, relationship.Entity, relationshipName));
            }

            columns.Add($"    CONSTRAINT [PK_{EscapeSqlIdentifier(entity.Name)}] PRIMARY KEY CLUSTERED ([Id] ASC)");
            builder.AppendLine($"CREATE TABLE [dbo].[{EscapeSqlIdentifier(entity.Name)}] (");
            builder.AppendLine(string.Join(",\n", columns));
            builder.AppendLine(");");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        foreach (var relationship in relationships
                     .OrderBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Target, StringComparer.OrdinalIgnoreCase))
        {
            var constraintName = $"FK_{relationship.Source}_{relationship.Target}_{relationship.RelationshipName}";
            builder.AppendLine(
                $"ALTER TABLE [dbo].[{EscapeSqlIdentifier(relationship.Source)}] WITH CHECK ADD CONSTRAINT [{EscapeSqlIdentifier(constraintName)}] FOREIGN KEY([{EscapeSqlIdentifier(relationship.RelationshipName)}]) REFERENCES [dbo].[{EscapeSqlIdentifier(relationship.Target)}]([Id]);");
            builder.AppendLine("GO");
            builder.AppendLine();
        }

        return NormalizeNewlines(builder.ToString());
    }

    private static string BuildSqlData(Workspace workspace)
    {
        return DdlSqlServerRenderer.RenderData(BuildDdlDatabase(workspace));
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
                    IsNullable = false,
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

        foreach (var entity in GetEntitiesTopologically(workspace.Model))
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
                        SqlLiteral = row.RelationshipIds.TryGetValue(relationshipName, out var relationshipValue)
                            ? ToSqlLiteral(relationshipValue)
                            : "NULL",
                    });
                }

                database.Inserts.Add(statement);
            }
        }

        return database;
    }

    private static string BuildCSharpConsumerModel(Workspace workspace, string modelTypeName, string namespaceName)
    {
        var builder = new StringBuilder();
        var entities = workspace.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .ToList();

        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Collections.ObjectModel;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using Meta.Core.Domain;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public static partial class {modelTypeName}");
        builder.AppendLine("    {");
        builder.AppendLine($"        private static readonly {modelTypeName}Instance _builtIn = {modelTypeName}InstanceFactory.CreateBuiltIn();");
        builder.AppendLine();
        builder.AppendLine($"        public static {modelTypeName}Instance BuiltIn => _builtIn;");
        foreach (var entity in entities)
        {
            var collectionName = entity.GetListName();
            builder.AppendLine($"        public static IReadOnlyList<{entity.Name}> {collectionName} => _builtIn.{collectionName};");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    public sealed class {modelTypeName}Instance");
        builder.AppendLine("    {");
        builder.AppendLine($"        internal {modelTypeName}Instance(");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var suffix = index == entities.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"            IReadOnlyList<{entity.Name}> {ToCamelIdentifier(entity.GetListName())}{suffix}");
        }

        builder.AppendLine("        )");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            builder.AppendLine($"            {entity.GetListName()} = {ToCamelIdentifier(entity.GetListName())};");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        foreach (var entity in entities)
        {
            builder.AppendLine($"        public IReadOnlyList<{entity.Name}> {entity.GetListName()} {{ get; }}");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    internal static class {modelTypeName}InstanceFactory");
        builder.AppendLine("    {");
        builder.AppendLine($"        internal static {modelTypeName}Instance CreateBuiltIn()");
        builder.AppendLine("        {");

        foreach (var entity in entities)
        {
            var records = workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var entityRecords)
                ? entityRecords
                    .OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(record => record.Id, StringComparer.Ordinal)
                    .ToList()
                : new List<GenericRecord>();

            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar} = new List<{entity.Name}>");
            builder.AppendLine("            {");
            foreach (var record in records)
            {
                builder.AppendLine($"                new {entity.Name}");
                builder.AppendLine("                {");
                builder.AppendLine($"                    Id = {ToCSharpStringLiteral(record.Id)},");

                foreach (var property in entity.Properties
                             .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(property => property.Name, StringComparer.Ordinal))
                {
                    var value = record.Values.TryGetValue(property.Name, out var propertyValue)
                        ? propertyValue ?? string.Empty
                        : string.Empty;
                    builder.AppendLine($"                    {property.Name} = {ToCSharpStringLiteral(value)},");
                }

                foreach (var relationship in entity.Relationships
                             .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
                {
                    var relationshipValue = record.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipId)
                        ? relationshipId ?? string.Empty
                        : string.Empty;
                    builder.AppendLine($"                    {relationship.GetColumnName()} = {ToCSharpStringLiteral(relationshipValue)},");
                }

                builder.AppendLine("                },");
            }

            builder.AppendLine("            };");
            builder.AppendLine();
        }

        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar}ById = new Dictionary<string, {entity.Name}>(global::System.StringComparer.Ordinal);");
            builder.AppendLine($"            foreach (var row in {rowsVar})");
            builder.AppendLine("            {");
            builder.AppendLine($"                {rowsVar}ById[row.Id] = row;");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
            {
                var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
                var targetVar = ToCamelIdentifier(targetEntity.GetListName());
                builder.AppendLine($"            foreach (var row in {rowsVar})");
                builder.AppendLine("            {");
                builder.AppendLine($"                row.{relationship.GetNavigationName()} = RequireTarget(");
                builder.AppendLine($"                    {targetVar}ById,");
                builder.AppendLine($"                    row.{relationship.GetColumnName()},");
                builder.AppendLine($"                    {ToCSharpStringLiteral(entity.Name)},");
                builder.AppendLine("                    row.Id,");
                builder.AppendLine($"                    {ToCSharpStringLiteral(relationship.GetColumnName())});");
                builder.AppendLine("            }");
                builder.AppendLine();
            }
        }

        builder.AppendLine($"            return new {modelTypeName}Instance(");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var suffix = index == entities.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"                new ReadOnlyCollection<{entity.Name}>({ToCamelIdentifier(entity.GetListName())}){suffix}");
        }

        builder.AppendLine("            );");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        internal static {modelTypeName}Instance CreateFromWorkspace(Workspace workspace)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (workspace == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentNullException(nameof(workspace));");
        builder.AppendLine("            }");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar} = new List<{entity.Name}>();");
            builder.AppendLine($"            if (workspace.Instance.RecordsByEntity.TryGetValue({ToCSharpStringLiteral(entity.Name)}, out var {rowsVar}Records))");
            builder.AppendLine("            {");
            builder.AppendLine($"                foreach (var record in {rowsVar}Records.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))");
            builder.AppendLine("                {");
            builder.AppendLine($"                    {rowsVar}.Add(new {entity.Name}");
            builder.AppendLine("                    {");
            builder.AppendLine("                        Id = record.Id ?? string.Empty,");
            foreach (var property in entity.Properties
                         .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(property => property.Name, StringComparer.Ordinal))
            {
                var localValueName = ToCamelIdentifier(property.Name) + "Value";
                builder.AppendLine($"                        {property.Name} = record.Values.TryGetValue({ToCSharpStringLiteral(property.Name)}, out var {localValueName}) ? {localValueName} ?? string.Empty : string.Empty,");
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
            {
                var localRelationshipIdName = ToCamelIdentifier(relationship.GetNavigationName()) + "RelationshipId";
                builder.AppendLine($"                        {relationship.GetColumnName()} = record.RelationshipIds.TryGetValue({ToCSharpStringLiteral(relationship.GetColumnName())}, out var {localRelationshipIdName}) ? {localRelationshipIdName} ?? string.Empty : string.Empty,");
            }

            builder.AppendLine("                    });");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar}ById = new Dictionary<string, {entity.Name}>(global::System.StringComparer.Ordinal);");
            builder.AppendLine($"            foreach (var row in {rowsVar})");
            builder.AppendLine("            {");
            builder.AppendLine($"                {rowsVar}ById[row.Id] = row;");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
            {
                var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
                var targetVar = ToCamelIdentifier(targetEntity.GetListName());
                builder.AppendLine($"            foreach (var row in {rowsVar})");
                builder.AppendLine("            {");
                builder.AppendLine($"                row.{relationship.GetNavigationName()} = RequireTarget(");
                builder.AppendLine($"                    {targetVar}ById,");
                builder.AppendLine($"                    row.{relationship.GetColumnName()},");
                builder.AppendLine($"                    {ToCSharpStringLiteral(entity.Name)},");
                builder.AppendLine("                    row.Id,");
                builder.AppendLine($"                    {ToCSharpStringLiteral(relationship.GetColumnName())});");
                builder.AppendLine("            }");
                builder.AppendLine();
            }
        }

        builder.AppendLine($"            return new {modelTypeName}Instance(");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var suffix = index == entities.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"                new ReadOnlyCollection<{entity.Name}>({ToCamelIdentifier(entity.GetListName())}){suffix}");
        }

        builder.AppendLine("            );");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static T RequireTarget<T>(");
        builder.AppendLine("            Dictionary<string, T> rowsById,");
        builder.AppendLine("            string targetId,");
        builder.AppendLine("            string sourceEntityName,");
        builder.AppendLine("            string sourceId,");
        builder.AppendLine("            string relationshipName)");
        builder.AppendLine("            where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (string.IsNullOrEmpty(targetId))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(");
        builder.AppendLine("                    $\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' is empty.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!rowsById.TryGetValue(targetId, out var target))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(");
        builder.AppendLine("                    $\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' points to missing Id '{targetId}'.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return target;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static string BuildCSharpToolingModel(Workspace workspace, string modelTypeName, string namespaceName)
    {
        var builder = new StringBuilder();
        var entities = workspace.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .ToList();
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Collections.ObjectModel;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using Meta.Core.Domain;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public sealed partial class {modelTypeName}");
        builder.AppendLine("    {");
        builder.AppendLine($"        internal {modelTypeName}(");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var suffix = index == entities.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"            IReadOnlyList<{entity.Name}> {ToCamelIdentifier(entity.GetListName())}{suffix}");
        }
        builder.AppendLine("        )");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            builder.AppendLine($"            {entity.GetListName()} = {ToCamelIdentifier(entity.GetListName())};");
        }
        builder.AppendLine("        }");
        builder.AppendLine();
        foreach (var entity in entities)
        {
            builder.AppendLine($"        public IReadOnlyList<{entity.Name}> {entity.GetListName()} {{ get; }}");
        }
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine($"    internal static class {modelTypeName}Factory");
        builder.AppendLine("    {");
        builder.AppendLine($"        internal static {modelTypeName} CreateFromWorkspace(Workspace workspace)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (workspace == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentNullException(nameof(workspace));");
        builder.AppendLine("            }");
        builder.AppendLine();
        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar} = new List<{entity.Name}>();");
            builder.AppendLine($"            if (workspace.Instance.RecordsByEntity.TryGetValue({ToCSharpStringLiteral(entity.Name)}, out var {rowsVar}Records))");
            builder.AppendLine("            {");
            builder.AppendLine($"                foreach (var record in {rowsVar}Records.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))");
            builder.AppendLine("                {");
            builder.AppendLine($"                    {rowsVar}.Add(new {entity.Name}");
            builder.AppendLine("                    {");
            builder.AppendLine("                        Id = record.Id ?? string.Empty,");
            foreach (var property in entity.Properties
                         .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(property => property.Name, StringComparer.Ordinal))
            {
                var localValueName = ToCamelIdentifier(property.Name) + "Value";
                builder.AppendLine($"                        {property.Name} = record.Values.TryGetValue({ToCSharpStringLiteral(property.Name)}, out var {localValueName}) ? {localValueName} ?? string.Empty : string.Empty,");
            }
            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
            {
                var localRelationshipIdName = ToCamelIdentifier(relationship.GetNavigationName()) + "RelationshipId";
                builder.AppendLine($"                        {relationship.GetColumnName()} = record.RelationshipIds.TryGetValue({ToCSharpStringLiteral(relationship.GetColumnName())}, out var {localRelationshipIdName}) ? {localRelationshipIdName} ?? string.Empty : string.Empty,");
            }
            builder.AppendLine("                    });");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine();
        }
        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            builder.AppendLine($"            var {rowsVar}ById = new Dictionary<string, {entity.Name}>(global::System.StringComparer.Ordinal);");
            builder.AppendLine($"            foreach (var row in {rowsVar})");
            builder.AppendLine("            {");
            builder.AppendLine($"                {rowsVar}ById[row.Id] = row;");
            builder.AppendLine("            }");
            builder.AppendLine();
        }
        foreach (var entity in entities)
        {
            var rowsVar = ToCamelIdentifier(entity.GetListName());
            foreach (var relationship in entity.Relationships
                         .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
            {
                var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
                var targetVar = ToCamelIdentifier(targetEntity.GetListName());
                builder.AppendLine($"            foreach (var row in {rowsVar})");
                builder.AppendLine("            {");
                builder.AppendLine($"                row.{relationship.GetNavigationName()} = RequireTarget(");
                builder.AppendLine($"                    {targetVar}ById,");
                builder.AppendLine($"                    row.{relationship.GetColumnName()},");
                builder.AppendLine($"                    {ToCSharpStringLiteral(entity.Name)},");
                builder.AppendLine("                    row.Id,");
                builder.AppendLine($"                    {ToCSharpStringLiteral(relationship.GetColumnName())});");
                builder.AppendLine("            }");
                builder.AppendLine();
            }
        }
        builder.AppendLine($"            return new {modelTypeName}(");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var suffix = index == entities.Count - 1 ? string.Empty : ",";
            builder.AppendLine($"                new ReadOnlyCollection<{entity.Name}>({ToCamelIdentifier(entity.GetListName())}){suffix}");
        }
        builder.AppendLine("            );");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static T RequireTarget<T>(");
        builder.AppendLine("            Dictionary<string, T> rowsById,");
        builder.AppendLine("            string targetId,");
        builder.AppendLine("            string sourceEntityName,");
        builder.AppendLine("            string sourceId,");
        builder.AppendLine("            string relationshipName)");
        builder.AppendLine("            where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (string.IsNullOrEmpty(targetId))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(");
        builder.AppendLine("                    $\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' is empty.\"");
        builder.AppendLine("                );");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!rowsById.TryGetValue(targetId, out var target))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(");
        builder.AppendLine("                    $\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' points to missing Id '{targetId}'.\"");
        builder.AppendLine("                );");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return target;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }
    private static string BuildCSharpEntity(GenericEntity entity, string namespaceName)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public sealed class {entity.Name}");
        builder.AppendLine("    {");
        builder.AppendLine("        public string Id { get; internal set; } = string.Empty;");

        foreach (var property in entity.Properties
                     .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"        public string {property.Name} {{ get; internal set; }} = string.Empty;");
        }

        foreach (var relationship in entity.Relationships
                     .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase))
        {
            var relationshipName = relationship.GetColumnName();
            var navigationName = relationship.GetNavigationName();
            builder.AppendLine($"        public string {relationshipName} {{ get; internal set; }} = string.Empty;");
            builder.AppendLine($"        public {relationship.Entity} {navigationName} {{ get; internal set; }} = new {relationship.Entity}();");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static string ResolveModelNamespaceName(string? modelName)
    {
        return string.IsNullOrWhiteSpace(modelName) ? "MetadataModel" : modelName.Trim();
    }

    private static string ResolveConsumerModelTypeName(GenericModel model)
    {
        var baseName = ResolveModelNamespaceName(model.Name);
        var entityNames = model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => entity.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!entityNames.Contains(baseName))
        {
            return baseName;
        }

        var candidate = baseName + "Model";
        var suffix = 2;
        while (entityNames.Contains(candidate))
        {
            candidate = baseName + "Model" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private static string ResolveToolingModelTypeName(GenericModel model)
    {
        var baseName = ResolveModelNamespaceName(model.Name) + "Model";
        var entityNames = model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => entity.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!entityNames.Contains(baseName))
        {
            return baseName;
        }
        var candidate = baseName + "2";
        var suffix = 3;
        while (entityNames.Contains(candidate))
        {
            candidate = baseName + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }
        return candidate;
    }
    private static string ToCSharpStringLiteral(string? value)
    {
        if (value == null)
        {
            return "\"\"";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string ToCamelIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "value";
        }

        if (name.Length == 1)
        {
            return char.ToLowerInvariant(name[0]).ToString();
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string BuildPostDeployScript()
    {
        return NormalizeNewlines(
            "-- Deterministic post-deploy script\n" +
            ":r .\\Data.sql\n");
    }

    private static string BuildSqlProjectFile(Workspace workspace)
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

    private static IReadOnlyList<GenericEntity> GetEntitiesTopologically(GenericModel model)
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
                    $"Cannot generate data script because relationship cycle includes '{entityName}'.");
            }

            visiting.Add(entityName);
            var entity = lookup[entityName];
            foreach (var relationship in entity.Relationships
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

    private static string EscapeSqlIdentifier(string? value)
    {
        return (value ?? string.Empty).Replace("]", "]]", StringComparison.Ordinal);
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

    private static string ComputeFileHash(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeCombinedHash(IReadOnlyDictionary<string, string> fileHashes)
    {
        var payload = string.Join(
            "\n",
            fileHashes
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}:{item.Value}"));
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}


