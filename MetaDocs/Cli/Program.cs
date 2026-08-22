using System.Text;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using MetaCli;
using MetaCli.Core;
using MetaDocs;
using MetaDocs.Core;
using Meta.Integration;
using Meta.Surfaces;

internal static class Program
{
    private const string AppName = "meta-docs";
    private const string ApplicationId = "app-meta-docs";
    private const string CommandWorkspaceDirectoryName = "meta-docs.MetaCli";

    private static readonly ConsolePresenter Presenter = new();

    private static int Main(string[] args)
    {
        if (CliVersion.TryWriteVersion(Presenter, AppName, args, out var versionExitCode))
        {
            return versionExitCode;
        }

        var exitCode = 0;
        var runtime = new MetaCliRuntime<MetaDocsModel>(
                CommandWorkspacePath,
                ApplicationId,
                setExitCode: code => exitCode = code)
            .UseDefaultHelp(options: new MetaCliHelpOptions("meta-docs browse"))
            .BindTarget("exec-author-page", OutputWorkspace(), RunAuthorPage)
            .BindReadOnly("exec-browse", RunBrowse)
            .BindReadOnly("exec-cli-matrix", RunCliMatrix)
            .BindReadOnly("exec-contents", RunContents)
            .BindReadOnly("exec-search", RunSearch)
            .Bind("exec-update-description", RunUpdateDescription)
            .BindTarget("exec-import-cli", OutputWorkspace(), RunImportCli)
            .BindTarget("exec-import-workspace-model", OutputWorkspace(), RunImportWorkspaceModel)
            .Bind(
                "exec-import-workspace-instances",
                [MetaCliWorkspace.Open("source-workspace")],
                RunImportWorkspaceInstances)
            .Bind("exec-include-instance-entity", RunIncludeInstanceEntity)
            .Bind("exec-include-instance-property", RunIncludeInstanceProperty)
            .Bind("exec-include-instance-relationship", RunIncludeInstanceRelationship)
            .Bind(
                "exec-merge",
                [MetaCliWorkspace.Create(
                    "output",
                    "output-xml",
                    "output-csharp",
                    "output-sql",
                    "output-connection-env")],
                RunMerge)
            .BindReadOnly("exec-validate", RunValidate)
            .BindReadOnly("exec-render-site", RunRenderSite);

        runtime.Run(args);
        return exitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static async Task RunAuthorPage(
        MetaCliInvocation invocation,
        MetaDocsModel model,
        MetaCliWorkspaces workspaces)
    {
        try
        {
            var page = new MetaDocsAuthoredPage(
                invocation.Required("id"),
                invocation.Required("title"),
                invocation.Required("summary"),
                invocation.Required("body"),
                Optional(invocation, "subject-type", "Guide"),
                Optional(invocation, "path"),
                Optional(invocation, "parent"),
                Optional(invocation, "slot", "Summary"),
                string.Empty,
                Optional(invocation, "source-id", "source:authored:metametabi-docs"),
                Optional(invocation, "source-name", "Authored MetaDocs pages"),
                ParseBoolean(Optional(invocation, "view-root")),
                Optional(invocation, "navigation-title"));
            var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
            await CreateOutputWhenSelectedAsync(invocation, model, workspaces).ConfigureAwait(false);
            Presenter.WriteInfo($"Authored page: {subject.DisplayName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot author MetaDocs page. {exception.Message}", exception);
        }
    }

    private static void RunBrowse(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var result = new MetaDocsBrowseService().Browse(model, Optional(invocation, "path"));
            Presenter.WriteInfo(result.Text);
            if (!result.Succeeded)
            {
                throw new MetaCliExitException(2);
            }
        }
        catch (MetaCliExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Cannot browse documentation. {exception.Message}", exception);
        }
    }

    private static void RunCliMatrix(MetaCliInvocation invocation, MetaDocsModel model)
    {
        var outputPath = Path.GetFullPath(invocation.Required("out"));
        var view = FirstNonEmpty(Optional(invocation, "view"), "commands").ToLowerInvariant();
        try
        {
            var matrix = new MetaDocsCliMatrixService().Build(model);
            MetaDocsCliMeshInvocationMatrix? invocationMatrix = null;
            var (csv, rowCount) = view switch
            {
                "commands" => (FormatCliCommandsCsv(matrix), matrix.Commands.Count),
                "cohorts" => (FormatCliDecisionCohortsCsv(matrix), matrix.DecisionCohorts.Count),
                "findings" => (FormatCliFindingsCsv(matrix), matrix.Findings.Count),
                "invocations" => BuildInvocationCsv(),
                _ => throw new MetaCliExitException(2, "--view must be commands, cohorts, findings, or invocations."),
            };
            if (invocation.Flag("require-conformant") && invocationMatrix is null)
            {
                throw new MetaCliExitException(2, "--require-conformant requires --view invocations.");
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException($"Could not resolve the output directory for '{outputPath}'.");
            }

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(
                outputPath,
                csv,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Presenter.WriteInfo($"Wrote CLI matrix ({view}, {rowCount} row(s)): {outputPath}");
            Presenter.WriteInfo(
                $"Coverage: {matrix.ApplicationCount} application(s), {matrix.CommandCount} command(s), " +
                $"{matrix.ParameterCount} parameter(s), {matrix.ParameterGroupCount} parameter group(s).");
            Presenter.WriteInfo(
                $"Classification: {matrix.CommandCount - matrix.UnclassifiedCount} classified, " +
                $"{matrix.UnclassifiedCount} unclassified, {matrix.Findings.Count} finding occurrence(s), " +
                $"{matrix.DecisionCohorts.Count} decision cohort(s).");
            foreach (var findingGroup in matrix.Findings
                         .GroupBy(static finding => finding.Code, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var affectedRowCount = findingGroup
                    .SelectMany(static finding => finding.AffectedCommands.Select(command => CommandKey(finding.Application, command)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                Presenter.WriteInfo(
                    $"  {findingGroup.Key}: {findingGroup.Count()} finding(s), {affectedRowCount} command row(s) - " +
                    findingGroup.First().Message);
            }

            if (invocationMatrix is not null)
            {
                Presenter.WriteInfo(
                    $"Mesh invocations: {invocationMatrix.MeshCount} mesh(es), " +
                    $"{invocationMatrix.ConformantCount} conformant, {invocationMatrix.ViolationCount} violation(s).");
                if (invocation.Flag("require-conformant") && invocationMatrix.ViolationCount != 0)
                {
                    throw new MetaCliExitException(
                        4,
                        $"CLI mesh conformance failed with {invocationMatrix.ViolationCount} violating invocation(s). See: {outputPath}");
                }
            }

            (string Csv, int Count) BuildInvocationCsv()
            {
                var roots = invocation.Values("mesh-root");
                if (roots.Count == 0)
                {
                    throw new MetaCliExitException(2, "--view invocations requires at least one --mesh-root.");
                }

                var service = new MetaDocsCliMeshInvocationService();
                invocationMatrix = service.Build(matrix, service.LoadSources(roots));
                return (FormatCliInvocationsCsv(invocationMatrix), invocationMatrix.Invocations.Count);
            }
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot generate CLI matrix. Output: {outputPath}. {exception.Message}", exception);
        }
    }

    private static void RunContents(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var contents = new MetaDocsNavigationService().Contents(
                model,
                Optional(invocation, "view"),
                ParseDepth(Optional(invocation, "depth")));
            Presenter.WriteInfo(FormatContents(contents));
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot list MetaDocs contents. {exception.Message}", exception);
        }
    }

    private static void RunSearch(MetaCliInvocation invocation, MetaDocsModel model)
    {
        var query = Optional(invocation, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            Presenter.WriteInfo(FormatMissingSearchQuery());
            throw new MetaCliExitException(2);
        }

        try
        {
            var matches = new MetaDocsQueryService().Search(
                model,
                query,
                Optional(invocation, "subject-type"),
                ParseLimit(Optional(invocation, "limit")));
            Presenter.WriteInfo(MetaDocsQueryService.FormatSearchResults(query, matches));
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot search documentation. {exception.Message}", exception);
        }
    }

    private static void RunUpdateDescription(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var body = ReadBody(invocation);
            var narrative = new MetaDocsQueryService().UpsertDescription(
                model,
                SubjectSelector(invocation),
                Optional(invocation, "slot", "Summary"),
                Optional(invocation, "title"),
                body);
            Presenter.WriteInfo($"Updated description: {narrative.DocumentationSubject.Id} ({narrative.Slot}).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update documentation description. {exception.Message}", exception);
        }
    }

    private static async Task RunImportCli(
        MetaCliInvocation invocation,
        MetaDocsModel model,
        MetaCliWorkspaces workspaces)
    {
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var cli = TypedWorkspaceModelMapper.Load<MetaCliModel>(sourceWorkspace, searchUpward: false);
            var application = new MetaDocsCliImporter().ImportApplication(
                model,
                cli,
                applicationId: Optional(invocation, "application"),
                parentSubjectId: Optional(invocation, "parent-subject"),
                sourceId: Optional(invocation, "source-id"));
            await CreateOutputWhenSelectedAsync(invocation, model, workspaces).ConfigureAwait(false);
            var commandCount = CountCurrentChildren(model, application, "CliCommand");
            Presenter.WriteInfo($"Refreshed CLI docs: {application.DisplayName} ({commandCount} command(s)).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import CLI documentation. Source workspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static async Task RunImportWorkspaceModel(
        MetaCliInvocation invocation,
        MetaDocsModel model,
        MetaCliWorkspaces workspaces)
    {
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var modelSubject = await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
                model,
                sourceWorkspace,
                Optional(invocation, "source-id"),
                Optional(invocation, "display-name"),
                Optional(invocation, "parent-subject")).ConfigureAwait(false);
            await CreateOutputWhenSelectedAsync(invocation, model, workspaces).ConfigureAwait(false);
            var entityCount = CountCurrentChildren(model, modelSubject, "Entity");
            Presenter.WriteInfo($"Refreshed model docs: {modelSubject.DisplayName} ({entityCount} entity subject(s)).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import workspace model documentation. SourceWorkspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportWorkspaceInstances(
        MetaCliInvocation invocation,
        MetaDocsModel model,
        MetaCliWorkspaces workspaces)
    {
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var result = new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
                model,
                workspaces.Required("source-workspace"),
                sourceWorkspace,
                Optional(invocation, "source-id"),
                Optional(invocation, "model-source-id"),
                Optional(invocation, "display-name")).GetAwaiter().GetResult();
            Presenter.WriteInfo($"Imported {result.ImportedInstanceCount} instance subject(s), {result.ImportedPropertyFactCount} property fact(s), {result.ImportedRelationshipCount} relationship(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import workspace instance documentation. Source workspace: {sourceWorkspace}. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceEntity(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeEntity(
                model,
                invocation.Required("entity"),
                Optional(invocation, "source-id"),
                Optional(invocation, "display-name-property"),
                Optional(invocation, "summary-property"));
            Presenter.WriteInfo($"Included instance entity policy: {spec.EntityName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance entity policy. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceProperty(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeProperty(
                model,
                invocation.Required("entity"),
                invocation.Required("property"),
                Optional(invocation, "source-id"));
            Presenter.WriteInfo($"Included instance property policy: {invocation.Required("entity")}.{spec.PropertyName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance property policy. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceRelationship(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeRelationship(
                model,
                invocation.Required("entity"),
                invocation.Required("relationship"),
                Optional(invocation, "source-id"));
            Presenter.WriteInfo($"Included instance relationship policy: {invocation.Required("entity")}.{spec.RelationshipSelector}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance relationship policy. {exception.Message}", exception);
        }
    }

    private static async Task RunMerge(
        MetaCliInvocation invocation,
        MetaCliWorkspaces workspaces)
    {
        var outputWorkspace = MetaCliWorkspace.OutputLocation(
            invocation,
            "output-xml",
            "output-csharp",
            "output-sql");
        try
        {
            var models = new List<MetaDocsModel>();
            foreach (var include in invocation.Values("include"))
            {
                models.Add(TypedWorkspaceModelMapper.LoadAsync<MetaDocsModel>(include, searchUpward: false).GetAwaiter().GetResult());
            }

            var merged = new MetaDocsSuiteMerger().MergeIntoNew(models);
            var outputMetaPath = Path.Combine(Path.GetFullPath(outputWorkspace), WorkspaceMetaFile.FileName);
            if (File.Exists(outputMetaPath))
            {
                TypedWorkspaceModelMapper.Save(merged, outputWorkspace);
            }
            else
            {
                await workspaces.CreateAsync("output", merged).ConfigureAwait(false);
            }
            Presenter.WriteInfo($"Rebuilt suite workspace: {Path.GetFullPath(outputWorkspace)}");
            Presenter.WriteInfo($"Included {models.Count} source workspace(s), {merged.DocumentationSourceList.Count} documentation source(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot merge MetaDocs workspaces. Output: {Path.GetFullPath(outputWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunRenderSite(MetaCliInvocation invocation, MetaDocsModel model)
    {
        var outputDirectory = invocation.Required("out");
        try
        {
            var html = new MetametabiDocsSiteRenderer().RenderSite(model);
            var outputRoot = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputRoot);
            var outputPath = Path.Combine(outputRoot, "docs.html");
            File.WriteAllText(outputPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            WriteThemeAssets(model, outputRoot);
            Presenter.WriteInfo($"Wrote {outputPath}");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot render MetaDocs site. Output: {Path.GetFullPath(outputDirectory)}. {exception.Message}", exception);
        }
    }

    private static void RunValidate(MetaCliInvocation invocation, MetaDocsModel model)
    {
        try
        {
            var result = new MetaDocsValidationService().Validate(
                model,
                new MetaDocsValidationOptions
                {
                    IncludeDescriptionDiagnostics = invocation.Flag("include-description-diagnostics"),
                });
            PrintValidationResult(result);
            if (result.HasErrors(invocation.Flag("warnings-as-errors")))
            {
                throw new MetaCliExitException(2);
            }
        }
        catch (MetaCliExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Cannot validate MetaDocs workspace. {exception.Message}", exception);
        }
    }

    private static IReadOnlyList<MetaCliWorkspaceParameter> OutputWorkspace() =>
        [MetaCliWorkspace.Create(
            "output",
            "output-xml",
            "output-csharp",
            "output-sql",
            "output-connection-env")];

    private static async Task CreateOutputWhenSelectedAsync(
        MetaCliInvocation invocation,
        MetaDocsModel model,
        MetaCliWorkspaces workspaces)
    {
        if (MetaCliWorkspace.OptionalOutputLocation(invocation) is not null)
        {
            await workspaces.CreateAsync("output", model).ConfigureAwait(false);
        }
    }

    private static string Optional(MetaCliInvocation invocation, string parameter, string defaultValue = "")
    {
        var value = invocation.Optional(parameter);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static MetaDocsSubjectSelector SubjectSelector(MetaCliInvocation invocation) =>
        new(
            Optional(invocation, "subject"),
            Optional(invocation, "model"),
            Optional(invocation, "cli"),
            Optional(invocation, "command"),
            Optional(invocation, "option"));

    private static string ReadBody(MetaCliInvocation invocation)
    {
        var inlineBody = Optional(invocation, "body");
        var stdin = invocation.Flag("body-stdin");
        if (!string.IsNullOrWhiteSpace(inlineBody) && stdin)
        {
            throw new MetaCliExitException(2, "Use either --body <text> or --body-stdin, not both.");
        }

        if (stdin)
        {
            return MetaCliStandardInput.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(inlineBody))
        {
            throw new MetaCliExitException(2, "Provide --body <text> or --body-stdin.");
        }

        return inlineBody;
    }

    private static int ParseLimit(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 25;
        }

        if (int.TryParse(value, out var limit) && limit > 0)
        {
            return limit;
        }

        throw new MetaCliExitException(2, "--limit must be a positive integer.");
    }

    private static int ParseDepth(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 4;
        }

        if (int.TryParse(value, out var depth) && depth > 0)
        {
            return depth;
        }

        throw new MetaCliExitException(2, "--depth must be a positive integer.");
    }

    private static bool ParseBoolean(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static string FormatContents(MetaDocsContentsResult contents)
    {
        var builder = new StringBuilder();
        builder.AppendLine(contents.Title);
        foreach (var node in contents.Nodes)
        {
            AppendContentNode(builder, node, 0);
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCliCommandsCsv(MetaDocsCliMatrix matrix)
    {
        var findingsByCommand = matrix.Findings
            .SelectMany(static finding => finding.AffectedCommands.Select(command => new
            {
                Key = CommandKey(finding.Application, command),
                Finding = finding,
            }))
            .GroupBy(static finding => finding.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static item => item.Finding).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        AppendCsvRow(
            builder,
            "ApplicationId",
            "Application",
            "CommandId",
            "Command",
            "CommandPath",
            "SurfaceVerb",
            "RoutePattern",
            "ActionFamily",
            "OperationIntent",
            "Subject",
            "SubjectScope",
            "SubjectSelection",
            "InputPattern",
            "ResultPattern",
            "SideEffect",
            "ClassificationStatus",
            "WorkspaceInputs",
            "OutputMode",
            "ParameterCount",
            "Parameters",
            "ParameterGroupCount",
            "ParameterGroups",
            "Findings",
            "FindingEvidence",
            "Summary");

        foreach (var row in matrix.Commands)
        {
            var commandKey = CommandKey(row.Application, row.Command);
            findingsByCommand.TryGetValue(commandKey, out var rowFindings);
            rowFindings ??= [];
            AppendCsvRow(
                builder,
                row.ApplicationId,
                row.Application,
                row.CommandId,
                row.Command,
                row.CommandPath,
                row.Verb,
                row.RoutePattern,
                row.ActionFamily,
                row.OperationIntent,
                row.Subject,
                row.SubjectScope,
                row.SubjectSelection,
                row.InputPattern,
                row.ResultPattern,
                row.SideEffect,
                row.ClassificationStatus,
                string.Join(" | ", row.WorkspaceInputs),
                row.OutputMode,
                (row.Options.Count + row.Arguments.Count).ToString(),
                string.Join(" | ", row.Options.Concat(row.Arguments).Select(FormatCliMatrixParameter)),
                row.ParameterGroups.Count.ToString(),
                string.Join(" | ", row.ParameterGroups.Select(FormatCliMatrixParameterGroup)),
                string.Join(" | ", rowFindings.Select(static finding => finding.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)),
                string.Join(" | ", rowFindings.Select(static finding => $"{finding.Code}: {finding.Evidence}").Distinct(StringComparer.Ordinal).OrderBy(static evidence => evidence, StringComparer.OrdinalIgnoreCase)),
                row.Summary);
        }

        return builder.ToString();
    }

    private static string FormatCliDecisionCohortsCsv(MetaDocsCliMatrix matrix)
    {
        var builder = new StringBuilder();
        AppendCsvRow(
            builder,
            "DecisionKey",
            "Code",
            "Category",
            "Decision",
            "OperationIntent",
            "ActionFamily",
            "SubjectScope",
            "Subject",
            "SubjectSelection",
            "InputPattern",
            "ResultPattern",
            "SideEffect",
            "SurfaceVerbs",
            "RoutePatterns",
            "ApplicationCount",
            "Applications",
            "CommandCount",
            "Commands",
            "Evidence");
        foreach (var cohort in matrix.DecisionCohorts)
        {
            AppendCsvRow(
                builder,
                cohort.DecisionKey,
                cohort.Code,
                cohort.Category,
                cohort.Title,
                string.Join(" | ", cohort.OperationIntents),
                string.Join(" | ", cohort.ActionFamilies),
                string.Join(" | ", cohort.SubjectScopes),
                string.Join(" | ", cohort.Subjects),
                string.Join(" | ", cohort.SubjectSelections),
                string.Join(" | ", cohort.InputPatterns),
                string.Join(" | ", cohort.ResultPatterns),
                string.Join(" | ", cohort.SideEffects),
                string.Join(" | ", cohort.SurfaceVerbs),
                string.Join(" | ", cohort.RoutePatterns),
                cohort.ApplicationCount.ToString(),
                string.Join(" | ", cohort.Applications),
                cohort.CommandCount.ToString(),
                string.Join(" | ", cohort.Commands),
                string.Join(" | ", cohort.Evidence));
        }

        return builder.ToString();
    }

    private static string FormatCliFindingsCsv(MetaDocsCliMatrix matrix)
    {
        var builder = new StringBuilder();
        AppendCsvRow(
            builder,
            "Code",
            "Category",
            "DecisionKey",
            "Application",
            "Command",
            "AffectedCommands",
            "Finding",
            "Evidence");
        foreach (var finding in matrix.Findings)
        {
            AppendCsvRow(
                builder,
                finding.Code,
                finding.Category,
                finding.DecisionKey,
                finding.Application,
                finding.Command,
                string.Join(" | ", finding.AffectedCommands),
                finding.Message,
                finding.Evidence);
        }

        return builder.ToString();
    }

    private static string FormatCliInvocationsCsv(MetaDocsCliMeshInvocationMatrix matrix)
    {
        var builder = new StringBuilder();
        AppendCsvRow(
            builder,
            "MeshWorkspace",
            "Mesh",
            "Operation",
            "StepIndex",
            "Step",
            "Executable",
            "Arguments",
            "Application",
            "Command",
            "Status",
            "Codes",
            "Evidence");
        foreach (var row in matrix.Invocations)
        {
            AppendCsvRow(
                builder,
                row.MeshWorkspace,
                row.Mesh,
                row.Operation,
                row.StepIndex.ToString(),
                row.Step,
                row.Executable,
                row.Arguments,
                row.Application,
                row.Command,
                row.Status,
                string.Join(" | ", row.Codes),
                string.Join(" | ", row.Evidence));
        }

        return builder.ToString();
    }

    private static string FormatCliMatrixParameter(MetaDocsCliMatrixParameter parameter)
    {
        var details = new List<string>
        {
            $"parameter={FirstNonEmpty(parameter.ParameterId, parameter.SubjectId)}",
            $"shape={FirstNonEmpty(parameter.ValueShape, "none")}",
            $"arity={FirstNonEmpty(parameter.ValueArity, "none")}",
            $"required={FormatNullableBoolean(parameter.Required)}",
            $"repeatable={FormatNullableBoolean(parameter.Repeatable)}",
        };
        if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
        {
            details.Add($"default={parameter.DefaultValue}");
        }

        if (parameter.Aliases.Count != 0)
        {
            details.Add($"aliases={string.Join("/", parameter.Aliases)}");
        }

        if (parameter.AllowedValues.Count != 0)
        {
            details.Add($"values={string.Join("/", parameter.AllowedValues)}");
        }

        var valueName = string.IsNullOrWhiteSpace(parameter.ValueName) ? string.Empty : " " + parameter.ValueName;
        return $"{parameter.Kind}:{parameter.Name}{valueName} [{string.Join(";", details)}]";
    }

    private static string FormatCliMatrixParameterGroup(MetaDocsCliMatrixParameterGroup group) =>
        $"{group.Name} [required={FormatNullableBoolean(group.Required)};multiple={FormatNullableBoolean(group.AllowsMultiple)}]:" +
        string.Join("/", group.Members);

    private static string FormatNullableBoolean(bool? value) =>
        value is null ? "unspecified" : value.Value ? "true" : "false";

    private static void AppendCsvRow(StringBuilder builder, params string?[] values)
    {
        builder.AppendLine(string.Join(",", values.Select(Csv)));
    }

    private static string Csv(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string CommandKey(string application, string command) =>
        $"{application}\u001f{command}";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static void AppendContentNode(StringBuilder builder, MetaDocsContentNode node, int depth)
    {
        builder.Append(new string(' ', depth * 2));
        builder.AppendLine(node.Title);
        foreach (var child in node.Children)
        {
            AppendContentNode(builder, child, depth + 1);
        }

        if (node.HasMoreChildren)
        {
            builder.Append(new string(' ', (depth + 1) * 2));
            builder.AppendLine("...");
        }
    }

    private static string FormatMissingSearchQuery() =>
        """
        Search needs text.

        Try:
          meta-docs search meta-sql
          meta-docs search deploy
          meta-docs search DocumentationSubject
          meta-docs browse
        """;

    private static void WriteThemeAssets(MetaDocsModel model, string outputDirectory)
    {
        var outputRoot = Path.GetFullPath(outputDirectory);
        foreach (var asset in model.DocumentationThemeAssetList
                     .Where(asset => !MetaDocsVocabulary.IsThemeAssetType(asset, "Css"))
                     .Where(asset => !string.IsNullOrWhiteSpace(asset.Content))
                     .Where(asset => !string.IsNullOrWhiteSpace(asset.Href)))
        {
            var href = asset.Href!.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(href) ||
                Uri.TryCreate(href, UriKind.Absolute, out _) ||
                href.Split('/').Any(static part => part == ".."))
            {
                throw new InvalidOperationException($"Theme asset '{asset.Id}' has an unsafe or non-local href '{asset.Href}'.");
            }

            var outputPath = Path.GetFullPath(Path.Combine(outputRoot, href));
            if (!outputPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Theme asset '{asset.Id}' resolves outside the output directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(
                outputPath,
                asset.Content!,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static int CountCurrentChildren(MetaDocsModel model, DocumentationSubject parent, string subjectType) =>
        model.DocumentationSubjectList.Count(row =>
            string.Equals(row.ParentSubject?.Id ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase) &&
            MetaDocsVocabulary.IsSubjectType(row, subjectType) &&
            !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase));

    private static void PrintValidationResult(MetaDocsValidationResult result)
    {
        Presenter.WriteInfo($"Diagnostics: {result.ErrorCount} error(s), {result.WarningCount} warning(s), {result.InfoCount} info.");
        if (result.Diagnostics.Count == 0)
        {
            return;
        }

        Presenter.WriteTable(
            new[] { "Severity", "Id", "Code", "Location", "Message" },
            result.Diagnostics
                .OrderBy(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(diagnostic => diagnostic.Location, StringComparer.OrdinalIgnoreCase)
                .Select(diagnostic => new[]
                {
                    diagnostic.Severity.ToString(),
                    diagnostic.Id,
                    diagnostic.Code,
                    diagnostic.Location,
                    diagnostic.Message,
                })
                .ToArray());
    }
}
