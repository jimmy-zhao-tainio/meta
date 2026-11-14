using System.Text;
using Meta.Operations;
using Meta.Operations.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Surfaces.CSharp;

/// <summary>Emits derived typed access code separately from authored C# workspace sources.</summary>
public static class MetaCSharpBindingWriter
{
    private const string Rules = "global::Meta.TypedModels.TypedWorkspaceBindingValidation";
    private const string Domain = "global::Meta.Operations.Domain.";

    public static MetaCSharp Write(GenericModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagnostics = WorkspaceValidator.Validate(model, new GenericInstance { ModelName = model.Name });
        if (diagnostics.HasErrors)
        {
            throw new InvalidOperationException("Cannot generate a binding for invalid metadata. " +
                string.Join(" | ", diagnostics.Issues.Where(issue => issue.Severity == IssueSeverity.Error)
                    .Take(5).Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}")));
        }

        RequireIdentifier(model.Name);
        var entities = model.Entities.OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.Ordinal).ToArray();
        foreach (var entity in entities)
        {
            RequireIdentifier(entity.Name);
            if (entity.Name == model.Name + "WorkspaceBinding" ||
                entity.Name == model.Name + "Model" || entity.Name == model.Name + "Instance")
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' conflicts with a generated C# type in namespace '{model.Name}'.");
            }
            foreach (var property in entity.Properties) RequireIdentifier(property.Name);
            foreach (var relationship in entity.Relationships) RequireIdentifier(relationship.GetNavigationName());
        }

        var source = BuildSource(model, entities);
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5).Select(diagnostic => diagnostic.ToString()).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException("C# binding writer produced invalid syntax. " + string.Join(" | ", errors));
        }

        return new MetaCSharp(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [model.Name + ".bindings.cs"] = tree.GetRoot().NormalizeWhitespace().ToFullString(),
        });
    }

    private static string BuildSource(GenericModel model, IReadOnlyList<GenericEntity> entities)
    {
        var builder = new StringBuilder();
        void Line(string value = "") => builder.AppendLine(value);
        var modelType = TypeName(model.Name, model.Name + "Model");
        Line("// Derived from the modeled contract. Keep outside authored workspace source sets.");
        Line("#nullable enable");
        Line("namespace " + Id(model.Name) + ";");
        Line("public sealed class " + Id(model.Name + "WorkspaceBinding") +
             " : global::Meta.TypedModels.ITypedWorkspaceBinding<" + modelType + ">");
        Line("{");
        Line("    private const string ContractSignature = " + Quote(model.ComputeContractSignature()) + ";");
        Line("    public " + modelType + " Read(" + Domain + "InMemoryWorkspace state)");
        Line("    {");
        Line("        global::System.ArgumentNullException.ThrowIfNull(state);");
        Line("        " + Rules + ".RequireContract(state.Model, ContractSignature);");
        Line("        " + Rules + ".RequireValid(state);");
        Line("        var typed = new " + modelType + "();");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            Line("        var index" + index + " = new global::System.Collections.Generic.Dictionary<string, " +
                 TypeName(model.Name, entity.Name) + ">(" + Domain + "MetaIdentity.Comparer);");
            Line("        foreach (var source in " + Rules + ".Records(state, " + Quote(entity.Name) + "))");
            Line("        {");
            Line("            var row = new " + TypeName(model.Name, entity.Name) + " { Id = source.Id };");
            foreach (var property in Properties(entity))
            {
                Line("            row." + Id(property.Name) + " = " + Rules + "." +
                     (property.IsNullable ? "OptionalValue" : "RequiredValue") + "(source, " + Quote(property.Name) + ");");
            }
            Line("            typed." + Id(entity.GetListName()) + ".Add(row);");
            Line("            index" + index + ".Add(source.Id, row);");
            Line("        }");
        }

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            if (entity.Relationships.Count == 0) continue;
            Line("        foreach (var source in " + Rules + ".Records(state, " + Quote(entity.Name) + "))");
            Line("        {");
            Line("            var row = index" + index + "[source.Id];");
            foreach (var relationship in Relationships(entity))
            {
                Line("            row." + Id(relationship.GetNavigationName()) + " = " + Rules + "." +
                     (relationship.IsNullable ? "OptionalTarget" : "RequiredTarget") + "(source, " +
                     Quote(relationship.GetColumnName()) + ", index" + TargetIndex(entities, relationship.Entity) + ");");
            }
            Line("        }");
        }
        Line("        return typed;");
        Line("    }");
        Line("    public " + Domain + "InMemoryWorkspace Write(" + modelType + " model, " + Domain + "GenericModel contract)");
        Line("    {");
        Line("        global::System.ArgumentNullException.ThrowIfNull(model);");
        Line("        " + Rules + ".RequireContract(contract, ContractSignature);");
        Line("        var instance = new " + Domain + "GenericInstance { ModelName = contract.Name };");
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            Line("        var index" + index + " = " + Rules + ".Index(model." + Id(entity.GetListName()) +
                 ", row => row.Id, " + Quote(entity.Name) + ");");
        }
        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            Line("        foreach (var row in " + Rules + ".Ordered(index" + index + "))");
            Line("        {");
            Line("            var target = new " + Domain + "GenericRecord { Id = row.Id };");
            foreach (var property in Properties(entity))
            {
                var access = "row." + Id(property.Name);
                Line("            if (" + access + " is not null) target.Values.Add(" + Quote(property.Name) + ", " + access + ");");
            }
            foreach (var relationship in Relationships(entity))
            {
                var access = "row." + Id(relationship.GetNavigationName());
                Line("            if (" + access + " is not null)");
                Line("            {");
                Line("                " + Rules + ".RequireCanonical(" + access + ", " + access + ".Id, index" +
                     TargetIndex(entities, relationship.Entity) + ", " + Quote(entity.Name + "." + relationship.GetNavigationName()) + ");");
                Line("                target.RelationshipIds.Add(" + Quote(relationship.GetColumnName()) + ", " + access + ".Id);");
                Line("            }");
            }
            Line("            instance.GetOrCreateEntityRecords(" + Quote(entity.Name) + ").Add(target);");
            Line("        }");
        }
        Line("        var state = new " + Domain + "InMemoryWorkspace(contract.Clone(), instance);");
        Line("        " + Rules + ".RequireValid(state);");
        Line("        return state;");
        Line("    }");
        Line("}");
        return builder.ToString();
    }

    private static IEnumerable<GenericProperty> Properties(GenericEntity entity) =>
        entity.Properties.OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(property => property.Name, StringComparer.Ordinal);

    private static IEnumerable<GenericRelationship> Relationships(GenericEntity entity) =>
        entity.Relationships.OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.GetColumnName(), StringComparer.Ordinal);

    private static int TargetIndex(IReadOnlyList<GenericEntity> entities, string name)
    {
        for (var index = 0; index < entities.Count; index++)
        {
            if (MetaName.Comparer.Equals(entities[index].Name, name)) return index;
        }
        throw new InvalidOperationException($"Relationship target '{name}' is not part of the modeled contract.");
    }

    private static string TypeName(string modelName, string typeName) => "global::" + Id(modelName) + "." + Id(typeName);
    private static string Quote(string value) => SyntaxFactory.Literal(value).ToFullString();
    private static string Id(string value) => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None ? "@" + value : value;

    private static void RequireIdentifier(string value)
    {
        var token = SyntaxFactory.ParseToken("@" + value);
        if (!token.IsKind(SyntaxKind.IdentifierToken) || !string.Equals(token.ValueText, value, StringComparison.Ordinal))
            throw new InvalidOperationException($"C# metadata name '{value}' is not a valid identifier.");
    }
}
