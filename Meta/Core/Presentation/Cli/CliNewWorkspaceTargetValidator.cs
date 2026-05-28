namespace Meta.Core.Presentation.Cli;

public sealed record CliNewWorkspaceTargetValidation(
    bool Ok,
    string FullPath,
    string ErrorMessage,
    IReadOnlyList<string> Details);

public static class CliNewWorkspaceTargetValidator
{
    public static CliNewWorkspaceTargetValidation Validate(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullPath = Path.GetFullPath(targetPath);
        if (File.Exists(fullPath))
        {
            return new CliNewWorkspaceTargetValidation(
                Ok: false,
                FullPath: fullPath,
                ErrorMessage: $"new workspace target '{fullPath}' must be a directory path.",
                Details:
                [
                    "  Target path points to an existing file."
                ]);
        }

        if (!Directory.Exists(fullPath))
        {
            return Success(fullPath);
        }

        var sampleEntries = Directory.EnumerateFileSystemEntries(fullPath)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (sampleEntries.Length == 0)
        {
            return Success(fullPath);
        }

        return new CliNewWorkspaceTargetValidation(
            Ok: false,
            FullPath: fullPath,
            ErrorMessage: $"target directory '{fullPath}' must be empty.",
            Details:
            [
                $"  Entries: {string.Join(", ", sampleEntries)}"
            ]);
    }

    private static CliNewWorkspaceTargetValidation Success(string fullPath) =>
        new(
            Ok: true,
            FullPath: fullPath,
            ErrorMessage: string.Empty,
            Details: Array.Empty<string>());
}
