internal sealed partial class CliRuntime
{
    async Task<int> GenerateAsync(string[] commandArgs)
    {
        var mode = CommandToken().Trim().ToLowerInvariant();
        var options = ReadGenerateOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            return PrintArgumentError("Error: generate requires --out <dir>.");
        }

        if (options.IncludeTooling && !string.Equals(mode, "csharp", StringComparison.OrdinalIgnoreCase))
        {
            return PrintArgumentError("Error: --tooling is only supported for 'generate csharp'.");
        }

        try
        {
            var workspace = await OpenXmlWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.ContractVersion);
            var state = workspace.State;
            var diagnostics = WorkspaceValidator.Validate(
                workspace.Model,
                workspace.Instance);
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                return PrintOperationValidationFailure("generate", Array.Empty<Operation>(), diagnostics);
            }

            GenerationManifest manifest;
            switch (mode)
            {
                case "sql":
                    manifest = GenerationService.GenerateSql(state, options.OutputDirectory);
                    presenter.WriteOk(
                        "generated sql",
                        ("Out", Path.GetFullPath(options.OutputDirectory)),
                        ("Files", manifest.FileHashes.Count.ToString(CultureInfo.InvariantCulture)));

                    return 0;
                case "csharp":
                    manifest = GenerationService.GenerateCSharp(
                        state,
                        options.OutputDirectory,
                        includeTooling: options.IncludeTooling,
                        sourceWorkspacePath: workspace.RootPath);
                    presenter.WriteOk(
                        "generated csharp",
                        ("Out", Path.GetFullPath(options.OutputDirectory)),
                        ("Tooling", options.IncludeTooling ? "yes" : "no"),
                        ("Files", manifest.FileHashes.Count.ToString(CultureInfo.InvariantCulture)));

                    return 0;
                case "ssdt":
                    manifest = GenerationService.GenerateSsdt(state, options.OutputDirectory);
                    presenter.WriteOk(
                        "generated ssdt",
                        ("Out", Path.GetFullPath(options.OutputDirectory)),
                        ("Files", manifest.FileHashes.Count.ToString(CultureInfo.InvariantCulture)));

                    return 0;
                default:
                    return PrintArgumentError($"Error: unknown generate mode '{mode}'.");
            }
        }
        catch (Exception exception)
        {
            return PrintGenerationError("E_GENERATION", exception.Message);
        }
    }
}

