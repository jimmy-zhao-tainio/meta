using Meta.Core.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meta.Adapters;

public sealed partial class CSharpMetaWorkspaceReader
{
    private static CSharpModelMap ReadModel(
        CSharpMetaSourceMap source)
    {
        RequireInstanceType(source);
        var collectionProperties = source.InstanceType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                !property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                !property.IsIndexer)
            .Select(property => new
            {
                Property = property,
                ElementType = TryGetReadOnlyListElementType(property.Type),
            })
            .ToArray();
        var unsupportedCollectionProperty = collectionProperties
            .FirstOrDefault(item => item.ElementType == null);
        if (unsupportedCollectionProperty != null)
        {
            throw new InvalidDataException(
                $"C# Meta instance member '{source.InstanceType.Name}.{unsupportedCollectionProperty.Property.Name}' must be an IReadOnlyList<TEntity> collection.");
        }

        var duplicateEntityName = collectionProperties
            .GroupBy(
                item => item.ElementType!.Name,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateEntityName != null)
        {
            throw new InvalidDataException(
                $"C# Meta instance exposes entity '{duplicateEntityName.Key}' more than once.");
        }

        var entityTypes = collectionProperties
            .Select(item => item.ElementType!)
            .ToHashSet(SymbolEqualityComparer.Default);
        var model = new GenericModel
        {
            Name = source.ModelName,
        };
        var entitiesByName = new Dictionary<string, CSharpEntityMap>(
            StringComparer.OrdinalIgnoreCase);
        var entitiesByType = new Dictionary<INamedTypeSymbol, CSharpEntityMap>(
            SymbolEqualityComparer.Default);

        foreach (var collection in collectionProperties
                     .OrderBy(item => item.ElementType!.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.ElementType!.Name, StringComparer.Ordinal))
        {
            var entityType = collection.ElementType!;
            RequireInstanceCollectionProperty(
                source.InstanceType,
                collection.Property);
            RequireEntityType(source, entityType);
            if (!string.Equals(
                    collection.Property.Name,
                    entityType.Name + "List",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"C# Meta entity collection '{source.InstanceType.Name}.{collection.Property.Name}' must be named '{entityType.Name}List'.");
            }

            RequireRootCollection(source, collection.Property);

            var entity = new GenericEntity
            {
                Name = entityType.Name,
            };
            var publicProperties = entityType.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(property =>
                    !property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public &&
                    !property.IsIndexer)
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();
            foreach (var property in publicProperties)
            {
                RequireReadWriteProperty(entityType, property);
            }

            var idProperty = publicProperties.SingleOrDefault(property =>
                string.Equals(
                    property.Name,
                    "Id",
                    StringComparison.Ordinal));
            if (idProperty == null ||
                idProperty.Type.SpecialType != SpecialType.System_String ||
                IsNullable(idProperty))
            {
                throw new InvalidDataException(
                    $"C# Meta entity '{entity.Name}' must declare a required public string Id property.");
            }

            var scalarProperties = new Dictionary<string, IPropertySymbol>(
                StringComparer.OrdinalIgnoreCase);
            var scalarDefaults = new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);
            var relationshipProperties = new Dictionary<string, IPropertySymbol>(
                StringComparer.OrdinalIgnoreCase);
            var idDefault = ReadStringPropertyDefault(
                source.Compilation,
                idProperty);
            foreach (var property in publicProperties.Where(property =>
                         !SymbolEqualityComparer.Default.Equals(
                             property,
                             idProperty)))
            {
                if (property.Type.SpecialType == SpecialType.System_String)
                {
                    var scalar = new GenericProperty
                    {
                        Name = property.Name,
                        IsNullable = IsNullable(property),
                    };
                    entity.Properties.Add(scalar);
                    scalarProperties.Add(scalar.Name, property);
                    scalarDefaults.Add(
                        scalar.Name,
                        ReadStringPropertyDefault(
                            source.Compilation,
                            property));
                    continue;
                }

                if (property.Type is INamedTypeSymbol relationshipTarget &&
                    entityTypes.Contains(relationshipTarget))
                {
                    RequireNullPropertyDefault(
                        source.Compilation,
                        property);
                    var relationship = new GenericRelationship
                    {
                        Entity = relationshipTarget.Name,
                        Role = string.Equals(
                                property.Name,
                                relationshipTarget.Name,
                                StringComparison.Ordinal)
                            ? string.Empty
                            : property.Name,
                        IsNullable = IsNullable(property),
                    };
                    entity.Relationships.Add(relationship);
                    relationshipProperties.Add(
                        relationship.GetColumnName(),
                        property);
                    continue;
                }

                throw new InvalidDataException(
                    $"C# Meta member '{entity.Name}.{property.Name}' is neither a string property nor an entity relationship.");
            }

            model.Entities.Add(entity);
            var entityMap = new CSharpEntityMap(
                entity,
                entityType,
                collection.Property,
                idProperty,
                idDefault,
                scalarProperties,
                scalarDefaults,
                relationshipProperties);
            entitiesByName.Add(entity.Name, entityMap);
            entitiesByType.Add(entityType, entityMap);
        }

        return new CSharpModelMap(
            model,
            entitiesByName,
            entitiesByType);
    }

    private static void RequireInstanceType(
        CSharpMetaSourceMap source)
    {
        if (source.InstanceType.TypeKind != TypeKind.Class ||
            source.InstanceType.DeclaredAccessibility !=
            Accessibility.Public ||
            !source.InstanceType.IsSealed ||
            source.InstanceType.ContainingType != null ||
            source.InstanceType.BaseType?.SpecialType !=
            SpecialType.System_Object ||
            !source.InstanceType.Locations.Any(location =>
                location.IsInSource))
        {
            throw new InvalidDataException(
                $"C# Meta instance '{source.InstanceType.Name}' must be a top-level public sealed source class.");
        }

        if (!SymbolEqualityComparer.Default.Equals(
                source.InstanceType.ContainingNamespace,
                source.RootType.ContainingNamespace))
        {
            throw new InvalidDataException(
                $"C# Meta instance '{source.InstanceType.Name}' must share namespace '{source.ModelName}'.");
        }
    }

    private static INamedTypeSymbol? TryGetReadOnlyListElementType(
        ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol
            {
                Name: "IReadOnlyList",
                Arity: 1,
            } named ||
            !string.Equals(
                named.ContainingNamespace?.ToDisplayString(),
                "System.Collections.Generic",
                StringComparison.Ordinal))
        {
            return null;
        }

        return named.TypeArguments[0] as INamedTypeSymbol;
    }

    private static void RequireEntityType(
        CSharpMetaSourceMap source,
        INamedTypeSymbol entityType)
    {
        if (entityType.TypeKind != TypeKind.Class ||
            entityType.DeclaredAccessibility != Accessibility.Public ||
            !entityType.IsSealed ||
            entityType.ContainingType != null ||
            entityType.BaseType?.SpecialType !=
            SpecialType.System_Object ||
            entityType.InstanceConstructors.Any(constructor =>
                !constructor.IsImplicitlyDeclared) ||
            !entityType.Locations.Any(location => location.IsInSource))
        {
            throw new InvalidDataException(
                $"C# Meta entity '{entityType.Name}' must be a top-level public sealed source class with implicit construction.");
        }

        if (!SymbolEqualityComparer.Default.Equals(
                entityType.ContainingNamespace,
                source.RootType.ContainingNamespace))
        {
            throw new InvalidDataException(
                $"C# Meta entity '{entityType.Name}' must share namespace '{source.ModelName}'.");
        }
    }

    private static void RequireRootCollection(
        CSharpMetaSourceMap source,
        IPropertySymbol instanceCollection)
    {
        var rootCollections = source.RootType.GetMembers(instanceCollection.Name)
            .OfType<IPropertySymbol>()
            .Where(property =>
                property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                SymbolEqualityComparer.Default.Equals(
                    property.Type,
                    instanceCollection.Type))
            .ToArray();
        if (rootCollections.Length != 1)
        {
            throw new InvalidDataException(
                $"C# Meta root '{source.RootType.Name}' must expose static collection '{instanceCollection.Name}'.");
        }

        var rootCollection = rootCollections[0];
        var syntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            rootCollection,
            $"C# Meta root collection '{source.RootType.Name}.{rootCollection.Name}'");
        if (syntax.ExpressionBody == null)
        {
            throw new InvalidDataException(
                $"C# Meta root collection '{source.RootType.Name}.{rootCollection.Name}' must return the BuiltIn collection directly.");
        }

        var semanticModel = source.Compilation.GetSemanticModel(
            syntax.SyntaxTree);
        if (semanticModel.GetOperation(syntax.ExpressionBody.Expression) is not
            IPropertyReferenceOperation
            {
                Instance: IFieldReferenceOperation fieldReference,
            } collectionReference ||
            !SymbolEqualityComparer.Default.Equals(
                collectionReference.Property,
                instanceCollection) ||
            !SymbolEqualityComparer.Default.Equals(
                fieldReference.Field,
                source.BuiltInField))
        {
            throw new InvalidDataException(
                $"C# Meta root collection '{source.RootType.Name}.{rootCollection.Name}' must return the matching BuiltIn collection.");
        }
    }

    private static void RequireReadWriteProperty(
        INamedTypeSymbol entityType,
        IPropertySymbol property)
    {
        if (property.GetMethod?.DeclaredAccessibility != Accessibility.Public ||
            property.SetMethod?.DeclaredAccessibility != Accessibility.Public)
        {
            throw new InvalidDataException(
                $"C# Meta member '{entityType.Name}.{property.Name}' must have public get and set accessors.");
        }

        var syntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            property,
            $"C# Meta member '{entityType.Name}.{property.Name}'");
        var accessors = syntax.AccessorList?.Accessors;
        if (accessors == null ||
            accessors.Value.Count != 2 ||
            accessors.Value.Count(accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration) &&
                accessor.Body == null &&
                accessor.ExpressionBody == null) != 1 ||
            accessors.Value.Count(accessor =>
                accessor.IsKind(SyntaxKind.SetAccessorDeclaration) &&
                accessor.Body == null &&
                accessor.ExpressionBody == null) != 1)
        {
            throw new InvalidDataException(
                $"C# Meta member '{entityType.Name}.{property.Name}' must be an automatic get/set property.");
        }
    }

    private static void RequireInstanceCollectionProperty(
        INamedTypeSymbol instanceType,
        IPropertySymbol property)
    {
        if (property.GetMethod?.DeclaredAccessibility !=
                Accessibility.Public ||
            property.SetMethod != null)
        {
            throw new InvalidDataException(
                $"C# Meta collection '{instanceType.Name}.{property.Name}' must be a public get-only property.");
        }

        var syntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            property,
            $"C# Meta collection '{instanceType.Name}.{property.Name}'");
        var accessors = syntax.AccessorList?.Accessors;
        if (accessors == null ||
            accessors.Value.Count != 1 ||
            !accessors.Value[0].IsKind(
                SyntaxKind.GetAccessorDeclaration) ||
            accessors.Value[0].Body != null ||
            accessors.Value[0].ExpressionBody != null)
        {
            throw new InvalidDataException(
                $"C# Meta collection '{instanceType.Name}.{property.Name}' must be an automatic get-only property.");
        }
    }

    private static string? ReadStringPropertyDefault(
        CSharpCompilation compilation,
        IPropertySymbol property)
    {
        var syntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            property,
            $"C# Meta member '{property.ContainingType.Name}.{property.Name}'");
        if (syntax.Initializer == null)
        {
            return null;
        }

        var semanticModel = compilation.GetSemanticModel(
            syntax.SyntaxTree);
        var operation = semanticModel.GetOperation(
            syntax.Initializer.Value);
        if (operation != null &&
            TryReadConstantString(operation, out var constant))
        {
            return constant;
        }

        if (operation != null &&
            Unwrap(operation) is IFieldReferenceOperation fieldReference &&
            fieldReference.Field.IsStatic &&
            fieldReference.Field.Name == nameof(string.Empty) &&
            fieldReference.Field.ContainingType.SpecialType ==
            SpecialType.System_String)
        {
            return string.Empty;
        }

        throw new InvalidDataException(
            $"C# Meta string member '{property.ContainingType.Name}.{property.Name}' must have no initializer or a statically known string initializer.");
    }

    private static void RequireNullPropertyDefault(
        CSharpCompilation compilation,
        IPropertySymbol property)
    {
        var syntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            property,
            $"C# Meta relationship '{property.ContainingType.Name}.{property.Name}'");
        if (syntax.Initializer == null)
        {
            return;
        }

        var semanticModel = compilation.GetSemanticModel(
            syntax.SyntaxTree);
        var constant = semanticModel.GetConstantValue(
            syntax.Initializer.Value);
        if (constant.HasValue &&
            constant.Value == null)
        {
            return;
        }

        throw new InvalidDataException(
            $"C# Meta relationship '{property.ContainingType.Name}.{property.Name}' may only have a null initializer.");
    }

    private static bool IsNullable(IPropertySymbol property)
    {
        return property.NullableAnnotation switch
        {
            NullableAnnotation.Annotated => true,
            NullableAnnotation.NotAnnotated => false,
            _ => throw new InvalidDataException(
                $"C# Meta member '{property.ContainingType.Name}.{property.Name}' must declare nullable intent under an enabled nullable context."),
        };
    }

    internal sealed record CSharpModelMap(
        GenericModel Model,
        IReadOnlyDictionary<string, CSharpEntityMap> EntitiesByName,
        IReadOnlyDictionary<INamedTypeSymbol, CSharpEntityMap> EntitiesByType);

    internal sealed record CSharpEntityMap(
        GenericEntity Entity,
        INamedTypeSymbol Type,
        IPropertySymbol CollectionProperty,
        IPropertySymbol IdProperty,
        string? IdDefault,
        IReadOnlyDictionary<string, IPropertySymbol> ScalarProperties,
        IReadOnlyDictionary<string, string?> ScalarDefaults,
        IReadOnlyDictionary<string, IPropertySymbol> RelationshipProperties);
}
