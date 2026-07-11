using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public static partial class GenerationService
{
    private static string BuildCSharpTooling(string modelTypeName, string namespaceName, string? workspacePath)
    {
        var builder = new StringBuilder();
        AppendGeneratedCSharpHeader(builder, requiresTooling: true, workspacePath: workspacePath);
        builder.AppendLine("using Meta.Core.Serialization;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine();
        builder.AppendLine($"namespace {namespaceName}");
        builder.AppendLine("{");
        builder.AppendLine($"    public static class {namespaceName}Tooling");
        builder.AppendLine("    {");
        builder.AppendLine($"        public static {modelTypeName} Load(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = false)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return {modelTypeName}.LoadFromXmlWorkspace(workspacePath, searchUpward);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task<{modelTypeName}> LoadAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = false,");
        builder.AppendLine("            CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine($"            return {modelTypeName}.LoadFromXmlWorkspaceAsync(workspacePath, searchUpward, cancellationToken);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static string CreateWorkspace(string workspacePath)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return TypedWorkspaceXmlSerializer.CreateWorkspace<{modelTypeName}>(workspacePath);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool IsWorkspace(string workspacePath)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return TypedWorkspaceXmlSerializer.IsWorkspace<{modelTypeName}>(workspacePath);");
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
        builder.AppendLine($"    public sealed partial class {modelTypeName} : IMetaWorkspaceModel<{modelTypeName}>");
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
        builder.AppendLine("            bool searchUpward = false)");
        builder.AppendLine("        {");
        builder.AppendLine($"            return {modelTypeName}XmlSerializer.Load(workspacePath, searchUpward);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine($"        public static Task<{modelTypeName}> LoadFromXmlWorkspaceAsync(");
        builder.AppendLine("            string workspacePath,");
        builder.AppendLine("            bool searchUpward = false,");
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
        builder.AppendLine("            if (searchUpward)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new NotSupportedException(\"Typed workspace loading does not search parent directories. Pass an explicit workspace path.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (HasRuntimeExtendedShape())");
        builder.AppendLine("            {");
        builder.AppendLine($"                return TypedWorkspaceXmlSerializer.Load<{modelTypeName}>(workspacePath, searchUpward);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine($"            var workspaceRootPath = TypedWorkspaceXmlSerializer.RequireWorkspace<{modelTypeName}>(workspacePath);");
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
        builder.AppendLine("            var rowIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);");
        builder.AppendLine("            builder.Append(\"<?xml version=\\\"1.0\\\" encoding=\\\"utf-8\\\"?>\\n\");");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("<" + rootName + ">\n")});");
        builder.AppendLine($"            builder.Append({ToCSharpStringLiteral("  <" + entity.GetListName() + ">\n")});");
        builder.AppendLine($"            foreach (var row in model.{entity.GetListName()}.OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase))");
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
            builder.AppendLine($"                {idSetName} ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);");
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
        builder.AppendLine("            var rowsById = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);");
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
            builder.AppendLine($"            var {rowsVar}ById = new Dictionary<string, {entity.Name}>(global::System.StringComparer.OrdinalIgnoreCase);");
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
}
