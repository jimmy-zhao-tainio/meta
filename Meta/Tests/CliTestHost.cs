using System.Diagnostics;

namespace Meta.Tests;

internal static class CliTestHost
{
    internal static string DotNetHost =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    internal static void AddAssemblyArgument(ProcessStartInfo startInfo, string assemblyName)
    {
        startInfo.ArgumentList.Add(ResolveAssemblyPath(assemblyName));
    }

    internal static string BuildArguments(string assemblyName, string arguments)
    {
        var assemblyPath = Quote(ResolveAssemblyPath(assemblyName));
        return string.IsNullOrWhiteSpace(arguments)
            ? assemblyPath
            : assemblyPath + " " + arguments;
    }

    private static string ResolveAssemblyPath(string assemblyName)
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"The test build did not produce the '{assemblyName}' CLI assembly at '{assemblyPath}'.",
                assemblyPath);
        }

        return assemblyPath;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
