using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Meta.Architecture.Tests;

public sealed class ProductionMetadataAccessBoundaryTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void ProductionAssembliesUseMetadataOrCompiledAccessInsteadOfRuntimeReflection()
    {
        var root = FindRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var projects = Directory.GetDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith("Meta", StringComparison.Ordinal))
            .SelectMany(path => Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(IsExcludedDirectory))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(projects);

        var violations = new List<string>();
        foreach (var project in projects)
        {
            var definition = XDocument.Load(project);
            var assemblyName = definition.Descendants("AssemblyName").Select(item => item.Value).FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(project);
            var targetFramework = definition.Descendants("TargetFramework").Select(item => item.Value).FirstOrDefault()
                ?? throw new InvalidOperationException($"Missing TargetFramework in '{project}'.");
            var assemblyPath = Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, targetFramework, assemblyName + ".dll");
            Assert.True(File.Exists(assemblyPath), $"Build the production project before running this integration test: '{assemblyPath}'.");
            foreach (var call in ReadCalls(assemblyPath).Where(call => ViolatesBoundary(call.Type, call.Member)))
            {
                violations.Add($"{Path.GetRelativePath(root, project)}: {call.Type}.{call.Member}");
            }
        }

        Assert.True(violations.Count == 0,
            "Production must use Meta metadata or compiled access:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Distinct().Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void ArtifactInspectionDetectsForbiddenWitnessesAndAllowsTypeKeysAndDiagnosticNames()
    {
        // These methods are inspected as metadata; the reflection witnesses below are never executed.
        var calls = ReadCalls(Path.Combine(AppContext.BaseDirectory, "Meta.Architecture.Tests.dll"));
        var violations = calls.Where(call => ViolatesBoundary(call.Type, call.Member)).ToHashSet();

        Assert.Contains(("System.Type", "GetProperties"), violations);
        Assert.Contains(("System.Reflection.Assembly", "GetName"), violations);
        Assert.Contains(("System.Reflection.Assembly", "GetManifestResourceStream"), violations);
        Assert.Contains(("System.Activator", "CreateInstance"), violations);
        Assert.Contains(("System.Type", "GetTypeFromHandle"), calls);
        Assert.Contains(("System.Reflection.MemberInfo", "get_Name"), calls);
        Assert.DoesNotContain(("System.Type", "GetTypeFromHandle"), violations);
        Assert.DoesNotContain(("System.Reflection.MemberInfo", "get_Name"), violations);
    }

    private static HashSet<(string Type, string Member)> ReadCalls(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var calls = new HashSet<(string, string)>();
        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            if (reference.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)reference.Parent);
            calls.Add((metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name), metadata.GetString(reference.Name)));
        }
        return calls;
    }

    private static bool ViolatesBoundary(string type, string member)
    {
        // Type.Name is inherited from MemberInfo; diagnostic names do not discover model structure.
        if (type == "System.Reflection.MemberInfo" && member == "get_Name") return false;
        if (type.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal)) return true;
        if (type == "System.Activator" && member.StartsWith("CreateInstance", StringComparison.Ordinal)) return true;
        if (type == "System.Type")
            return member is "GetType" or "GetProperty" or "GetProperties" or "GetField" or "GetFields" or
                "GetMethod" or "GetMethods" or "GetMember" or "GetMembers" or "GetDefaultMembers" or
                "GetConstructor" or "GetConstructors" or "GetNestedType" or "GetNestedTypes" or "InvokeMember" or
                "GetGenericArguments" or "MakeGenericType" or "GetInterfaces" or "GetInterface" or
                "get_BaseType" or "get_IsClass" or "get_IsGenericType" or "GetGenericTypeDefinition" or "get_Assembly";
        if (type is "System.Reflection.Assembly" or "System.Reflection.AssemblyName" or
            "System.Reflection.MemberInfo" or "System.Reflection.PropertyInfo" or "System.Reflection.FieldInfo" or
            "System.Reflection.MethodBase" or "System.Reflection.MethodInfo" or "System.Reflection.ConstructorInfo" or
            "System.Reflection.EventInfo" or "System.Reflection.ParameterInfo" or "System.Reflection.TypeInfo") return true;
        if (type.StartsWith("System.Reflection.", StringComparison.Ordinal))
            return member is "GetValue" or "SetValue" or "Invoke" or "CreateDelegate" or
                "GetCustomAttribute" or "GetCustomAttributes" or "GetCustomAttributesData" or "GetTypeInfo" or
                "GetRuntimeProperties" or "GetRuntimeProperty" or "GetRuntimeFields" or "GetRuntimeField" or
                "GetRuntimeMethods" or "GetRuntimeMethod" or "GetRuntimeBaseDefinition" or "GetRuntimeInterfaceMap";
        if (type == "System.AppDomain" && member == "GetAssemblies") return true;
        if (type == "System.Runtime.Loader.AssemblyLoadContext" && member.StartsWith("Load", StringComparison.Ordinal)) return true;
        return type == "System.Delegate" && member == "DynamicInvoke";
    }

    private static bool IsExcludedDirectory(string segment) =>
        segment.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
        segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("obj", StringComparison.OrdinalIgnoreCase);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Metadata.Framework.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the meta repository root.");
    }

    private static object? ForbiddenReflectionWitness()
    {
        var type = typeof(ProductionMetadataAccessBoundaryTests);
        _ = type.GetProperties();
        var assembly = type.Assembly;
        _ = assembly.GetName().Version;
        using var resource = assembly.GetManifestResourceStream("boundary-witness");
        return Activator.CreateInstance(type);
    }

    private static string AllowedDiagnosticWitness(object value) => value.GetType().Name;
}
