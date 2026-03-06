using System.Runtime.InteropServices;
using Microsoft.Win32;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory, "Metadata.Framework.sln");
if (repoRoot is null)
{
    WriteError("Could not locate the repository root.");
    return 1;
}

var tools = new[]
{
    new ToolSpec("meta.exe", Path.Combine(repoRoot, "Meta.Cli", "bin", "publish", "win-x64", "meta.exe")),
    new ToolSpec("meta-weave.exe", Path.Combine(repoRoot, "MetaWeave.Cli", "bin", "publish", "win-x64", "meta-weave.exe")),
};

var missing = tools.Where(tool => !File.Exists(tool.SourcePath)).ToArray();
if (missing.Length > 0)
{
    WriteError("Required published CLI binaries are missing.");
    foreach (var tool in missing)
    {
        Console.WriteLine($"Missing: {tool.SourcePath}");
    }

    Console.WriteLine();
    Console.WriteLine("Build the foundation CLIs first:");
    Console.WriteLine("  dotnet build Metadata.Framework.sln");
    Console.WriteLine("  dotnet build MetaWeave.sln");
    return 1;
}

var targetDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "meta",
    "bin");

Directory.CreateDirectory(targetDir);

foreach (var tool in tools)
{
    File.Copy(tool.SourcePath, Path.Combine(targetDir, tool.FileName), overwrite: true);
}

EnsureUserPathContains(targetDir);
BroadcastEnvironmentChange();

Console.WriteLine("Installed:");
foreach (var tool in tools)
{
    Console.WriteLine($"  {Path.Combine(targetDir, tool.FileName)}");
}

Console.WriteLine();
Console.WriteLine("Restart cmd to pick up PATH changes.");
return 0;

static string? FindRepoRoot(string startDirectory, string markerFileName)
{
    var current = new DirectoryInfo(startDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, markerFileName)))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}

static void EnsureUserPathContains(string targetDir)
{
    using var environmentKey = Registry.CurrentUser.OpenSubKey("Environment", writable: true)
        ?? throw new InvalidOperationException("Could not open HKCU\\Environment.");

    var currentPath = environmentKey.GetValue("Path") as string ?? string.Empty;
    var segments = currentPath
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    if (!segments.Any(segment => string.Equals(segment, targetDir, StringComparison.OrdinalIgnoreCase)))
    {
        segments.Add(targetDir);
        environmentKey.SetValue("Path", string.Join(';', segments), RegistryValueKind.ExpandString);
    }
}

static void BroadcastEnvironmentChange()
{
    _ = NativeMethods.SendMessageTimeout(
        new IntPtr(0xffff),
        0x001A,
        IntPtr.Zero,
        "Environment",
        0x0002,
        5000,
        out _);
}

static void WriteError(string message)
{
    Console.Error.WriteLine($"Error: {message}");
}

internal sealed record ToolSpec(string FileName, string SourcePath);

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);
}
