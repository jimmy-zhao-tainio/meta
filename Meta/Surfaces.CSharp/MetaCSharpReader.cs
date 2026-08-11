using Meta.Operations.Domain;
using Meta.Operations;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meta.Surfaces.CSharp;

public static class MetaCSharpReader
{
    public static InMemoryWorkspace Read(MetaCSharp csharp)
    {
        ArgumentNullException.ThrowIfNull(csharp);
        if (csharp.Sources.Count == 0)
        {
            throw new InvalidDataException(
                "C# metadata contains no source files.");
        }

        var trees = csharp.Sources
            .OrderBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(source => Parse(source.Key, source.Value))
            .ToArray();
        EnsureCompiles(trees);
        var roots = trees
            .Select(tree => tree.GetCompilationUnitRoot())
            .ToArray();
        var model = ReadModel(roots);
        var instance = ReadInstance(roots, model);
        var diagnostics = WorkspaceValidator.Validate(
            model,
            instance);
        if (diagnostics.HasErrors)
        {
            var errors = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue =>
                    $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidDataException(
                "C# metadata is invalid. " +
                string.Join(" | ", errors));
        }

        return new InMemoryWorkspace(model, instance);
    }

    private static SyntaxTree Parse(
        string fileName,
        string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source ?? string.Empty,
            path: fileName);
        var errors = tree.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidDataException(
                $"C# metadata source '{fileName}' has syntax errors. " +
                string.Join(" | ", errors));
        }

        return tree;
    }

    private static void EnsureCompiles(
        IReadOnlyCollection<SyntaxTree> trees)
    {
        var platformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var compilation = CSharpCompilation.Create(
            "MetaCSharpWorkspace",
            trees,
            platformAssemblies
                .Split(Path.PathSeparator)
                .Select(path =>
                    MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidDataException(
                "C# metadata does not compile. " +
                string.Join(" | ", errors));
        }
    }

    private static GenericModel ReadModel(
        IReadOnlyCollection<CompilationUnitSyntax> roots)
    {
        var namespaces = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>())
            .Select(namespaceDeclaration =>
                ReadSimpleName(namespaceDeclaration.Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (namespaces.Length != 1)
        {
            throw new InvalidDataException(
                "C# metadata must declare one shared simple namespace.");
        }

        var model = new GenericModel
        {
            Name = namespaces[0],
        };
        var entityDeclarations = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Where(IsEntityDeclaration)
            .ToArray();
        foreach (var declaration in entityDeclarations)
        {
            var name = declaration.Identifier.ValueText;
            if (model.FindEntity(name) != null)
            {
                throw new InvalidDataException(
                    $"C# metadata declares entity '{name}' more than once.");
            }

            model.Entities.Add(new GenericEntity
            {
                Name = name,
            });
        }

        foreach (var declaration in entityDeclarations)
        {
            var entity = model.FindEntity(
                declaration.Identifier.ValueText)!;
            foreach (var propertyDeclaration in declaration.Members
                         .OfType<PropertyDeclarationSyntax>())
            {
                var name = propertyDeclaration.Identifier.ValueText;
                if (MetaName.Comparer.Equals(name, "Id"))
                {
                    continue;
                }

                if (TryReadStringType(
                        propertyDeclaration.Type,
                        out var isNullable))
                {
                    entity.Properties.Add(new GenericProperty
                    {
                        Name = name,
                        IsNullable = isNullable,
                    });
                    continue;
                }

                var targetType = ReadEntityType(
                    propertyDeclaration.Type,
                    out isNullable);
                var target = model.FindEntity(targetType) ??
                    throw new InvalidDataException(
                        $"C# property '{entity.Name}.{name}' has unsupported type '{propertyDeclaration.Type}'.");
                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = target.Name,
                    Role = MetaName.Comparer.Equals(name, target.Name)
                        ? string.Empty
                        : name,
                    IsNullable = isNullable,
                });
            }
        }

        RequireTypedModelRoot(roots, model);
        return model;
    }

    private static void RequireTypedModelRoot(
        IReadOnlyCollection<CompilationUnitSyntax> roots,
        GenericModel model)
    {
        var modelTypes = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Where(type => type.Identifier.ValueText == model.Name + "Model")
            .ToArray();
        if (modelTypes.Length != 1)
        {
            throw new InvalidDataException(
                $"C# metadata must declare one '{model.Name}Model' type.");
        }

        var modelType = modelTypes[0];
        var factories = modelType.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method =>
                method.Identifier.ValueText == "CreateEmpty" &&
                method.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                ReadSimpleTypeName(method.ReturnType) == model.Name + "Model")
            .ToArray();
        if (factories.Length != 1)
        {
            throw new InvalidDataException(
                $"{model.Name}Model must expose one static CreateEmpty method.");
        }

        var listProperties = modelType.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(property => TryReadListElementType(property.Type, out _))
            .ToArray();
        foreach (var entity in model.Entities)
        {
            var matches = listProperties
                .Where(property =>
                    property.Identifier.ValueText == entity.GetListName() &&
                    TryReadListElementType(property.Type, out var elementType) &&
                    MetaName.Comparer.Equals(elementType, entity.Name))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"{model.Name}Model must expose one List<{entity.Name}> {entity.GetListName()} property.");
            }
        }

        if (listProperties.Length != model.Entities.Count)
        {
            throw new InvalidDataException(
                $"{model.Name}Model contains a list property that is not part of the model.");
        }
    }

    private static GenericInstance ReadInstance(
        IReadOnlyCollection<CompilationUnitSyntax> roots,
        GenericModel model)
    {
        var instanceTypes = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Where(type => type.Identifier.ValueText == model.Name + "Instance")
            .ToArray();
        if (instanceTypes.Length != 1)
        {
            throw new InvalidDataException(
                $"C# metadata must declare one '{model.Name}Instance' type.");
        }

        var factories = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>())
            .Where(method =>
                method.Identifier.ValueText == "CreateBuiltIn" &&
                method.Parent == instanceTypes[0])
            .ToArray();
        if (factories.Length != 1 ||
            factories[0].Body == null)
        {
            throw new InvalidDataException(
                "C# metadata must declare one CreateBuiltIn method body.");
        }

        var method = factories[0];
        RequireBuiltInRoot(roots, model, method);
        var body = method.Body!;
        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        var modelVariables = body.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable =>
                variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                InvocationName(invocation) == "CreateEmpty")
            .Select(variable => variable.Identifier.ValueText)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (modelVariables.Length != 1)
        {
            throw new InvalidDataException(
                $"{model.Name}Instance.CreateBuiltIn must create one {model.Name}Model with CreateEmpty.");
        }

        var modelVariable = modelVariables[0];
        var recordsByVariable = new Dictionary<string, (GenericEntity Entity, GenericRecord Record)>(StringComparer.Ordinal);
        foreach (var variable in body.DescendantNodes()
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (variable.Identifier.ValueText == modelVariable)
            {
                continue;
            }

            if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax creation)
            {
                continue;
            }

            var entity = model.FindEntity(ReadSimpleTypeName(creation.Type));
            if (entity == null)
            {
                throw new InvalidDataException(
                    $"C# instance variable '{variable.Identifier.ValueText}' has an unsupported type '{creation.Type}'.");
            }

            recordsByVariable.Add(
                variable.Identifier.ValueText,
                (entity, ReadRecordCreation(creation, entity)));
        }

        var addedVariables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in body.Statements.OfType<ExpressionStatementSyntax>())
        {
            if (statement.Expression is InvocationExpressionSyntax invocation &&
                TryReadModelAdd(invocation, modelVariable, model, out var entity, out var recordVariable))
            {
                if (!recordsByVariable.TryGetValue(recordVariable, out var record) ||
                    !ReferenceEquals(record.Entity, entity) ||
                    !addedVariables.Add(recordVariable))
                {
                    throw new InvalidDataException(
                        $"C# instance adds record variable '{recordVariable}' to an invalid model collection.");
                }

                instance.GetOrCreateEntityRecords(entity.Name).Add(record.Record);
                continue;
            }

            if (statement.Expression is AssignmentExpressionSyntax assignment)
            {
                ReadRelationshipAssignment(
                    assignment,
                    recordsByVariable,
                    instance);
                continue;
            }

            throw new InvalidDataException(
                "C# CreateBuiltIn contains an unsupported expression statement.");
        }

        if (recordsByVariable.Keys.Any(variable => !addedVariables.Contains(variable)))
        {
            throw new InvalidDataException(
                "C# CreateBuiltIn declares a record that is not added to the model.");
        }

        var returns = body.Statements
            .OfType<ReturnStatementSyntax>()
            .ToArray();
        if (returns.Length != 1 ||
            returns[0].Expression is not IdentifierNameSyntax returnedModel ||
            returnedModel.Identifier.ValueText != modelVariable)
        {
            throw new InvalidDataException(
                "C# CreateBuiltIn must return the model created by CreateEmpty.");
        }

        return instance;
    }

    private static GenericRecord ReadRecordCreation(
        ObjectCreationExpressionSyntax recordCreation,
        GenericEntity entity)
    {
        if (recordCreation.Initializer == null)
        {
            throw new InvalidDataException(
                $"C# record for '{entity.Name}' must use an object initializer.");
        }

        string? id = null;
        var record = new GenericRecord();
        foreach (var assignment in recordCreation.Initializer.Expressions
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not IdentifierNameSyntax member)
            {
                throw new InvalidDataException(
                    $"C# record initializer for '{entity.Name}' contains an unsupported member.");
            }

            var memberName = member.Identifier.ValueText;
            if (MetaName.Comparer.Equals(memberName, "Id"))
            {
                id = ReadString(assignment.Right);
                continue;
            }

            var property = entity.Properties.FirstOrDefault(candidate =>
                MetaName.Comparer.Equals(candidate.Name, memberName)) ??
                throw new InvalidDataException(
                    $"C# record initializer uses unknown property '{entity.Name}.{memberName}'.");
            var value = ReadOptionalString(assignment.Right);
            if (value != null)
            {
                record.Values.Add(property.Name, value);
            }
        }

        record.Id = MetaIdentity.Require(
            id,
            $"C# record for entity '{entity.Name}' has invalid Id.");
        return record;
    }

    private static bool TryReadModelAdd(
        InvocationExpressionSyntax invocation,
        string modelVariable,
        GenericModel model,
        out GenericEntity entity,
        out string recordVariable)
    {
        entity = null!;
        recordVariable = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax add ||
            add.Name.Identifier.ValueText != "Add" ||
            add.Expression is not MemberAccessExpressionSyntax list ||
            list.Expression is not IdentifierNameSyntax modelName ||
            modelName.Identifier.ValueText != modelVariable ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not IdentifierNameSyntax recordName)
        {
            return false;
        }

        entity = model.Entities.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(candidate.GetListName(), list.Name.Identifier.ValueText))!;
        if (entity == null)
        {
            throw new InvalidDataException(
                $"C# model collection '{list.Name.Identifier.ValueText}' is not declared by the model.");
        }

        recordVariable = recordName.Identifier.ValueText;
        return true;
    }

    private static void ReadRelationshipAssignment(
        AssignmentExpressionSyntax assignment,
        IReadOnlyDictionary<string, (GenericEntity Entity, GenericRecord Record)> recordsByVariable,
        GenericInstance instance)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax member ||
            member.Expression is not IdentifierNameSyntax sourceName ||
            !recordsByVariable.TryGetValue(sourceName.Identifier.ValueText, out var source))
        {
            throw new InvalidDataException(
                "C# relationship assignment must target a declared record.");
        }

        var relationship = source.Entity.Relationships.FirstOrDefault(candidate =>
            MetaName.Comparer.Equals(candidate.GetNavigationName(), member.Name.Identifier.ValueText));
        if (relationship == null)
        {
            throw new InvalidDataException(
                $"C# relationship assignment uses unknown navigation '{source.Entity.Name}.{member.Name.Identifier.ValueText}'.");
        }

        if (assignment.Right.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return;
        }

        if (assignment.Right is not IdentifierNameSyntax targetName ||
            !recordsByVariable.TryGetValue(targetName.Identifier.ValueText, out var target) ||
            !MetaName.Comparer.Equals(target.Entity.Name, relationship.Entity))
        {
            throw new InvalidDataException(
                $"C# relationship assignment for '{source.Entity.Name}.{relationship.GetNavigationName()}' targets the wrong entity.");
        }

        if (!source.Record.RelationshipIds.TryAdd(
                relationship.GetColumnName(),
                target.Record.Id))
        {
            throw new InvalidDataException(
                $"C# relationship '{source.Entity.Name}.{relationship.GetColumnName()}' is assigned more than once.");
        }
    }

    private static void RequireBuiltInRoot(
        IReadOnlyCollection<CompilationUnitSyntax> roots,
        GenericModel model,
        MethodDeclarationSyntax factory)
    {
        var fields = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>())
            .Where(field =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
            .Where(field => ReadSimpleTypeName(field.Declaration.Type) == model.Name + "Model")
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable =>
                variable.Initializer?.Value is InvocationExpressionSyntax invocation &&
                InvocationName(invocation) == factory.Identifier.ValueText)
            .ToArray();
        if (fields.Length != 1)
        {
            throw new InvalidDataException(
                "C# metadata must initialize one static readonly BuiltIn field from CreateBuiltIn.");
        }

        var fieldName = fields[0].Identifier.ValueText;
        var builtInProperties = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>())
            .Where(property =>
                property.Identifier.ValueText == "BuiltIn" &&
                property.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                ReadSimpleTypeName(property.Type) == model.Name + "Model" &&
                property.ExpressionBody?.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == fieldName)
            .ToArray();
        if (builtInProperties.Length != 1)
        {
            throw new InvalidDataException(
                "C# metadata must expose the CreateBuiltIn result through one static BuiltIn property.");
        }
    }

    private static string InvocationName(
        InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static bool IsEntityDeclaration(
        ClassDeclarationSyntax declaration)
    {
        return declaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(property =>
                property.Identifier.ValueText == "Id" &&
                TryReadStringType(property.Type, out _));
    }

    private static bool TryReadStringType(
        TypeSyntax type,
        out bool isNullable)
    {
        isNullable = type is NullableTypeSyntax;
        var underlying = type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
        return underlying is PredefinedTypeSyntax predefined &&
               predefined.Keyword.IsKind(SyntaxKind.StringKeyword);
    }

    private static string ReadEntityType(
        TypeSyntax type,
        out bool isNullable)
    {
        isNullable = type is NullableTypeSyntax;
        var underlying = type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
        return ReadSimpleTypeName(underlying);
    }

    private static bool TryReadListElementType(
        TypeSyntax type,
        out string elementType)
    {
        if (type is GenericNameSyntax generic &&
            generic.Identifier.ValueText == "List" &&
            generic.TypeArgumentList.Arguments.Count == 1)
        {
            elementType = ReadSimpleTypeName(
                generic.TypeArgumentList.Arguments[0]);
            return true;
        }

        elementType = string.Empty;
        return false;
    }

    private static string ReadString(ExpressionSyntax expression)
    {
        return ReadOptionalString(expression) ??
               throw new InvalidDataException(
                   "C# metadata requires a string literal.");
    }

    private static string? ReadOptionalString(
        ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return null;
        }

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        throw new InvalidDataException(
            $"C# metadata expression '{expression}' is not a supported string value.");
    }

    private static string ReadSimpleName(NameSyntax name)
    {
        return name is IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : throw new InvalidDataException(
                $"C# metadata name '{name}' must be a simple identifier.");
    }

    private static string ReadSimpleTypeName(TypeSyntax type)
    {
        return type is IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : throw new InvalidDataException(
                $"C# metadata type '{type}' must be a simple identifier.");
    }
}
