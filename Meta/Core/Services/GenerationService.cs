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
                ? BuildCSharpToolingModelTypedSerializer(workspace, modelTypeName, namespaceName)
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

            WriteText(Path.Combine(outputRoot, toolingFileName), BuildCSharpTooling(modelTypeName, namespaceName, workspace.WorkspaceRootPath));

            const string modelXmlFileName = "model.xml";
            if (!emittedFiles.Add(modelXmlFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# tooling output because file name collides on '{modelXmlFileName}'.");
            }

            WriteText(Path.Combine(outputRoot, modelXmlFileName), BuildModelXml(workspace.Model));
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

            WriteText(
                Path.Combine(outputRoot, entityFileName),
                BuildCSharpEntity(
                    entity,
                    namespaceName,
                    workspace.WorkspaceRootPath,
                    requiresTooling: includeTooling));
        }

        return BuildManifest(outputRoot);
    }

    private static string BuildCSharpTooling(string modelTypeName, string namespaceName, string? workspacePath)
    {
        var builder = new StringBuilder();
        AppendGeneratedCSharpHeader(builder, requiresTooling: true, workspacePath: workspacePath);
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public static class {namespaceName}Tooling");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static {modelTypeName} Load(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return {modelTypeName}.LoadFromXmlWorkspace(workspacePath, searchUpward);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task<{modelTypeName}> LoadAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine($"            return {modelTypeName}.LoadFromXmlWorkspaceAsync(workspacePath, searchUpward, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static void Save({modelTypeName} model, string workspacePath)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (model == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentNullException(nameof(model));");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            model.SaveToXmlWorkspace(workspacePath);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task SaveAsync({modelTypeName} model, string workspacePath,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (model == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentNullException(nameof(model));");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            model.SaveToXmlWorkspace(workspacePath);");
        builder.AppendLine("            return Task.CompletedTask;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static string BuildCSharpToolingModelTypedSerializer(Workspace workspace, string modelTypeName, string namespaceName)
    {
        var builder = new StringBuilder();
        AppendGeneratedCSharpHeader(builder, requiresTooling: true, workspacePath: workspace.WorkspaceRootPath);
        var entities = workspace.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .ToList();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.IO;");
        builder.AppendLine("using System.Linq;");
        builder.AppendLine("using System.Reflection;");
        builder.AppendLine("using System.Text;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using System.Xml;");
        builder.AppendLine("using Meta.Core.Serialization;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public sealed partial class {modelTypeName}");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static {modelTypeName} CreateEmpty() => new();");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            builder.AppendLine($"        public List<{entity.Name}> {entity.GetListName()} {{ get; set; }} = new();");
            builder.AppendLine();
        }

        builder.AppendLine($"        public static {modelTypeName} LoadFromXmlWorkspace(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return {modelTypeName}XmlSerializer.Load(workspacePath, searchUpward);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task<{modelTypeName}> LoadFromXmlWorkspaceAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = true,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            return Task.FromResult(LoadFromXmlWorkspace(workspacePath, searchUpward));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void SaveToXmlWorkspace(string workspacePath)");
        builder.AppendLine("        {");
        builder.AppendLine($"            {modelTypeName}XmlSerializer.Save(this, workspacePath);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public Task SaveToXmlWorkspaceAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            SaveToXmlWorkspace(workspacePath);");
        builder.AppendLine("            return Task.CompletedTask;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendCSharpToolingModelXmlSerializer(builder, workspace.Model, modelTypeName, entities);
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static void AppendCSharpToolingModelXmlSerializer(
        StringBuilder builder,
        GenericModel model,
        string modelTypeName,
        IReadOnlyList<GenericEntity> entities)
    {
        var rootName = string.IsNullOrWhiteSpace(model.Name) ? "MetadataModel" : model.Name;
        var relationships = entities
            .SelectMany(entity => entity.Relationships.Select(relationship => (Entity: entity, Relationship: relationship)))
            .OrderBy(item => item.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Relationship.Entity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entity.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Relationship.GetColumnName(), StringComparer.Ordinal)
            .ToList();
        var relationshipEntities = relationships
            .Select(item => item.Entity)
            .Distinct()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        builder.AppendLine($"    internal static class {modelTypeName}XmlSerializer");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);");
        builder.AppendLine();
        builder.AppendLine($"        internal static {modelTypeName} Load(string workspacePath, bool searchUpward)");
        builder.AppendLine("        {");
        builder.AppendLine("            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);");
        builder.AppendLine("            if (HasRuntimeExtendedShape())");
        builder.AppendLine("            {");
        builder.AppendLine($"                return TypedWorkspaceXmlSerializer.Load<{modelTypeName}>(workspacePath, searchUpward);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var workspaceRootPath = searchUpward");
        builder.AppendLine("                ? TypedWorkspaceXmlSerializer.DiscoverWorkspaceRoot(workspacePath)");
        builder.AppendLine("                : TypedWorkspaceXmlSerializer.ResolveWorkspaceRootFromPath(workspacePath);");
        builder.AppendLine("            if (!Directory.Exists(workspaceRootPath))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new DirectoryNotFoundException($\"Workspace '{workspaceRootPath}' was not found.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            var model = new {modelTypeName}();");
        builder.AppendLine("            var loadState = new LoadState();");
        builder.AppendLine("            var relationshipBuffers = new RelationshipBuffers();");

        builder.AppendLine("            var instanceDirectoryPath = TypedWorkspaceXmlSerializer.ResolveInstanceDirectoryPath(workspaceRootPath);");
        builder.AppendLine("            if (!Directory.Exists(instanceDirectoryPath))");
        builder.AppendLine("            {");
        builder.AppendLine("                return model;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, \"*.xml\")");
        builder.AppendLine("                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)");
        builder.AppendLine("                         .ThenBy(path => path, StringComparer.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                LoadShard(model, shardPath, loadState, relationshipBuffers);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var loadIndexes = new LoadIndexes(model);");
        var relationshipGroupCount = (relationships.Count + 31) / 32;
        for (var groupIndex = 0; groupIndex < relationshipGroupCount; groupIndex++)
        {
            builder.AppendLine($"            ResolveRelationshipGroup{groupIndex + 1}(loadIndexes, relationshipBuffers);");
        }

        builder.AppendLine("            return model;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        internal static void Save({modelTypeName} model, string workspacePath)");
        builder.AppendLine("        {");
        builder.AppendLine("            ArgumentNullException.ThrowIfNull(model);");
        builder.AppendLine("            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);");
        builder.AppendLine("            if (HasRuntimeExtendedShape())");
        builder.AppendLine("            {");
        builder.AppendLine("                TypedWorkspaceXmlSerializer.Save(model, workspacePath);");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            TypedWorkspaceXmlSerializer.SaveModel(model, workspacePath);");
        builder.AppendLine();
        builder.AppendLine("            var saveIndexes = new SaveIndexes(model);");
        builder.AppendLine();
        builder.AppendLine("            var workspaceRootPath = Path.GetFullPath(workspacePath);");
        builder.AppendLine("            var instanceDirectoryPath = TypedWorkspaceXmlSerializer.ResolveInstanceDirectoryPath(workspaceRootPath);");
        builder.AppendLine("            Directory.CreateDirectory(instanceDirectoryPath);");
        builder.AppendLine("            var expectedShardPaths = BuildExpectedShardPaths(instanceDirectoryPath);");
        var saveShardGroupCount = (entities.Count + 31) / 32;
        for (var groupIndex = 0; groupIndex < saveShardGroupCount; groupIndex++)
        {
            builder.AppendLine($"            SaveShardGroup{groupIndex + 1}(model, instanceDirectoryPath, saveIndexes);");
        }

        builder.AppendLine("            foreach (var shardPath in Directory.GetFiles(instanceDirectoryPath, \"*.xml\")");
        builder.AppendLine("                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)");
        builder.AppendLine("                         .ThenBy(path => path, StringComparer.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!expectedShardPaths.Contains(Path.GetFullPath(shardPath)))");
        builder.AppendLine("                {");
        builder.AppendLine("                    File.Delete(shardPath);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();

        for (var groupIndex = 0; groupIndex < saveShardGroupCount; groupIndex++)
        {
            AppendCSharpSaveShardGroup(builder, modelTypeName, entities.Skip(groupIndex * 32).Take(32).ToList(), groupIndex + 1);
        }

        builder.AppendLine("        private static void LoadShard(" + modelTypeName + " model, string shardPath, LoadState loadState, RelationshipBuffers relationshipBuffers)");
        builder.AppendLine("        {");
        builder.AppendLine("            using var reader = XmlReader.Create(shardPath, new XmlReaderSettings");
        builder.AppendLine("            {");
        builder.AppendLine("                IgnoreComments = true,");
        builder.AppendLine("                IgnoreWhitespace = true,");
        builder.AppendLine("            });");
        builder.AppendLine("            reader.MoveToContent();");
        builder.AppendLine($"            if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, {ToCSharpStringLiteral(rootName)}, StringComparison.Ordinal))");
        builder.AppendLine("            {");
        builder.AppendLine($"                throw new InvalidDataException($\"Instance XML '{{shardPath}}' root must be <{rootName}>.\");");
        builder.AppendLine("            }");
        builder.AppendLine("            if (reader.IsEmptyElement)");
        builder.AppendLine("            {");
        builder.AppendLine("                reader.Read();");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine($"            reader.ReadStartElement({ToCSharpStringLiteral(rootName)});");
        builder.AppendLine("            while (reader.NodeType == XmlNodeType.Element)");
        builder.AppendLine("            {");
        builder.AppendLine("                switch (reader.LocalName)");
        builder.AppendLine("                {");
        foreach (var entity in entities)
        {
            builder.AppendLine($"                    case {ToCSharpStringLiteral(entity.GetListName())}:");
            builder.AppendLine($"                        Load{entity.GetListName()}(model, reader, loadState, relationshipBuffers);");
            builder.AppendLine("                        break;");
        }

        builder.AppendLine("                    default:");
        builder.AppendLine("                        throw new InvalidDataException($\"Unknown XML element '{reader.LocalName}' in '{shardPath}'.\");");
        builder.AppendLine("                }");
        builder.AppendLine("                reader.MoveToContent();");
        builder.AppendLine("            }");
        builder.AppendLine("            reader.ReadEndElement();");
        builder.AppendLine("        }");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            AppendCSharpLoadEntityList(builder, modelTypeName, entity);
            AppendCSharpReadEntity(builder, entity);
            AppendCSharpSerializeEntityShard(builder, rootName, modelTypeName, entities, entity);
        }

        foreach (var entity in relationshipEntities)
        {
            builder.AppendLine($"        private sealed class {entity.Name}Relationships");
            builder.AppendLine("        {");
            builder.AppendLine($"            public {entity.Name} Row {{ get; set; }} = null!;");
            foreach (var relationship in entity.Relationships
                         .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
            {
                builder.AppendLine($"            public string {relationship.GetColumnName()} {{ get; set; }} = string.Empty;");
            }

            builder.AppendLine("        }");
            builder.AppendLine();
        }

        builder.AppendLine("        private sealed class RelationshipBuffers");
        builder.AppendLine("        {");
        foreach (var entity in relationshipEntities)
        {
            builder.AppendLine($"            public List<{entity.Name}Relationships>? {entity.Name}Relationships {{ get; set; }}");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        for (var groupIndex = 0; groupIndex < relationshipGroupCount; groupIndex++)
        {
            AppendCSharpResolveRelationshipGroup(builder, entities, relationships.Skip(groupIndex * 32).Take(32).ToList(), groupIndex + 1);
        }

        AppendCSharpSerializerHelpers(builder, modelTypeName, entities);
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendCSharpResolveRelationshipGroup(
        StringBuilder builder,
        IReadOnlyList<GenericEntity> entities,
        IReadOnlyList<(GenericEntity Entity, GenericRelationship Relationship)> relationships,
        int groupNumber)
    {
        builder.AppendLine($"        private static void ResolveRelationshipGroup{groupNumber}(LoadIndexes loadIndexes, RelationshipBuffers relationshipBuffers)");
        builder.AppendLine("        {");
        foreach (var (entity, relationship) in relationships)
        {
            var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
            var targetIndex = "loadIndexes." + ToPascalIdentifier(targetEntity.GetListName()) + "ById";
            var relationshipList = "relationshipBuffers." + entity.Name + "Relationships ?? Enumerable.Empty<" + entity.Name + "Relationships>()";
            var relationshipMember = relationship.GetColumnName();
            var navigationName = relationship.GetNavigationName();
            builder.AppendLine($"            foreach (var relationship in {relationshipList})");
            builder.AppendLine("            {");
            if (relationship.IsNullable)
            {
                builder.AppendLine($"                relationship.Row.{navigationName} = string.IsNullOrWhiteSpace(relationship.{relationshipMember})");
                builder.AppendLine("                    ? null");
                builder.AppendLine("                    : RequireTarget(");
                builder.AppendLine($"                        {targetIndex},");
                builder.AppendLine($"                        relationship.{relationshipMember},");
                builder.AppendLine($"                        {ToCSharpStringLiteral(entity.Name)},");
                builder.AppendLine("                        relationship.Row.Id,");
                builder.AppendLine($"                        {ToCSharpStringLiteral(relationshipMember)});");
            }
            else
            {
                builder.AppendLine($"                relationship.Row.{navigationName} = RequireTarget(");
                builder.AppendLine($"                    {targetIndex},");
                builder.AppendLine($"                    relationship.{relationshipMember},");
                builder.AppendLine($"                    {ToCSharpStringLiteral(entity.Name)},");
                builder.AppendLine("                    relationship.Row.Id,");
                builder.AppendLine($"                    {ToCSharpStringLiteral(relationshipMember)});");
            }
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpSaveShardGroup(
        StringBuilder builder,
        string modelTypeName,
        IReadOnlyList<GenericEntity> entities,
        int groupNumber)
    {
        builder.AppendLine($"        private static void SaveShardGroup{groupNumber}({modelTypeName} model, string instanceDirectoryPath, SaveIndexes saveIndexes)");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            var listName = entity.GetListName();
            var shardName = entity.Name + ".xml";
            var shardPathVariable = ToCamelIdentifier(entity.Name) + "ShardPath";
            builder.AppendLine($"            model.{listName} ??= new List<{entity.Name}>();");
            builder.AppendLine($"            var {shardPathVariable} = Path.Combine(instanceDirectoryPath, {ToCSharpStringLiteral(shardName)});");
            builder.AppendLine($"            if (model.{listName}.Count == 0)");
            builder.AppendLine("            {");
            builder.AppendLine($"                DeleteIfExists({shardPathVariable});");
            builder.AppendLine("            }");
            builder.AppendLine("            else");
            builder.AppendLine("            {");
            builder.AppendLine($"                TypedWorkspaceXmlSerializer.WriteBytesIfChanged({shardPathVariable}, Serialize{entity.Name}Shard(model, saveIndexes));");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpLoadEntityList(StringBuilder builder, string modelTypeName, GenericEntity entity)
    {
        builder.AppendLine($"        private static void Load{entity.GetListName()}({modelTypeName} model, XmlReader reader, LoadState loadState, RelationshipBuffers relationshipBuffers)");
        builder.AppendLine("        {");
        builder.AppendLine($"            if (reader.IsEmptyElement)");
        builder.AppendLine("            {");
        builder.AppendLine($"                reader.ReadStartElement({ToCSharpStringLiteral(entity.GetListName())});");
        builder.AppendLine("                return;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            reader.ReadStartElement({ToCSharpStringLiteral(entity.GetListName())});");
        builder.AppendLine("            while (reader.NodeType == XmlNodeType.Element)");
        builder.AppendLine("            {");
        builder.AppendLine($"                if (!string.Equals(reader.LocalName, {ToCSharpStringLiteral(entity.Name)}, StringComparison.Ordinal))");
        builder.AppendLine("                {");
        builder.AppendLine($"                    throw new InvalidDataException($\"Unknown XML element '{{reader.LocalName}}' in '{entity.GetListName()}'.\");");
        builder.AppendLine("                }");
        builder.AppendLine($"                var row = Read{entity.Name}(reader, relationshipBuffers);");
        builder.AppendLine($"                loadState.Add{entity.Name}Id(row.Id);");
        builder.AppendLine($"                model.{entity.GetListName()}.Add(row);");
        builder.AppendLine("                reader.MoveToContent();");
        builder.AppendLine("            }");
        builder.AppendLine("            reader.ReadEndElement();");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpReadEntity(StringBuilder builder, GenericEntity entity)
    {
        builder.AppendLine($"        private static {entity.Name} Read{entity.Name}(XmlReader reader, RelationshipBuffers relationshipBuffers)");
        builder.AppendLine("        {");
        builder.AppendLine($"            var row = new {entity.Name}();");
        if (entity.Relationships.Count > 0)
        {
            builder.AppendLine($"            var relationships = new {entity.Name}Relationships {{ Row = row }};");
        }

        builder.AppendLine("            if (reader.HasAttributes)");
        builder.AppendLine("            {");
        builder.AppendLine("                while (reader.MoveToNextAttribute())");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (IsNamespaceDeclaration(reader))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        continue;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    switch (reader.LocalName)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        case \"Id\":");
        builder.AppendLine("                            row.Id = reader.Value;");
        builder.AppendLine("                            break;");
        foreach (var relationship in entity.Relationships
                     .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
        {
            builder.AppendLine($"                        case {ToCSharpStringLiteral(relationship.GetColumnName())}:");
            builder.AppendLine($"                            relationships.{relationship.GetColumnName()} = reader.Value;");
            builder.AppendLine("                            break;");
        }

        builder.AppendLine("                        default:");
        builder.AppendLine($"                            throw new InvalidDataException($\"Unknown XML attribute '{{reader.LocalName}}' on '{entity.Name}'.\");");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                reader.MoveToElement();");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (reader.IsEmptyElement)");
        builder.AppendLine("            {");
        builder.AppendLine($"                reader.ReadStartElement({ToCSharpStringLiteral(entity.Name)});");
        if (entity.Relationships.Count > 0)
        {
            builder.AppendLine($"                (relationshipBuffers.{entity.Name}Relationships ??= new List<{entity.Name}Relationships>()).Add(relationships);");
        }

        builder.AppendLine("                return row;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            reader.ReadStartElement({ToCSharpStringLiteral(entity.Name)});");
        builder.AppendLine("            while (reader.NodeType == XmlNodeType.Element)");
        builder.AppendLine("            {");
        builder.AppendLine("                switch (reader.LocalName)");
        builder.AppendLine("                {");
        foreach (var property in entity.Properties
                     .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(property => property.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"                    case {ToCSharpStringLiteral(property.Name)}:");
            builder.AppendLine($"                        row.{property.Name} = reader.ReadElementContentAsString();");
            builder.AppendLine("                        break;");
        }

        builder.AppendLine("                    default:");
        builder.AppendLine($"                        throw new InvalidDataException($\"Unknown XML element '{{reader.LocalName}}' on '{entity.Name}'.\");");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            reader.ReadEndElement();");
        if (entity.Relationships.Count > 0)
        {
            builder.AppendLine($"            (relationshipBuffers.{entity.Name}Relationships ??= new List<{entity.Name}Relationships>()).Add(relationships);");
        }

        builder.AppendLine("            return row;");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpSerializeEntityShard(
        StringBuilder builder,
        string rootName,
        string modelTypeName,
        IReadOnlyList<GenericEntity> entities,
        GenericEntity entity)
    {
        builder.AppendLine($"        private static byte[] Serialize{entity.Name}Shard({modelTypeName} model, SaveIndexes saveIndexes)");
        builder.AppendLine("        {");
        builder.AppendLine("            var builder = new StringBuilder();");
        builder.AppendLine("            var rowIds = new HashSet<string>(StringComparer.Ordinal);");
        builder.AppendLine("            builder.Append(\"<?xml version=\\\"1.0\\\" encoding=\\\"utf-8\\\"?>\\n\");");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("<" + rootName + ">\n")});");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("  <" + entity.GetListName() + ">\n")});");
        builder.AppendLine($"            foreach (var row in model.{entity.GetListName()})");
        builder.AppendLine("            {");
        builder.AppendLine("                ArgumentNullException.ThrowIfNull(row);");
        builder.AppendLine($"                var rowId = RequireIdentity(row.Id, {ToCSharpStringLiteral($"Entity '{entity.Name}' contains a row with empty Id.")});");
        builder.AppendLine("                if (!rowIds.Add(rowId))");
        builder.AppendLine("                {");
        builder.AppendLine($"                    throw new InvalidOperationException($\"Entity '{entity.Name}' contains duplicate Id '{{rowId}}'.\");");
        builder.AppendLine("                }");
        builder.AppendLine($"                builder.Append({ToCSharpStringLiteral("    <" + entity.Name + " Id=\"")});");
        builder.AppendLine("                AppendXmlAttribute(builder, rowId);");
        builder.AppendLine("                builder.Append('\"');");
        foreach (var relationship in entity.Relationships
                     .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
        {
            var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
            var targetIndexName = "saveIndexes." + ToPascalIdentifier(targetEntity.GetListName()) + "ById";
            var navigationName = relationship.GetNavigationName();
            if (relationship.IsNullable)
            {
                builder.AppendLine($"                if (row.{navigationName} != null)");
                builder.AppendLine("                {");
                AppendCSharpRelationshipAttributeWrite(builder, entity, relationship, targetIndexName, navigationName, indent: "                    ");
                builder.AppendLine("                }");
            }
            else
            {
                AppendCSharpRelationshipAttributeWrite(builder, entity, relationship, targetIndexName, navigationName, indent: "                ");
            }
        }

        builder.AppendLine("                builder.Append(\">\\n\");");
        foreach (var property in entity.Properties
                     .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.IsNullable)
            {
                builder.AppendLine($"                if (!string.IsNullOrWhiteSpace(row.{property.Name}))");
                builder.AppendLine("                {");
                builder.AppendLine($"                    AppendElement(builder, {ToCSharpStringLiteral(property.Name)}, row.{property.Name}!, \"      \");");
                builder.AppendLine("                }");
            }
            else
            {
                builder.AppendLine($"                AppendElement(builder, {ToCSharpStringLiteral(property.Name)}, RequireText(row.{property.Name}, $\"Entity '{entity.Name}' row '{{row.Id}}' is missing required property '{property.Name}'.\"), \"      \");");
            }
        }

        builder.AppendLine($"                builder.Append({ToCSharpStringLiteral("    </" + entity.Name + ">\n")});");
        builder.AppendLine("            }");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("  </" + entity.GetListName() + ">\n")});");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("</" + rootName + ">\n")});");
        builder.AppendLine("            return Utf8NoBom.GetBytes(builder.ToString());");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpRelationshipAttributeWrite(
        StringBuilder builder,
        GenericEntity entity,
        GenericRelationship relationship,
        string targetIndexName,
        string navigationName,
        string indent)
    {
        builder.AppendLine($"{indent}var {ToCamelIdentifier(relationship.GetColumnName())} = RequireIdentity(row.{navigationName}?.Id, $\"Relationship '{entity.Name}.{relationship.GetColumnName()}' on row '{entity.Name}:{{row.Id}}' is empty.\");");
        builder.AppendLine($"{indent}if (!{targetIndexName}.TryGetValue({ToCamelIdentifier(relationship.GetColumnName())}, out var {ToCamelIdentifier(navigationName)}Canonical) || !ReferenceEquals({ToCamelIdentifier(navigationName)}Canonical, row.{navigationName}))");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    throw new InvalidOperationException($\"Relationship '{entity.Name}.{relationship.GetColumnName()}' on row '{entity.Name}:{{row.Id}}' references an object that is not the canonical row for Id '{{{ToCamelIdentifier(relationship.GetColumnName())}}}'.\");");
        builder.AppendLine($"{indent}}}");
        builder.AppendLine($"{indent}builder.Append(' ');");
        builder.AppendLine($"{indent}builder.Append({ToCSharpStringLiteral(relationship.GetColumnName())});");
        builder.AppendLine($"{indent}builder.Append(\"=\\\"\");");
        builder.AppendLine($"{indent}AppendXmlAttribute(builder, {ToCamelIdentifier(relationship.GetColumnName())});");
        builder.AppendLine($"{indent}builder.Append('\"');");
    }

    private static void AppendCSharpIndexCache(
        StringBuilder builder,
        string modelTypeName,
        IReadOnlyList<GenericEntity> entities,
        string className)
    {
        builder.AppendLine($"        private sealed class {className}");
        builder.AppendLine("        {");
        builder.AppendLine($"            private readonly {modelTypeName} model;");
        builder.AppendLine();
        builder.AppendLine($"            public {className}({modelTypeName} model)");
        builder.AppendLine("            {");
        builder.AppendLine("                this.model = model;");
        builder.AppendLine("            }");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            var fieldName = ToCamelIdentifier(entity.GetListName()) + "ById";
            var propertyName = ToPascalIdentifier(entity.GetListName()) + "ById";
            builder.AppendLine($"            private Dictionary<string, {entity.Name}>? {fieldName};");
            builder.AppendLine();
            builder.AppendLine($"            public Dictionary<string, {entity.Name}> {propertyName} => {fieldName} ??= BuildById(model.{entity.GetListName()}, row => row.Id, {ToCSharpStringLiteral(entity.Name)});");
            builder.AppendLine();
        }

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpRuntimeShapeGroup(
        StringBuilder builder,
        IReadOnlyList<GenericEntity> entities,
        int groupNumber)
    {
        builder.AppendLine($"        private static bool HasRuntimeExtendedEntityShapeGroup{groupNumber}()");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            var knownProperties = new List<string> { "Id" };
            knownProperties.AddRange(entity.Properties
                .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => property.Name));
            knownProperties.AddRange(entity.Relationships
                .OrderBy(relationship => relationship.GetNavigationName(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(relationship => relationship.GetNavigationName(), StringComparer.Ordinal)
                .Select(relationship => relationship.GetNavigationName()));
            builder.AppendLine($"            if (HasUnexpectedProperties(typeof({entity.Name}),");
            for (var propertyIndex = 0; propertyIndex < knownProperties.Count; propertyIndex++)
            {
                var suffix = propertyIndex == knownProperties.Count - 1 ? "))" : ",";
                builder.AppendLine($"                {ToCSharpStringLiteral(knownProperties[propertyIndex])}{suffix}");
            }
            builder.AppendLine("            {");
            builder.AppendLine("                return true;");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendCSharpSerializerHelpers(StringBuilder builder, string modelTypeName, IReadOnlyList<GenericEntity> entities)
    {
        builder.AppendLine("        private static readonly string[] ShardFileNames =");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            builder.AppendLine($"            {ToCSharpStringLiteral(entity.Name + ".xml")},");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("        private static HashSet<string> BuildExpectedShardPaths(string instanceDirectoryPath)");
        builder.AppendLine("        {");
        builder.AppendLine("            var expectedShardPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);");
        builder.AppendLine("            foreach (var shardFileName in ShardFileNames)");
        builder.AppendLine("            {");
        builder.AppendLine("                expectedShardPaths.Add(Path.GetFullPath(Path.Combine(instanceDirectoryPath, shardFileName)));");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return expectedShardPaths;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private sealed class LoadState");
        builder.AppendLine("        {");
        foreach (var entity in entities)
        {
            var idSetName = ToCamelIdentifier(entity.Name) + "Ids";
            builder.AppendLine($"            private HashSet<string>? {idSetName};");
            builder.AppendLine();
            builder.AppendLine($"            public void Add{entity.Name}Id(string? id)");
            builder.AppendLine("            {");
            builder.AppendLine($"                var normalizedId = RequireIdentity(id, {ToCSharpStringLiteral($"Entity '{entity.Name}' contains a row with empty Id.")});");
            builder.AppendLine($"                {idSetName} ??= new HashSet<string>(StringComparer.Ordinal);");
            builder.AppendLine($"                if (!{idSetName}.Add(normalizedId))");
            builder.AppendLine("                {");
            builder.AppendLine($"                    throw new InvalidDataException($\"Entity '{entity.Name}' contains duplicate Id '{{normalizedId}}'.\");");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            builder.AppendLine();
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        AppendCSharpIndexCache(builder, modelTypeName, entities, "LoadIndexes");
        AppendCSharpIndexCache(builder, modelTypeName, entities, "SaveIndexes");
        builder.AppendLine("        private static readonly Lazy<bool> RuntimeExtendedShape = new(DetectRuntimeExtendedShape);");
        builder.AppendLine();
        builder.AppendLine("        private static bool HasRuntimeExtendedShape()");
        builder.AppendLine("        {");
        builder.AppendLine("            return RuntimeExtendedShape.Value;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool DetectRuntimeExtendedShape()");
        builder.AppendLine("        {");
        var runtimeShapeGroupCount = (entities.Count + 31) / 32;
        for (var groupIndex = 0; groupIndex < runtimeShapeGroupCount; groupIndex++)
        {
            builder.AppendLine($"            if (HasRuntimeExtendedEntityShapeGroup{groupIndex + 1}())");
            builder.AppendLine("            {");
            builder.AppendLine("                return true;");
            builder.AppendLine("            }");
        }

        builder.AppendLine();
        builder.AppendLine("            return HasUnexpectedModelLists();");
        builder.AppendLine("        }");
        builder.AppendLine();
        for (var groupIndex = 0; groupIndex < runtimeShapeGroupCount; groupIndex++)
        {
            AppendCSharpRuntimeShapeGroup(builder, entities.Skip(groupIndex * 32).Take(32).ToList(), groupIndex + 1);
        }

        builder.AppendLine("        private static bool HasUnexpectedModelLists()");
        builder.AppendLine("        {");
        builder.AppendLine("            var knownLists = new HashSet<string>(StringComparer.Ordinal)");
        builder.AppendLine("            {");
        foreach (var entity in entities)
        {
            builder.AppendLine($"                {ToCSharpStringLiteral(entity.GetListName())},");
        }

        builder.AppendLine("            };");
        builder.AppendLine($"            return typeof({modelTypeName}).GetProperties(BindingFlags.Instance | BindingFlags.Public)");
        builder.AppendLine("                .Any(property =>");
        builder.AppendLine("                    property.PropertyType.IsGenericType &&");
        builder.AppendLine("                    property.PropertyType.GetGenericTypeDefinition() == typeof(List<>) &&");
        builder.AppendLine("                    !knownLists.Contains(property.Name));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool HasUnexpectedProperties(Type type, params string[] knownProperties)");
        builder.AppendLine("        {");
        builder.AppendLine("            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)");
        builder.AppendLine("                .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)");
        builder.AppendLine("                .Any(property => !knownProperties.Contains(property.Name, StringComparer.Ordinal));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static Dictionary<string, T> BuildById<T>(IEnumerable<T> rows, Func<T, string> getId, string entityName)");
        builder.AppendLine("            where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            var rowsById = new Dictionary<string, T>(StringComparer.Ordinal);");
        builder.AppendLine("            foreach (var row in rows)");
        builder.AppendLine("            {");
        builder.AppendLine("                ArgumentNullException.ThrowIfNull(row);");
        builder.AppendLine("                var id = RequireIdentity(getId(row), $\"Entity '{entityName}' contains a row with empty Id.\");");
        builder.AppendLine("                if (!rowsById.TryAdd(id, row))");
        builder.AppendLine("                {");
        builder.AppendLine("                    throw new InvalidOperationException($\"Entity '{entityName}' contains duplicate Id '{id}'.\");");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            return rowsById;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static T RequireTarget<T>(Dictionary<string, T> rowsById, string targetId, string sourceEntityName, string sourceId, string relationshipName)");
        builder.AppendLine("            where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            var normalizedTargetId = RequireIdentity(targetId, $\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' is empty.\");");
        builder.AppendLine("            if (!rowsById.TryGetValue(normalizedTargetId, out var target))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new InvalidDataException($\"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' points to missing Id '{normalizedTargetId}'.\");");
        builder.AppendLine("            }");
        builder.AppendLine("            return target;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static string RequireIdentity(string? value, string errorMessage)");
        builder.AppendLine("        {");
        builder.AppendLine("            var normalizedValue = value?.Trim() ?? string.Empty;");
        builder.AppendLine("            if (string.IsNullOrEmpty(normalizedValue))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new InvalidOperationException(errorMessage);");
        builder.AppendLine("            }");
        builder.AppendLine("            return normalizedValue;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static string RequireText(string? value, string errorMessage)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (string.IsNullOrWhiteSpace(value))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new InvalidOperationException(errorMessage);");
        builder.AppendLine("            }");
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void AppendElement(StringBuilder builder, string name, string value, string indent)");
        builder.AppendLine("        {");
        builder.AppendLine("            builder.Append(indent);");
        builder.AppendLine("            builder.Append('<');");
        builder.AppendLine("            builder.Append(name);");
        builder.AppendLine("            builder.Append('>');");
        builder.AppendLine("            AppendXmlText(builder, value);");
        builder.AppendLine("            builder.Append(\"</\");");
        builder.AppendLine("            builder.Append(name);");
        builder.AppendLine("            builder.Append(\">\\n\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void AppendXmlAttribute(StringBuilder builder, string value)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (var character in value)");
        builder.AppendLine("            {");
        builder.AppendLine("                switch (character)");
        builder.AppendLine("                {");
        builder.AppendLine("                    case '&': builder.Append(\"&amp;\"); break;");
        builder.AppendLine("                    case '<': builder.Append(\"&lt;\"); break;");
        builder.AppendLine("                    case '>': builder.Append(\"&gt;\"); break;");
        builder.AppendLine("                    case '\"': builder.Append(\"&quot;\"); break;");
        builder.AppendLine("                    case '\\'': builder.Append(\"&apos;\"); break;");
        builder.AppendLine("                    default: builder.Append(character); break;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void AppendXmlText(StringBuilder builder, string value)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (var character in value)");
        builder.AppendLine("            {");
        builder.AppendLine("                switch (character)");
        builder.AppendLine("                {");
        builder.AppendLine("                    case '&': builder.Append(\"&amp;\"); break;");
        builder.AppendLine("                    case '<': builder.Append(\"&lt;\"); break;");
        builder.AppendLine("                    case '>': builder.Append(\"&gt;\"); break;");
        builder.AppendLine("                    default: builder.Append(character); break;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool IsNamespaceDeclaration(XmlReader reader)");
        builder.AppendLine("        {");
        builder.AppendLine("            return string.Equals(reader.Prefix, \"xmlns\", StringComparison.Ordinal) ||");
        builder.AppendLine("                   string.Equals(reader.LocalName, \"xmlns\", StringComparison.Ordinal);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void DeleteIfExists(string path)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (File.Exists(path))");
        builder.AppendLine("            {");
        builder.AppendLine("                File.Delete(path);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
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
        return DdlSqlServerRenderer.RenderSchema(BuildDdlDatabase(workspace));
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
        AppendGeneratedCSharpHeader(builder, requiresTooling: false, workspacePath: workspace.WorkspaceRootPath);
        var entities = workspace.Model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .ToList();

        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Collections.ObjectModel;");
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
            var records = workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var entityRecords)
                ? entityRecords
                    .OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(record => record.Id, StringComparer.Ordinal)
                    .ToList()
                : new List<GenericRecord>();
            for (var recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                var record = records[recordIndex];
                foreach (var relationship in entity.Relationships
                             .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
                {
                    var relationshipValue = record.RelationshipIds.TryGetValue(relationship.GetColumnName(), out var relationshipId)
                        ? relationshipId ?? string.Empty
                        : string.Empty;
                    var targetEntity = entities.First(target => string.Equals(target.Name, relationship.Entity, StringComparison.OrdinalIgnoreCase));
                    var targetVar = ToCamelIdentifier(targetEntity.GetListName());
                    if (relationship.IsNullable && string.IsNullOrWhiteSpace(relationshipValue))
                    {
                        builder.AppendLine($"            {rowsVar}[{recordIndex.ToString(CultureInfo.InvariantCulture)}].{relationship.GetNavigationName()} = null;");
                    }
                    else
                    {
                        builder.AppendLine($"            {rowsVar}[{recordIndex.ToString(CultureInfo.InvariantCulture)}].{relationship.GetNavigationName()} = RequireTarget(");
                        builder.AppendLine($"                {targetVar}ById,");
                        builder.AppendLine($"                {ToCSharpStringLiteral(relationshipValue)},");
                        builder.AppendLine($"                {ToCSharpStringLiteral(entity.Name)},");
                        builder.AppendLine($"                {ToCSharpStringLiteral(record.Id)},");
                        builder.AppendLine($"                {ToCSharpStringLiteral(relationship.GetColumnName())});");
                    }
                }
            }

            if (records.Count > 0 && entity.Relationships.Count > 0)
            {
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

    private static string BuildCSharpEntity(
        GenericEntity entity,
        string namespaceName,
        string? workspacePath,
        bool requiresTooling)
    {
        var builder = new StringBuilder();
        AppendGeneratedCSharpHeader(builder, requiresTooling: requiresTooling, workspacePath: workspacePath);
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public sealed class {entity.Name}");
        builder.AppendLine("    {");
        builder.AppendLine("        public string Id { get; set; } = string.Empty;");
        builder.AppendLine();

        foreach (var property in entity.Properties
                     .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.IsNullable)
            {
                builder.AppendLine($"        public string? {property.Name} {{ get; set; }}");
            }
            else
            {
                builder.AppendLine($"        public string {property.Name} {{ get; set; }} = string.Empty;");
            }

            builder.AppendLine();
        }

        foreach (var relationship in entity.Relationships
                     .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal))
        {
            var navigationName = relationship.GetNavigationName();
            if (relationship.IsNullable)
            {
                builder.AppendLine($"        public {relationship.Entity}? {navigationName} {{ get; set; }}");
            }
            else
            {
                builder.AppendLine($"        public {relationship.Entity} {navigationName} {{ get; set; }} = null!;");
            }

            builder.AppendLine();
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static void AppendGeneratedCSharpHeader(StringBuilder builder, bool requiresTooling, string? workspacePath)
    {
        var displayWorkspacePath = GetDisplayWorkspacePath(workspacePath);
        var regenerationCommand = requiresTooling
            ? $"meta generate csharp --workspace {QuoteForShellComment(displayWorkspacePath)} --out <dir> --tooling"
            : $"meta generate csharp --workspace {QuoteForShellComment(displayWorkspacePath)} --out <dir>";

        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("// This file was generated by Meta CLI.");
        builder.AppendLine("// Do not edit this file by hand.");
        builder.AppendLine("//");
        builder.AppendLine($"// Source workspace: {displayWorkspacePath}");
        builder.AppendLine("// Regenerate with:");
        builder.AppendLine($"//   {regenerationCommand}");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine();
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
    }

    private static string GetDisplayWorkspacePath(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return ".\\<workspace>";
        }

        var trimmedPath = workspacePath
            .Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leafName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(leafName)
            ? ".\\<workspace>"
            : $".\\{leafName}";
    }

    private static string QuoteForShellComment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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

    private static string BuildModelXml(GenericModel model)
    {
        var document = Meta.Core.Serialization.ModelXmlCodec.BuildDocument(model);
        return document.ToString(global::System.Xml.Linq.SaveOptions.None);
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

    private static string ToPascalIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Value";
        }

        if (name.Length == 1)
        {
            return char.ToUpperInvariant(name[0]).ToString();
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
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



