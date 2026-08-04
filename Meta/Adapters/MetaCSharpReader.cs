using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meta.Adapters;

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

        return model;
    }

    private static GenericInstance ReadInstance(
        IReadOnlyCollection<CompilationUnitSyntax> roots,
        GenericModel model)
    {
        var factories = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>())
            .Where(method =>
                method.Identifier.ValueText == "CreateBuiltIn")
            .ToArray();
        if (factories.Length != 1 ||
            factories[0].Body == null)
        {
            throw new InvalidDataException(
                "C# metadata must declare one CreateBuiltIn method body.");
        }

        var method = factories[0];
        RequireBuiltInRoot(roots, method);
        var body = method.Body!;
        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        var collectionEntityByVariable =
            new Dictionary<string, GenericEntity>(
                StringComparer.Ordinal);

        foreach (var local in body.DescendantNodes()
                     .OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not
                    ObjectCreationExpressionSyntax creation ||
                    !TryReadListElementType(
                        creation.Type,
                        out var entityType))
                {
                    continue;
                }

                var entity = model.FindEntity(entityType) ??
                    throw new InvalidDataException(
                        $"C# collection '{variable.Identifier.ValueText}' uses unknown entity '{entityType}'.");
                if (!collectionEntityByVariable.TryAdd(
                        variable.Identifier.ValueText,
                        entity))
                {
                    throw new InvalidDataException(
                        $"C# collection variable '{variable.Identifier.ValueText}' is duplicated.");
                }

                ReadRecords(
                    creation,
                    entity,
                    instance.GetOrCreateEntityRecords(entity.Name));
            }
        }

        RequireReturnedCollections(
            method,
            model,
            collectionEntityByVariable);
        RejectUnsupportedCollectionUses(
            method,
            collectionEntityByVariable.Keys);
        ReadRelationshipAssignments(
            method,
            collectionEntityByVariable,
            instance);
        return instance;
    }

    private static void ReadRecords(
        ObjectCreationExpressionSyntax collectionCreation,
        GenericEntity entity,
        ICollection<GenericRecord> records)
    {
        if (collectionCreation.Initializer == null)
        {
            return;
        }

        foreach (var expression in
                 collectionCreation.Initializer.Expressions)
        {
            if (expression is not ObjectCreationExpressionSyntax recordCreation ||
                !MetaName.Comparer.Equals(
                    ReadSimpleTypeName(recordCreation.Type),
                    entity.Name) ||
                recordCreation.Initializer == null)
            {
                throw new InvalidDataException(
                    $"C# collection for '{entity.Name}' contains an unsupported initializer.");
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
                    MetaName.Comparer.Equals(
                        candidate.Name,
                        memberName)) ??
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
            if (records.Any(existing =>
                    MetaIdentity.Comparer.Equals(
                        existing.Id,
                        record.Id)))
            {
                throw new InvalidDataException(
                    $"C# entity '{entity.Name}' contains duplicate Id '{record.Id}'.");
            }

            records.Add(record);
        }
    }

    private static void ReadRelationshipAssignments(
        MethodDeclarationSyntax method,
        IReadOnlyDictionary<string, GenericEntity>
            collectionEntityByVariable,
        GenericInstance instance)
    {
        foreach (var assignment in method.Body!.DescendantNodes()
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (!TryReadRecordMember(
                    assignment.Left,
                    out var variableName,
                    out var recordIndex,
                    out var memberName))
            {
                continue;
            }

            if (assignment.Parent is not ExpressionStatementSyntax statement ||
                statement.Parent != method.Body)
            {
                throw new InvalidDataException(
                    "C# relationship assignments must be direct statements in CreateBuiltIn.");
            }

            if (!collectionEntityByVariable.TryGetValue(
                    variableName,
                    out var entity))
            {
                throw new InvalidDataException(
                    $"C# relationship assignment uses unknown collection '{variableName}'.");
            }

            var records = instance.RecordsByEntity[entity.Name];
            if (recordIndex < 0 || recordIndex >= records.Count)
            {
                throw new InvalidDataException(
                    $"C# relationship assignment for '{entity.Name}' uses invalid record index {recordIndex}.");
            }

            var relationship = entity.Relationships.FirstOrDefault(
                candidate => MetaName.Comparer.Equals(
                    candidate.GetNavigationName(),
                    memberName)) ??
                throw new InvalidDataException(
                    $"C# relationship assignment uses unknown navigation '{entity.Name}.{memberName}'.");
            var record = records[recordIndex];
            var relationshipName = relationship.GetColumnName();
            if (assignment.Right.IsKind(
                    SyntaxKind.NullLiteralExpression))
            {
                continue;
            }

            if (assignment.Right is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not IdentifierNameSyntax invokedName ||
                invokedName.Identifier.ValueText != "RequireTarget" ||
                invocation.ArgumentList.Arguments.Count < 2)
            {
                throw new InvalidDataException(
                    $"C# relationship assignment for '{entity.Name}.{memberName}' is unsupported.");
            }

            var targetId = MetaIdentity.Require(
                ReadString(
                    invocation.ArgumentList.Arguments[1].Expression),
                $"C# relationship '{entity.Name}.{relationshipName}' has invalid target Id.");
            if (!record.RelationshipIds.TryAdd(
                    relationshipName,
                    targetId))
            {
                throw new InvalidDataException(
                    $"C# relationship '{entity.Name}.{relationshipName}' is assigned more than once for record '{record.Id}'.");
            }
        }
    }

    private static void RequireBuiltInRoot(
        IReadOnlyCollection<CompilationUnitSyntax> roots,
        MethodDeclarationSyntax factory)
    {
        var fields = roots
            .SelectMany(root => root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>())
            .Where(field =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
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

    private static void RequireReturnedCollections(
        MethodDeclarationSyntax method,
        GenericModel model,
        IReadOnlyDictionary<string, GenericEntity>
            collectionEntityByVariable)
    {
        var returns = method.Body!.Statements
            .OfType<ReturnStatementSyntax>()
            .ToArray();
        if (returns.Length != 1 ||
            returns[0].Expression is not ObjectCreationExpressionSyntax root)
        {
            throw new InvalidDataException(
                "CreateBuiltIn must directly return one workspace instance.");
        }

        var returnedVariables = new HashSet<string>(StringComparer.Ordinal);
        var returnedEntities = new HashSet<string>(MetaName.Comparer);
        foreach (var argument in root.ArgumentList?.Arguments ?? default)
        {
            if (argument.Expression is not ObjectCreationExpressionSyntax collection ||
                collection.Type is not GenericNameSyntax generic ||
                generic.Identifier.ValueText != "ReadOnlyCollection" ||
                generic.TypeArgumentList.Arguments.Count != 1 ||
                collection.ArgumentList?.Arguments.Count != 1 ||
                collection.ArgumentList.Arguments[0].Expression is not
                    IdentifierNameSyntax variable)
            {
                throw new InvalidDataException(
                    "CreateBuiltIn must return entity lists as ReadOnlyCollection values.");
            }

            var variableName = variable.Identifier.ValueText;
            if (!collectionEntityByVariable.TryGetValue(
                    variableName,
                    out var entity) ||
                !MetaName.Comparer.Equals(
                    ReadSimpleTypeName(
                        generic.TypeArgumentList.Arguments[0]),
                    entity.Name) ||
                !returnedVariables.Add(variableName) ||
                !returnedEntities.Add(entity.Name))
            {
                throw new InvalidDataException(
                    $"CreateBuiltIn returns invalid entity collection '{variableName}'.");
            }
        }

        if (returnedVariables.Count != collectionEntityByVariable.Count ||
            returnedEntities.Count != model.Entities.Count)
        {
            throw new InvalidDataException(
                "CreateBuiltIn must return each entity collection exactly once.");
        }
    }

    private static void RejectUnsupportedCollectionUses(
        MethodDeclarationSyntax method,
        IEnumerable<string> collectionVariables)
    {
        var names = collectionVariables.ToHashSet(StringComparer.Ordinal);
        foreach (var identifier in method.Body!.DescendantNodes()
                     .OfType<IdentifierNameSyntax>()
                     .Where(identifier => names.Contains(
                         identifier.Identifier.ValueText)))
        {
            var allowed =
                identifier.Parent is ForEachStatementSyntax forEach &&
                forEach.Expression == identifier ||
                identifier.Parent is ElementAccessExpressionSyntax element &&
                element.Expression == identifier ||
                identifier.Parent is ArgumentSyntax argument &&
                argument.Expression == identifier &&
                argument.Parent?.Parent is ObjectCreationExpressionSyntax collection &&
                collection.Type is GenericNameSyntax generic &&
                generic.Identifier.ValueText == "ReadOnlyCollection";
            if (!allowed)
            {
                throw new InvalidDataException(
                    $"C# entity collection '{identifier.Identifier.ValueText}' is used in an unsupported expression.");
            }
        }
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

    private static bool TryReadRecordMember(
        ExpressionSyntax expression,
        out string variableName,
        out int recordIndex,
        out string memberName)
    {
        variableName = string.Empty;
        recordIndex = -1;
        memberName = string.Empty;
        if (expression is not MemberAccessExpressionSyntax member ||
            member.Expression is not ElementAccessExpressionSyntax element ||
            element.Expression is not IdentifierNameSyntax variable ||
            element.ArgumentList.Arguments.Count != 1 ||
            !int.TryParse(
                element.ArgumentList.Arguments[0].Expression.ToString(),
                out recordIndex))
        {
            return false;
        }

        variableName = variable.Identifier.ValueText;
        memberName = member.Name.Identifier.ValueText;
        return true;
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
