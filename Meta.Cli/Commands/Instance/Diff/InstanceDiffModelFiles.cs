internal sealed partial class CliRuntime
{
    private static string ResolveModelXmlPath(
        string workspacePath,
        Meta.Core.Domain.Workspace workspace)
    {
        var workspaceRoot = workspace.WorkspaceRootPath;
        var metadataRoot = workspace.MetadataRootPath;
        var config = workspace.WorkspaceConfig;
        var layout = config.WorkspaceLayout.FirstOrDefault();
        var configuredModelPath = layout?.ModelFilePath ?? string.Empty;
        var normalizedRelative = string.IsNullOrWhiteSpace(configuredModelPath)
            ? System.IO.Path.Combine("metadata", "model.xml")
            : configuredModelPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            System.IO.Path.IsPathRooted(normalizedRelative)
                ? normalizedRelative
                : System.IO.Path.Combine(workspaceRoot, normalizedRelative),
            System.IO.Path.Combine(metadataRoot, "model.xml"),
            System.IO.Path.Combine(workspaceRoot, "model.xml"),
        };

        var match = candidates.FirstOrDefault(System.IO.File.Exists);
        if (string.IsNullOrWhiteSpace(match))
        {
            throw new System.IO.FileNotFoundException(
                $"Could not resolve model.xml for workspace '{workspacePath}'.");
        }

        return System.IO.Path.GetFullPath(match);
    }

    private bool AreModelXmlFilesByteIdentical(
        string leftWorkspacePath,
        Meta.Core.Domain.Workspace leftWorkspace,
        string rightWorkspacePath,
        Meta.Core.Domain.Workspace rightWorkspace,
        out string leftModelPath,
        out string rightModelPath)
    {
        leftModelPath = ResolveModelXmlPath(leftWorkspacePath, leftWorkspace);
        rightModelPath = ResolveModelXmlPath(rightWorkspacePath, rightWorkspace);
        var leftBytes = System.IO.File.ReadAllBytes(leftModelPath);
        var rightBytes = System.IO.File.ReadAllBytes(rightModelPath);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }
}
