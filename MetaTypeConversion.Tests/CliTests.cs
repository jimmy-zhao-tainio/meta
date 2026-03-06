using System.Diagnostics;

namespace MetaTypeConversion.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsInitCommand()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MetaTypeConversion CLI", result.Output);
        Assert.Contains("init", result.Output);
        Assert.Contains("check", result.Output);
        Assert.Contains("resolve", result.Output);
    }

    [Fact]
    public void Init_Help_ShowsRequiredOptions()
    {
        var result = RunCli("init --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--new-workspace <path>", result.Output);
    }

    [Fact]
    public void Init_CreatesWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "metatypeconversion-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunCli($"init --new-workspace \"{workspacePath}\"");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: metatypeconversion workspace created", result.Output);
            Assert.True(File.Exists(Path.Combine(workspacePath, "workspace.xml")));
            Assert.True(File.Exists(Path.Combine(workspacePath, "metadata", "model.xml")));
            Assert.Contains("ConversionImplementations:", result.Output);
            Assert.Contains("TypeMappings:", result.Output);
            Assert.DoesNotContain("ConversionImplementations: 0", result.Output);
            Assert.DoesNotContain("TypeMappings: 0", result.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(workspacePath);
        }
    }

    [Fact]
    public void Check_ValidatesSeededWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "metatypeconversion-check-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var init = RunCli($"init --new-workspace \"{workspacePath}\"");
            Assert.Equal(0, init.ExitCode);

            var check = RunCli($"check --workspace \"{workspacePath}\"");
            Assert.Equal(0, check.ExitCode);
            Assert.Contains("OK: metatypeconversion check", check.Output);
            Assert.Contains("Errors: 0", check.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(workspacePath);
        }
    }

    [Fact]
    public void Resolve_ReturnsTargetTypeAndImplementation()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "metatypeconversion-resolve-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var init = RunCli($"init --new-workspace \"{workspacePath}\"");
            Assert.Equal(0, init.ExitCode);

            var resolve = RunCli($"resolve --workspace \"{workspacePath}\" --source-type sqlserver:type:nvarchar");
            Assert.Equal(0, resolve.ExitCode);
            Assert.Contains("OK: metatypeconversion resolve", resolve.Output);
            Assert.Contains("SourceTypeId: sqlserver:type:nvarchar", resolve.Output);
            Assert.Contains("TargetTypeId: meta:type:String", resolve.Output);
            Assert.Contains("ConversionImplementation: Direct", resolve.Output);
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

        return RunProcess(startInfo, "Could not start meta-type-conversion CLI process.");
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
        var cliPath = Path.Combine(repoRoot, "MetaTypeConversion.Cli", "bin", "Debug", "net8.0", "meta-type-conversion.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled MetaTypeConversion CLI at '{cliPath}'. Build MetaTypeConversion.Cli before running tests.");
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
