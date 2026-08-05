using System.Text;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Surfaces;

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
            [$"{state.Model.Name}.cs"] = normalizedSource,
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
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Collections.ObjectModel;");
        builder.AppendLine();
        builder.Append("namespace ").Append(Id(model.Name)).AppendLine(";");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            AppendEntity(builder, entity);
            builder.AppendLine();
        }

        builder.Append("public sealed class ")
            .Append(Id(model.Name + "Instance"))
            .AppendLine();
        builder.AppendLine("{");
        foreach (var entity in entities)
        {
            builder.Append("    public ReadOnlyCollection<")
                .Append(Id(entity.Name))
                .Append("> ")
                .Append(Id(entity.GetListName()))
                .AppendLine(" { get; }");
        }

        builder.AppendLine();
        builder.Append("    public ")
            .Append(Id(model.Name + "Instance"))
            .Append('(');
        for (var index = 0; index < entities.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("ReadOnlyCollection<")
                .Append(Id(entities[index].Name))
                .Append("> ")
                .Append(Id(ToParameterName(entities[index].GetListName())));
        }

        builder.AppendLine(")");
        builder.AppendLine("    {");
        foreach (var entity in entities)
        {
            builder.Append("        ")
                .Append(Id(entity.GetListName()))
                .Append(" = ")
                .Append(Id(ToParameterName(entity.GetListName())))
                .AppendLine(";");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        builder.Append("public static partial class ")
            .Append(Id(model.Name))
            .AppendLine();
        builder.AppendLine("{");
        builder.Append("    private static readonly ")
            .Append(Id(model.Name + "Instance"))
            .AppendLine(" _builtIn = CreateBuiltIn();");
        builder.Append("    public static ")
            .Append(Id(model.Name + "Instance"))
            .AppendLine(" BuiltIn => _builtIn;");
        builder.AppendLine();
        builder.Append("    public static ")
            .Append(Id(model.Name + "Instance"))
            .AppendLine(" CreateBuiltIn()");
        builder.AppendLine("    {");

        foreach (var entity in entities)
        {
            AppendCollection(builder, entity, state.Instance);
            builder.AppendLine();
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

                    builder.Append("        ")
                        .Append(Id(ToParameterName(entity.GetListName())))
                        .Append('[')
                        .Append(index)
                        .Append("].")
                        .Append(Id(relationship.GetNavigationName()))
                        .Append(" = RequireTarget(")
                        .Append(Id(ToParameterName(relationship.Entity + "List")))
                        .Append(", ")
                        .Append(Quote(targetId))
                        .AppendLine(");");
                }
            }
        }

        builder.Append("        return new ")
            .Append(Id(model.Name + "Instance"))
            .Append('(');
        for (var index = 0; index < entities.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("new ReadOnlyCollection<")
                .Append(Id(entities[index].Name))
                .Append(">(")
                .Append(Id(ToParameterName(entities[index].GetListName())))
                .Append(')');
        }

        builder.AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();

        foreach (var entity in entities)
        {
            builder.Append("    private static ")
                .Append(Id(entity.Name))
                .Append(" RequireTarget(IReadOnlyList<")
                .Append(Id(entity.Name))
                .Append("> records, string id)");
            builder.AppendLine();
            builder.AppendLine("    {");
            builder.AppendLine("        foreach (var record in records)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (record.Id == id)");
            builder.AppendLine("            {");
            builder.AppendLine("                return record;");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.Append("        throw new InvalidOperationException(")
                .Append(Quote("C# metadata relationship target was not found."))
                .AppendLine(");");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendEntity(StringBuilder builder, GenericEntity entity)
    {
        builder.Append("public sealed class ").Append(Id(entity.Name)).AppendLine();
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

    private static void AppendCollection(
        StringBuilder builder,
        GenericEntity entity,
        GenericInstance instance)
    {
        builder.Append("        var ")
            .Append(Id(ToParameterName(entity.GetListName())))
            .Append(" = new List<")
            .Append(Id(entity.Name))
            .AppendLine(">");
        builder.AppendLine("        {");
        foreach (var record in GetRecords(instance, entity.Name))
        {
            builder.Append("            new ")
                .Append(Id(entity.Name))
                .AppendLine();
            builder.AppendLine("            {");
            builder.Append("                Id = ").Append(Quote(record.Id));

            foreach (var property in entity.Properties
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                if (record.Values.TryGetValue(property.Name, out var value))
                {
                    builder.Append(",\n                ")
                        .Append(Id(property.Name))
                        .Append(" = ")
                        .Append(Quote(value));
                }
            }

            builder.AppendLine();
            builder.AppendLine("            },");
        }

        builder.AppendLine("        };");
    }

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

    private static string ToParameterName(string listName)
    {
        return char.ToLowerInvariant(listName[0]) + listName[1..];
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
