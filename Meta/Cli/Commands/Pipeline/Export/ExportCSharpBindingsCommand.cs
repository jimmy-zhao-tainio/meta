internal sealed partial class CliRuntime
{
    async Task<int> ExportCSharpBindingsAsync(string[] commandArgs)
    {
        var sourcePath = ResolveExistingDirectoryLinks(WorkspacePath());
        var outputPath = ResolveExistingDirectoryLinks(RequiredValue("out"));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sourcePrefix = Path.EndsInDirectorySeparator(sourcePath)
            ? sourcePath
            : sourcePath + Path.DirectorySeparatorChar;
        if (string.Equals(sourcePath, outputPath, comparison) ||
            outputPath.StartsWith(sourcePrefix, comparison))
        {
            return PrintArgumentError(
                "C# bindings must be exported to a separate directory outside the source workspace.");
        }

        if (Directory.Exists(outputPath) || File.Exists(outputPath))
        {
            return PrintArgumentError("The C# bindings output directory must not already exist.");
        }

        var model = await WorkspaceComposition.MaterializeModelAsync(CurrentWorkspace)
            .ConfigureAwait(false);
        var output = MetaCSharpBindingWriter.Write(model);
        var parentPath = Path.GetDirectoryName(outputPath)!;
        var stagingPath = Path.Combine(parentPath, ".__csharp-bindings." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingPath);
        try
        {
            foreach (var source in output.Sources)
            {
                // The writer emits flat artifacts, never workspace sources or a descriptor.
                if (!string.Equals(Path.GetFileName(source.Key), source.Key, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("C# binding artifact names must be file names.");
                }

                await File.WriteAllTextAsync(
                        Path.Combine(stagingPath, source.Key),
                        source.Value,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    .ConfigureAwait(false);
            }

            Directory.Move(stagingPath, outputPath);
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
        }

        presenter.WriteOk("exported csharp bindings", ("Out", outputPath));
        return 0;
    }

    private static string ResolveExistingDirectoryLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var resolved = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            var directory = new DirectoryInfo(resolved);
            if (directory.Exists && directory.LinkTarget is not null)
            {
                resolved = directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
            }
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }
}
