using System;
using System.IO;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Serialization;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace Meta.Core.Tests;

internal static class TestWorkspaceFactory
{
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

    private static async Task<string> CreateWorkspaceFromContractFilesAsync(string sourceModelPath, string sourceInstancePath, string rootPrefix)
    {
        var workspaceRoot = CreateTempRoot(rootPrefix);
        var services = new ServiceCollection();
        var model = ModelXmlCodec.LoadFromPath(sourceModelPath);
        var instance = InstanceXmlCodec.LoadFromPath(sourceInstancePath, model, sourceShardFileName: string.Empty);
        var workspace = new Workspace
        {
            WorkspaceRootPath = workspaceRoot,
            MetadataRootPath = Path.Combine(workspaceRoot, "metadata"),
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = model,
            Instance = instance,
            IsDirty = true,
        };
        await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);
        return workspaceRoot;
    }

    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine(FindRepositoryRoot(), "Meta", "Tests", "TestData", fileName);
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
