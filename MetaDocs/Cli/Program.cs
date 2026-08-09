using System.Text;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using MetaCli;
using MetaCli.Core;
using MetaDocs;
using MetaDocs.Core;
using Meta.Core.Serialization;

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
            .Bind("exec-merge", RunMerge)
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

    private static void RunMerge(MetaCliInvocation invocation)
    {
        var outputWorkspace = WorkspaceOrCurrent(invocation);
        try
        {
            var models = new List<MetaDocsModel>();
            foreach (var include in invocation.Values("include"))
            {
                models.Add(TypedWorkspaceModelMapper.LoadAsync<MetaDocsModel>(include, searchUpward: false).GetAwaiter().GetResult());
            }

            var merged = new MetaDocsSuiteMerger().MergeIntoNew(models);
            TypedWorkspaceModelMapper.Save(merged, outputWorkspace);
            MetaCliWorkspace.DescribeXml(outputWorkspace);
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

    private static string WorkspaceOrCurrent(MetaCliInvocation invocation) =>
        Optional(invocation, "workspace", Directory.GetCurrentDirectory());

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
