using System.Text.RegularExpressions;

internal sealed partial class CliRuntime
{
    private static readonly Regex RenameModelNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    async Task<int> ModelRenameModelAsync(string[] commandArgs)
    {
        var options = ReadModelRenameModelOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        return await ExecuteOperationAsync(
                options.WorkspacePath,
                () => new Operation.RenameModel(
                    options.OldModelName,
                    options.NewModelName),
                "model rename-model",
                "model renamed",
                ("Workspace", Path.GetFullPath(options.WorkspacePath)),
                ("From", options.OldModelName),
                ("To", options.NewModelName))
            .ConfigureAwait(false);
    }

    (bool Ok, string OldModelName, string NewModelName, string WorkspacePath, string ErrorMessage)
        ReadModelRenameModelOptions(string[] commandArgs, int startIndex)
    {
        var oldModelName = RequiredValue("Old").Trim();
        var newModelName = RequiredValue("New").Trim();
        var workspacePath = WorkspacePath();
        if (string.IsNullOrWhiteSpace(oldModelName) || string.IsNullOrWhiteSpace(newModelName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: missing required arguments <Old> <New>.");
        }

        if (!RenameModelNamePattern.IsMatch(newModelName))
        {
            return (false, string.Empty, string.Empty, string.Empty, "Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, oldModelName, newModelName, workspacePath, string.Empty);
    }
}
