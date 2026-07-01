using System.Text;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using MetaCli;
using MetaCli.Core;
using MetaDocs;
using MetaDocs.Core;

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
            .UseDefaultHelp()
            .Bind("exec-author-page", RunAuthorPage)
            .Bind("exec-import-cli", RunImportCli)
            .Bind("exec-import-command-prose", RunImportCommandProse)
            .Bind("exec-import-workspace-model", RunImportWorkspaceModel)
            .Bind("exec-import-workspace-instances", RunImportWorkspaceInstances)
            .Bind("exec-include-instance-entity", RunIncludeInstanceEntity)
            .Bind("exec-include-instance-property", RunIncludeInstanceProperty)
            .Bind("exec-include-instance-relationship", RunIncludeInstanceRelationship)
            .Bind("exec-merge", RunMerge)
            .Bind("exec-validate", RunValidate)
            .Bind("exec-render-site", RunRenderSite);

        runtime.Run(args);
        return exitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static void RunAuthorPage(MetaCliInvocation invocation)
    {
        var workspace = RequireOneWorkspaceTarget(invocation);
        try
        {
            var model = LoadOrCreate(workspace.ExistingWorkspace);
            var page = new MetaDocsAuthoredPage(
                invocation.Required("id"),
                invocation.Required("title"),
                invocation.Required("summary"),
                invocation.Required("body"),
                Optional(invocation, "kind", "Guide"),
                Optional(invocation, "path"),
                Optional(invocation, "parent"),
                Optional(invocation, "slot", "Summary"),
                string.Empty,
                Optional(invocation, "source-id", "source:authored:metametabi-docs"),
                Optional(invocation, "source-name", "Authored MetaDocs pages"));
            var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            Presenter.WriteInfo($"Authored page: {subject.DisplayName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot author MetaDocs page. Workspace: {Path.GetFullPath(workspace.OutputWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportCli(MetaCliInvocation invocation)
    {
        var workspace = RequireOneWorkspaceTarget(invocation);
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var model = LoadOrCreate(workspace.ExistingWorkspace);
            var cli = MetaCliModel.LoadFromXmlWorkspace(sourceWorkspace, searchUpward: false);
            var application = new MetaDocsCliImporter().ImportApplication(
                model,
                cli,
                applicationId: Optional(invocation, "application"),
                groupName: Optional(invocation, "group"),
                sourceId: Optional(invocation, "source-id"));
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            var commandCount = CountCurrentChildren(model, application, "CliCommand");
            Presenter.WriteInfo($"Imported {application.DisplayName}: {commandCount} command(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import CLI documentation. Source workspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportCommandProse(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        var sourceRoot = invocation.Required("source-root");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var result = new MetaDocsMarkdownCommandProseImporter().ImportCommandProseAsync(
                model,
                sourceRoot,
                Optional(invocation, "source-id")).GetAwaiter().GetResult();
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Imported command prose from {result.SourceFileCount} markdown file(s): {result.MatchedApplicationCount} app(s), {result.MatchedCommandCount} command(s), {result.ImportedNarrativeCount} narrative(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import command prose. Workspace: {Path.GetFullPath(workspace)}. SourceRoot: {Path.GetFullPath(sourceRoot)}. {exception.Message}", exception);
        }
    }

    private static void RunImportWorkspaceModel(MetaCliInvocation invocation)
    {
        var workspace = RequireOneWorkspaceTarget(invocation);
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var model = LoadOrCreate(workspace.ExistingWorkspace);
            var root = new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
                model,
                sourceWorkspace,
                Optional(invocation, "source-id"),
                Optional(invocation, "display-name")).GetAwaiter().GetResult();
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            var entityCount = CountCurrentChildren(model, FindModelSubject(model, root) ?? root, "Entity");
            Presenter.WriteInfo($"Imported {root.DisplayName}: {entityCount} entity subject(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import workspace model documentation. SourceWorkspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportWorkspaceInstances(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var result = new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
                model,
                sourceWorkspace,
                Optional(invocation, "source-id"),
                Optional(invocation, "model-source-id"),
                Optional(invocation, "display-name")).GetAwaiter().GetResult();
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Imported {result.ImportedInstanceCount} instance subject(s), {result.ImportedPropertyFactCount} property fact(s), {result.ImportedRelationshipCount} relationship(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import workspace instance documentation. SourceWorkspace: {Path.GetFullPath(sourceWorkspace)}. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceEntity(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeEntity(
                model,
                invocation.Required("entity"),
                Optional(invocation, "source-id"),
                Optional(invocation, "display-name-property"),
                Optional(invocation, "summary-property"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Included instance entity policy: {spec.EntityName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance entity policy. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceProperty(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeProperty(
                model,
                invocation.Required("entity"),
                invocation.Required("property"),
                Optional(invocation, "source-id"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Included instance property policy: {invocation.Required("entity")}.{spec.PropertyName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance property policy. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunIncludeInstanceRelationship(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeRelationship(
                model,
                invocation.Required("entity"),
                invocation.Required("relationship"),
                Optional(invocation, "source-id"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Included instance relationship policy: {invocation.Required("entity")}.{spec.RelationshipSelector}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update instance relationship policy. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunMerge(MetaCliInvocation invocation)
    {
        var outputWorkspace = invocation.Required("workspace");
        try
        {
            var models = new List<MetaDocsModel>();
            foreach (var include in invocation.Values("include"))
            {
                models.Add(MetaDocsModel.LoadFromXmlWorkspaceAsync(include, searchUpward: false).GetAwaiter().GetResult());
            }

            var merged = new MetaDocsSuiteMerger().MergeIntoNew(models);
            merged.SaveToXmlWorkspace(outputWorkspace);
            Presenter.WriteInfo($"Merged {models.Count} workspace(s): {merged.DocumentationSourceList.Count} source(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot merge MetaDocs workspaces. Output: {Path.GetFullPath(outputWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunRenderSite(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        var outputDirectory = invocation.Required("out");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
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
            throw new InvalidOperationException($"Cannot render MetaDocs site. Workspace: {Path.GetFullPath(workspace)}. Output: {Path.GetFullPath(outputDirectory)}. {exception.Message}", exception);
        }
    }

    private static void RunValidate(MetaCliInvocation invocation)
    {
        var workspace = invocation.Required("workspace");
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var result = new MetaDocsValidationService().Validate(
                model,
                new MetaDocsValidationOptions
                {
                    IncludeProseDiagnostics = invocation.Flag("include-prose-diagnostics"),
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
            throw new InvalidOperationException($"Cannot validate MetaDocs workspace. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static MetaDocsModel LoadOrCreate(string workspacePath) =>
        string.IsNullOrWhiteSpace(workspacePath)
            ? MetaDocsModel.CreateEmpty()
            : MetaDocsModel.LoadFromXmlWorkspaceAsync(workspacePath, searchUpward: false).GetAwaiter().GetResult();

    private static (string ExistingWorkspace, string NewWorkspace, string OutputWorkspace) RequireOneWorkspaceTarget(
        MetaCliInvocation invocation)
    {
        var workspace = Optional(invocation, "workspace");
        var newWorkspace = Optional(invocation, "new-workspace");
        if (string.IsNullOrWhiteSpace(workspace) == string.IsNullOrWhiteSpace(newWorkspace))
        {
            throw new MetaCliExitException(2, "Provide exactly one of --workspace <path> or --new-workspace <path>.");
        }

        return (workspace, newWorkspace, string.IsNullOrWhiteSpace(workspace) ? newWorkspace : workspace);
    }

    private static string Optional(MetaCliInvocation invocation, string parameter, string defaultValue = "")
    {
        var value = invocation.Optional(parameter);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static void WriteThemeAssets(MetaDocsModel model, string outputDirectory)
    {
        var outputRoot = Path.GetFullPath(outputDirectory);
        foreach (var asset in model.DocumentationThemeAssetList
                     .Where(asset => !string.Equals(asset.AssetKind, "Css", StringComparison.OrdinalIgnoreCase))
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

    private static int CountCurrentChildren(MetaDocsModel model, DocumentationSubject parent, string kind) =>
        model.DocumentationSubjectList.Count(row =>
            string.Equals(row.ParentKey ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase));

    private static DocumentationSubject? FindModelSubject(MetaDocsModel model, DocumentationSubject root) =>
        model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.ParentKey ?? string.Empty, root.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Kind, "Model", StringComparison.OrdinalIgnoreCase));

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
