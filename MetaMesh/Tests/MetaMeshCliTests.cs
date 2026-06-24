using System.Diagnostics;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaMesh.Core;

namespace MetaMesh.Tests;

public sealed class MetaMeshCliTests
{
    [Fact]
    public void Help_ShowsExpectedCommands()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-mesh <command>", result.Output);
        Assert.Contains("scan", result.Output);
        Assert.Contains("suggest", result.Output);
        Assert.Contains("impact", result.Output);
        Assert.Contains("mount", result.Output);
        Assert.Contains("link", result.Output);
        Assert.Contains("describe", result.Output);
        Assert.DoesNotContain("doctor", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScanCheckLinkAndImpact_UseLogicalHandles()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "BIStackDemo.MetaMesh");
        var sourcePath = Path.Combine(root, "source", "AdventureWorks2022", "Schema");
        var warehousePath = Path.Combine(root, "dw", "AdventureWorksMetaDemo", "Warehouse");

        try
        {
            CreateWorkspace(sourcePath, "MetaSchema", "System");
            CreateWorkspace(warehousePath, "MetaDataWarehouse", "Warehouse");

            var scan = RunCli($"scan \"{root}\" --new-workspace \"{meshPath}\" --name BIStackDemo");

            Assert.Equal(0, scan.ExitCode);
            Assert.Contains("source", scan.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("warehouse", scan.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(meshPath, "model.xml")));
            Assert.True(File.Exists(Path.Combine(meshPath, "instances", "WorkspaceInstance.xml")));
            Assert.True(File.Exists(Path.Combine(meshPath, "instances", "WorkspaceMount.xml")));

            var model = global::MetaMesh.MetaMeshModel.LoadFromXmlWorkspace(meshPath, searchUpward: false);
            Assert.Equal("BIStackDemo", Assert.Single(model.MeshList).Name);
            Assert.Contains(model.WorkspaceInstanceList, item => item.Handle == "source" && item.ModelName == "MetaSchema");
            Assert.Contains(model.WorkspaceInstanceList, item => item.Handle == "warehouse" && item.ModelName == "MetaDataWarehouse");
            Assert.Contains(model.WorkspaceMountList, item => item.PhysicalPath == Path.Combine("source", "AdventureWorks2022", "Schema"));

            var check = RunCli($"check --mesh \"{meshPath}\"");

            Assert.Equal(0, check.ExitCode);
            Assert.Contains("Ok", check.Output);

            var link = RunCli($"link --mesh \"{meshPath}\" --from source --to warehouse --kind derives");

            Assert.Equal(0, link.ExitCode);
            Assert.Contains("source", link.Output);
            Assert.Contains("warehouse", link.Output);

            var impact = RunCli($"impact --mesh \"{meshPath}\" --workspace source");

            Assert.Equal(0, impact.ExitCode);
            Assert.Contains("Affected handles", impact.Output);
            Assert.Contains("warehouse", impact.Output);

            var show = RunCli($"show --mesh \"{meshPath}\"");

            Assert.Equal(0, show.ExitCode);
            Assert.Contains("BIStackDemo", show.Output);
            Assert.Contains("derives", show.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static void CreateWorkspace(string path, string modelName, string entityName)
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = path,
            MetadataRootPath = path,
            WorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace.CreateDefault(),
            Model = new GenericModel
            {
                Name = modelName,
                Entities =
                {
                    new GenericEntity
                    {
                        Name = entityName,
                        Properties =
                        {
                            new GenericProperty { Name = "Name", DataType = "string" }
                        }
                    }
                }
            },
            Instance = new GenericInstance { ModelName = modelName },
            IsDirty = true,
        };

        workspace.Instance.GetOrCreateEntityRecords(entityName).Add(new GenericRecord
        {
            Id = entityName.ToLowerInvariant() + ":sample",
            Values =
            {
                ["Name"] = "Sample"
            }
        });

        new WorkspaceService().SaveAsync(workspace).GetAwaiter().GetResult();
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = Path.Combine(repoRoot, "MetaMesh", "Cli", "bin", "Debug", "net8.0", "meta-mesh.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled meta-mesh CLI at '{cliPath}'. Build MetaMesh.Cli before running tests.");
        }

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
                            ?? throw new InvalidOperationException("Could not start meta-mesh CLI process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

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

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
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

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
