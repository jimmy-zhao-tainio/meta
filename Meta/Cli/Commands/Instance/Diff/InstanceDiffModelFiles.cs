internal sealed partial class CliRuntime
{
    private static string ResolveModelXmlPath(
        string workspacePath,
        OpenedXmlWorkspace workspace)
    {
        var candidates = new[]
        {
            workspace.ModelFilePath,
            System.IO.Path.Combine(workspace.RootPath, "model.xml"),
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
        OpenedXmlWorkspace leftWorkspace,
        string rightWorkspacePath,
        OpenedXmlWorkspace rightWorkspace,
        out string leftModelPath,
        out string rightModelPath)
    {
        leftModelPath = ResolveModelXmlPath(leftWorkspacePath, leftWorkspace);
        rightModelPath = ResolveModelXmlPath(rightWorkspacePath, rightWorkspace);
        var leftBytes = System.IO.File.ReadAllBytes(leftModelPath);
        var rightBytes = System.IO.File.ReadAllBytes(rightModelPath);
        return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    private static string ResolveInstanceDiffOutputPath(string rightWorkspacePath, string suffix)
    {
        var rightFullPath = System.IO.Path.GetFullPath(rightWorkspacePath);
        var parent = System.IO.Directory.GetParent(rightFullPath)?.FullName ?? Environment.CurrentDirectory;
        var rightName = System.IO.Path.GetFileName(rightFullPath);
        if (string.IsNullOrWhiteSpace(rightName))
        {
            rightName = "workspace";
        }

        return System.IO.Path.Combine(parent, $"{rightName}.{suffix}");
    }
}
