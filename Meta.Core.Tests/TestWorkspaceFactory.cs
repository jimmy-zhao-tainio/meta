using System;
using System.IO;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;

namespace Meta.Core.Tests;

internal static class TestWorkspaceFactory
{
    public static (string ModelPath, string InstancePath, string RootPath) CreateCanonicalSampleContractFiles()
    {
        return CopyContractsToTemp(
            GetTestDataPath("SampleModel.xml"),
            GetTestDataPath("SampleInstance.xml"),
            "metadata-sample-contracts");
    }

    public static string CreateTempWorkspaceFromCanonicalSample()
    {
        return CreateTempCanonicalWorkspaceFromCanonicalSampleAsync()
            .GetAwaiter()
            .GetResult();
    }

    public static Task<string> CreateTempCanonicalWorkspaceFromCanonicalSampleAsync()
    {
        return CreateWorkspaceFromContractFilesAsync(
            GetTestDataPath("SampleModel.xml"),
            GetTestDataPath("SampleInstance.xml"),
            "metadata-studio-tests");
    }

    public static async Task<(Workspace Workspace, string RootPath)> LoadCanonicalSampleWorkspaceAsync(ServiceCollection services)
    {
        var rootPath = await CreateTempCanonicalWorkspaceFromCanonicalSampleAsync().ConfigureAwait(false);
        var workspace = await services.WorkspaceService.LoadAsync(rootPath).ConfigureAwait(false);
        return (workspace, rootPath);
    }

    public static Task<string> CreateTempSuggestDemoWorkspaceAsync()
    {
        return CreateWorkspaceFromContractFilesAsync(
            GetTestDataPath("SuggestDemoModel.xml"),
            GetTestDataPath("SuggestDemoInstance.xml"),
            "metadata-suggest-tests");
    }

    public static void DeleteDirectorySafe(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static (string ModelPath, string InstancePath, string RootPath) CopyContractsToTemp(string sourceModelPath, string sourceInstancePath, string rootPrefix)
    {
        var root = CreateTempRoot(rootPrefix);
        var modelPath = Path.Combine(root, "SampleModel.xml");
        var instancePath = Path.Combine(root, "SampleInstance.xml");
        File.Copy(sourceModelPath, modelPath, overwrite: true);
        File.Copy(sourceInstancePath, instancePath, overwrite: true);
        return (modelPath, instancePath, root);
    }

    private static async Task<string> CreateWorkspaceFromContractFilesAsync(string sourceModelPath, string sourceInstancePath, string rootPrefix)
    {
        var contractsRoot = CreateTempRoot(rootPrefix + "-contracts");
        var workspaceRoot = CreateTempRoot(rootPrefix);
        var modelPath = Path.Combine(contractsRoot, "model.xml");
        var instancePath = Path.Combine(contractsRoot, "instance.xml");
        File.Copy(sourceModelPath, modelPath, overwrite: true);
        File.Copy(sourceInstancePath, instancePath, overwrite: true);

        try
        {
            var services = new ServiceCollection();
            var workspace = await services.ImportService.ImportXmlAsync(modelPath, instancePath).ConfigureAwait(false);
            workspace.WorkspaceRootPath = workspaceRoot;
            workspace.MetadataRootPath = string.Empty;
            workspace.IsDirty = true;
            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);
            return workspaceRoot;
        }
        finally
        {
            DeleteDirectorySafe(contractsRoot);
        }
    }

    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine(FindRepositoryRoot(), "Meta.Core.Tests", "TestData", fileName);
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
}
