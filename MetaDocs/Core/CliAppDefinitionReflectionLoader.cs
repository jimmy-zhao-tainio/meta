using System.Reflection;
using Meta.Core.Presentation.Cli;

namespace MetaDocs.Core;

public sealed class CliAppDefinitionReflectionLoader
{
    public CliAppDefinition Load(string assemblyPath, string typeName, string methodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("CLI definition assembly was not found.", fullPath);
        }

        var assembly = Assembly.LoadFrom(fullPath);
        var type = assembly.GetType(typeName, throwOnError: true)
                   ?? throw new InvalidOperationException($"Assembly '{fullPath}' does not contain type '{typeName}'.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                     ?? throw new InvalidOperationException($"Type '{typeName}' does not expose public static method '{methodName}'.");

        if (method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException($"Method '{typeName}.{methodName}' must not require parameters.");
        }

        var result = method.Invoke(null, null);
        return result as CliAppDefinition
               ?? throw new InvalidOperationException($"Method '{typeName}.{methodName}' must return {nameof(CliAppDefinition)}.");
    }
}
