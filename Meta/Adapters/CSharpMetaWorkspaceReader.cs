using System.Collections.Immutable;
using System.Text;
using Meta.Core.Operations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Meta.Adapters;

public sealed partial class CSharpMetaWorkspaceReader
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences =
        new(BuildPlatformReferences);

    public GenericMetadataState Read(string workspacePath)
    {
        return ReadDocument(workspacePath).State.Clone();
    }

    internal CSharpMetaWorkspaceDocument ReadDocument(string workspacePath)
    {
        var root = RequireWorkspacePath(workspacePath);
        var snapshot = CSharpMetaWorkspaceFiles.CaptureSnapshot(root);
        var sourceFiles = snapshot.Files
            .Where(file => string.Equals(
                Path.GetExtension(file.FullPath),
                ".cs",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            throw new InvalidDataException(
                $"C# workspace '{root}' does not contain any C# source files.");
        }

        var syntaxTrees = sourceFiles
            .Select(file => CSharpSyntaxTree.ParseText(
                DecodeSourceText(file.Contents),
                new CSharpParseOptions(
                    LanguageVersion.Latest,
                    DocumentationMode.Parse),
                file.FullPath))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "Meta.CSharpWorkspace",
            syntaxTrees,
            PlatformReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));

        ThrowOnCompilationErrors(root, compilation);
        var sourceMap = FindSourceMap(compilation);
        var modelMap = ReadModel(sourceMap);
        RequireClosedSourceContract(sourceMap, modelMap);
        var instance = ReadInstance(sourceMap, modelMap);
        var state = new GenericMetadataState(modelMap.Model, instance);
        _ = new MetaOperationInterpreter().Apply(
            state,
            MetaOperationPlan.Empty);

        return new CSharpMetaWorkspaceDocument(
            root,
            state,
            snapshot.Fingerprint);
    }

    private static SourceText DecodeSourceText(byte[] contents)
    {
        using var stream = new MemoryStream(
            contents,
            writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return SourceText.From(text, reader.CurrentEncoding);
    }

    private static CSharpMetaSourceMap FindSourceMap(
        CSharpCompilation compilation)
    {
        var builtInProperties = compilation.SyntaxTrees
            .SelectMany(tree =>
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                return tree.GetRoot()
                    .DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .Select(syntax => semanticModel.GetDeclaredSymbol(syntax))
                    .OfType<IPropertySymbol>();
            })
            .Where(property =>
                property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                string.Equals(
                    property.Name,
                    "BuiltIn",
                    StringComparison.Ordinal) &&
                property.Type is INamedTypeSymbol)
            .ToArray();
        if (builtInProperties.Length != 1)
        {
            throw new InvalidDataException(
                $"A C# Meta workspace must expose exactly one public static BuiltIn property; found {builtInProperties.Length}.");
        }

        var builtIn = builtInProperties[0];
        var rootType = builtIn.ContainingType;
        var instanceType = (INamedTypeSymbol)builtIn.Type;
        if (!rootType.IsStatic ||
            rootType.DeclaredAccessibility != Accessibility.Public ||
            rootType.ContainingType != null ||
            !rootType.Locations.Any(location => location.IsInSource))
        {
            throw new InvalidDataException(
                $"C# Meta workspace root '{rootType.Name}' must be a public static source class.");
        }

        var namespaceName = rootType.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new InvalidDataException(
                $"C# Meta workspace root '{rootType.Name}' must be declared in a named namespace.");
        }

        var builtInSyntax = RequireSingleDeclaration<PropertyDeclarationSyntax>(
            builtIn,
            $"BuiltIn property '{rootType.Name}.{builtIn.Name}'");
        if (builtInSyntax.ExpressionBody == null)
        {
            throw new InvalidDataException(
                $"C# Meta property '{rootType.Name}.{builtIn.Name}' must return its backing field directly.");
        }

        var builtInSemanticModel = compilation.GetSemanticModel(
            builtInSyntax.SyntaxTree);
        if (builtInSemanticModel.GetOperation(
                builtInSyntax.ExpressionBody.Expression) is not
            IFieldReferenceOperation builtInFieldReference)
        {
            throw new InvalidDataException(
                $"C# Meta property '{rootType.Name}.{builtIn.Name}' must return its backing field directly.");
        }

        var builtInField = builtInFieldReference.Field;
        if (!builtInField.IsStatic ||
            !builtInField.IsReadOnly ||
            !SymbolEqualityComparer.Default.Equals(
                builtInField.ContainingType,
                rootType) ||
            !SymbolEqualityComparer.Default.Equals(
                builtInField.Type,
                instanceType))
        {
            throw new InvalidDataException(
                $"C# Meta property '{rootType.Name}.{builtIn.Name}' must return a static readonly field of type '{instanceType.Name}'.");
        }

        var fieldSyntax = RequireSingleDeclaration<VariableDeclaratorSyntax>(
            builtInField,
            $"BuiltIn field '{rootType.Name}.{builtInField.Name}'");
        if (fieldSyntax.Initializer == null)
        {
            throw new InvalidDataException(
                $"C# Meta field '{rootType.Name}.{builtInField.Name}' must invoke the BuiltIn factory.");
        }

        var fieldSemanticModel = compilation.GetSemanticModel(
            fieldSyntax.SyntaxTree);
        if (fieldSemanticModel.GetOperation(
                fieldSyntax.Initializer.Value) is not
            IInvocationOperation factoryInvocation)
        {
            throw new InvalidDataException(
                $"C# Meta field '{rootType.Name}.{builtInField.Name}' must invoke the BuiltIn factory.");
        }

        var factoryMethod = factoryInvocation.TargetMethod;
        if (!factoryMethod.IsStatic ||
            factoryMethod.Parameters.Length != 0 ||
            !string.Equals(
                factoryMethod.Name,
                "CreateBuiltIn",
                StringComparison.Ordinal) ||
            !SymbolEqualityComparer.Default.Equals(
                factoryMethod.ReturnType,
                instanceType) ||
            factoryMethod.ContainingType.ContainingType != null ||
            !SymbolEqualityComparer.Default.Equals(
                factoryMethod.ContainingNamespace,
                rootType.ContainingNamespace))
        {
            throw new InvalidDataException(
                $"C# Meta field '{rootType.Name}.{builtInField.Name}' must invoke one static CreateBuiltIn method returning '{instanceType.Name}'.");
        }

        var factorySyntax = RequireSingleDeclaration<MethodDeclarationSyntax>(
            factoryMethod,
            $"BuiltIn factory '{factoryMethod.ContainingType.Name}.{factoryMethod.Name}'");
        var factorySemanticModel = compilation.GetSemanticModel(
            factorySyntax.SyntaxTree);

        return new CSharpMetaSourceMap(
            compilation,
            rootType,
            instanceType,
            builtInField,
            factoryMethod,
            factorySyntax,
            factorySemanticModel,
            namespaceName);
    }

    private static TSyntax RequireSingleDeclaration<TSyntax>(
        ISymbol symbol,
        string subject)
        where TSyntax : SyntaxNode
    {
        var declarations = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TSyntax>()
            .ToArray();
        if (declarations.Length != 1)
        {
            throw new InvalidDataException(
                $"{subject} must have exactly one source declaration; found {declarations.Length}.");
        }

        return declarations[0];
    }

    private static string RequireWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException(
                "C# workspace path is required.",
                nameof(workspacePath));
        }

        var root = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"C# workspace '{root}' was not found.");
        }

        return root;
    }

    private static void ThrowOnCompilationErrors(
        string root,
        CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(10)
            .Select(diagnostic => FormatDiagnostic(root, diagnostic))
            .ToArray();
        if (errors.Length == 0)
        {
            return;
        }

        throw new InvalidDataException(
            "C# workspace does not compile. " + string.Join(" | ", errors));
    }

    private static string FormatDiagnostic(
        string root,
        Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return $"{diagnostic.Id}: {diagnostic.GetMessage()}";
        }

        var span = diagnostic.Location.GetLineSpan();
        var path = Path.GetRelativePath(root, span.Path);
        return $"{path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}) {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    private static ImmutableArray<MetadataReference> BuildPlatformReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray() ?? [];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException(
                "The .NET platform assembly list is unavailable.");
        }

        return paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    internal sealed record CSharpMetaSourceMap(
        CSharpCompilation Compilation,
        INamedTypeSymbol RootType,
        INamedTypeSymbol InstanceType,
        IFieldSymbol BuiltInField,
        IMethodSymbol FactoryMethod,
        MethodDeclarationSyntax FactorySyntax,
        SemanticModel FactorySemanticModel,
        string ModelName);

    internal sealed record CSharpMetaWorkspaceDocument(
        string WorkspacePath,
        GenericMetadataState State,
        string Fingerprint);
}
