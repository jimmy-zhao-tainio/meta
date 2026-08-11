using System.Text;
using Meta.Operations.Domain;
using Meta.Operations;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Surfaces.CSharp;

public static class MetaCSharpWriter
{
    public static MetaCSharp Write(InMemoryWorkspace state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var diagnostics = WorkspaceValidator.Validate(
            state.Model,
            state.Instance);
        if (diagnostics.HasErrors)
        {
            var errors = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue =>
                    $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException(
                "Cannot write C# for invalid metadata. " +
                string.Join(" | ", errors));
        }

        ValidateIdentifiers(state.Model);
        var source = BuildSource(state);
        var tree = CSharpSyntaxTree.ParseText(source);
        var syntaxErrors = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        if (syntaxErrors.Length > 0)
        {
            throw new InvalidOperationException(
                "C# writer produced invalid syntax. " +
                string.Join(" | ", syntaxErrors));
        }

        var normalizedSource = tree.GetRoot().NormalizeWhitespace().ToFullString();
        EnsureCompiles(normalizedSource);

        return new MetaCSharp(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{state.Model.Name}.meta.cs"] = normalizedSource,
        });
    }

    private static string BuildSource(InMemoryWorkspace state)
    {
        var model = state.Model;
        var entities = model.Entities
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.Append("namespace ").Append(Id(model.Name)).AppendLine(";");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            AppendEntity(builder, entity);
            builder.AppendLine();
        }

        AppendTypedModel(builder, model, entities);
        builder.AppendLine();

        builder.Append("public static partial class ")
            .Append(Id(model.Name + "Instance"))
            .AppendLine();
        builder.AppendLine("{");
        builder.Append("    private static readonly ")
            .Append(Id(model.Name + "Model"))
            .AppendLine(" _builtIn = CreateBuiltIn();");
        builder.Append("    public static ")
            .Append(Id(model.Name + "Model"))
            .AppendLine(" BuiltIn => _builtIn;");
        builder.AppendLine();
        builder.Append("    public static ")
            .Append(Id(model.Name + "Model"))
            .AppendLine(" CreateBuiltIn()");
        builder.AppendLine("    {");
        builder.Append("        var model = ")
            .Append(Id(model.Name + "Model"))
            .AppendLine(".CreateEmpty();");
        builder.AppendLine();

        var recordVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nextRecordNumber = 0;
        foreach (var entity in entities)
        {
            foreach (var record in GetRecords(state.Instance, entity.Name))
            {
                var variableName = "record" + nextRecordNumber++;
                recordVariables.Add(RecordKey(entity.Name, record.Id), variableName);
                AppendRecord(builder, entity, record, variableName);
                builder.Append("        model.")
                    .Append(Id(entity.GetListName()))
                    .Append(".Add(")
                    .Append(variableName)
                    .AppendLine(");");
                builder.AppendLine();
            }
        }

        foreach (var entity in entities)
        {
            var records = GetRecords(state.Instance, entity.Name);
            for (var index = 0; index < records.Count; index++)
            {
                foreach (var relationship in entity.Relationships
                             .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
                {
                    if (!records[index].RelationshipIds.TryGetValue(
                            relationship.GetColumnName(),
                            out var targetId) ||
                        string.IsNullOrWhiteSpace(targetId))
                    {
                        continue;
                    }

                    var sourceRecord = records[index];
                    if (!recordVariables.TryGetValue(
                            RecordKey(entity.Name, sourceRecord.Id),
                            out var sourceVariable) ||
                        !recordVariables.TryGetValue(
                            RecordKey(relationship.Entity, targetId),
                            out var targetVariable))
                    {
                        throw new InvalidOperationException(
                            $"C# relationship '{entity.Name}.{relationship.GetNavigationName()}' points to a record that is not present.");
                    }

                    builder.Append("        ")
                        .Append(sourceVariable)
                        .Append('.')
                        .Append(Id(relationship.GetNavigationName()))
                        .Append(" = ")
                        .Append(targetVariable)
                        .AppendLine(";");
                }
            }
        }

        builder.AppendLine("        return model;");
        builder.AppendLine("    }");

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendTypedModel(
        StringBuilder builder,
        GenericModel model,
        IReadOnlyList<GenericEntity> entities)
    {
        builder.Append("public sealed partial class ")
            .Append(Id(model.Name + "Model"))
            .AppendLine();
        builder.AppendLine("{");
        builder.Append("    public static ")
            .Append(Id(model.Name + "Model"))
            .AppendLine(" CreateEmpty() => new();");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            builder.Append("    public List<")
                .Append(Id(entity.Name))
                .Append("> ")
                .Append(Id(entity.GetListName()))
                .AppendLine(" { get; set; } = new();");
        }

        builder.AppendLine("}");
    }

    private static void AppendEntity(StringBuilder builder, GenericEntity entity)
    {
        builder.Append("public sealed partial class ").Append(Id(entity.Name)).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    public string Id { get; set; } = null!;");

        foreach (var property in entity.Properties
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            builder.Append("    public string")
                .Append(property.IsNullable ? "?" : string.Empty)
                .Append(' ')
                .Append(Id(property.Name))
                .Append(" { get; set; }");
            if (!property.IsNullable)
            {
                builder.Append(" = null!;");
            }

            builder.AppendLine();
        }

        foreach (var relationship in entity.Relationships
                     .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
        {
            builder.Append("    public ")
                .Append(Id(relationship.Entity))
                .Append(relationship.IsNullable ? "?" : string.Empty)
                .Append(' ')
                .Append(Id(relationship.GetNavigationName()))
                .Append(" { get; set; }");
            if (!relationship.IsNullable)
            {
                builder.Append(" = null!;");
            }

            builder.AppendLine();
        }

        builder.AppendLine("}");
    }

    private static void AppendRecord(
        StringBuilder builder,
        GenericEntity entity,
        GenericRecord record,
        string variableName)
    {
        builder.Append("        var ")
            .Append(variableName)
            .Append(" = new ")
            .Append(Id(entity.Name))
            .AppendLine();
        builder.AppendLine("        {");
        builder.Append("            Id = ").Append(Quote(record.Id));
        foreach (var property in entity.Properties
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            if (record.Values.TryGetValue(property.Name, out var value))
            {
                builder.Append(",\n            ")
                    .Append(Id(property.Name))
                    .Append(" = ")
                    .Append(Quote(value));
            }
        }

        builder.AppendLine();
        builder.AppendLine("        };");
    }

    private static string RecordKey(string entityName, string id) =>
        entityName + "\u001f" + id;

    private static IReadOnlyList<GenericRecord> GetRecords(
        GenericInstance instance,
        string entityName)
    {
        return instance.RecordsByEntity.TryGetValue(entityName, out var records)
            ? records
                .OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Id, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<GenericRecord>();
    }

    private static string Quote(string value)
    {
        return SyntaxFactory.Literal(value ?? string.Empty).ToFullString();
    }

    private static string Id(string value)
    {
        return SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
               SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            ? "@" + value
            : value;
    }

    private static void ValidateIdentifiers(GenericModel model)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        RequireIdentifier(model.Name, "model");
        foreach (var entity in model.Entities)
        {
            RequireIdentifier(entity.Name, "entity");
            if (!names.Add(entity.Name))
            {
                throw new InvalidOperationException(
                    $"C# metadata contains duplicate entity identifier '{entity.Name}'.");
            }

            foreach (var property in entity.Properties)
            {
                RequireIdentifier(property.Name, $"property '{entity.Name}.{property.Name}'");
            }

            foreach (var relationship in entity.Relationships)
            {
                RequireIdentifier(
                    relationship.GetNavigationName(),
                    $"relationship '{entity.Name}.{relationship.GetNavigationName()}'");
                RequireIdentifier(relationship.Entity, "relationship target");
            }
        }
    }

    private static void RequireIdentifier(string value, string description)
    {
        var token = SyntaxFactory.ParseToken("@" + value);
        if (!token.IsKind(SyntaxKind.IdentifierToken) ||
            !StringComparer.Ordinal.Equals(token.ValueText, value))
        {
            throw new InvalidOperationException(
                $"C# {description} '{value}' is not a valid C# identifier.");
        }
    }

    private static void EnsureCompiles(string source)
    {
        var platformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var compilation = CSharpCompilation.Create(
            "MetaCSharpWorkspace",
            new[] { CSharpSyntaxTree.ParseText(source) },
            platformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "C# writer produced code that does not compile. " +
                string.Join(" | ", errors));
        }
    }
}
