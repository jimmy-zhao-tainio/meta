internal sealed partial class CliRuntime
{
    async Task<int> ModelSuggestAsync(string[] commandArgs)
    {
        if (commandArgs.Length >= 3 && !commandArgs[2].StartsWith("--", StringComparison.Ordinal))
        {
            var mode = commandArgs[2].Trim().ToLowerInvariant();
            return PrintCommandUnknownError($"model suggest {mode}");
        }

        var options = ParseModelSuggestOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var workspace = await LoadWorkspaceForCommandAsync(options.WorkspacePath).ConfigureAwait(false);
        PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
        var report = ModelSuggestService.Analyze(workspace);

        PrintModelSuggestReport(
            report,
            options.ShowKeys,
            options.Explain,
            options.PrintCommands);
        return 0;
    }

    (bool Ok, string WorkspacePath, bool ShowKeys, bool Explain, bool PrintCommands, string ErrorMessage)
        ParseModelSuggestOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var showKeys = false;
        var explain = false;
        var printCommands = false;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, workspacePath, showKeys, explain, printCommands, "Error: --workspace requires a path.");
                }

                workspacePath = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--show-keys", StringComparison.OrdinalIgnoreCase))
            {
                showKeys = true;
                continue;
            }

            if (string.Equals(arg, "--explain", StringComparison.OrdinalIgnoreCase))
            {
                explain = true;
                continue;
            }

            if (string.Equals(arg, "--print-commands", StringComparison.OrdinalIgnoreCase))
            {
                printCommands = true;
                continue;
            }

            return (false, workspacePath, showKeys, explain, printCommands, $"Error: unknown option '{arg}'.");
        }

        return (true, workspacePath, showKeys, explain, printCommands, string.Empty);
    }

    void PrintModelSuggestReport(
        ModelSuggestReport report,
        bool showKeys,
        bool explain,
        bool printCommands)
    {
        presenter.WriteOk(
            "model suggest",
            ("Workspace", report.WorkspaceRootPath),
            ("Model", report.ModelName),
            ("Suggestions", report.EligibleRelationshipSuggestions.Count.ToString(CultureInfo.InvariantCulture)),
            ("WeakSuggestions", report.WeakRelationshipSuggestions.Count.ToString(CultureInfo.InvariantCulture)));

        // Keep a fixed, compact structure for default output.
        presenter.WriteInfo(string.Empty);
        PrintRelationshipSection(report.EligibleRelationshipSuggestions, explain);
        presenter.WriteInfo(string.Empty);
        PrintWeakRelationshipSection(report.WeakRelationshipSuggestions, explain);
        if (printCommands)
        {
            PrintSuggestedCommandSection(report.WorkspaceRootPath, report.EligibleRelationshipSuggestions);
        }

        if (showKeys)
        {
            presenter.WriteInfo(string.Empty);
            PrintKeySection(report.BusinessKeys, explain);
        }
    }

    void PrintRelationshipSection(IReadOnlyList<LookupRelationshipSuggestion> suggestions, bool explain)
    {
        presenter.WriteInfo("Relationship suggestions");
        if (suggestions.Count == 0)
        {
            presenter.WriteInfo("  (none)");
            return;
        }

        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            presenter.WriteInfo(
                $"  {(index + 1).ToString(CultureInfo.InvariantCulture)}) {suggestion.Source.EntityName}.{suggestion.Source.PropertyName} -> {suggestion.TargetLookup.EntityName} (lookup: {suggestion.TargetLookup.EntityName}.{suggestion.TargetLookup.PropertyName})");

            if (!explain)
            {
                continue;
            }

            presenter.WriteInfo("     Plan:");
            presenter.WriteInfo(
                $"       - Add relationship {suggestion.Source.EntityName} -> {suggestion.TargetLookup.EntityName}");
            presenter.WriteInfo(
                $"       - Rewrite {suggestion.Source.EntityName} rows by resolving {suggestion.Source.PropertyName} against {suggestion.TargetLookup.EntityName}.{suggestion.TargetLookup.PropertyName}");
            presenter.WriteInfo(
                $"       - Drop {suggestion.Source.EntityName}.{suggestion.Source.PropertyName} after successful rewrite");
        }
    }

    void PrintWeakRelationshipSection(IReadOnlyList<WeakLookupRelationshipSuggestion> suggestions, bool explain)
    {
        presenter.WriteInfo("Weak relationship suggestions");
        if (suggestions.Count == 0)
        {
            presenter.WriteInfo("  (none)");
            return;
        }

        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            var candidates = string.Join(
                ", ",
                suggestion.Candidates.Select(candidate =>
                    string.IsNullOrWhiteSpace(candidate.Role)
                        ? $"{candidate.TargetLookup.EntityName} (lookup: {candidate.TargetLookup.EntityName}.{candidate.TargetLookup.PropertyName})"
                        : $"{candidate.TargetLookup.EntityName} (lookup: {candidate.TargetLookup.EntityName}.{candidate.TargetLookup.PropertyName}, role: {candidate.Role})"));
            presenter.WriteInfo(
                $"  {(index + 1).ToString(CultureInfo.InvariantCulture)}) {suggestion.Source.EntityName}.{suggestion.Source.PropertyName} -> {candidates}");

            if (!explain)
            {
                continue;
            }

            presenter.WriteInfo("     Plan candidates:");
            foreach (var candidate in suggestion.Candidates)
            {
                var relationshipLabel = string.IsNullOrWhiteSpace(candidate.Role)
                    ? candidate.TargetLookup.EntityName
                    : $"{candidate.TargetLookup.EntityName} (role: {candidate.Role})";
                presenter.WriteInfo(
                    $"       - Add relationship {suggestion.Source.EntityName} -> {relationshipLabel}");
                presenter.WriteInfo(
                    $"       - Rewrite {suggestion.Source.EntityName} rows by resolving {suggestion.Source.PropertyName} against {candidate.TargetLookup.EntityName}.{candidate.TargetLookup.PropertyName}");
                presenter.WriteInfo(
                    $"       - Drop {suggestion.Source.EntityName}.{suggestion.Source.PropertyName} after successful rewrite");
            }
        }
    }

    void PrintKeySection(IReadOnlyList<BusinessKeyCandidate> keys, bool explain)
    {
        presenter.WriteInfo("Candidate business keys");
        if (keys.Count == 0)
        {
            presenter.WriteInfo("  (none)");
            presenter.WriteInfo(string.Empty);
            return;
        }

        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            presenter.WriteInfo($"  {(index + 1).ToString(CultureInfo.InvariantCulture)}) {key.Target.EntityName}.{key.Target.PropertyName}");

            if (!explain)
            {
                continue;
            }

            presenter.WriteInfo("     Details:");
            presenter.WriteInfo(
                $"       - rows={key.Target.RowCount.ToString(CultureInfo.InvariantCulture)}, non-null={key.Target.NonNullCount.ToString(CultureInfo.InvariantCulture)}, non-blank={key.Target.NonBlankCount.ToString(CultureInfo.InvariantCulture)}, distinct={key.Target.DistinctNonBlankCount.ToString(CultureInfo.InvariantCulture)}, unique={(key.Target.IsUniqueOverNonBlank ? "yes" : "no")}");
            foreach (var reason in key.Reasons)
            {
                presenter.WriteInfo("       - " + reason);
            }

            foreach (var blocker in key.Blockers)
            {
                presenter.WriteInfo("       - Blocker: " + blocker);
            }
        }
    }

    void PrintSuggestedCommandSection(string workspacePath, IReadOnlyList<LookupRelationshipSuggestion> suggestions)
    {
        presenter.WriteInfo(string.Empty);
        presenter.WriteInfo("Suggested commands");
        if (suggestions.Count == 0)
        {
            presenter.WriteInfo("  (none)");
            return;
        }

        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            presenter.WriteInfo(
                $"  {(index + 1).ToString(CultureInfo.InvariantCulture)}) meta model refactor property-to-relationship --workspace {QuoteIfNeeded(workspacePath)} --source {suggestion.Source.EntityName}.{suggestion.Source.PropertyName} --target {suggestion.TargetLookup.EntityName} --lookup {suggestion.TargetLookup.PropertyName}");
        }
    }

    static string QuoteIfNeeded(string path)
    {
        return path.Contains(' ', StringComparison.Ordinal) ? "\"" + path + "\"" : path;
    }

}
