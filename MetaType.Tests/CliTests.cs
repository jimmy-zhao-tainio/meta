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
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.Combine(repoRoot, "MetaType.Cli", "MetaType.Cli.csproj")}\" -- {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-type CLI process.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
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
