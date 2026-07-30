using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meta.Adapters;

public sealed partial class CSharpMetaWorkspaceReader
{
    private static void RequireClosedSourceContract(
        CSharpMetaSourceMap source,
        CSharpModelMap modelMap)
    {
        var factoryType = source.FactoryMethod.ContainingType;
        var expectedTypes = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default)
        {
            source.RootType,
            source.InstanceType,
            factoryType,
        };
        expectedTypes.UnionWith(
            modelMap.EntitiesByName.Values.Select(entity => entity.Type));

        var declaredTypes = source.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot()
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Select(declaration =>
                    source.Compilation.GetSemanticModel(tree)
                        .GetDeclaredSymbol(declaration))
                .OfType<INamedTypeSymbol>())
            .Concat(
                source.Compilation.SyntaxTrees.SelectMany(tree =>
                    tree.GetRoot()
                        .DescendantNodes()
                        .OfType<DelegateDeclarationSyntax>()
                        .Select(declaration =>
                            source.Compilation.GetSemanticModel(tree)
                                .GetDeclaredSymbol(declaration))
                        .OfType<INamedTypeSymbol>()))
            .ToHashSet(SymbolEqualityComparer.Default);
        var unexpectedType = declaredTypes.FirstOrDefault(type =>
            !expectedTypes.Any(expected =>
                SymbolEqualityComparer.Default.Equals(expected, type)));
        if (unexpectedType != null)
        {
            throw new InvalidDataException(
                $"C# Meta workspace contains unsupported type '{unexpectedType.ToDisplayString()}'.");
        }

        foreach (var type in expectedTypes)
        {
            if (type.DeclaringSyntaxReferences.Length != 1)
            {
                throw new InvalidDataException(
                    $"C# Meta type '{type.Name}' must have exactly one source declaration.");
            }
        }

        if (source.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot()
                .DescendantNodes()
                .OfType<AttributeSyntax>())
            .Any())
        {
            throw new InvalidDataException(
                "C# Meta workspaces do not permit attributes.");
        }

        if (source.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot()
                .DescendantNodes()
                .OfType<GlobalStatementSyntax>())
            .Any())
        {
            throw new InvalidDataException(
                "C# Meta workspaces do not permit global statements.");
        }

        var rootMembers = new HashSet<ISymbol>(
            SymbolEqualityComparer.Default)
        {
            source.BuiltInField,
        };
        rootMembers.UnionWith(source.RootType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property =>
                string.Equals(
                    property.Name,
                    "BuiltIn",
                    StringComparison.Ordinal) ||
                modelMap.EntitiesByName.Values.Any(entity =>
                    string.Equals(
                        entity.CollectionProperty.Name,
                        property.Name,
                        StringComparison.Ordinal))));
        RequireOnlyMembers(source.RootType, rootMembers);

        var instanceMembers = new HashSet<ISymbol>(
            modelMap.EntitiesByName.Values
                .Select(entity => (ISymbol)entity.CollectionProperty),
            SymbolEqualityComparer.Default);
        var instanceConstructors = source.InstanceType.InstanceConstructors
            .Where(constructor => !constructor.IsImplicitlyDeclared)
            .ToArray();
        if (instanceConstructors.Length != 1)
        {
            throw new InvalidDataException(
                $"C# Meta instance '{source.InstanceType.Name}' must declare exactly one constructor.");
        }

        instanceMembers.Add(instanceConstructors[0]);
        RequireOnlyMembers(source.InstanceType, instanceMembers);

        var factoryMembers = new HashSet<ISymbol>(
            SymbolEqualityComparer.Default)
        {
            source.FactoryMethod,
        };
        var targetHelpers = factoryType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method =>
                !method.IsImplicitlyDeclared &&
                string.Equals(
                    method.Name,
                    "RequireTarget",
                    StringComparison.Ordinal))
            .ToArray();
        if (targetHelpers.Length > 1)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{factoryType.Name}' declares more than one RequireTarget helper.");
        }

        foreach (var helper in targetHelpers)
        {
            RequireTargetHelper(source, helper);
            factoryMembers.Add(helper);
        }

        RequireOnlyMembers(factoryType, factoryMembers);

        foreach (var entity in modelMap.EntitiesByName.Values)
        {
            var entityMembers = new HashSet<ISymbol>(
                SymbolEqualityComparer.Default)
            {
                entity.IdProperty,
            };
            entityMembers.UnionWith(entity.ScalarProperties.Values);
            entityMembers.UnionWith(entity.RelationshipProperties.Values);
            RequireOnlyMembers(entity.Type, entityMembers);
        }
    }

    private static void RequireOnlyMembers(
        INamedTypeSymbol type,
        IReadOnlySet<ISymbol> allowedMembers)
    {
        var unexpected = type.GetMembers()
            .FirstOrDefault(member =>
                !member.IsImplicitlyDeclared &&
                !allowedMembers.Contains(member) &&
                (member is not IMethodSymbol method ||
                 method.AssociatedSymbol == null ||
                 !allowedMembers.Contains(method.AssociatedSymbol)));
        if (unexpected != null)
        {
            throw new InvalidDataException(
                $"C# Meta type '{type.Name}' contains unsupported member '{unexpected.Name}'.");
        }
    }
}
