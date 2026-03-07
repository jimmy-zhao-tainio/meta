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
    new ToolSpec("meta.exe", Path.Combine(repoRoot, "Meta.Cli", "bin", "publish", "win-x64", "meta.exe")),
    new ToolSpec("meta-weave.exe", Path.Combine(repoRoot, "MetaWeave.Cli", "bin", "publish", "win-x64", "meta-weave.exe")),
};

var missing = tools.Where(tool => !File.Exists(tool.SourcePath)).ToArray();
if (missing.Length > 0)
{
    presenter.WriteFailure(
        "required published CLI binaries are missing.",
        missing.Select(tool => $"  Missing: {tool.SourcePath}")
            .Concat(new[]
            {
                "Next: dotnet build Metadata.Framework.sln",
                "Next: dotnet build MetaWeave.sln"
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
    File.Copy(tool.SourcePath, Path.Combine(targetDir, tool.FileName), overwrite: true);
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
    presenter.WriteInfo("  Installs meta.exe and meta-weave.exe into %LOCALAPPDATA%\\meta\\bin.");
    presenter.WriteInfo("  Adds that directory to the user PATH if it is missing.");
    presenter.WriteInfo("  Expects published binaries under the current meta checkout.");
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

