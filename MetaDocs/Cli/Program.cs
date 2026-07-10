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
            .UseDefaultHelp(options: new MetaCliHelpOptions("meta-docs browse"))
            .Bind("exec-author-page", RunAuthorPage)
            .Bind("exec-browse", RunBrowse)
            .Bind("exec-contents", RunContents)
            .Bind("exec-search", RunSearch)
            .Bind("exec-update-description", RunUpdateDescription)
            .Bind("exec-add-example", RunAddExample)
            .Bind("exec-add-example-section", RunAddExampleSection)
            .Bind("exec-add-example-code", RunAddExampleCode)
            .Bind("exec-import-cli", RunImportCli)
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
                Optional(invocation, "subject-type", "Guide"),
                Optional(invocation, "path"),
                Optional(invocation, "parent"),
                Optional(invocation, "slot", "Summary"),
                string.Empty,
                Optional(invocation, "source-id", "source:authored:metametabi-docs"),
                Optional(invocation, "source-name", "Authored MetaDocs pages"),
                ParseBoolean(Optional(invocation, "view-root")),
                Optional(invocation, "navigation-title"),
                Optional(invocation, "body-format", "Markdown"));
            var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            Presenter.WriteInfo($"Authored page: {subject.DisplayName}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot author MetaDocs page. Workspace: {Path.GetFullPath(workspace.OutputWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunBrowse(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrDiscovered(invocation);
        try
        {
            var model = LoadMetaDocsWorkspace(workspace);
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
            throw new InvalidOperationException($"Cannot browse documentation. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunContents(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrDiscovered(invocation);
        try
        {
            var model = LoadMetaDocsWorkspace(workspace);
            var contents = new MetaDocsNavigationService().Contents(
                model,
                Optional(invocation, "view"),
                ParseDepth(Optional(invocation, "depth")));
            Presenter.WriteInfo(FormatContents(contents));
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot list MetaDocs contents. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunSearch(MetaCliInvocation invocation)
    {
        var query = Optional(invocation, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            Presenter.WriteInfo(FormatMissingSearchQuery());
            throw new MetaCliExitException(2);
        }

        var workspace = WorkspaceOrDiscovered(invocation);
        try
        {
            var model = LoadMetaDocsWorkspace(workspace);
            var matches = new MetaDocsQueryService().Search(
                model,
                query,
                Optional(invocation, "subject-type"),
                ParseLimit(Optional(invocation, "limit")));
            Presenter.WriteInfo(MetaDocsQueryService.FormatSearchResults(query, matches));
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot search documentation. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunUpdateDescription(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
        try
        {
            var body = ReadBody(invocation);
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var narrative = new MetaDocsQueryService().UpsertDescription(
                model,
                SubjectSelector(invocation),
                Optional(invocation, "slot", "Summary"),
                Optional(invocation, "title"),
                body,
                Optional(invocation, "body-format", "Markdown"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Updated description: {narrative.DocumentationSubject.Id} ({narrative.Slot}).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot update documentation description. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunAddExample(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
        try
        {
            var body = ReadBody(invocation);
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var example = new MetaDocsExampleAuthoringService().UpsertExample(
                model,
                SubjectSelector(invocation),
                invocation.Required("id"),
                invocation.Required("title"),
                Optional(invocation, "summary"),
                invocation.Required("section-id"),
                body,
                Optional(invocation, "body-format", "PlainText"),
                Optional(invocation, "previous-example"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Added example: {example.Title}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot add documentation example. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunAddExampleSection(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
        try
        {
            var body = ReadBody(invocation);
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var section = new MetaDocsExampleAuthoringService().UpsertSection(
                model,
                invocation.Required("example"),
                invocation.Required("id"),
                Optional(invocation, "title"),
                body,
                Optional(invocation, "body-format", "PlainText"),
                Optional(invocation, "previous-section"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Added example section: {section.Id}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot add documentation example section. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
        }
    }

    private static void RunAddExampleCode(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
        try
        {
            var code = ReadCode(invocation);
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
            var codeRow = new MetaDocsExampleAuthoringService().UpsertCode(
                model,
                invocation.Required("section"),
                invocation.Required("id"),
                Optional(invocation, "title"),
                Optional(invocation, "language"),
                code,
                Optional(invocation, "previous-code"));
            model.SaveToXmlWorkspace(workspace);
            Presenter.WriteInfo($"Added example code: {codeRow.Id}.");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot add documentation example code. Workspace: {Path.GetFullPath(workspace)}. {exception.Message}", exception);
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
                parentSubjectId: Optional(invocation, "parent-subject"),
                sourceId: Optional(invocation, "source-id"));
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            var commandCount = CountCurrentChildren(model, application, "CliCommand");
            Presenter.WriteInfo($"Refreshed CLI docs: {application.DisplayName} ({commandCount} command(s)).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import CLI documentation. Source workspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportWorkspaceModel(MetaCliInvocation invocation)
    {
        var workspace = RequireOneWorkspaceTarget(invocation);
        var sourceWorkspace = invocation.Required("source-workspace");
        try
        {
            var model = LoadOrCreate(workspace.ExistingWorkspace);
            var modelSubject = new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
                model,
                sourceWorkspace,
                Optional(invocation, "source-id"),
                Optional(invocation, "display-name"),
                Optional(invocation, "parent-subject")).GetAwaiter().GetResult();
            model.SaveToXmlWorkspace(workspace.OutputWorkspace);
            var entityCount = CountCurrentChildren(model, modelSubject, "Entity");
            Presenter.WriteInfo($"Refreshed model docs: {modelSubject.DisplayName} ({entityCount} entity subject(s)).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot import workspace model documentation. SourceWorkspace: {Path.GetFullPath(sourceWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunImportWorkspaceInstances(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
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
        var workspace = WorkspaceOrCurrent(invocation);
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
        var workspace = WorkspaceOrCurrent(invocation);
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
        var workspace = WorkspaceOrCurrent(invocation);
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
        var outputWorkspace = WorkspaceOrCurrent(invocation);
        try
        {
            var models = new List<MetaDocsModel>();
            foreach (var include in invocation.Values("include"))
            {
                models.Add(MetaDocsModel.LoadFromXmlWorkspaceAsync(include, searchUpward: false).GetAwaiter().GetResult());
            }

            var merged = new MetaDocsSuiteMerger().MergeIntoNew(models);
            merged.SaveToXmlWorkspace(outputWorkspace);
            Presenter.WriteInfo($"Rebuilt suite workspace: {Path.GetFullPath(outputWorkspace)}");
            Presenter.WriteInfo($"Included {models.Count} source workspace(s), {merged.DocumentationSourceList.Count} documentation source(s).");
        }
        catch (Exception exception) when (exception is not MetaCliExitException)
        {
            throw new InvalidOperationException($"Cannot merge MetaDocs workspaces. Output: {Path.GetFullPath(outputWorkspace)}. {exception.Message}", exception);
        }
    }

    private static void RunRenderSite(MetaCliInvocation invocation)
    {
        var workspace = WorkspaceOrCurrent(invocation);
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
        var workspace = WorkspaceOrCurrent(invocation);
        try
        {
            var model = MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
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
        if (!string.IsNullOrWhiteSpace(workspace) && !string.IsNullOrWhiteSpace(newWorkspace))
        {
            throw new MetaCliExitException(2, "Provide only one of --workspace <path> or --new-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(workspace) && string.IsNullOrWhiteSpace(newWorkspace))
        {
            workspace = Directory.GetCurrentDirectory();
        }

        return (workspace, newWorkspace, string.IsNullOrWhiteSpace(workspace) ? newWorkspace : workspace);
    }

    private static string WorkspaceOrCurrent(MetaCliInvocation invocation) =>
        Optional(invocation, "workspace", Directory.GetCurrentDirectory());

    private static string WorkspaceOrDiscovered(MetaCliInvocation invocation)
    {
        var workspace = Optional(invocation, "workspace");
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            return workspace;
        }

        var current = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(current, "workspace.xml")))
        {
            return current;
        }

        return TryFindDefaultDocsWorkspace(current, out var discovered)
            ? discovered
            : current;
    }

    private static MetaDocsModel LoadMetaDocsWorkspace(string workspace)
    {
        if (!File.Exists(Path.Combine(workspace, "workspace.xml")))
        {
            throw new InvalidOperationException(MissingWorkspaceMessage(workspace));
        }

        return MetaDocsModel.LoadFromXmlWorkspaceAsync(workspace, searchUpward: false).GetAwaiter().GetResult();
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
            return Console.In.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(inlineBody))
        {
            throw new MetaCliExitException(2, "Provide --body <text> or --body-stdin.");
        }

        return inlineBody;
    }

    private static string ReadCode(MetaCliInvocation invocation)
    {
        var inlineCode = Optional(invocation, "code");
        var stdin = invocation.Flag("code-stdin");
        if (!string.IsNullOrWhiteSpace(inlineCode) && stdin)
        {
            throw new MetaCliExitException(2, "Use either --code <text> or --code-stdin, not both.");
        }

        if (stdin)
        {
            return Console.In.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(inlineCode))
        {
            throw new MetaCliExitException(2, "Provide --code <text> or --code-stdin.");
        }

        return inlineCode;
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

    private static string QuoteArgument(string value) =>
        value.Any(char.IsWhiteSpace) || value.Length == 0
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static string DisplayPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = Directory.GetCurrentDirectory();
        var relative = Path.GetRelativePath(current, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath
            : relative;
    }

    private static bool TryFindDefaultDocsWorkspace(string current, out string workspace)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(current, "MetaDocs", "Docs", "SuiteWorkspace"),
                     Path.Combine(current, "Docs", "SuiteWorkspace"),
                 })
        {
            if (File.Exists(Path.Combine(candidate, "workspace.xml")))
            {
                workspace = candidate;
                return true;
            }
        }

        workspace = string.Empty;
        return false;
    }

    private static string MissingWorkspaceMessage(string workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Current directory is not a MetaDocs workspace.");
        builder.AppendLine($"Workspace: {Path.GetFullPath(workspace)}");

        if (TryFindDefaultDocsWorkspace(Directory.GetCurrentDirectory(), out var discovered))
        {
            var relative = DisplayPath(discovered);
            builder.AppendLine();
            builder.AppendLine("Found a docs workspace:");
            builder.AppendLine($"  {relative}");
            builder.AppendLine();
            builder.AppendLine("Try:");
            builder.AppendLine($"  meta-docs browse --workspace {QuoteArgument(relative)}");
            builder.AppendLine($"  cd {QuoteArgument(relative)}");
            builder.AppendLine("  meta-docs browse");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("Try:");
            builder.AppendLine("  cd <MetaDocs workspace>");
            builder.AppendLine("  meta-docs browse");
        }

        return builder.ToString().TrimEnd();
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
