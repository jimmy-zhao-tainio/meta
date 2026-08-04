using System.Text.RegularExpressions;

internal sealed partial class CliRuntime
{
    private static readonly Regex RenameModelNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    async Task<int> ModelRenameModelAsync(string[] commandArgs)
    {
        var oldModelName = RequiredValue("Old").Trim();
        var newModelName = RequiredValue("New").Trim();
        if (!RenameModelNamePattern.IsMatch(newModelName))
        {
            return PrintArgumentError("Error: <New> must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return await ExecuteOperationAsync(
                new Operation.RenameModel(oldModelName, newModelName),
                "model rename-model",
                "model renamed",
                ("From", oldModelName),
                ("To", newModelName))
            .ConfigureAwait(false);
    }
}
