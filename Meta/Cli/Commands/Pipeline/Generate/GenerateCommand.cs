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
            var state = await WorkspaceComposition.MaterializeAsync(CurrentWorkspace)
                .ConfigureAwait(false);
            var diagnostics = WorkspaceValidator.Validate(
                state.Model,
                state.Instance);
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
                        sourceWorkspacePath: options.WorkspacePath);
                    presenter.WriteOk(
                        "generated csharp",
                        ("Out", Path.GetFullPath(options.OutputDirectory)),
                        ("Tooling", options.IncludeTooling ? "yes" : "no"),
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

