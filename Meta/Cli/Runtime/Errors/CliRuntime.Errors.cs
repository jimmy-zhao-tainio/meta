internal sealed partial class CliRuntime
{
    string BuildWhere(params (string Key, string? Value)[] fields)
    {
        return string.Join(
            ", ",
            fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Key) && !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{field.Key}={field.Value}"));
    }

    int PrintFormattedErrorWithTable(
        string code,
        string message,
        int exitCode,
        IReadOnlyList<(string Key, string Value)> where,
        IReadOnlyList<string> hints,
        string tableTitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var normalizedMessage = NormalizeErrorMessage(message);
        var normalizedHints = hints
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        var normalizedWhere = where
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .ToList();
    
        var normalizedHumanDetails = NormalizeHumanErrorDetails(normalizedHints);
        var errorDocument = BuildErrorDocument(normalizedMessage, normalizedHumanDetails);
        RenderErrorDocument(
            errorDocument,
            () =>
            {
                presenter.WriteInfo($"{tableTitle}:");
                presenter.WriteTable(headers, rows);
            });
        return exitCode;
    }

    string NormalizeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }
    
        const string Prefix = "Error:";
        var trimmed = message.Trim();
        if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[Prefix.Length..].Trim();
        }
    
        return trimmed;
    }

    int PrintFormattedError(
        string code,
        string message,
        int exitCode,
        string? where = null,
        IReadOnlyList<string>? hints = null,
        IReadOnlyList<string>? suggestions = null)
    {
        var normalizedMessage = NormalizeErrorMessage(message);
        var mergedHints = new List<string>();
        if (suggestions is { Count: > 0 })
        {
            mergedHints.Add("Did you mean: " + string.Join(", ", suggestions.Take(3)));
        }
    
        if (hints is { Count: > 0 })
        {
            mergedHints.AddRange(hints.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
    
        var normalizedHints = mergedHints
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
        var wherePairs = ParseWherePairs(where);
    
        PrintHumanFailure(normalizedMessage, normalizedHints);
    
        return exitCode;
    }

    void PrintHumanFailure(string message, IReadOnlyList<string>? details = null)
    {
        var normalizedMessage = NormalizeErrorMessage(message);
        var humanDetails = NormalizeHumanErrorDetails(details ?? Array.Empty<string>());
        var errorDocument = BuildErrorDocument(normalizedMessage, humanDetails);
        RenderErrorDocument(errorDocument);
    }

    ErrorDocument BuildErrorDocument(string message, IReadOnlyList<string> normalizedDetails)
    {
        var usage = normalizedDetails
            .FirstOrDefault(item => item.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase));
        var next = normalizedDetails
            .FirstOrDefault(item => item.StartsWith("Next:", StringComparison.OrdinalIgnoreCase));
        var details = normalizedDetails
            .Where(item =>
                !item.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase) &&
                !item.StartsWith("Next:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (string.IsNullOrWhiteSpace(next))
        {
            next = $"Next: {BuildNextHelpHintForCurrentArgs()}";
        }

        return new ErrorDocument(
            Message: message,
            Usage: usage,
            Details: details,
            Next: next);
    }

    void RenderErrorDocument(ErrorDocument document, Action? body = null)
    {
        presenter.WriteInfo($"Error: {document.Message}");

        var details = document.Details ?? Array.Empty<string>();
        var hasUsage = !string.IsNullOrWhiteSpace(document.Usage);
        var hasDetails = details.Count > 0;
        var hasBody = body != null;

        if (hasUsage || hasDetails || hasBody)
        {
            presenter.WriteInfo(string.Empty);
        }

        if (hasUsage)
        {
            presenter.WriteInfo(document.Usage!);
        }

        if (hasDetails)
        {
            if (hasUsage)
            {
                presenter.WriteInfo(string.Empty);
            }

            foreach (var detail in details)
            {
                presenter.WriteInfo(detail);
            }
        }

        if (hasBody)
        {
            if (hasUsage || hasDetails)
            {
                presenter.WriteInfo(string.Empty);
            }

            body!();
        }

        presenter.WriteInfo(string.Empty);
        presenter.WriteInfo(document.Next);
    }

    IReadOnlyList<string> NormalizeHumanErrorDetails(IEnumerable<string> details)
    {
        var normalized = details?
            .Select(item => item?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList() ?? new List<string>();
        var usage = normalized.FirstOrDefault(item => item.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase));
        var next = normalized.FirstOrDefault(item => item.StartsWith("Next:", StringComparison.OrdinalIgnoreCase));
        var detailLines = normalized
            .Where(item =>
            !item.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase) &&
            !item.StartsWith("Next:", StringComparison.OrdinalIgnoreCase))
            .ToList();
    
        if (string.IsNullOrWhiteSpace(next) && !string.IsNullOrWhiteSpace(usage))
        {
            var usageSyntax = NormalizeUsageSyntax(usage);
            var nextFromUsage = BuildNextHelpHintFromUsage(usageSyntax);
            if (!string.IsNullOrWhiteSpace(nextFromUsage))
            {
                next = $"Next: {nextFromUsage}";
            }
        }

        if (string.IsNullOrWhiteSpace(next))
        {
            next = $"Next: {BuildNextHelpHintForCurrentArgs()}";
        }
    
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(usage))
        {
            result.Add(usage);
        }
    
        result.AddRange(detailLines);
    
        if (!string.IsNullOrWhiteSpace(next))
        {
            result.Add(next);
        }
    
        return result
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    int PrintOperationValidationFailure(
        string commandName,
        IReadOnlyList<WorkspaceOp> operations,
        WorkspaceDiagnostics diagnostics)
    {
        var headline = BuildOperationValidationHeadline(commandName, operations, diagnostics);
        PrintHumanFailure(headline, BuildHumanValidationBlockers(commandName, operations, diagnostics));
        return 4;
    }

    IReadOnlyList<string> BuildHumanValidationBlockers(
        string commandName,
        IReadOnlyList<WorkspaceOp> operations,
        WorkspaceDiagnostics diagnostics)
    {
        var orderedIssues = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error || (globalStrict && issue.Severity == IssueSeverity.Warning))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Message, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Location, StringComparer.OrdinalIgnoreCase)
            .ToList();
    
        var blockers = orderedIssues
            .Select(FormatHumanValidationIssue)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
    
        var details = new List<string>();
        if (string.Equals(commandName, "delete", StringComparison.OrdinalIgnoreCase) && blockers.Count > 0)
        {
            details.Add($"Blocked by existing relationships ({blockers.Count}).");
        }
        else
        {
            var blockerCount = orderedIssues.Count;
            details.Add($"Blocked by validation issues ({blockerCount}).");
        }
    
        if (blockers.Count == 0)
        {
            details.Add("No blocker details available.");
            return details;
        }
    
        details.AddRange(blockers);
        if (orderedIssues.Count > blockers.Count)
        {
            details.Add($"... {orderedIssues.Count - blockers.Count} more validation blocker(s).");
        }
    
        return details;
    }

    string FormatHumanValidationIssue(DiagnosticIssue issue)
    {
        if (issue == null)
        {
            return string.Empty;
        }
    
        if (string.Equals(issue.Code, "instance.relationship.orphan", StringComparison.OrdinalIgnoreCase))
        {
            var locationMatch = Regex.Match(
                issue.Location ?? string.Empty,
                @"^instance/([^/]+)/([^/]+)/relationship/([^/]+)/([^/]+)$",
                RegexOptions.IgnoreCase);
            if (locationMatch.Success)
            {
                return $"{BuildEntityInstanceAddress(locationMatch.Groups[1].Value, locationMatch.Groups[2].Value)} references {BuildEntityInstanceAddress(locationMatch.Groups[3].Value, locationMatch.Groups[4].Value)}";
            }
    
            var messageMatch = Regex.Match(
                issue.Message ?? string.Empty,
                "^Entity '([^']+)' record '([^']+)' points to missing '([^']+)' id '([^']+)'\\.?$",
                RegexOptions.IgnoreCase);
            if (messageMatch.Success)
            {
                return $"{BuildEntityInstanceAddress(messageMatch.Groups[1].Value, messageMatch.Groups[2].Value)} references {BuildEntityInstanceAddress(messageMatch.Groups[3].Value, messageMatch.Groups[4].Value)}";
            }
        }
    
        if (string.Equals(issue.Code, "instance.relationship.missing", StringComparison.OrdinalIgnoreCase))
        {
            var locationMatch = Regex.Match(
                issue.Location ?? string.Empty,
                @"^instance/([^/]+)/([^/]+)/relationship/([^/]+)$",
                RegexOptions.IgnoreCase);
            if (locationMatch.Success)
            {
                return $"{BuildEntityInstanceAddress(locationMatch.Groups[1].Value, locationMatch.Groups[2].Value)} is missing required relationship {locationMatch.Groups[3].Value}";
            }
        }
    
        if (string.Equals(issue.Code, "instance.required.missing", StringComparison.OrdinalIgnoreCase))
        {
            var locationMatch = Regex.Match(
                issue.Location ?? string.Empty,
                @"^instance/([^/]+)/([^/]+)/([^/]+)$",
                RegexOptions.IgnoreCase);
            if (locationMatch.Success)
            {
                return $"{BuildEntityInstanceAddress(locationMatch.Groups[1].Value, locationMatch.Groups[2].Value)} is missing required value {locationMatch.Groups[3].Value}";
            }
        }
    
        return NormalizeErrorMessage(issue.Message ?? string.Empty);
    }

    string BuildOperationValidationHeadline(
        string commandName,
        IReadOnlyList<WorkspaceOp> operations,
        WorkspaceDiagnostics diagnostics)
    {
        if (string.Equals(commandName, "delete", StringComparison.OrdinalIgnoreCase))
        {
            var deleteOp = operations
                .FirstOrDefault(op => string.Equals(op.Type, WorkspaceOpTypes.DeleteRows, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(deleteOp?.EntityName))
            {
                var targetId = deleteOp.Ids?
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (targetId is { Count: 1 })
                {
                    return $"Cannot delete {BuildEntityInstanceAddress(deleteOp.EntityName, targetId[0])}";
                }
    
                return $"Cannot delete {deleteOp.EntityName}";
            }
        }
    
        if (string.Equals(commandName, "model drop-entity", StringComparison.OrdinalIgnoreCase))
        {
            var targetEntity = operations
                .FirstOrDefault(op => string.Equals(op.Type, WorkspaceOpTypes.DeleteEntity, StringComparison.OrdinalIgnoreCase))
                ?.EntityName;
            if (!string.IsNullOrWhiteSpace(targetEntity))
            {
                return $"Cannot drop entity {targetEntity}";
            }
        }
    
        if (diagnostics.ErrorCount == 0 && diagnostics.WarningCount > 0 && globalStrict)
        {
            return $"Cannot complete {commandName} in strict mode";
        }
    
        return $"Cannot complete {commandName}";
    }

    int PrintUsageError(string usage)
    {
        var normalizedUsage = NormalizeUsageSyntax(usage);
        var next = BuildNextHelpHintFromUsage(normalizedUsage);
        var missingArgument = DetectMissingRequiredArgumentToken(normalizedUsage);
        var message = string.IsNullOrWhiteSpace(missingArgument)
            ? "missing required argument."
            : $"missing required argument {missingArgument}.";
        return PrintFormattedError(
            "E_USAGE",
            message,
            exitCode: 1,
            hints: new[]
            {
                $"Usage: {normalizedUsage}",
                $"Next: {next}",
            });
    }

    int PrintArgumentError(string message)
    {
        var normalized = NormalizeErrorMessage(message);
        normalized = RewriteArgumentMessage(normalized);
        var detailHints = new List<string>();
    
        if (Regex.IsMatch(normalized, "illegal characters in path", RegexOptions.IgnoreCase))
        {
            var illegalCharMatch = Regex.Match(normalized, "character\\s+'([^']+)'", RegexOptions.IgnoreCase);
            if (illegalCharMatch.Success)
            {
                detailHints.Add($"path contains illegal character '{illegalCharMatch.Groups[1].Value}' for Windows filenames.");
            }
        }
    
        if (normalized.Contains("generate requires --out", StringComparison.OrdinalIgnoreCase))
        {
            var mode = args.Length >= 2 ? args[1].Trim().ToLowerInvariant() : "sql";
            if (mode is not ("sql" or "csharp" or "ssdt"))
            {
                mode = "sql";
            }
    
            detailHints.Add($"example: meta generate {mode} --out .\\out\\{mode}");
        }
        else if (normalized.Contains("--equals", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("--contains", StringComparison.OrdinalIgnoreCase))
        {
            var entityName = TryGetEntityFromCurrentCommandArgs() ?? "Cube";
            detailHints.Add($"example: meta query {entityName} --contains Id sample");
        }
        else if (normalized.Contains("--new-workspace", StringComparison.OrdinalIgnoreCase) ||
                 normalized.Contains("import requires --new-workspace", StringComparison.OrdinalIgnoreCase))
        {
            var importMode = args.Length >= 2 ? args[1].Trim().ToLowerInvariant() : "sql";
            if (string.Equals(importMode, "sql", StringComparison.OrdinalIgnoreCase))
            {
                detailHints.Add("example: meta import sql \"Server=...;Database=...;...\" dbo --new-workspace .\\ImportedWorkspace");
            }
            else if (string.Equals(importMode, "csv", StringComparison.OrdinalIgnoreCase))
            {
                detailHints.Add("example: meta import csv .\\landing.csv --entity Landing --new-workspace .\\ImportedWorkspace");
            }
        }
        var usage = BuildUsageHintForCurrentArgs();
        var next = BuildNextHelpHintForCurrentArgs();
        var hints = new List<string>();
        if (detailHints.Count > 0)
        {
            hints.Add(detailHints[0]);
        }
    
        var normalizedUsage = NormalizeUsageSyntax(usage);
        hints.Add(string.IsNullOrWhiteSpace(normalizedUsage)
            ? "Usage: meta <command> [options]"
            : $"Usage: {normalizedUsage}");
    
        if (!string.IsNullOrWhiteSpace(next))
        {
            hints.Add($"Next: {next}");
        }
    
        return PrintFormattedError(
            "E_ARGUMENT",
            normalized,
            exitCode: 1,
            hints: hints);
    }

    string DetectMissingRequiredArgumentToken(string normalizedUsage)
    {
        if (string.IsNullOrWhiteSpace(normalizedUsage))
        {
            return string.Empty;
        }

        var usageTokens = normalizedUsage
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (usageTokens.Count > 0 && string.Equals(usageTokens[0], "meta", StringComparison.OrdinalIgnoreCase))
        {
            usageTokens.RemoveAt(0);
        }

        var actualTokens = args?.ToList() ?? new List<string>();
        var actualIndex = 0;
        foreach (var usageToken in usageTokens)
        {
            if (usageToken.StartsWith("[", StringComparison.Ordinal) ||
                usageToken.StartsWith("--", StringComparison.Ordinal) ||
                string.Equals(usageToken, "...", StringComparison.Ordinal))
            {
                break;
            }

            var isPlaceholder = usageToken.StartsWith("<", StringComparison.Ordinal) &&
                                usageToken.EndsWith(">", StringComparison.Ordinal);
            if (actualIndex >= actualTokens.Count)
            {
                return isPlaceholder ? usageToken : $"<{usageToken}>";
            }

            if (!isPlaceholder &&
                !string.Equals(actualTokens[actualIndex], usageToken, StringComparison.OrdinalIgnoreCase))
            {
                return $"<{usageToken}>";
            }

            actualIndex++;
        }

        return string.Empty;
    }

    string RewriteArgumentMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "invalid argument.";
        }

        var normalized = message.Trim();

        var unknownOptionMatch = Regex.Match(
            normalized,
            "^unknown(?:\\s+\\S+)? option '([^']+)'\\.?$",
            RegexOptions.IgnoreCase);
        if (unknownOptionMatch.Success)
        {
            return $"unknown option {unknownOptionMatch.Groups[1].Value}.";
        }

        var missingOptionValueMatch = Regex.Match(
            normalized,
            "^(--[^\\s]+) requires (?:a\\s+|an\\s+)?(.+?)\\.?$",
            RegexOptions.IgnoreCase);
        if (missingOptionValueMatch.Success)
        {
            var option = missingOptionValueMatch.Groups[1].Value;
            var valueHint = missingOptionValueMatch.Groups[2].Value.Trim();
            return string.IsNullOrWhiteSpace(valueHint)
                ? $"missing required argument for option {option}."
                : $"missing required argument <{valueHint}> for option {option}.";
        }

        var globalWorkspaceMatch = Regex.Match(
            normalized,
            "^global --workspace requires a path\\.?$",
            RegexOptions.IgnoreCase);
        if (globalWorkspaceMatch.Success)
        {
            return "missing required argument <path> for option --workspace.";
        }

        return normalized;
    }

    int PrintDataError(string code, string message)
    {
        var normalized = NormalizeErrorMessage(message);
        var workspace = TryLoadWorkspaceForHints();
        var where = string.Empty;
        var hints = new List<string>();
        IReadOnlyList<string> suggestions = Array.Empty<string>();
        var finalCode = code;
        var resolvedWorkspacePath = ResolveWorkspacePathForHints();
    
        if ((string.Equals(finalCode, "E_FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(finalCode, "E_DIRECTORY_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(finalCode, "E_WORKSPACE_INVALID", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(finalCode, "E_OPERATION", StringComparison.OrdinalIgnoreCase)) &&
            IsWorkspaceNotFoundMessage(normalized))
        {
            finalCode = "E_WORKSPACE_NOT_FOUND";
            normalized = "Workspace was not found.";
            where = BuildWhere(("workspace", Path.GetFullPath(resolvedWorkspacePath)));
            hints.Clear();
            hints.Add("Next: meta init .");
        }
    
        if (string.Equals(finalCode, "E_FORMAT", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("unsupported --from", StringComparison.OrdinalIgnoreCase))
        {
            var formatMatch = Regex.Match(message, "unsupported --from '([^']+)'", RegexOptions.IgnoreCase);
            var provided = formatMatch.Success ? formatMatch.Groups[1].Value : string.Empty;
            var allowed = new[] { "tsv", "csv" };
            suggestions = SuggestValues(provided, allowed);
            normalized = "Unsupported value for --from.";
    
            string? filePath = null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--file", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = args[i + 1];
                    break;
                }
            }
    
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var extension = Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant();
                if (allowed.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    hints.Add($"Detected file extension '.{extension}'.");
                    hints.Add($"Next: meta bulk-insert <Entity> --from {extension} --file <path> --key Id");
                }
            }
    
            if (hints.Count == 0)
            {
                hints.Add("Allowed values: tsv, csv.");
                hints.Add("Next: meta bulk-insert <Entity> --from tsv --file <path> --key Id");
            }
        }
    
        if (string.Equals(finalCode, "E_FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finalCode, "E_DIRECTORY_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Input path was not found.";
            hints.Clear();
            hints.Add("Next: verify the path with: Get-ChildItem <path>");
        }
    
        if (string.Equals(finalCode, "E_IO", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(normalized, "illegal characters in path", RegexOptions.IgnoreCase))
        {
            normalized = "Path is invalid for Windows.";
            var illegalCharMatch = Regex.Match(normalized, "character\\s+'([^']+)'", RegexOptions.IgnoreCase);
            hints.Clear();
            if (illegalCharMatch.Success)
            {
                hints.Add($"Illegal character: '{illegalCharMatch.Groups[1].Value}'.");
            }
            else
            {
                hints.Add("Path contains illegal filename characters.");
            }
    
            hints.Add("Next: use a valid Windows path and retry.");
        }
        else if (string.Equals(finalCode, "E_IO", StringComparison.OrdinalIgnoreCase))
        {
            var pathMatch = Regex.Match(normalized, "['\"]([^'\"]+)['\"]");
            if (pathMatch.Success)
            {
                var pathValue = pathMatch.Groups[1].Value;
                var illegal = pathValue.FirstOrDefault(ch => ch is '|' or '<' or '>' or '"' or '?' or '*');
                if (illegal != default)
                {
                    normalized = "Path is invalid for Windows.";
                    hints.Clear();
                    hints.Add($"Illegal character: '{illegal}'.");
                    hints.Add("Next: use a valid Windows path and retry.");
                }
            }
        }
    
        var xmlStartMatch = Regex.Match(normalized, "line\\s+(\\d+)\\s+position\\s+(\\d+)", RegexOptions.IgnoreCase);
        var xmlEndMatch = Regex.Match(normalized, "Line\\s+(\\d+),\\s+position\\s+(\\d+)", RegexOptions.IgnoreCase);
        if (string.Equals(finalCode, "E_WORKSPACE_XML_INVALID", StringComparison.OrdinalIgnoreCase) || xmlEndMatch.Success)
        {
            finalCode = "E_WORKSPACE_XML_INVALID";
            var fileMatch = Regex.Match(
                normalized,
                "(metadata[\\\\/](?:model\\.xml|instance[\\\\/][^\\s:]+\\.xml))",
                RegexOptions.IgnoreCase);
            var file = fileMatch.Success
                ? fileMatch.Groups[1].Value.Replace('\\', '/')
                : "metadata/model.xml";
            normalized = $"Cannot parse {file}.";
            if (xmlEndMatch.Success)
            {
                where = BuildWhere(
                    ("file", file),
                    ("line", xmlEndMatch.Groups[1].Value),
                    ("startPos", xmlStartMatch.Success ? xmlStartMatch.Groups[2].Value : xmlEndMatch.Groups[2].Value),
                    ("endPos", xmlEndMatch.Groups[2].Value));
            }
    
            hints.Clear();
            if (xmlEndMatch.Success)
            {
                hints.Add($"Location: line {xmlEndMatch.Groups[1].Value}, position {xmlEndMatch.Groups[2].Value}.");
            }
    
            var resolvedFileForHints = ResolveWorkspaceFileForHint(resolvedWorkspacePath, file);
            if (!string.IsNullOrWhiteSpace(resolvedFileForHints) && File.Exists(resolvedFileForHints))
            {
                var content = File.ReadAllText(resolvedFileForHints);
                if (content.Contains("<<<<<<<", StringComparison.Ordinal) ||
                    content.Contains("=======", StringComparison.Ordinal) ||
                    content.Contains(">>>>>>>", StringComparison.Ordinal))
                {
                    hints.Add($"Next: resolve git merge markers in {file}.");
                }
            }
    
            if (!hints.Any(line => line.StartsWith("Next:", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("Next: meta check");
            }
        }
    
        if (string.Equals(finalCode, "E_SQL", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Cannot connect to the SQL database.";
            hints.Clear();
            hints.Add("Next: verify server/database name and permissions in SSMS.");
        }
    
        var entityMissingMatch = Regex.Match(
            normalized,
            "^Entity '([^']+)' (?:does not exist(?: in model)?|was not found)\\.?$",
            RegexOptions.IgnoreCase);
        if (entityMissingMatch.Success)
        {
            finalCode = "E_ENTITY_NOT_FOUND";
            var entityName = entityMissingMatch.Groups[1].Value;
            normalized = $"Entity '{entityName}' was not found.";
            where = BuildWhere(("entity", entityName));
            suggestions = SuggestValues(entityName, workspace?.Model.Entities.Select(entity => entity.Name) ?? Array.Empty<string>());
            hints.Clear();
            hints.Add("Next: meta list entities");
        }
        else
        {
            var propertyMissingMatch = Regex.Match(
                normalized,
                "^Field '([^']+)' does not exist on entity '([^']+)'\\.?$",
                RegexOptions.IgnoreCase);
            if (propertyMissingMatch.Success)
            {
                finalCode = "E_PROPERTY_NOT_FOUND";
                var fieldName = propertyMissingMatch.Groups[1].Value;
                var entityName = propertyMissingMatch.Groups[2].Value;
                normalized = $"Property '{entityName}.{fieldName}' was not found.";
                where = BuildWhere(("entity", entityName), ("field", fieldName));
                var entity = workspace?.Model.FindEntity(entityName);
                suggestions = SuggestValues(
                    fieldName,
                        entity == null
                            ? Array.Empty<string>()
                        : entity.Properties.Select(property => property.Name)
                            .Concat(entity.Relationships.Select(relationship => relationship.GetColumnName()))
                            .Concat(new[] { "Id" }));
                hints.Clear();
                hints.Add($"Next: meta list properties {entityName}");
            }
            else
            {
                var unsupportedPropertyOrColumn = Regex.Match(
                    normalized,
                    "^(?:Field|Column) '([^']+)' is not a property or relationship on entity '([^']+)'\\.?$",
                    RegexOptions.IgnoreCase);
                if (unsupportedPropertyOrColumn.Success)
                {
                    finalCode = "E_PROPERTY_NOT_FOUND";
                    var fieldName = unsupportedPropertyOrColumn.Groups[1].Value;
                    var entityName = unsupportedPropertyOrColumn.Groups[2].Value;
                    normalized = $"Property '{entityName}.{fieldName}' was not found.";
                    where = BuildWhere(("entity", entityName), ("field", fieldName));
                    var entity = workspace?.Model.FindEntity(entityName);
                    suggestions = SuggestValues(
                        fieldName,
                        entity == null
                            ? Array.Empty<string>()
                        : entity.Properties.Select(property => property.Name)
                                .Concat(entity.Relationships.Select(relationship => relationship.GetColumnName()))
                                .Concat(new[] { "Id" }));
                    hints.Clear();
                    hints.Add($"Next: meta list properties {entityName}");
                }
                else
                {
                    var propertyOnEntityMissing = Regex.Match(
                        normalized,
                        "^Property '([^']+)' does not exist on entity '([^']+)'\\.?$",
                        RegexOptions.IgnoreCase);
                    if (propertyOnEntityMissing.Success)
                    {
                        finalCode = "E_PROPERTY_NOT_FOUND";
                        var fieldName = propertyOnEntityMissing.Groups[1].Value;
                        var entityName = propertyOnEntityMissing.Groups[2].Value;
                        normalized = $"Property '{entityName}.{fieldName}' was not found.";
                        where = BuildWhere(("entity", entityName), ("field", fieldName));
                        var entity = workspace?.Model.FindEntity(entityName);
                        suggestions = SuggestValues(
                            fieldName,
                                entity == null
                                    ? Array.Empty<string>()
                                : entity.Properties.Select(property => property.Name)
                                    .Concat(entity.Relationships.Select(relationship => relationship.GetColumnName()))
                                    .Concat(new[] { "Id" }));
                        hints.Clear();
                        hints.Add($"Next: meta list properties {entityName}");
                    }
                    else
                    {
                        var dottedPropertyMissing = Regex.Match(
                            normalized,
                            "^Property '([^'.]+)\\.([^']+)' does not exist\\.?$",
                            RegexOptions.IgnoreCase);
                        if (dottedPropertyMissing.Success)
                        {
                            finalCode = "E_PROPERTY_NOT_FOUND";
                            var entityName = dottedPropertyMissing.Groups[1].Value;
                            var fieldName = dottedPropertyMissing.Groups[2].Value;
                            normalized = $"Property '{entityName}.{fieldName}' was not found.";
                            where = BuildWhere(("entity", entityName), ("field", fieldName));
                            var entity = workspace?.Model.FindEntity(entityName);
                            suggestions = SuggestValues(
                                fieldName,
                                entity == null
                                    ? Array.Empty<string>()
                                : entity.Properties.Select(property => property.Name)
                                        .Concat(entity.Relationships.Select(relationship => relationship.GetColumnName()))
                                        .Concat(new[] { "Id" }));
                            hints.Clear();
                            hints.Add($"Next: meta list properties {entityName}");
                        }
                    }
                }
            }
        }
    
        var alreadyExistsMatch = Regex.Match(
            normalized,
            "^(?:Entity|Property) '([^']+)' already exists\\.?$",
            RegexOptions.IgnoreCase);
        if (alreadyExistsMatch.Success)
        {
            hints.Clear();
            hints.Add("Next: meta list entities");
        }
    
        var duplicateIdMatch = Regex.Match(
            normalized,
            "^Cannot create '([^']+)' with Id '([^']+)' because it already exists\\.?$",
            RegexOptions.IgnoreCase);
        if (duplicateIdMatch.Success)
        {
            var entityName = duplicateIdMatch.Groups[1].Value;
            var id = duplicateIdMatch.Groups[2].Value;
            normalized = $"Instance '{BuildEntityInstanceAddress(entityName, id)}' already exists.";
            where = BuildWhere(("entity", entityName), ("id", id));
            hints.Clear();
            hints.Add($"Next: meta instance update {entityName} {QuoteInstanceId(id)} --set <Field>=<Value>");
        }
    
        var entityNotEmptyMatch = Regex.Match(
            normalized,
            "^Entity '([^']+)' has rows and cannot be removed\\.?$",
            RegexOptions.IgnoreCase);
        if (entityNotEmptyMatch.Success)
        {
            finalCode = "E_ENTITY_NOT_EMPTY";
            var entityName = entityNotEmptyMatch.Groups[1].Value;
            var entityRows = GetEntityRows(workspace, entityName);
            var rowCount = entityRows.Count;
            var firstRowId = entityRows
                .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Select(row => row.Id)
                .FirstOrDefault() ?? "1";
            normalized = $"Cannot drop entity {entityName}";
            where = BuildWhere(
                ("entity", entityName),
                ("rows", rowCount.ToString(CultureInfo.InvariantCulture)));
            hints.Clear();
            hints.Add($"{entityName} has {rowCount.ToString(CultureInfo.InvariantCulture)} instances.");
            hints.Add($"Next: meta view instance {entityName} {QuoteInstanceId(firstRowId)}");
        }
    
        var entityInboundMatch = Regex.Match(
            normalized,
            "^Entity '([^']+)' has inbound relationships and cannot be removed\\.?$",
            RegexOptions.IgnoreCase);
        if (entityInboundMatch.Success)
        {
            finalCode = "E_ENTITY_HAS_INBOUND_RELATIONSHIPS";
            var entityName = entityInboundMatch.Groups[1].Value;
            var inboundCount = workspace?.Model.Entities
                .SelectMany(fromEntity => fromEntity.Relationships
                    .Where(relationship => string.Equals(relationship.Entity, entityName, StringComparison.OrdinalIgnoreCase)))
                .Count() ?? 0;
            normalized = $"Cannot drop entity {entityName}";
            where = BuildWhere(
                ("entity", entityName),
                ("inboundRelationships", inboundCount.ToString(CultureInfo.InvariantCulture)));
            hints.Clear();
            hints.Add($"Inbound relationships: {inboundCount.ToString(CultureInfo.InvariantCulture)}.");
            hints.Add($"Next: meta graph inbound {entityName}");
        }
    
        var relationshipInUseMatch = Regex.Match(
            normalized,
            "^Relationship '([^']+)->([^']+)' is in use and cannot be removed\\.?$",
            RegexOptions.IgnoreCase);
        if (relationshipInUseMatch.Success)
        {
            finalCode = "E_RELATIONSHIP_IN_USE";
            var fromEntity = relationshipInUseMatch.Groups[1].Value;
            var toEntity = relationshipInUseMatch.Groups[2].Value;
            var occurrenceCount = workspace == null
                ? 0
                : GetEntityRows(workspace, fromEntity)
                    .Count(row => TryGetRelationshipId(row, toEntity, out _));
            normalized = $"Cannot drop relationship {fromEntity}->{toEntity}";
            where = BuildWhere(
                ("fromEntity", fromEntity),
                ("toEntity", toEntity),
                ("occurrences", occurrenceCount.ToString(CultureInfo.InvariantCulture)));
            hints.Clear();
            hints.Add($"Relationship usage exists in {occurrenceCount.ToString(CultureInfo.InvariantCulture)} instance(s).");
            var sampleRowId = GetEntityRows(workspace, fromEntity)
                .Where(row => TryGetRelationshipId(row, toEntity, out _))
                .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
                .Select(row => row.Id)
                .FirstOrDefault() ?? "1";
            hints.Add($"Next: meta instance relationship set {fromEntity} {QuoteInstanceId(sampleRowId)} --to <RelationshipSelector> <ToId>");
        }
    
        var relationshipNotFoundMatch = Regex.Match(
            normalized,
            "^Relationship '([^']+)->([^']+)' does not exist\\.?$",
            RegexOptions.IgnoreCase);
        if (relationshipNotFoundMatch.Success)
        {
            finalCode = "E_RELATIONSHIP_NOT_FOUND";
            var fromEntity = relationshipNotFoundMatch.Groups[1].Value;
            var toEntity = relationshipNotFoundMatch.Groups[2].Value;
            normalized = $"Relationship '{fromEntity}->{toEntity}' was not found.";
            where = BuildWhere(("fromEntity", fromEntity), ("toEntity", toEntity));
            hints.Clear();
            hints.Add($"Next: meta list relationships {fromEntity}");
        }
    
        var rowNotFoundMatch = Regex.Match(
            normalized,
            "^Instance with Id '([^']+)' does not exist in entity '([^']+)'\\.?$",
            RegexOptions.IgnoreCase);
        if (!rowNotFoundMatch.Success)
        {
            rowNotFoundMatch = Regex.Match(
                normalized,
                "^Instance '([^\\s']+)\\s+([^']+)' was not found\\.?$",
                RegexOptions.IgnoreCase);
        }
        if (rowNotFoundMatch.Success)
        {
            finalCode = "E_ROW_NOT_FOUND";
            var id = rowNotFoundMatch.Groups[1].Value;
            var entityName = rowNotFoundMatch.Groups[2].Value;
            if (Regex.IsMatch(normalized, "^Instance '([^\\s']+)\\s+([^']+)' was not found\\.?$", RegexOptions.IgnoreCase))
            {
                entityName = rowNotFoundMatch.Groups[1].Value;
                id = rowNotFoundMatch.Groups[2].Value;
            }
    
            var rows = GetEntityRows(workspace, entityName);
            normalized = $"Instance '{BuildEntityInstanceAddress(entityName, id)}' was not found.";
            where = BuildWhere(
                ("entity", entityName),
                ("id", id),
                ("rows", rows.Count.ToString(CultureInfo.InvariantCulture)));
            hints.Clear();
            hints.Add($"Next: meta query {entityName} --contains Id {QuoteInstanceId(id)}");
            suggestions = SuggestValues(
                id,
                rows.Select(row => row.Id).Where(value => !string.IsNullOrWhiteSpace(value)).Take(20));
        }
    
        return PrintFormattedError(
            finalCode,
            normalized,
            exitCode: 4,
            where: string.IsNullOrWhiteSpace(where) ? null : where,
            hints: hints,
            suggestions: suggestions);
    }

    bool IsWorkspaceNotFoundMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }
    
        if (Regex.IsMatch(
                message,
                "^Could not find model\\.xml in '.*'\\.?$",
                RegexOptions.IgnoreCase))
        {
            return true;
        }
    
        if (Regex.IsMatch(
                message,
                "^Could not find workspace metadata (starting from|under) '.*'\\.?$",
                RegexOptions.IgnoreCase))
        {
            return true;
        }
    
        var mentionsWorkspaceConfig = message.Contains("workspace.xml", StringComparison.OrdinalIgnoreCase);
        return mentionsWorkspaceConfig &&
               message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    int PrintGenerationError(string code, string message)
    {
        var normalized = NormalizeErrorMessage(message);
        var looksLikeXml = Regex.IsMatch(normalized, "Line\\s+\\d+,\\s+position\\s+\\d+", RegexOptions.IgnoreCase);
        if (looksLikeXml)
        {
            return PrintDataError("E_WORKSPACE_XML_INVALID", normalized);
        }
    
        var next = BuildNextHelpHintForCurrentArgs();
        var hints = string.IsNullOrWhiteSpace(next)
            ? Array.Empty<string>()
            : new[] { $"Next: {next}" };
        return PrintFormattedError(code, normalized, exitCode: 5, hints: hints);
    }
}

