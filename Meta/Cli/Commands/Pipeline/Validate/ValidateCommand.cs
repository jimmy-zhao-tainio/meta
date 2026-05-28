internal sealed partial class CliRuntime
{
    async Task<int> CheckWorkspaceAsync(string[] commandArgs)
    {
        var options = ParseValidateOptions(commandArgs, startIndex: 1);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }
    
        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
    
        var diagnostics = services.ValidationService.Validate(workspace);
    
        workspace.Diagnostics = diagnostics;

        if (diagnostics.ErrorCount == 0 && diagnostics.WarningCount == 0)
        {
            presenter.WriteOk("check (0 errors, 0 warnings)");
        }
        else
        {
            presenter.WriteInfo(
                $"check: errors={diagnostics.ErrorCount} warnings={diagnostics.WarningCount}");
            foreach (var issue in diagnostics.Issues
                         .OrderByDescending(item => item.Severity)
                         .ThenBy(item => item.Message, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                presenter.WriteInfo($"  [{issue.Severity}] {NormalizeErrorMessage(issue.Message)}");
            }
        }
    
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return 2;
        }
    
        return 0;
    }
}

