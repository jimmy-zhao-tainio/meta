using System.Runtime.InteropServices;
using Meta.Core.Presentation;
using Microsoft.Win32;

var presenter = new ConsolePresenter();

if (args.Length > 0 && IsHelpToken(args[0]))
{
    PrintHelp(presenter);
    return 0;
}

try
{
    var installerDir = AppContext.BaseDirectory;
    var payloadBinDir = Path.Combine(installerDir, "payload", "meta", "bin");

    if (!Directory.Exists(payloadBinDir))
    {
        presenter.WriteFailure(
            "Cannot install meta CLIs.",
            new[]
            {
                $"Payload directory was not found: {payloadBinDir}",
                "Next: package install-meta.exe with payload\\meta\\bin beside it."
            });
        return 1;
    }

    var tools = new[]
    {
        new ToolSpec("meta.exe"),
        new ToolSpec("meta-weave.exe"),
        new ToolSpec("meta-docs.exe"),
    };

    var missing = tools
        .Where(tool => !File.Exists(Path.Combine(payloadBinDir, tool.FileName)))
        .ToArray();
    if (missing.Length > 0)
    {
        presenter.WriteFailure(
            "Cannot install meta CLIs.",
            missing.Select(tool => $"Missing: {tool.FileName}")
                .Concat(new[]
                {
                    $"Payload: {payloadBinDir}",
                    "Next: package install-meta.exe with payload\\meta\\bin containing required binaries."
                }));
        return 1;
    }

    var targetDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "meta",
        "bin");

    Directory.CreateDirectory(targetDir);
    CopyPayloadRecursively(payloadBinDir, targetDir);

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
}
catch (Exception ex)
{
    presenter.WriteFailure(
        "Cannot install meta CLIs.",
        new[]
        {
            ex.Message,
            "Next: verify the installer payload and that the user PATH can be updated."
        });
    return 1;
}

static void PrintHelp(ConsolePresenter presenter)
{
    presenter.WriteUsage("install-meta.exe");
    presenter.WriteInfo(string.Empty);
    presenter.WriteInfo("Notes:");
    presenter.WriteInfo("  Installs meta.exe, meta-weave.exe, and meta-docs.exe into %LOCALAPPDATA%\\meta\\bin.");
    presenter.WriteInfo("  Adds that directory to the user PATH if it is missing.");
    presenter.WriteInfo("  Copies all files from payload\\meta\\bin beside install-meta.exe.");
    presenter.WriteNext("keep install-meta.exe and payload\\meta\\bin together");
}

static bool IsHelpToken(string value)
{
    return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
}

static void CopyPayloadRecursively(string sourceDirectory, string targetDirectory)
{
    foreach (var sourcePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
        var targetPath = Path.Combine(targetDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }
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

internal sealed record ToolSpec(string FileName);

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
