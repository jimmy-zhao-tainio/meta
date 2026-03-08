using System.Diagnostics;

namespace MetaFabric.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsAuthoringAndSuggestCommands()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-fabric <command>", result.Output);
        Assert.Contains("add-weave", result.Output);
        Assert.Contains("add-binding", result.Output);
        Assert.Contains("add-scope", result.Output);
        Assert.Contains("suggest", result.Output);
        Assert.Contains("check", result.Output);
    }

    [Fact]
    public void Suggest_Help_ShowsPrintCommandsOption()
    {
        var result = RunCli("suggest --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace <path>", result.Output);
        Assert.Contains("--print-commands", result.Output);
    }

    [Fact]
    public void Suggest_SanctionedUnscopedFabric_PrintsScopeSuggestion()
    {
        var result = RunCli($"suggest --workspace \"{GetFixtureWorkspacePath("Fabric-Suggest-Scoped-Group-CategoryItem")}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: fabric suggest", result.Output);
        Assert.Contains("Suggestions: 1", result.Output);
        Assert.Contains("WeakSuggestions: 0", result.Output);
        Assert.Contains("ChildItem -> ParentGroup", result.Output);
        Assert.Contains("source parent: GroupId", result.Output);
        Assert.Contains("target parent: CategoryId", result.Output);
    }

    [Fact]
    public void Suggest_PrintCommands_EmitsAddScopeCommand()
    {
        var workspacePath = GetFixtureWorkspacePath("Fabric-Suggest-Scoped-Group-CategoryItem");
        var result = RunCli($"suggest --workspace \"{workspacePath}\" --print-commands");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Commands", result.Output);
        Assert.Contains($"meta-fabric add-scope --workspace \"{workspacePath}\" --binding ChildItem --parent-binding ParentGroup --source-parent-reference GroupId --target-parent-reference CategoryId", result.Output);
    }

    [Fact]
    public void AuthoringCommands_CanBuildScopedFabricWorkspace()
    {
        var root = CreateTempRoot("metafabric-authoring");
        try
        {
            var workspacePath = Path.Combine(root, "Fabric");
            var parentWeavePath = GetWeaveWorkspacePath("Weave-Scoped-Group-Category");
            var childWeavePath = GetWeaveWorkspacePath("Weave-Scoped-Item-CategoryItem");

            var init = RunCli($"init --new-workspace \"{workspacePath}\"");
            Assert.Equal(0, init.ExitCode);

            var addParentWeave = RunCli($"add-weave --workspace \"{workspacePath}\" --alias Parent --workspace-path \"{parentWeavePath}\"");
            Assert.Equal(0, addParentWeave.ExitCode);

            var addChildWeave = RunCli($"add-weave --workspace \"{workspacePath}\" --alias Child --workspace-path \"{childWeavePath}\"");
            Assert.Equal(0, addChildWeave.ExitCode);

            var addParentBinding = RunCli($"add-binding --workspace \"{workspacePath}\" --name ParentGroup --weave Parent --binding \"Group.Name -> Category.Name\"");
            Assert.Equal(0, addParentBinding.ExitCode);

            var addChildBinding = RunCli($"add-binding --workspace \"{workspacePath}\" --name ChildItem --weave Child --binding \"Item.Name -> CategoryItem.Name\"");
            Assert.Equal(0, addChildBinding.ExitCode);

            var addScope = RunCli($"add-scope --workspace \"{workspacePath}\" --binding ChildItem --parent-binding ParentGroup --source-parent-reference GroupId --target-parent-reference CategoryId");
            Assert.Equal(0, addScope.ExitCode);

            var check = RunCli($"check --workspace \"{workspacePath}\"");
            Assert.Equal(0, check.ExitCode);
            Assert.Contains("OK: fabric check", check.Output);
            Assert.Contains("ResolvedRows: 5", check.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Check_SanctionedScopedFabric_Passes()
    {
        var result = RunCli($"check --workspace \"{GetFixtureWorkspacePath("Fabric-Scoped-Group-CategoryItem")}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: fabric check", result.Output);
        Assert.Contains("Weaves: 2", result.Output);
        Assert.Contains("Bindings: 2", result.Output);
        Assert.Contains("ResolvedRows: 5", result.Output);
        Assert.Contains("Errors: 0", result.Output);
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

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-fabric CLI process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
    }

    private static string GetFixtureWorkspacePath(string name)
    {
        return Path.Combine(FindRepositoryRoot(), "MetaFabric.Workspaces", name);
    }

    private static string GetWeaveWorkspacePath(string name)
    {
        return Path.Combine(FindRepositoryRoot(), "MetaWeave.Workspaces", name);
    }

    private static string ResolveCliPath(string repoRoot)
    {
        var cliPath = Path.Combine(repoRoot, "MetaFabric.Cli", "bin", "Debug", "net8.0", "meta-fabric.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled MetaFabric CLI at '{cliPath}'. Build MetaFabric.Cli before running tests.");
        }

        return cliPath;
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

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
