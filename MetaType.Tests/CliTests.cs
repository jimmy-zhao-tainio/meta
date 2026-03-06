using System.Diagnostics;

namespace MetaType.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsInitCommand()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MetaType CLI", result.Output);
        Assert.Contains("init", result.Output);
    }

    [Fact]
    public void Init_Help_ShowsRequiredOptions()
    {
        var result = RunCli("init --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--new-workspace <path>", result.Output);
    }

    [Fact]
    public void Init_FailsWhenNewWorkspaceMissing_AndDoesNotCreateTargetDirectory()
    {
        var result = RunCli("init");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Command: init", result.Output);
    }

    [Fact]
    public void Init_FailsWhenUnknownOptionProvided()
    {
        var result = RunCli("init --bad nope");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error: unknown option '--bad'.", result.Output);
    }

    [Fact]
    public void Init_CreatesWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "metatype-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunCli($"init --new-workspace \"{workspacePath}\"");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: metatype workspace created", result.Output);
            Assert.True(File.Exists(Path.Combine(workspacePath, "workspace.xml")));
            Assert.True(File.Exists(Path.Combine(workspacePath, "metadata", "model.xml")));
            Assert.Contains("TypeSystems: 6", result.Output);
            Assert.Contains("Types:", result.Output);
            Assert.Contains("TypeSpecs:", result.Output);
            var typeXml = File.ReadAllText(Path.Combine(workspacePath, "metadata", "instance", "Type.xml"));
            Assert.Contains("sqlserver:type:nvarchar", typeXml);
        }
        finally
        {
            DeleteDirectoryIfExists(workspacePath);
        }
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = ResolveCliPath(repoRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = arguments,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return RunProcess(startInfo, "Could not start meta-type CLI process.");
    }

    private static (int ExitCode, string Output) RunProcess(ProcessStartInfo startInfo, string errorMessage)
    {
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(errorMessage);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                TryKillProcessTree(process);
                process.WaitForExit();
                throw new TimeoutException($"Timed out waiting for process: {startInfo.FileName} {startInfo.Arguments}", exception);
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout + stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKillProcessTree(process);
                process.WaitForExit();
            }
        }
    }

    private static string ResolveCliPath(string repoRoot)
    {
        var cliPath = Path.Combine(repoRoot, "MetaType.Cli", "bin", "Debug", "net8.0", "meta-type.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled MetaType CLI at '{cliPath}'. Build MetaType.Cli before running tests.");
        }

        return cliPath;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            var parent = Directory.GetParent(directory);
            if (parent == null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
