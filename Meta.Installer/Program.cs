using System.Runtime.InteropServices;
using Meta.Core.Presentation;
using Microsoft.Win32;

var presenter = new ConsolePresenter();

if (args.Length > 0 && IsHelpToken(args[0]))
{
    PrintHelp(presenter);
    return 0;
}

var repoRoot = FindRepoRoot(AppContext.BaseDirectory, "Metadata.Framework.sln");
if (repoRoot is null)
{
    presenter.WriteFailure("could not locate the repository root.", new[] { "Next: run install-meta.exe from a built meta checkout." });
    return 1;
}

var tools = new[]
{
    new ToolSpec("meta.exe", ResolveBuiltToolPath(repoRoot, "Meta.Cli", "meta.exe")),
    new ToolSpec("meta-weave.exe", ResolveBuiltToolPath(repoRoot, "MetaWeave.Cli", "meta-weave.exe")),
    new ToolSpec("meta-fabric.exe", ResolveBuiltToolPath(repoRoot, "MetaFabric.Cli", "meta-fabric.exe")),
};

var missing = tools.Where(tool => tool.SourcePath is null).ToArray();
if (missing.Length > 0)
{
    presenter.WriteFailure(
        "required CLI binaries are missing.",
        missing.Select(tool => $"  Missing: {tool.FileName}")
            .Concat(new[]
            {
                "Next: dotnet build Metadata.Framework.sln",
                "Next: dotnet build MetaWeave.sln",
                "Next: dotnet build MetaFabric.sln"
            }));
    return 1;
}

var targetDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "meta",
    "bin");

Directory.CreateDirectory(targetDir);

foreach (var tool in tools)
{
    File.Copy(tool.SourcePath!, Path.Combine(targetDir, tool.FileName), overwrite: true);
}

EnsureUserPathContains(targetDir);
BroadcastEnvironmentChange();

presenter.WriteOk(
    "install meta",
    ("Target", targetDir),
    ("Tools", tools.Length.ToString()));
Console.WriteLine();
presenter.WriteKeyValueBlock(
    "Installed",
    tools.Select(tool => (tool.FileName, Path.Combine(targetDir, tool.FileName))));
presenter.WriteNext("restart cmd to pick up PATH changes");
return 0;

static void PrintHelp(ConsolePresenter presenter)
{
    presenter.WriteUsage("install-meta.exe");
    presenter.WriteInfo(string.Empty);
    presenter.WriteInfo("Notes:");
    presenter.WriteInfo("  Installs meta.exe, meta-weave.exe, and meta-fabric.exe into %LOCALAPPDATA%\\meta\\bin.");
    presenter.WriteInfo("  Adds that directory to the user PATH if it is missing.");
    presenter.WriteInfo("  Uses the newest available built binary from the current meta checkout.");
    presenter.WriteNext("dotnet build Metadata.Framework.sln");
}

static bool IsHelpToken(string value)
{
    return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
}

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

static string? ResolveBuiltToolPath(string repoRoot, string projectDirectory, string fileName)
{
    var candidates = new[]
    {
        Path.Combine(repoRoot, projectDirectory, "bin", "publish", "win-x64", fileName),
        Path.Combine(repoRoot, projectDirectory, "bin", "Debug", "net8.0", fileName),
        Path.Combine(repoRoot, projectDirectory, "bin", "Release", "net8.0", fileName)
    };

    return candidates
        .Where(File.Exists)
        .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
        .FirstOrDefault();
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

internal sealed record ToolSpec(string FileName, string? SourcePath);

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
