using System.Diagnostics;
using MetaCli;
using MetaCli.Core;
using MetaDocs;
using MetaDocs.Core;

namespace MetaDocs.Tests;

public sealed class MetaDocsRuntimeTests
{
    [Fact]
    public void Cli_HelpIsDerivedFromAuthoredMetaCliWorkspace()
    {
        var repoRoot = FindRepositoryRoot();
        var commandWorkspace = Path.Combine(repoRoot, "MetaDocs", "Cli", "meta-docs.MetaCli");
        var integrity = new MetaCliWorkspaceService().ValidateIntegrity(commandWorkspace);

        Assert.False(
            integrity.HasErrors,
            string.Join(Environment.NewLine, integrity.Issues.Select(issue => $"{issue.Code}: {issue.Message} ({issue.Location})")));

        var appHelp = RunCli("help");
        Assert.Equal(0, appHelp.ExitCode);
        Assert.Contains("meta-docs <command> [options]", appHelp.Output);
        Assert.Contains("browse", appHelp.Output);
        Assert.Contains("contents", appHelp.Output);
        Assert.Contains("import-cli", appHelp.Output);
        Assert.Contains("render-site", appHelp.Output);
        Assert.Contains("search", appHelp.Output);
        Assert.Contains("update-description", appHelp.Output);
        Assert.DoesNotContain("meta-docs read", appHelp.Output);
        Assert.DoesNotContain("meta-docs show", appHelp.Output);
        Assert.DoesNotContain("update-prose", appHelp.Output);
        Assert.Contains("Next: meta-docs browse", appHelp.Output);
        Assert.DoesNotContain("import-command-prose", appHelp.Output);

        var rootBrowse = RunCli("browse");
        Assert.Equal(0, rootBrowse.ExitCode);
        AssertBrowseHasNoRouteNoise(rootBrowse.Output);
        Assert.Contains("meta-docs browse cli", rootBrowse.Output);

        var commandHelp = RunCli("help import-cli");
        Assert.Equal(0, commandHelp.ExitCode);
        Assert.Contains("meta-docs import-cli", commandHelp.Output);
        Assert.Contains("Refresh CLI reference documentation", commandHelp.Output);
        Assert.Contains("authored descriptions", commandHelp.Output);
        Assert.Contains("--source-workspace <path>", commandHelp.Output);
        Assert.Contains("--new-workspace <path>", commandHelp.Output);

        var modelImportHelp = RunCli("help import-workspace-model");
        Assert.Equal(0, modelImportHelp.ExitCode);
        Assert.Contains("Refresh model reference documentation", modelImportHelp.Output);
        Assert.Contains("authored descriptions", modelImportHelp.Output);

        var mergeHelp = RunCli("help merge");
        Assert.Equal(0, mergeHelp.ExitCode);
        Assert.Contains("Rebuild a suite workspace", mergeHelp.Output);
        Assert.Contains("Suite workspace to rebuild", mergeHelp.Output);

        var searchHelp = RunCli("help search");
        Assert.Equal(0, searchHelp.ExitCode);
        Assert.Contains("meta-docs search [<query>]", searchHelp.Output);

        var contentsHelp = RunCli("help contents");
        Assert.Equal(0, contentsHelp.ExitCode);
        Assert.Contains("meta-docs contents", contentsHelp.Output);

        var browseHelp = RunCli("help browse");
        Assert.Equal(0, browseHelp.ExitCode);
        Assert.Contains("meta-docs browse", browseHelp.Output);

        var source = File.ReadAllText(Path.Combine(repoRoot, "MetaDocs", "Cli", "Program.cs"));
        Assert.Contains("MetaCliRuntime<MetaDocsModel>", source);
        Assert.Contains(".UseDefaultHelp", source);
        Assert.Contains("meta-docs browse", source);
        Assert.DoesNotContain("RunImportCommandProse", source);
        Assert.DoesNotContain("MetaDocsMarkdownCommandProseImporter", source);
        Assert.DoesNotContain("ParseAuthorPageArgs", source);
        Assert.DoesNotContain("ReadStringOption", source);
        Assert.DoesNotContain("CliAppDefinition", source);
        Assert.DoesNotContain("CliCommandDefinition", source);
        Assert.DoesNotContain("CliOptionDefinition", source);
        Assert.DoesNotContain("CliHelpRenderer", source);
    }

    [Fact]
    public void Cli_BrowseSearchAndUpdateDescriptionDefaultWorkspaceToCurrentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-cli-browse-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = MetaDocsModel.CreateEmpty();
            new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."));
            AddModelSubject(model, "MetaDocs");
            model.SaveToXmlWorkspace(root);

            var rootBrowse = RunCli("browse", root);
            Assert.Equal(0, rootBrowse.ExitCode);
            Assert.Contains("meta-docs browse cli", rootBrowse.Output);
            Assert.Contains("meta-docs browse model", rootBrowse.Output);
            Assert.DoesNotContain("Coverage", rootBrowse.Output);

            var cliBrowse = RunCli("browse cli", root);
            Assert.Equal(0, cliBrowse.ExitCode);
            Assert.Contains("meta-docs browse cli/meta-transform-binding", cliBrowse.Output);

            var contents = RunCli("contents --depth 3", root);
            Assert.Equal(0, contents.ExitCode);
            Assert.Contains("meta-transform-binding", contents.Output);
            Assert.Contains("bind", contents.Output);
            Assert.Contains("--source-schema", contents.Output);

            var modelBrowse = RunCli("browse model/MetaDocs", root);
            Assert.Equal(0, modelBrowse.ExitCode);
            Assert.Contains("MetaDocs", modelBrowse.Output);

            var commandBrowse = RunCli("browse cli/meta-transform-binding/bind", root);
            Assert.Equal(0, commandBrowse.ExitCode);
            Assert.Contains("Bind transforms.", commandBrowse.Output);
            Assert.DoesNotContain("Prose:", commandBrowse.Output);
            Assert.DoesNotContain("kind CliCommand", commandBrowse.Output);
            Assert.DoesNotContain("source:cli:meta-transform-binding:app:command:bind", commandBrowse.Output);

            var search = RunCli("search --source-schema --limit 5", root);
            Assert.Equal(0, search.ExitCode);
            Assert.Contains("Search \"--source-schema\"", search.Output);
            Assert.Contains("--source-schema", search.Output);
            Assert.Contains("open: meta-docs browse cli/meta-transform-binding/bind", search.Output);
            Assert.DoesNotContain("meta-docs read", search.Output);
            Assert.DoesNotContain("kind CliOption", search.Output);
            Assert.DoesNotContain("source:cli:meta-transform-binding:app:command:bind:option:source-schema", search.Output);

            var update = RunCli(
                "update-description --cli meta-transform-binding --command bind --option --source-schema --body-stdin",
                root,
                "Use this when the binding command should read a source schema workspace.");
            Assert.Equal(0, update.ExitCode);
            Assert.Contains("Updated description:", update.Output);

            var updatedBrowse = RunCli("browse cli/meta-transform-binding/bind", root);
            Assert.Equal(0, updatedBrowse.ExitCode);
            Assert.Contains("Use this when the binding command should read a source schema workspace.", updatedBrowse.Output);

            var reloaded = MetaDocsModel.LoadFromXmlWorkspace(root, searchUpward: false);
            Assert.Contains(reloaded.DocumentationNarrativeList, row =>
                row.DocumentationSubject?.Id == "source:cli:meta-transform-binding:app:command:bind:option:source-schema" &&
                row.Slot == "Summary" &&
                row.Origin == "Authored");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Cli_ImportRefreshMergePreservesAuthoredModelDescriptions()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true);
        var authoredWorkspace = Path.Combine(Path.GetTempPath(), "metadocs-authored-" + Guid.NewGuid().ToString("N"));
        var docsWorkspace = Path.Combine(Path.GetTempPath(), "metadocs-source-docs-" + Guid.NewGuid().ToString("N"));
        var suiteWorkspace = Path.Combine(Path.GetTempPath(), "metadocs-suite-" + Guid.NewGuid().ToString("N"));
        var siteOutput = Path.Combine(Path.GetTempPath(), "metadocs-site-" + Guid.NewGuid().ToString("N"));
        try
        {
            var authored = MetaDocsModel.CreateEmpty();
            var reference = AddPublicReferenceTree(authored);
            authored.SaveToXmlWorkspace(authoredWorkspace);

            var import = RunCli(
                $"import-workspace-model --source-workspace {QuoteArgument(sourceWorkspace)} --new-workspace {QuoteArgument(docsWorkspace)} --source-id source:workspace-model:sample --display-name SampleDocs --parent-subject {reference.MetaModels.Id}");
            Assert.Equal(0, import.ExitCode);
            Assert.Contains("Refreshed model docs: SampleModel (2 entity subject(s)).", import.Output);

            var update = RunCli(
                $"update-description --workspace {QuoteArgument(docsWorkspace)} --model SampleModel --body-stdin",
                standardInput: "SampleModel turns source metadata into browsable documentation.");
            Assert.Equal(0, update.ExitCode);
            Assert.Contains("Updated description:", update.Output);

            WriteSampleModel(sourceWorkspace, includeEmail: false);
            var refresh = RunCli(
                $"import-workspace-model --source-workspace {QuoteArgument(sourceWorkspace)} --workspace {QuoteArgument(docsWorkspace)} --source-id source:workspace-model:sample --display-name SampleDocs --parent-subject {reference.MetaModels.Id}");
            Assert.Equal(0, refresh.ExitCode);
            Assert.Contains("Refreshed model docs: SampleModel (2 entity subject(s)).", refresh.Output);

            var refreshedDocs = MetaDocsModel.LoadFromXmlWorkspace(docsWorkspace, searchUpward: false);
            var modelSubject = Assert.Single(refreshedDocs.DocumentationSubjectList, row =>
                IsSubjectType(row, "Model") &&
                row.DisplayName == "SampleModel");
            Assert.True(string.IsNullOrEmpty(modelSubject.Summary));
            Assert.Equal("SampleModel turns source metadata into browsable documentation.", FindNarrative(refreshedDocs, modelSubject, "Summary").Body);
            Assert.DoesNotContain(refreshedDocs.DocumentationSubjectList, row =>
                IsSubjectType(row, "Property") &&
                row.DisplayName == "Email");

            var merge = RunCli(
                $"merge --include {QuoteArgument(authoredWorkspace)} --include {QuoteArgument(docsWorkspace)} --workspace {QuoteArgument(suiteWorkspace)}");
            Assert.Equal(0, merge.ExitCode);
            Assert.Contains("Rebuilt suite workspace:", merge.Output);
            Assert.Contains("Included 2 source workspace(s), 2 documentation source(s).", merge.Output);

            var browse = RunCli($"browse --workspace {QuoteArgument(suiteWorkspace)} model/SampleModel");
            Assert.Equal(0, browse.ExitCode);
            Assert.StartsWith("SampleModel turns source metadata into browsable documentation.", browse.Output, StringComparison.Ordinal);

            var render = RunCli($"render-site --workspace {QuoteArgument(suiteWorkspace)} --out {QuoteArgument(siteOutput)}");
            Assert.Equal(0, render.ExitCode);
            var html = File.ReadAllText(Path.Combine(siteOutput, "docs.html"));
            Assert.Contains("SampleModel turns source metadata into browsable documentation.", html, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(sourceWorkspace);
            DeleteDirectoryIfExists(authoredWorkspace);
            DeleteDirectoryIfExists(docsWorkspace);
            DeleteDirectoryIfExists(suiteWorkspace);
            DeleteDirectoryIfExists(siteOutput);
        }
    }

    [Fact]
    public void Cli_AddExampleAuthorsStructuredExamplesForBrowseSearchMergeAndRender()
    {
        var docsWorkspace = Path.Combine(Path.GetTempPath(), "metadocs-example-docs-" + Guid.NewGuid().ToString("N"));
        var suiteWorkspace = Path.Combine(Path.GetTempPath(), "metadocs-example-suite-" + Guid.NewGuid().ToString("N"));
        var siteOutput = Path.Combine(Path.GetTempPath(), "metadocs-example-site-" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = MetaDocsModel.CreateEmpty();
            var reference = AddPublicReferenceTree(model);
            new MetaDocsCliImporter().ImportApplication(model, CreateMetaSqlApp(), parentSubjectId: reference.MetaBiCli.Id);
            model.SaveToXmlWorkspace(docsWorkspace);

            var addExample = RunCli(
                $"add-example --workspace {QuoteArgument(docsWorkspace)} --cli meta-sql --command deploy --id example:meta-sql:deploy --title \"Deploy a manifest\" --section-id example:meta-sql:deploy:overview --body-stdin",
                standardInput: "Apply a deploy manifest after source and live fingerprint validation.");
            Assert.Equal(0, addExample.ExitCode);
            Assert.Contains("Added example: Deploy a manifest.", addExample.Output);

            var addCode = RunCli(
                $"add-example-code --workspace {QuoteArgument(docsWorkspace)} --section example:meta-sql:deploy:overview --id example:meta-sql:deploy:command --language powershell --code-stdin",
                standardInput: "meta-sql deploy --connection-env META_SQL_DEV --manifest-workspace .\\DeployPlan --source-workspace .\\MetaSql");
            Assert.Equal(0, addCode.ExitCode);
            Assert.Contains("Added example code:", addCode.Output);

            var validate = RunCli($"validate --workspace {QuoteArgument(docsWorkspace)}");
            Assert.Equal(0, validate.ExitCode);
            Assert.Contains("Diagnostics: 0 error(s), 0 warning(s), 0 info.", validate.Output);

            var browse = RunCli($"browse --workspace {QuoteArgument(docsWorkspace)} cli/meta-sql/deploy");
            Assert.Equal(0, browse.ExitCode);
            Assert.Contains("Examples:", browse.Output);
            Assert.Contains("Deploy a manifest", browse.Output);
            Assert.Contains("meta-sql deploy --connection-env META_SQL_DEV", browse.Output);

            var search = RunCli($"search --workspace {QuoteArgument(docsWorkspace)} \"Deploy a manifest\"");
            Assert.Equal(0, search.ExitCode);
            Assert.Contains("meta-sql deploy", search.Output);
            Assert.Contains("example Deploy a manifest", search.Output);

            var merge = RunCli($"merge --include {QuoteArgument(docsWorkspace)} --workspace {QuoteArgument(suiteWorkspace)}");
            Assert.Equal(0, merge.ExitCode);
            var suite = MetaDocsModel.LoadFromXmlWorkspace(suiteWorkspace, searchUpward: false);
            var suiteExample = Assert.Single(suite.DocumentationExampleList);
            Assert.Equal("Deploy a manifest", suiteExample.Title);
            Assert.Equal("meta-sql deploy", suiteExample.DocumentationSubject.DisplayName);

            var render = RunCli($"render-site --workspace {QuoteArgument(suiteWorkspace)} --out {QuoteArgument(siteOutput)}");
            Assert.Equal(0, render.ExitCode);
            var html = File.ReadAllText(Path.Combine(siteOutput, "docs.html"));
            Assert.Contains("example-block", html, StringComparison.Ordinal);
            Assert.Contains("Deploy a manifest", html, StringComparison.Ordinal);
            Assert.Contains("meta-sql deploy --connection-env META_SQL_DEV", html, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(docsWorkspace);
            DeleteDirectoryIfExists(suiteWorkspace);
            DeleteDirectoryIfExists(siteOutput);
        }
    }

    [Fact]
    public async Task Cli_BrowseNavigatesDocumentationScreensAndRecoversFromWrongGuesses()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-cli-browse-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repoRoot = FindRepositoryRoot();
            var model = MetaDocsModel.CreateEmpty();
            new MetaDocsCliImporter().ImportApplication(model, CreateMetaSqlApp());
            await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
                model,
                Path.Combine(repoRoot, "MetaDocs", "Workspace"),
                "source:workspace-model:meta-docs",
                "MetaDocs");
            AddLowLevelDeployProperty(model);
            model.SaveToXmlWorkspace(root);

            var rootBrowse = RunCli("browse", root);
            Assert.Equal(0, rootBrowse.ExitCode);
            Assert.StartsWith("MetaDocs", rootBrowse.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(rootBrowse.Output);
            Assert.Contains("meta-docs browse cli", rootBrowse.Output);
            Assert.Contains("meta-docs browse model", rootBrowse.Output);

            var cliIndex = RunCli("browse cli", root);
            Assert.Equal(0, cliIndex.ExitCode);
            Assert.StartsWith("CLI tools", cliIndex.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(cliIndex.Output);
            Assert.Contains("meta-sql", cliIndex.Output);
            Assert.Contains("open: meta-docs browse cli/meta-sql", cliIndex.Output);

            var cliApp = RunCli("browse cli/meta-sql", root);
            Assert.Equal(0, cliApp.ExitCode);
            Assert.StartsWith("SQL workspace extraction, deploy planning, and execution tooling.", cliApp.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(cliApp.Output);
            AssertDoesNotStartWithSelectedRoute(cliApp.Output, "meta-sql");
            Assert.DoesNotContain("meta-sql is a command-line tool", cliApp.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Commands:", cliApp.Output);
            Assert.Contains("deploy", cliApp.Output);
            Assert.Contains("open: meta-docs browse cli/meta-sql/deploy", cliApp.Output);
            Assert.DoesNotContain("commands 4", cliApp.Output, StringComparison.OrdinalIgnoreCase);

            var deploy = RunCli("browse cli/meta-sql/deploy", root);
            Assert.Equal(0, deploy.ExitCode);
            Assert.StartsWith("Apply a deploy manifest after source and live fingerprint validation.", deploy.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(deploy.Output);
            AssertDoesNotStartWithSelectedRoute(deploy.Output, "meta-sql deploy");
            Assert.Contains("Usage:", deploy.Output);
            Assert.Contains("meta-sql deploy", deploy.Output);
            Assert.Contains("--connection-env <value>", deploy.Output);
            Assert.Contains("--manifest-workspace <path>", deploy.Output);
            Assert.Contains("--source-workspace <path>", deploy.Output);
            Assert.Contains("Options:", deploy.Output);
            Assert.Contains("--connection-env <value>", deploy.Output);
            Assert.Contains("Environment variable containing the SQL Server connection string.", deploy.Output);
            Assert.Contains("Up:", deploy.Output);

            var extract = RunCli("browse cli/meta-sql/extract/sqlserver", root);
            Assert.Equal(0, extract.ExitCode);
            Assert.StartsWith("Extract SQL Server database objects into a MetaSql workspace.", extract.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(extract.Output);
            AssertDoesNotStartWithSelectedRoute(extract.Output, "meta-sql extract sqlserver");

            var unknownCommand = RunCli("browse cli/meta-sql/diff", root);
            Assert.NotEqual(0, unknownCommand.ExitCode);
            Assert.Contains("Could not find command 'diff' under CLI application 'meta-sql'.", unknownCommand.Output);
            Assert.Contains("Available commands:", unknownCommand.Output);
            Assert.Contains("open: meta-docs browse cli/meta-sql/deploy", unknownCommand.Output);
            Assert.Contains("meta-docs search diff", unknownCommand.Output);

            var unknownCli = RunCli("browse cli/meta-sq", root);
            Assert.NotEqual(0, unknownCli.ExitCode);
            Assert.Contains("Could not find CLI application 'meta-sq'.", unknownCli.Output);
            Assert.Contains("meta-sql", unknownCli.Output);
            Assert.Contains("meta-docs browse cli", unknownCli.Output);

            var modelIndex = RunCli("browse model", root);
            Assert.Equal(0, modelIndex.ExitCode);
            Assert.StartsWith("Models", modelIndex.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(modelIndex.Output);
            Assert.Contains("open: meta-docs browse model/MetaDocs", modelIndex.Output);

            var modelPage = RunCli("browse model/MetaDocs", root);
            Assert.Equal(0, modelPage.ExitCode);
            Assert.StartsWith("Entities:", modelPage.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(modelPage.Output);
            AssertDoesNotStartWithSelectedRoute(modelPage.Output, "MetaDocs");
            Assert.DoesNotContain("MetaDocs is a workspace model", modelPage.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DocumentationSubject", modelPage.Output);
            Assert.Contains("open: meta-docs browse model/MetaDocs/DocumentationSubject", modelPage.Output);

            var entityPage = RunCli("browse model/MetaDocs/DocumentationSubject", root);
            Assert.Equal(0, entityPage.ExitCode);
            Assert.StartsWith("Properties:", entityPage.Output, StringComparison.Ordinal);
            AssertBrowseHasNoRouteNoise(entityPage.Output);
            AssertDoesNotStartWithSelectedRoute(entityPage.Output, "DocumentationSubject");
            Assert.DoesNotContain("DocumentationSubject is an entity", entityPage.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Properties:", entityPage.Output);
            Assert.Contains("Relationships:", entityPage.Output);
            Assert.Contains("Up:", entityPage.Output);

            var search = RunCli("search deploy --limit 20", root);
            Assert.Equal(0, search.ExitCode);
            Assert.Contains("open: meta-docs browse cli/meta-sql/deploy", search.Output);
            Assert.DoesNotContain("meta-docs read", search.Output);
            var commandIndex = search.Output.IndexOf("meta-sql deploy", StringComparison.Ordinal);
            var propertyIndex = search.Output.IndexOf("DeployOrdinal", StringComparison.Ordinal);
            Assert.True(commandIndex >= 0);
            Assert.True(propertyIndex >= 0);
            Assert.True(commandIndex < propertyIndex);

            var emptySearch = RunCli("search", root);
            Assert.NotEqual(0, emptySearch.ExitCode);
            Assert.Contains("Search needs text.", emptySearch.Output);
            Assert.Contains("meta-docs browse", emptySearch.Output);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AuthoringService_UpsertsPageNarrativeAndViewNode()
    {
        var model = MetaDocsModel.CreateEmpty();
        var page = new MetaDocsAuthoredPage(
            "docs:home",
            "meta + meta-bi",
            "Model-first documentation for meta and meta-bi.",
            "MetaDocs stores authored descriptions beside refreshable generated facts.");

        var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
        new MetaDocsAuthoringService().UpsertPage(
            model,
            page with { Body = "Updated authored description.", Summary = "Updated summary." });

        subject = Assert.Single(model.DocumentationSubjectList, row => row.Id == "docs:home");
        Assert.True(IsSubjectType(subject, "Guide"));
        Assert.Equal("Updated summary.", subject.Summary);
        Assert.True(IsSourceType(subject.DocumentationSource, "AuthoredDocumentation"));

        var narrative = Assert.Single(model.DocumentationNarrativeList, row =>
            row.DocumentationSubject?.Id == subject.Id &&
            row.Origin == "Authored");
        Assert.Equal("Updated authored description.", narrative.Body);
        Assert.Equal("Current", narrative.ReviewStatus);

        Assert.Contains(model.DocumentationViewNodeList, row =>
            row.DocumentationSubject?.Id == subject.Id &&
            row.Title == "meta + meta-bi");
        Assert.False(new MetaDocsValidationService().Validate(model).HasErrors());
    }

    [Fact]
    public void ImportApplication_CreatesGenericSubjectsFactsAndPreservesAuthoredNarrative()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(
            model,
            CreateBindingApp("Bind transforms."),
            new CliDocumentationProfile(
                applicationSummary: "Authored application summary.",
                commands:
                [
                    new CliCommandCommentary(
                        "bind",
                        "Authored purpose.",
                        "Authored when.",
                        "Authored how.")
                ],
                options:
                [
                    new CliOptionCommentary(
                        "bind",
                        "--source-schema",
                        "Authored option explanation.",
                        "Authored option when.",
                        ".\\Schema")
                ]));

        importer.ImportApplication(
            model,
            CreateBindingApp("Bind transforms after help text changed."));

        var application = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliApplication"));
        Assert.Equal("source:cli:meta-transform-binding:app", application.Id);
        Assert.Equal("Authored application summary.", FindNarrative(model, application, "Summary").Body);

        var command = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliCommand"));
        Assert.Equal("meta-transform-binding bind", command.DisplayName);
        Assert.Equal("Bind transforms after help text changed.", command.Summary);
        Assert.Equal("Changed", command.Status);
        Assert.Equal("Authored purpose.", FindNarrative(model, command, "Summary").Body);
        Assert.Equal("Authored when.", FindNarrative(model, command, "Usage").Body);
        Assert.Equal("Authored how.", FindNarrative(model, command, "ImplementationNote").Body);
        Assert.Equal("1", FindFact(model, command, "Cli", "OptionCount").Value);

        var option = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliOption"));
        Assert.Equal("--source-schema", option.DisplayName);
        Assert.Equal("Source schema workspace.", option.Summary);
        Assert.Equal("Authored option explanation.", FindNarrative(model, option, "Summary").Body);
        Assert.Equal("Authored option when.", FindNarrative(model, option, "Usage").Body);
        Assert.Equal(".\\Schema", FindFact(model, option, "Cli", "ExampleValue").Value);
    }

    [Fact]
    public void ImportApplication_RemovedSourceCommandIsPrunedFromGeneratedCliDocs()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateBindingApp("Bind transforms.", includeInspect: true));
        importer.ImportApplication(model, CreateBindingApp("Bind transforms.", includeInspect: false));

        Assert.DoesNotContain(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "CliCommand") &&
            row.NativeId == "inspect");
        Assert.DoesNotContain(model.DocumentationFactList, row =>
            row.DocumentationSubject?.Id.Contains("inspect", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ImportApplication_SameFingerprintReusesImportBatch()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();

        importer.ImportApplication(model, CreateBindingApp("Bind transforms."));
        var source = Assert.Single(model.DocumentationSourceList, row => IsSourceType(row, "MetaCliWorkspace"));
        var batch = Assert.Single(model.DocumentationImportBatchList);
        var importedAt = source.ImportedAt;

        importer.ImportApplication(model, CreateBindingApp("Bind transforms."));

        var reimportedSource = Assert.Single(model.DocumentationSourceList, row => IsSourceType(row, "MetaCliWorkspace"));
        var reimportedBatch = Assert.Single(model.DocumentationImportBatchList);
        Assert.Equal(batch.Id, reimportedBatch.Id);
        Assert.Equal(importedAt, reimportedSource.ImportedAt);
        Assert.All(
            model.DocumentationFactList.Where(row => ReferenceEquals(row.DocumentationSource, reimportedSource)),
            fact => Assert.Equal(batch.Id, fact.DocumentationImportBatch.Id));
    }

    [Fact]
    public void ImportApplication_SameFingerprintReusesImportBatchAfterWorkspaceReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-cli-import-reload-" + Guid.NewGuid().ToString("N"));
        try
        {
            var model = MetaDocsModel.CreateEmpty();
            var importer = new MetaDocsCliImporter();

            importer.ImportApplication(model, CreateBindingApp("Bind transforms."));
            var batch = Assert.Single(model.DocumentationImportBatchList);
            var importedAt = Assert.Single(model.DocumentationSourceList, row => IsSourceType(row, "MetaCliWorkspace")).ImportedAt;
            model.SaveToXmlWorkspace(root);

            var reloaded = MetaDocsModel.LoadFromXmlWorkspace(root, searchUpward: false);
            var subjectStatuses = reloaded.DocumentationSubjectList.ToDictionary(row => row.Id, row => row.Status);
            var factStatuses = reloaded.DocumentationFactList.ToDictionary(row => row.Id, row => row.Status);
            importer.ImportApplication(reloaded, CreateBindingApp("Bind transforms."));

            var reimportedSource = Assert.Single(reloaded.DocumentationSourceList, row => IsSourceType(row, "MetaCliWorkspace"));
            var reimportedBatch = Assert.Single(reloaded.DocumentationImportBatchList);
            Assert.Equal(batch.Id, reimportedBatch.Id);
            Assert.Equal(importedAt, reimportedSource.ImportedAt);
            Assert.All(
                reloaded.DocumentationFactList.Where(row => row.DocumentationSource.Id == reimportedSource.Id),
                fact => Assert.Equal(batch.Id, fact.DocumentationImportBatch.Id));
            Assert.All(
                reloaded.DocumentationSubjectList.Where(row => subjectStatuses.ContainsKey(row.Id)),
                subject => Assert.Equal(subjectStatuses[subject.Id], subject.Status));
            Assert.All(
                reloaded.DocumentationFactList.Where(row => factStatuses.ContainsKey(row.Id)),
                fact => Assert.Equal(factStatuses[fact.Id], fact.Status));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ImportApplication_CapturesAllowedValuesAndParameterGroups()
    {
        var model = MetaDocsModel.CreateEmpty();
        var cli = CreateCliApp(
            "demo-cli",
            commands: new TestCliCommand(
                "run",
                "Run demo.",
                new TestCliOption("--mode", "<value>", "Execution mode."),
                new TestCliOption("--id", "<value>", "Explicit id."),
                new TestCliOption("--auto-id", string.Empty, "Generate id.")));
        var arityOne = cli.ValueArityList.Single(row => row.Id == "arity-one");
        var modeShape = new ValueShape
        {
            Id = "shape-mode",
            Name = "Mode",
            ValueLabel = "<mode>",
            ValueArity = arityOne,
        };
        cli.ValueShapeList.Add(modeShape);
        var modeParameter = cli.ParameterList.Single(parameter => parameter.Name == "mode");
        modeParameter.ValueShape = modeShape;
        cli.AllowedValueList.Add(new AllowedValue
        {
            Id = "mode-fast",
            ValueShape = modeShape,
            Value = "fast",
            Description = "Optimize for speed.",
        });
        cli.AllowedValueList.Add(new AllowedValue
        {
            Id = "mode-safe",
            ValueShape = modeShape,
            Value = "safe",
            Description = "Optimize for checks.",
        });

        var executable = cli.ExecutableCommandList.Single();
        var idParameter = cli.ParameterList.Single(parameter => parameter.Name == "id");
        var autoIdParameter = cli.ParameterList.Single(parameter => parameter.Name == "auto-id");
        var group = new ParameterGroup
        {
            Id = "group-id-choice",
            ExecutableCommand = executable,
            Name = "IdChoice",
            Description = "Choose an explicit id or generated id.",
            IsRequired = "true",
            AllowsMultiple = "false",
        };
        var idMember = new ParameterGroupMember
        {
            Id = "group-id-choice-id",
            ParameterGroup = group,
            Parameter = idParameter,
        };
        cli.ParameterGroupList.Add(group);
        cli.ParameterGroupMemberList.Add(idMember);
        cli.ParameterGroupMemberList.Add(new ParameterGroupMember
        {
            Id = "group-id-choice-auto",
            ParameterGroup = group,
            Parameter = autoIdParameter,
            PreviousMember = idMember,
        });

        var reference = AddPublicReferenceTree(model);
        new MetaDocsCliImporter().ImportApplication(model, cli, parentSubjectId: reference.MetaCli.Id);

        var application = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliApplication"));
        Assert.Equal(string.Empty, application.Summary);
        Assert.True(string.IsNullOrWhiteSpace(FindNarrative(model, application, "Summary").Body));

        var command = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliCommand"));
        Assert.Equal("1", FindFact(model, command, "Cli", "ParameterGroupCount").Value);

        var mode = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "CliOption") &&
            row.DisplayName == "--mode");
        Assert.Equal("Mode", FindFact(model, mode, "Cli", "ValueShape").Value);
        Assert.Equal("One", FindFact(model, mode, "Cli", "ValueArity").Value);
        Assert.Equal("fast, safe", FindFact(model, mode, "Cli", "AllowedValues").Value);
        Assert.Equal(2, model.DocumentationSubjectList.Count(row =>
            IsSubjectType(row, "CliAllowedValue") &&
            row.ParentSubject?.Id == mode.Id));

        var importedGroup = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliParameterGroup"));
        Assert.Equal("IdChoice", importedGroup.DisplayName);
        Assert.Equal("true", FindFact(model, importedGroup, "Cli", "Required").Value);
        Assert.Equal("false", FindFact(model, importedGroup, "Cli", "AllowsMultiple").Value);
        Assert.Equal("auto-id, id", FindFact(model, importedGroup, "Cli", "Members").Value);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);
        Assert.Contains("&lt;mode&gt;: fast, safe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<p class=\"panel-lead\"></p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportWorkspaceModel_CreatesModelEntityPropertyAndRelationshipSubjects()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true);
        var model = MetaDocsModel.CreateEmpty();

        await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");

        var workspace = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Workspace"));
        Assert.Equal("Sample docs", workspace.DisplayName);
        var source = Assert.Single(model.DocumentationSourceList, row => IsSourceType(row, "WorkspaceModel"));
        Assert.NotNull(source.SourceFingerprint);
        Assert.Equal(64, source.SourceFingerprint.Length);
        Assert.All(
            model.DocumentationFactList.Where(row => row.DocumentationSource == source),
            fact =>
            {
                Assert.NotNull(fact.SourceFingerprint);
                Assert.Equal(64, fact.SourceFingerprint.Length);
            });

        var modelSubject = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Model"));
        Assert.Equal("SampleModel", modelSubject.DisplayName);
        Assert.Equal(string.Empty, modelSubject.Summary);
        Assert.Equal("2", FindFact(model, modelSubject, "Model", "EntityCount").Value);

        var customer = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Entity") &&
            row.DisplayName == "Customer");
        Assert.Equal(string.Empty, customer.Summary);
        Assert.Equal("2", FindFact(model, customer, "Model", "PropertyCount").Value);

        var email = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Property") &&
            row.DisplayName == "Email");
        Assert.Equal(string.Empty, email.Summary);
        Assert.Equal("True", FindFact(model, email, "Model", "Nullable").Value);

        var orderCustomer = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Relationship") &&
            row.DisplayName == "Customer");
        Assert.Equal(string.Empty, orderCustomer.Summary);
        Assert.Equal("Customer", FindFact(model, orderCustomer, "Model", "TargetEntity").Value);
        Assert.Contains(model.DocumentationRelationshipList, row =>
            row.FromSubject?.Id == orderCustomer.Id &&
            IsRelationshipType(row, "ReferencesEntity") &&
            row.ToSubject?.Id == customer.Id);
        Assert.Empty(model.DocumentationNarrativeList);
    }

    [Fact]
    public async Task ImportWorkspaceModel_ReimportPreservesAuthoredNarrativePrunesRemovedPropertyAndUpdatesFacts()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true);
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsWorkspaceModelImporter();

        await importer.ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");
        var customer = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Entity") &&
            row.DisplayName == "Customer");
        customer.Summary = "Stored customer summary.";
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = $"{customer.Id}:narrative:summary:900",
            DocumentationSubject = customer,
            Slot = "Summary",
            Title = "Summary",
            Body = "Authored customer description.",
            BodyFormat = "PlainText",
            Origin = "Authored",
            ReviewStatus = "Current",
        });
        var email = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Property") &&
            row.DisplayName == "Email");
        model.DocumentationExampleList.Add(new DocumentationExample
        {
            Id = "example:email:removed",
            DocumentationSubject = email,
            Title = "Removed property example",
            Origin = "Authored",
            ReviewStatus = "Current",
        });
        model.DocumentationExampleSectionList.Add(new DocumentationExampleSection
        {
            Id = "example:email:removed:section",
            DocumentationExample = model.DocumentationExampleList.Single(row => row.Id == "example:email:removed"),
            Body = "Example tied to a property that disappears.",
            BodyFormat = "PlainText",
        });
        model.DocumentationExampleCodeList.Add(new DocumentationExampleCode
        {
            Id = "example:email:removed:code",
            DocumentationExampleSection = model.DocumentationExampleSectionList.Single(row => row.Id == "example:email:removed:section"),
            Language = "text",
            Code = "Email",
        });

        WriteSampleModel(sourceWorkspace, includeEmail: false);
        await importer.ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");

        customer = Assert.Single(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Entity") &&
            row.DisplayName == "Customer");
        Assert.Equal("Stored customer summary.", customer.Summary);
        Assert.Equal("1", FindFact(model, customer, "Model", "PropertyCount").Value);
        Assert.Equal("Authored customer description.", Assert.Single(model.DocumentationNarrativeList, row =>
            row.DocumentationSubject?.Id == customer.Id &&
            row.Origin == "Authored").Body);

        Assert.DoesNotContain(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Property") &&
            row.DisplayName == "Email");
        Assert.DoesNotContain(model.DocumentationExampleList, row => row.Id == "example:email:removed");
        Assert.DoesNotContain(model.DocumentationExampleSectionList, row => row.Id == "example:email:removed:section");
        Assert.DoesNotContain(model.DocumentationExampleCodeList, row => row.Id == "example:email:removed:code");
    }

    [Fact]
    public void SuiteMerge_PreservesDistinctSourcesAndDoesNotCollideOnDisplayName()
    {
        var left = MetaDocsModel.CreateEmpty();
        var right = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(left, CreateSameNamedApp(), sourceId: "source:cli:left");
        importer.ImportApplication(right, CreateSameNamedApp(), sourceId: "source:cli:right");

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { left, right });

        Assert.Contains(suite.DocumentationWorkspaceList, row => IsWorkspaceType(row, "SuiteDocumentation"));
        Assert.Contains(suite.DocumentationSourceList, row => row.Id == "source:cli:left");
        Assert.Contains(suite.DocumentationSourceList, row => row.Id == "source:cli:right");
        Assert.Equal(2, suite.DocumentationSubjectList.Count(row =>
            IsSubjectType(row, "CliApplication") &&
            row.DisplayName == "same-cli"));
        Assert.Equal(
            suite.DocumentationSubjectList.Count,
            suite.DocumentationSubjectList.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SuiteMerge_DoesNotMutateSourceWorkspaces()
    {
        var source = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(source, CreateSameNamedApp(), sourceId: "source:cli:left");
        var sourceReference = Assert.Single(source.DocumentationSubjectList, row => IsSubjectType(row, "CliApplication"));
        var sourceWorkspace = Assert.Single(source.DocumentationWorkspaceList);

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { source });
        var suiteReference = Assert.Single(suite.DocumentationSubjectList, row => IsSubjectType(row, "CliApplication"));

        Assert.NotSame(sourceReference, suiteReference);
        Assert.Same(sourceWorkspace, source.DocumentationSourceList.Single().DocumentationWorkspace);
        Assert.NotSame(source.DocumentationSourceList.Single(), suite.DocumentationSourceList.Single(row => row.Id == "source:cli:left"));
    }

    [Fact]
    public void SuiteMerge_PreservesStructuredExamples()
    {
        var source = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(source, CreateSameNamedApp(), sourceId: "source:cli:left");
        var authoring = new MetaDocsExampleAuthoringService();
        var example = authoring.UpsertExample(
            source,
            new MetaDocsSubjectSelector(Cli: "same-cli"),
            "example:same-cli:overview",
            "Run the CLI",
            "Example summary.",
            "example:same-cli:overview:section",
            "Run the command from the workspace you want to inspect.",
            "PlainText",
            string.Empty);
        var section = Assert.Single(source.DocumentationExampleSectionList);
        authoring.UpsertCode(
            source,
            section.Id,
            "example:same-cli:overview:command",
            "Command",
            "powershell",
            "same-cli show",
            string.Empty);

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { source });

        var suiteExample = Assert.Single(suite.DocumentationExampleList);
        Assert.NotSame(example, suiteExample);
        Assert.Equal("Run the CLI", suiteExample.Title);
        Assert.Equal("same-cli", suiteExample.DocumentationSubject.DisplayName);
        var suiteSection = Assert.Single(suite.DocumentationExampleSectionList);
        Assert.Same(suiteExample, suiteSection.DocumentationExample);
        Assert.Equal("Run the command from the workspace you want to inspect.", suiteSection.Body);
        var suiteCode = Assert.Single(suite.DocumentationExampleCodeList);
        Assert.Same(suiteSection, suiteCode.DocumentationExampleSection);
        Assert.Equal("same-cli show", suiteCode.Code);
    }

    [Fact]
    public void SuiteMerge_RepeatedMergeIsDeterministicAndDoesNotDuplicateStableRows()
    {
        var source = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(source, CreateSameNamedApp(), sourceId: "source:cli:left");

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { source, source });

        Assert.Equal(1, suite.DocumentationSourceList.Count(row => row.Id == "source:cli:left"));
        Assert.Equal(1, suite.DocumentationSubjectList.Count(row => IsSubjectType(row, "CliApplication")));
        Assert.Equal(1, suite.DocumentationThemeAssetList.Count(row => row.Id == "theme:metametabi-static:asset:css"));
        var brandMark = Assert.Single(suite.DocumentationThemeAssetList, row => row.Id == "theme:metametabi-static:asset:brand-mark");
        Assert.Equal(string.Empty, brandMark.Href);
        Assert.Contains("<circle cx=\"11\" cy=\"11\" r=\"11\" fill=\"#0a0a0a\"/>", brandMark.Content, StringComparison.Ordinal);
        Assert.Contains("points=\"9.5,8 13.5,11 9.5,14\"", brandMark.Content, StringComparison.Ordinal);
        Assert.Equal(1, suite.DocumentationViewList.Count(row => row.Id == "view:default"));
    }

    [Fact]
    public void PublicReferenceSubjectHierarchy_DrivesRenderPlacement()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), parentSubjectId: reference.MetaCli.Id);
        importer.ImportApplication(model, CreateMetaDocsApp(), parentSubjectId: reference.MetaCli.Id);
        importer.ImportApplication(model, CreateBindingApp("Bind transforms."), parentSubjectId: reference.MetaBiCli.Id);
        AddModelSubject(model, "MetaDocs", reference.MetaModels);
        AddModelSubject(model, "MetaTransformBinding", reference.MetaBiModels);
        var view = Assert.Single(model.DocumentationViewList, row => row.Id == "view:default");
        Assert.Equal(reference.Root.Id, view.RootSubject?.Id);
        Assert.Equal(reference.Root.DisplayName, view.Title);
        Assert.Equal(reference.Root.Summary, view.Summary);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("<title>meta + meta-bi &#183; Reference</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>meta + meta-bi reference</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Command-line and model references for the current public MetaDocs suite.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-cli\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-models\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-bi-cli\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-bi-models\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cli-meta-docs\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cli-meta-transform-binding\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#model-metadocs\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#model-metatransformbinding\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductFamily:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceSurface:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SuiteMerge_PreservesAuthoredReferenceRootAndGeneratedSubjectParents()
    {
        var authored = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(authored);
        var generated = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(generated, CreateMetaApp(), parentSubjectId: reference.MetaCli.Id);
        new MetaDocsCliImporter().ImportApplication(generated, CreateBindingApp("Bind transforms."), parentSubjectId: reference.MetaBiCli.Id);
        AddModelSubject(generated, "MetaDocs", EnsureSubject(generated, reference.MetaModels.Id));
        AddModelSubject(generated, "MetaTransformBinding", EnsureSubject(generated, reference.MetaBiModels.Id));

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { authored, generated });

        var view = Assert.Single(suite.DocumentationViewList, row => row.Id == "view:default");
        Assert.Equal("public:reference", view.RootSubject?.Id);
        Assert.Equal("meta + meta-bi reference", view.Title);
        Assert.Equal("Command-line and model references for the current public MetaDocs suite.", view.Summary);
        Assert.Equal("public:meta:cli", Assert.Single(suite.DocumentationSubjectList, row => row.DisplayName == "meta").ParentSubject?.Id);
        Assert.Equal("public:meta-bi:cli", Assert.Single(suite.DocumentationSubjectList, row => row.DisplayName == "meta-transform-binding").ParentSubject?.Id);
        Assert.Equal("public:meta:models", Assert.Single(suite.DocumentationSubjectList, row => row.DisplayName == "MetaDocs").ParentSubject?.Id);
        Assert.Equal("public:meta-bi:models", Assert.Single(suite.DocumentationSubjectList, row => row.DisplayName == "MetaTransformBinding").ParentSubject?.Id);
        Assert.DoesNotContain(new MetaDocsValidationService().Validate(suite).Diagnostics, row =>
            row.Id is "MDOC031" or "MDOC032" or "MDOC033" or "MDOC035");
    }

    [Fact]
    public void Validate_ReturnsStableDiagnosticsForBrokenLifecycleState()
    {
        var model = MetaDocsModel.CreateEmpty();
        var sourceType = MetaDocsVocabulary.EnsureSourceType(model, "MetaCliWorkspace");
        var commandType = MetaDocsVocabulary.EnsureSubjectType(model, "CliCommand");
        var factType = MetaDocsVocabulary.EnsureFactType(model, "Cli");
        var valueType = MetaDocsVocabulary.EnsureValueType(model, "String");
        var relationshipType = MetaDocsVocabulary.EnsureRelationshipType(model, "References");
        var viewType = MetaDocsVocabulary.EnsureViewType(model, "Site");
        var source = new DocumentationSource
        {
            Id = "source:test",
            DisplayName = "Source",
            DocumentationSourceType = sourceType,
            Status = "Current",
        };
        var missingSubject = new DocumentationSubject
        {
            Id = "subject:missing",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            DisplayName = "Missing",
            DisplayPath = "Missing",
            Status = "Current",
        };
        var batch = new DocumentationImportBatch
        {
            Id = "source:test:batch:001",
            DocumentationSource = source,
            ImportedAt = "2026-01-01T00:00:00Z",
            ImporterId = "test",
            ImporterVersion = "1",
            Status = "Current",
        };
        var left = new DocumentationSubject
        {
            Id = "subject:same",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            DisplayName = "Left",
            DisplayPath = "Same.Display",
            Status = "Current",
        };
        var right = new DocumentationSubject
        {
            Id = "subject:same",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            DisplayName = "Right",
            DisplayPath = "Same.Display",
            Status = "Current",
        };
        model.DocumentationSourceList.Add(source);
        model.DocumentationImportBatchList.Add(batch);
        model.DocumentationSubjectList.Add(left);
        model.DocumentationSubjectList.Add(right);
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "fact:missing",
            DocumentationSubject = missingSubject,
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            DocumentationFactType = factType,
            Name = "Name",
            Value = "x",
            DocumentationValueType = valueType,
            Status = "Current",
        });
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = "narrative:review",
            DocumentationSubject = left,
            Slot = "Summary",
            Body = "Draft",
            BodyFormat = "PlainText",
            Origin = "Authored",
            ReviewStatus = "NeedsReview",
        });
        model.DocumentationRelationshipList.Add(new DocumentationRelationship
        {
            Id = "relationship:broken",
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            FromSubject = left,
            ToSubject = missingSubject,
            DocumentationRelationshipType = relationshipType,
        });
        model.DocumentationViewList.Add(new DocumentationView
        {
            Id = "view:default",
            Name = "Default",
            DocumentationViewType = viewType,
        });
        model.DocumentationViewNodeList.Add(new DocumentationViewNode
        {
            Id = "view:default:node:missing",
            DocumentationView = model.DocumentationViewList.Single(),
            DocumentationSubject = missingSubject,
            Title = "Missing",
        });

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC001" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC002" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC012" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC005" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC006" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC008");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC010" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC011" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.True(result.HasErrors());

        var resultWithDescriptionDiagnostics = new MetaDocsValidationService().Validate(
            model,
            new MetaDocsValidationOptions { IncludeDescriptionDiagnostics = true });
        Assert.Contains(resultWithDescriptionDiagnostics.Diagnostics, diagnostic =>
            diagnostic.Id == "MDOC008" &&
            diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Validate_DoesNotWarnForPrunedGeneratedCliSubjects()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms.", includeInspect: true));
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms.", includeInspect: false));

        var result = new MetaDocsValidationService().Validate(model);

        Assert.DoesNotContain(model.DocumentationSubjectList, row => row.NativeId == "inspect");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC007");
    }

    [Fact]
    public void Validate_DoesNotTreatGuideOnlyDefaultViewAsPublicReferenceView()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsAuthoringService().UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "docs:home",
                "Home",
                "Authored guide.",
                "Authored guide."));

        var result = new MetaDocsValidationService().Validate(model);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Id is "MDOC031" or "MDOC032" or "MDOC033" or "MDOC035");
    }

    [Fact]
    public void Validate_DoesNotTreatLegacyDefaultViewAsPublicReferenceView()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."));
        var view = Assert.Single(model.DocumentationViewList, row => row.Id == "view:default");
        view.Title = "Command surface.";
        view.Summary = "Modeled documentation for metadata-shaped things.";

        var result = new MetaDocsValidationService().Validate(model);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Id is "MDOC031" or "MDOC032" or "MDOC033" or "MDOC035");
    }

    [Fact]
    public void Validate_ChecksCliReferenceCompletenessAndParentage()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."));

        var source = Assert.Single(model.DocumentationSourceList, source => IsSourceType(source, "MetaCliWorkspace"));
        var batch = Assert.Single(model.DocumentationImportBatchList);
        var applicationType = MetaDocsVocabulary.EnsureSubjectType(model, "CliApplication");
        var commandType = MetaDocsVocabulary.EnsureSubjectType(model, "CliCommand");
        var optionType = MetaDocsVocabulary.EnsureSubjectType(model, "CliOption");
        var cliFactType = MetaDocsVocabulary.EnsureFactType(model, "Cli");
        var numberValueType = MetaDocsVocabulary.EnsureValueType(model, "Number");
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:empty:app",
            DocumentationSource = source,
            DocumentationSubjectType = applicationType,
            SourceTypeName = "MetaCli.Application",
            NativeId = "empty-cli",
            DisplayName = "empty-cli",
            DisplayPath = "empty-cli",
            Status = "Current",
        });
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:orphan:command:run",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            SourceTypeName = "MetaCli.ExecutableCommand",
            NativeId = "run",
            DisplayName = "orphan run",
            DisplayPath = "orphan run",
            Status = "Current",
        });
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:orphan:option:flag",
            DocumentationSource = source,
            DocumentationSubjectType = optionType,
            SourceTypeName = "MetaCli.Option",
            NativeId = "--flag",
            DisplayName = "--flag",
            DisplayPath = "orphan run --flag",
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:orphan:command:run:fact:cli:usagecount",
            DocumentationSubject = model.DocumentationSubjectList.Single(subject => subject.Id == "source:cli:orphan:command:run"),
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            DocumentationFactType = cliFactType,
            Name = "UsageCount",
            Value = "1",
            DocumentationValueType = numberValueType,
            Status = "Current",
        });

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC024" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC025" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC026" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC028" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC030");

        var resultWithDescriptionDiagnostics = new MetaDocsValidationService().Validate(
            model,
            new MetaDocsValidationOptions { IncludeDescriptionDiagnostics = true });
        Assert.Contains(resultWithDescriptionDiagnostics.Diagnostics, diagnostic =>
            diagnostic.Id == "MDOC030" &&
            diagnostic.Severity == MetaDocsDiagnosticSeverity.Info);
    }

    [Fact]
    public void InstanceImportPolicy_DefaultsToNoInstanceDataAndHonorsExplicitIncludes()
    {
        var model = MetaDocsModel.CreateEmpty();
        var policy = new MetaDocsInstanceImportPolicy(model);

        Assert.False(policy.IncludesEntity("Customer"));
        Assert.False(policy.IncludesProperty("Customer", "Name"));
        Assert.False(policy.IncludesRelationship("Customer", "Order"));

        var source = new DocumentationSource
        {
            Id = "source:model",
            DisplayName = "Model",
            DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, "WorkspaceModel"),
            Status = "Current",
        };
        var root = new DocumentationInstanceImportSpec
        {
            Id = "instance-spec:sample",
            DocumentationSource = source,
            Name = "Sample",
            IncludeInstances = "include",
            SafetyStatus = "Approved",
        };
        var entity = new DocumentationEntityImportSpec
        {
            Id = "instance-spec:sample:entity:customer",
            DocumentationInstanceImportSpec = root,
            EntityName = "Customer",
            IncludeInstances = "include",
            DisplayNameProperty = "Name",
            SummaryProperty = "Description",
            ReviewStatus = "Current",
        };
        model.DocumentationSourceList.Add(source);
        model.DocumentationInstanceImportSpecList.Add(root);
        model.DocumentationEntityImportSpecList.Add(entity);
        model.DocumentationPropertyImportSpecList.Add(new DocumentationPropertyImportSpec
        {
            Id = "instance-spec:sample:entity:customer:property:name",
            DocumentationEntityImportSpec = entity,
            PropertyName = "Name",
            Include = "include",
            ReviewStatus = "Current",
        });
        model.DocumentationRelationshipImportSpecList.Add(new DocumentationRelationshipImportSpec
        {
            Id = "instance-spec:sample:entity:customer:relationship:order",
            DocumentationEntityImportSpec = entity,
            RelationshipSelector = "Order",
            Include = "include",
            ReviewStatus = "Current",
        });

        policy = new MetaDocsInstanceImportPolicy(model);
        Assert.True(policy.IncludesEntity("Customer", "source:model"));
        Assert.True(policy.IncludesProperty("Customer", "Name", "source:model"));
        Assert.True(policy.IncludesRelationship("Customer", "Order", "source:model"));
        Assert.False(policy.IncludesProperty("Customer", "Secret", "source:model"));
        Assert.False(policy.IncludesRelationship("Customer", "Hidden", "source:model"));

        root.IncludeInstances = "disabled";
        Assert.False(new MetaDocsInstanceImportPolicy(model).IncludesEntity("Customer", "source:model"));
    }

    [Fact]
    public async Task ImportWorkspaceInstances_EmptyPolicyImportsNoInstanceSubjectsOrFacts()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();

        var result = await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        Assert.Equal(0, result.ImportedInstanceCount);
        Assert.DoesNotContain(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance"));
        Assert.DoesNotContain(model.DocumentationFactList, row => IsFactType(row, "InstancePropertyValue"));
    }

    [Fact]
    public async Task ImportWorkspaceInstances_IncludedEntityDoesNotDumpArbitraryProperties()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(
            model,
            "Customer",
            displayNameProperty: "Name");

        var result = await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        Assert.Equal(2, result.ImportedInstanceCount);
        var source = Assert.Single(model.DocumentationSourceList, row => IsSourceType(row, "WorkspaceInstances"));
        Assert.NotNull(source.SourceFingerprint);
        Assert.Equal(64, source.SourceFingerprint.Length);
        Assert.All(
            model.DocumentationFactList.Where(row => row.DocumentationSource == source),
            fact =>
            {
                Assert.NotNull(fact.SourceFingerprint);
                Assert.Equal(64, fact.SourceFingerprint.Length);
            });
        Assert.Equal(0, result.ImportedPropertyFactCount);
        Assert.Equal(2, model.DocumentationSubjectList.Count(row => IsSubjectType(row, "Instance") && row.SourceTypeName == "Customer"));
        Assert.DoesNotContain(model.DocumentationFactList, row => IsFactType(row, "InstancePropertyValue"));
        Assert.Contains(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance") && row.DisplayName == "Ada");
    }

    [Fact]
    public async Task ImportWorkspaceInstances_IncludedPropertyImportsOnlyThatProperty()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        var editor = new MetaDocsInstanceImportPolicyEditor();
        editor.IncludeEntity(model, "Customer", displayNameProperty: "Name");
        editor.IncludeProperty(model, "Customer", "Name");
        Assert.Equal("Name", Assert.Single(model.DocumentationEntityImportSpecList).DisplayNameProperty);

        await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        Assert.Equal(2, model.DocumentationFactList.Count(row => IsFactType(row, "InstancePropertyValue") && row.Name == "Name"));
        Assert.DoesNotContain(model.DocumentationFactList, row => IsFactType(row, "InstancePropertyValue") && row.Name == "Email");
    }

    [Fact]
    public async Task ImportWorkspaceInstances_UsesSourceDisplayPathForInstanceSubjectPath()
    {
        var sourceWorkspace = CreateDisplayPathSourceWorkspace(includeDisplayPath: false);
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsWorkspaceInstanceImporter();
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(
            model,
            "Page",
            displayNameProperty: "Name");

        await importer.ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:pages",
            modelSourceId: "source:workspace-model:pages",
            displayName: "Authored pages");

        WriteDisplayPathSourceModel(sourceWorkspace, includeDisplayPath: true);
        WriteDisplayPathSourceInstances(sourceWorkspace, includeDisplayPath: true);
        await importer.ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:pages",
            modelSourceId: "source:workspace-model:pages",
            displayName: "Authored pages");

        Assert.Contains(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Instance") &&
            row.NativeId == "page-meta-cli" &&
            row.DisplayPath == "Authored pages.Reference.Meta.CLI");
        Assert.Contains(model.DocumentationSubjectList, row =>
            IsSubjectType(row, "Instance") &&
            row.NativeId == "page-meta-bi-cli" &&
            row.DisplayPath == "Authored pages.Reference.Meta-BI.CLI");
        var diagnostics = new MetaDocsValidationService().Validate(model).Diagnostics;
        Assert.DoesNotContain(diagnostics, row => row.Id == "MDOC002");
        Assert.DoesNotContain(diagnostics, row => row.Id == "MDOC014");
    }

    [Fact]
    public async Task ImportWorkspaceInstances_IncludedRelationshipImportsOnlyThatRelationship()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        var editor = new MetaDocsInstanceImportPolicyEditor();
        editor.IncludeEntity(model, "Customer", displayNameProperty: "Name");
        editor.IncludeEntity(model, "Order");
        editor.IncludeRelationship(model, "Order", "Customer");

        var result = await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        Assert.Equal(1, result.ImportedRelationshipCount);
        Assert.Contains(model.DocumentationRelationshipList, row =>
            IsRelationshipType(row, "InstanceRelationship:Customer") &&
            row.FromSubject?.Id.Contains(":instance:order:", StringComparison.OrdinalIgnoreCase) == true &&
            row.ToSubject?.Id.Contains(":instance:customer:", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task ImportWorkspaceInstances_ReimportPreservesNarrativeAndMarksRemovedInstanceMissing()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(model, "Customer", displayNameProperty: "Name");
        var importer = new MetaDocsWorkspaceInstanceImporter();

        await importer.ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");
        var ada = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance") && row.NativeId == "cust-1");
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = $"{ada.Id}:narrative:summary:900",
            DocumentationSubject = ada,
            Slot = "Summary",
            Title = "Summary",
            Body = "Authored Ada description.",
            BodyFormat = "PlainText",
            Origin = "Authored",
            ReviewStatus = "Current",
        });

        WriteSampleInstances(sourceWorkspace, includeSecondCustomer: false);
        await importer.ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        ada = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance") && row.NativeId == "cust-1");
        Assert.Equal("Authored Ada description.", Assert.Single(model.DocumentationNarrativeList, row =>
            row.DocumentationSubject?.Id == ada.Id &&
            row.Origin == "Authored").Body);
        Assert.DoesNotContain(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance") && row.NativeId == "cust-2");
    }

    [Fact]
    public async Task Validate_WarnsWhenInstancePolicyReferencesMissingProperty()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");
        var editor = new MetaDocsInstanceImportPolicyEditor();
        editor.IncludeEntity(model, "Customer", "source:workspace-model:sample");
        editor.IncludeProperty(model, "Customer", "MissingProperty", "source:workspace-model:sample");

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "MDOC017" &&
            diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task ImportWorkspaceInstances_LinksToModelSubjectsWhenModelDocsExist()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");
        var editor = new MetaDocsInstanceImportPolicyEditor();
        editor.IncludeEntity(model, "Customer", "source:workspace-model:sample", displayNameProperty: "Name");
        editor.IncludeProperty(model, "Customer", "Name", "source:workspace-model:sample");

        await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        var customerEntity = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Entity") && row.DisplayName == "Customer");
        var ada = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "Instance") && row.NativeId == "cust-1");
        Assert.Equal(customerEntity.Id, ada.ParentSubject?.Id);
        Assert.Contains(model.DocumentationRelationshipList, row =>
            row.FromSubject?.Id == ada.Id &&
            IsRelationshipType(row, "DocumentsProperty") &&
            row.ToSubject?.Id.EndsWith(":property:name", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task SuiteMerge_PreservesInstanceSubjectsFromDifferentSources()
    {
        var leftWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var rightWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var left = MetaDocsModel.CreateEmpty();
        var right = MetaDocsModel.CreateEmpty();
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(left, "Customer", displayNameProperty: "Name");
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(right, "Customer", displayNameProperty: "Name");
        await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            left,
            leftWorkspace,
            sourceId: "source:workspace-instances:left",
            displayName: "Sample docs");
        await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            right,
            rightWorkspace,
            sourceId: "source:workspace-instances:right",
            displayName: "Sample docs");

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { left, right });

        Assert.Equal(4, suite.DocumentationSubjectList.Count(row => IsSubjectType(row, "Instance")));
        Assert.Equal(
            suite.DocumentationSubjectList.Count,
            suite.DocumentationSubjectList.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(new MetaDocsValidationService().Validate(suite).Diagnostics, diagnostic => diagnostic.Id == "MDOC002");
    }

    [Fact]
    public async Task RenderSite_ExcludesImportedInstanceSubjectsFromPublicReference()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true, includeInstances: true);
        var model = MetaDocsModel.CreateEmpty();
        AddPublicReferenceTree(model);
        new MetaDocsInstanceImportPolicyEditor().IncludeEntity(model, "Customer", displayNameProperty: "Name");
        new MetaDocsInstanceImportPolicyEditor().IncludeProperty(model, "Customer", "Name");
        await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
            model,
            sourceWorkspace,
            sourceId: "source:workspace-instances:sample",
            modelSourceId: "source:workspace-model:sample",
            displayName: "Sample docs");

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.DoesNotContain("Ada", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Name: Ada", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected opt-in instance documentation", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderSite_UsesModeledThemeAndRendersGenericContent()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), parentSubjectId: reference.MetaCli.Id);
        importer.ImportApplication(model, CreateBindingApp("Bind transforms."), parentSubjectId: reference.MetaBiCli.Id);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains(".topbar{", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://metametabi.com/assets/", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml;base64,", html, StringComparison.Ordinal);
        Assert.Contains("<svg width=\"22\" height=\"22\" viewBox=\"0 0 22 22\" fill=\"none\" xmlns=\"http://www.w3.org/2000/svg\" class=\"brand-mark\" aria-hidden=\"true\" focusable=\"false\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img class=\"brand-mark\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("metametabi-mark.svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("brand-mark::before", html, StringComparison.Ordinal);
        Assert.DoesNotContain("#0875dc", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#064987", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meta+meta-bi", html, StringComparison.Ordinal);
        Assert.Contains(".brand{display:inline-flex;align-items:center;gap:10px;font-size:14px;font-weight:600", html, StringComparison.Ordinal);
        Assert.Contains(".home-button{height:36px;display:inline-flex;justify-content:center;align-items:center;padding:0 20px;border-radius:6px;background:#0a0a0a;color:#fff;font-size:13px;font-weight:500", html, StringComparison.Ordinal);
        Assert.Contains(".nav-link.is-active,.nav-surface.is-active{color:#1f2937;background:var(--accent-soft);font-weight:600}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("font-weight:800", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"brand\" href=\"https://metametabi.com\" aria-label=\"metametabi.com home\">", html, StringComparison.Ordinal);
        Assert.Contains("<a class=\"home-button\" href=\"https://metametabi.com\">Home</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"nav\"", html, StringComparison.Ordinal);
        Assert.Contains("meta + meta-bi reference", html, StringComparison.Ordinal);
        Assert.Contains("class=\"app\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"sidebar\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"viewer\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"panel is-active\" id=\"home\" data-panel=\"home\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-cli\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cli-meta-transform-binding\"", html, StringComparison.Ordinal);
        Assert.Contains("data-panel-link=\"cli-meta-transform-binding\"", html, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('hashchange'", html, StringComparison.Ordinal);
        Assert.Contains("link.classList.toggle('is-active', link.getAttribute('href') === '#' + target.id);", html, StringComparison.Ordinal);
        Assert.Contains("viewer.scrollTop = 0", html, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-docs-nav", html, StringComparison.Ordinal);
        Assert.Contains(">Meta-BI<", html, StringComparison.Ordinal);
        Assert.Contains(">CLI<", html, StringComparison.Ordinal);
        Assert.Contains("meta status", html, StringComparison.Ordinal);
        Assert.Contains("meta-transform-binding bind", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSite_TreatsNestedCliCommandsAsCommandEntries()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        var source = new DocumentationSource
        {
            Id = "source:cli:sample",
            DisplayName = "sample",
            DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, "MetaCliWorkspace"),
            Status = "Current",
        };
        var batch = new DocumentationImportBatch
        {
            Id = "source:cli:sample:batch:test",
            DocumentationSource = source,
            ImportedAt = "2026-01-01T00:00:00Z",
            ImporterId = "test",
            ImporterVersion = "1",
            Status = "Current",
        };
        model.DocumentationSourceList.Add(source);
        model.DocumentationImportBatchList.Add(batch);
        var applicationType = MetaDocsVocabulary.EnsureSubjectType(model, "CliApplication");
        var commandType = MetaDocsVocabulary.EnsureSubjectType(model, "CliCommand");
        var cliFactType = MetaDocsVocabulary.EnsureFactType(model, "Cli");
        var stringValueType = MetaDocsVocabulary.EnsureValueType(model, "String");
        var numberValueType = MetaDocsVocabulary.EnsureValueType(model, "Number");
        var app = new DocumentationSubject
        {
            Id = "source:cli:sample:app",
            DocumentationSource = source,
            DocumentationSubjectType = applicationType,
            SourceTypeName = "MetaCli.Application",
            NativeId = "sample",
            DisplayName = "sample",
            DisplayPath = "sample",
            Summary = "Sample CLI.",
            ParentSubject = reference.MetaCli,
            Status = "Current",
        };
        var parent = new DocumentationSubject
        {
            Id = "source:cli:sample:app:command:parent",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            SourceTypeName = "MetaCli.ExecutableCommand",
            NativeId = "parent",
            DisplayName = "sample parent",
            DisplayPath = "sample parent",
            Summary = "Parent command.",
            ParentSubject = app,
            Status = "Current",
        };
        var child = new DocumentationSubject
        {
            Id = "source:cli:sample:app:command:parent:command:child",
            DocumentationSource = source,
            DocumentationSubjectType = commandType,
            SourceTypeName = "MetaCli.ExecutableCommand",
            NativeId = "child",
            DisplayName = "sample parent child",
            DisplayPath = "sample parent child",
            Summary = "Child command.",
            ParentSubject = parent,
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(app);
        model.DocumentationSubjectList.Add(parent);
        model.DocumentationSubjectList.Add(child);
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:sample:app:command:parent:command:child:fact:cli:commandpath",
            DocumentationSubject = child,
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            DocumentationFactType = cliFactType,
            Name = "CommandPath",
            Value = "sample parent child",
            DocumentationValueType = stringValueType,
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:sample:app:command:parent:command:child:fact:cli:usagecount",
            DocumentationSubject = child,
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            DocumentationFactType = cliFactType,
            Name = "UsageCount",
            Value = "0",
            DocumentationValueType = numberValueType,
            Status = "Current",
        });

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);
        var diagnostics = new MetaDocsValidationService().Validate(model).Diagnostics;

        Assert.Contains("2 commands exposed by sample.", html, StringComparison.Ordinal);
        Assert.Contains("sample parent child", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Subcommands:", html, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, row => row.Code == "CliCommandMissingApplication");
    }

    [Fact]
    public async Task RenderSite_RendersModelReferenceAsCompactTablesAndHidesInternals()
    {
        var sourceWorkspace = CreateSourceModelWorkspace(includeEmail: true);
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs",
            reference.MetaModels.Id);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("<th>Entity</th><th>Properties</th><th>Relationships</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Name</th><th>Type</th><th>Required</th><th>Nullable</th><th>Description</th>", html, StringComparison.Ordinal);
        Assert.Contains("<th>Name</th><th>Target</th><th>Role</th><th>Column</th><th>Required</th>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"model-samplemodel\" data-panel=\"model-samplemodel\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"card model-entity-card\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"card model-entity-card\" open", html, StringComparison.Ordinal);
        Assert.Contains("id=\"subject-source-workspace-model-sample-entity-customer\"", html, StringComparison.Ordinal);
        Assert.Contains("data-local-anchor=\"subject-source-workspace-model-sample-entity-customer\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"subject-source-workspace-model-sample-entity-order-relationship-customer-customer\"", html, StringComparison.Ordinal);
        Assert.Contains("data-local-anchor=\"subject-source-workspace-model-sample-entity-order-relationship-customer-customer\"", html, StringComparison.Ordinal);
        Assert.Contains("<h4 class=\"subsection-title\">Referenced by</h4>", html, StringComparison.Ordinal);
        Assert.Contains("const link = event.target.closest('[data-local-anchor]');", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-summary\"", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:24px minmax(0,1fr) auto", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-toggle\" aria-hidden=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-main\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-name\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Entity Customer.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"entity-summary-text\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-counts\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-body\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-body-inner\"", html, StringComparison.Ordinal);
        Assert.Contains(".entity-body{display:grid;grid-template-columns:24px minmax(0,1fr)", html, StringComparison.Ordinal);
        Assert.Contains(".entity-body-inner{grid-column:2;min-width:0}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position:absolute", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<details class=\"inline-details\">", html, StringComparison.Ordinal);
        Assert.Contains("<summary>Technical metadata</summary>", html, StringComparison.Ordinal);
        Assert.Contains("Customer", html, StringComparison.Ordinal);
        Assert.Contains("Email", html, StringComparison.Ordinal);
        Assert.DoesNotContain("RootPath", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ContractSignature", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceFingerprint", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderSite_ConsumesModeledShellTemplateAndThemeAsset()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        new MetaDocsCliImporter().ImportApplication(model, CreateMetaApp(), parentSubjectId: reference.MetaCli.Id);
        var template = Assert.Single(model.DocumentationTemplateList, row => IsTemplateType(row, "SiteShell"));
        template.Html = "MODELED {{title}} {{css}} {{navigation}} {{content}} {{script}}";
        var css = Assert.Single(model.DocumentationThemeAssetList, row => IsThemeAssetType(row, "Css"));
        css.Content = "/* modeled css */ .custom{}";

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.StartsWith("MODELED meta + meta-bi &#183; Reference", html, StringComparison.Ordinal);
        Assert.Contains("/* modeled css */", html, StringComparison.Ordinal);
        Assert.Contains("meta status", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSite_RendersCliReferenceWithAuthoredDescriptionOptionsExamplesAndNoPublicConfessions()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."), parentSubjectId: reference.MetaBiCli.Id);
        var command = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliCommand"));
        var exampleAuthoring = new MetaDocsExampleAuthoringService();
        exampleAuthoring.UpsertExample(
            model,
            new MetaDocsSubjectSelector(Cli: "meta-transform-binding", Command: "bind"),
            $"{command.Id}:example:bind",
            "Bind a transform workspace",
            string.Empty,
            $"{command.Id}:example:bind:section",
            "Bind every script in a transform workspace against source and target schema workspaces.",
            "PlainText",
            string.Empty);
        exampleAuthoring.UpsertCode(
            model,
            $"{command.Id}:example:bind:section",
            $"{command.Id}:example:bind:command",
            "Command",
            "powershell",
            "meta-transform-binding bind --transform-workspace TransformWS --source-schema SchemaWS --target-schema SchemaWS",
            string.Empty);
        var application = Assert.Single(model.DocumentationSubjectList, row => IsSubjectType(row, "CliApplication"));
        var hiddenProperty = new DocumentationSubject
        {
            Id = $"{application.Id}:property:sourcefingerprint",
            DocumentationSource = application.DocumentationSource,
            ParentSubject = application,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "Property"),
            SourceTypeName = "Property",
            NativeId = "SourceFingerprint",
            DisplayName = "SourceFingerprint",
            DisplayPath = $"{application.DisplayPath}.SourceFingerprint",
            Summary = "Internal refresh fingerprint.",
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(hiddenProperty);
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = $"{application.Id}:fact:test:sourcefingerprint",
            DocumentationSubject = application,
            DocumentationFactType = MetaDocsVocabulary.EnsureFactType(model, "Test"),
            Name = "SourceFingerprint",
            Value = "first suite outside MetaDocs today",
            DocumentationValueType = MetaDocsVocabulary.EnsureValueType(model, "String"),
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = $"{hiddenProperty.Id}:fact:model:name",
            DocumentationSubject = hiddenProperty,
            DocumentationFactType = MetaDocsVocabulary.EnsureFactType(model, "Model"),
            Name = "Name",
            Value = "SourceFingerprint",
            DocumentationValueType = MetaDocsVocabulary.EnsureValueType(model, "String"),
            Status = "Current",
        });

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("meta-transform-binding", html, StringComparison.Ordinal);
        Assert.Contains("meta-transform-binding bind", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cli-meta-transform-binding\" data-panel=\"cli-meta-transform-binding\"", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"card cli-command-card\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"card cli-command-card\" open", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-summary\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-toggle\" aria-hidden=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-main\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-name\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-counts\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-body\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"command-body-inner\"", html, StringComparison.Ordinal);
        Assert.Contains("<th>Option</th><th>Description</th><th>Value</th><th>Required</th>", html, StringComparison.Ordinal);
        Assert.Contains("--source-schema &lt;path&gt;", html, StringComparison.Ordinal);
        Assert.Contains("Bind transforms.", html, StringComparison.Ordinal);
        Assert.Contains("meta-transform-binding bind --transform-workspace", html, StringComparison.Ordinal);
        Assert.DoesNotContain("> - bind all transform scripts", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("> - `--source-schema`", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Samples\\Demos", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Samples/Demos", html, StringComparison.OrdinalIgnoreCase);
        foreach (var phrase in new[]
                 {
                     "first suite",
                     "outside MetaDocs today",
                     "need public CLI definition factories",
                     "future work",
                     "remaining gaps",
                     "Codex",
                     "SourceFingerprint",
                 })
        {
            Assert.DoesNotContain(phrase, html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("class=\"chev\" aria-hidden=\"true\">&gt;</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSite_RendersApplicationAndModelExamplesAfterDetailCards()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        new MetaDocsCliImporter().ImportApplication(model, CreateMetaDocsApp(), parentSubjectId: reference.MetaCli.Id);
        AddModelSubject(model, "MetaDocs", reference.MetaModels);

        var modelSubject = Assert.Single(
            model.DocumentationSubjectList,
            row => IsSubjectType(row, "Model") && row.DisplayName == "MetaDocs");
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:model:test:entity:documentationexample",
            DocumentationSource = modelSubject.DocumentationSource,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "Entity"),
            ParentSubject = modelSubject,
            SourceTypeName = "GenericEntity",
            NativeId = "DocumentationExample",
            DisplayName = "DocumentationExample",
            DisplayPath = "MetaDocs.DocumentationExample",
            Summary = "Structured documentation examples.",
            Status = "Current",
        });

        var examples = new MetaDocsExampleAuthoringService();
        examples.UpsertExample(
            model,
            new MetaDocsSubjectSelector(Cli: "meta-docs"),
            "example:meta-docs:app-general",
            "Plan documentation work",
            string.Empty,
            "example:meta-docs:app-general:overview",
            "Use the MetaDocs CLI to refresh, browse, and render documentation workspaces.",
            "PlainText",
            string.Empty);
        examples.UpsertExample(
            model,
            new MetaDocsSubjectSelector(Model: "MetaDocs"),
            "example:metadocs:model-general",
            "Shape a docs workspace",
            string.Empty,
            "example:metadocs:model-general:overview",
            "Use the model reference to understand the durable documentation workspace structure.",
            "PlainText",
            string.Empty);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        var cliPanelIndex = html.IndexOf("id=\"cli-meta-docs\" data-panel=\"cli-meta-docs\"", StringComparison.Ordinal);
        Assert.True(cliPanelIndex >= 0);
        var commandOverviewIndex = html.IndexOf("<h3>Commands</h3>", cliPanelIndex, StringComparison.Ordinal);
        var commandDetailIndex = html.IndexOf("<details class=\"card cli-command-card\">", cliPanelIndex, StringComparison.Ordinal);
        var applicationExampleIndex = html.IndexOf("Plan documentation work", cliPanelIndex, StringComparison.Ordinal);
        Assert.True(commandOverviewIndex >= 0);
        Assert.True(commandDetailIndex > commandOverviewIndex);
        Assert.True(applicationExampleIndex > commandDetailIndex);

        var modelPanelIndex = html.IndexOf("id=\"model-metadocs\" data-panel=\"model-metadocs\"", StringComparison.Ordinal);
        Assert.True(modelPanelIndex >= 0);
        var entityIndexIndex = html.IndexOf("<h3>Entity index</h3>", modelPanelIndex, StringComparison.Ordinal);
        var entityDetailIndex = html.IndexOf("<details class=\"card model-entity-card\"", modelPanelIndex, StringComparison.Ordinal);
        var modelExampleIndex = html.IndexOf("Shape a docs workspace", modelPanelIndex, StringComparison.Ordinal);
        Assert.True(entityIndexIndex >= 0);
        Assert.True(entityDetailIndex > entityIndexIndex);
        Assert.True(modelExampleIndex > entityDetailIndex);

        Assert.Contains("<h4 class=\"subsection-title\">General examples</h4>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSite_UsesSinglePanelRoutesAndModeledPublicHierarchy()
    {
        var model = MetaDocsModel.CreateEmpty();
        var reference = AddPublicReferenceTree(model);
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), parentSubjectId: reference.MetaCli.Id);
        importer.ImportApplication(model, CreateMetaDocsApp(), parentSubjectId: reference.MetaCli.Id);
        importer.ImportApplication(model, CreateSimpleApp("meta-data-type"), parentSubjectId: reference.MetaBiCli.Id);
        importer.ImportApplication(model, CreateSimpleApp("meta-convert"), parentSubjectId: reference.MetaBiCli.Id);
        AddModelSubject(model, "MetaDocs", reference.MetaModels);
        AddModelSubject(model, "MetaTransformBinding", reference.MetaBiModels);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("href=\"#cli-meta-docs\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cli-meta-docs\" data-panel=\"cli-meta-docs\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-cli\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#group-public-meta-bi-cli\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#model-metadocs\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"model-metadocs\" data-panel=\"model-metadocs\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cli-meta-data-type\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#cli-meta-convert\"", html, StringComparison.Ordinal);

        var metaBiIndex = html.IndexOf("<div class=\"nav-product\">Meta-BI</div>", StringComparison.Ordinal);
        Assert.True(metaBiIndex >= 0);
        Assert.True(html.IndexOf("href=\"#cli-meta-data-type\"", StringComparison.Ordinal) > metaBiIndex);
        Assert.True(html.IndexOf("href=\"#cli-meta-convert\"", StringComparison.Ordinal) > metaBiIndex);

        Assert.DoesNotContain("What is Meta?", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Documentation lifecycle", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Selected opt-in instance documentation", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntersectionObserver", html, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", html, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunCli(
        string arguments,
        string? workingDirectory = null,
        string? standardInput = null)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = Path.Combine(repoRoot, "MetaDocs", "Cli", "bin", "Debug", "net8.0", "meta-docs.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled meta-docs executable at '{cliPath}'. Build MetaDocs.Cli before running tests.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-docs process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            TryKillProcessTree(process);
            process.WaitForExit();
            throw new TimeoutException($"Timed out waiting for process: {startInfo.FileName} {startInfo.Arguments}", exception);
        }

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
    }

    private static string QuoteArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void AssertBrowseHasNoRouteNoise(string output)
    {
        Assert.DoesNotContain("Path:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("MetaDocs >", output, StringComparison.Ordinal);
    }

    private static void AssertDoesNotStartWithSelectedRoute(string output, string route)
    {
        var firstLine = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];
        Assert.False(string.Equals(route, firstLine, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWorkspaceType(DocumentationWorkspace workspace, string name) =>
        MetaDocsVocabulary.IsWorkspaceType(workspace, name);

    private static bool IsSourceType(DocumentationSource source, string name) =>
        MetaDocsVocabulary.IsSourceType(source, name);

    private static bool IsSubjectType(DocumentationSubject subject, string name) =>
        MetaDocsVocabulary.IsSubjectType(subject, name);

    private static bool IsFactType(DocumentationFact fact, string name) =>
        MetaDocsVocabulary.IsFactType(fact, name);

    private static bool IsRelationshipType(DocumentationRelationship relationship, string name) =>
        MetaDocsVocabulary.IsRelationshipType(relationship, name);

    private static bool IsTemplateType(DocumentationTemplate template, string name) =>
        MetaDocsVocabulary.IsTemplateType(template, name);

    private static bool IsThemeAssetType(DocumentationThemeAsset asset, string name) =>
        MetaDocsVocabulary.IsThemeAssetType(asset, name);

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static PublicReferenceSubjects AddPublicReferenceTree(MetaDocsModel model)
    {
        var authoring = new MetaDocsAuthoringService();
        var root = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:reference",
                "meta + meta-bi reference",
                "Command-line and model references for the current public MetaDocs suite.",
                "Command-line and model references for the current public MetaDocs suite.",
                SubjectType: "ReferenceRoot",
                DisplayPath: "Reference",
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference",
                IsViewRoot: true));
        var meta = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta",
                "Meta",
                "Foundation models and tools for authored metadata workspaces.",
                "Foundation models and tools for authored metadata workspaces.",
                SubjectType: "ReferenceGroup",
                DisplayPath: "Reference.Meta",
                ParentSubjectId: root.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));
        var metaCli = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta:cli",
                "CLI",
                "Command-line tools for the core metadata foundation.",
                "Command-line tools for the core metadata foundation.",
                SubjectType: "ReferenceSection",
                DisplayPath: "Reference.Meta.CLI",
                ParentSubjectId: meta.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));
        var metaModels = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta:models",
                "Models",
                "Model references for the core metadata foundation.",
                "Model references for the core metadata foundation.",
                SubjectType: "ReferenceSection",
                DisplayPath: "Reference.Meta.Models",
                ParentSubjectId: meta.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));
        var metaBi = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta-bi",
                "Meta-BI",
                "BI-side models and tools built on the metadata foundation.",
                "BI-side models and tools built on the metadata foundation.",
                SubjectType: "ReferenceGroup",
                DisplayPath: "Reference.Meta-BI",
                ParentSubjectId: root.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));
        var metaBiCli = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta-bi:cli",
                "CLI",
                "Command-line tools for BI modeling, conversion, validation, and execution.",
                "Command-line tools for BI modeling, conversion, validation, and execution.",
                SubjectType: "ReferenceSection",
                DisplayPath: "Reference.Meta-BI.CLI",
                ParentSubjectId: metaBi.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));
        var metaBiModels = authoring.UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "public:meta-bi:models",
                "Models",
                "Model references for the sanctioned BI metadata workspaces.",
                "Model references for the sanctioned BI metadata workspaces.",
                SubjectType: "ReferenceSection",
                DisplayPath: "Reference.Meta-BI.Models",
                ParentSubjectId: metaBi.Id,
                SourceId: "source:authored:public-reference",
                SourceDisplayName: "Public reference"));

        return new PublicReferenceSubjects(root, meta, metaCli, metaModels, metaBi, metaBiCli, metaBiModels);
    }

    private static DocumentationSubject EnsureSubject(MetaDocsModel model, string id)
    {
        var existing = model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var source = model.DocumentationSourceList.FirstOrDefault(row => row.Id == "source:test:parent-stubs")
            ?? new DocumentationSource
            {
                Id = "source:test:parent-stubs",
                DisplayName = "Parent stubs",
                DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, "Test"),
                Status = "Current",
            };
        if (!model.DocumentationSourceList.Contains(source))
        {
            model.DocumentationSourceList.Add(source);
        }

        var subject = new DocumentationSubject
        {
            Id = id,
            DocumentationSource = source,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "ReferenceSection"),
            SourceTypeName = "Test",
            NativeId = id,
            DisplayName = id,
            DisplayPath = id,
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(subject);
        return subject;
    }

    private static void AddModelSubject(MetaDocsModel model, string modelName, DocumentationSubject? parentSubject = null)
    {
        var source = model.DocumentationSourceList.FirstOrDefault(row => row.Id == "source:model:test")
            ?? new DocumentationSource
            {
                Id = "source:model:test",
                DisplayName = "Model test",
                DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, "WorkspaceModel"),
                Status = "Current",
            };
        var batch = model.DocumentationImportBatchList.FirstOrDefault(row => row.Id == "source:model:test:batch:test")
            ?? new DocumentationImportBatch
            {
                Id = "source:model:test:batch:test",
                DocumentationSource = source,
                ImportedAt = "2026-01-01T00:00:00Z",
                ImporterId = "test",
                ImporterVersion = "1",
                SourceFingerprint = "test",
                Status = "Current",
            };
        if (!model.DocumentationSourceList.Contains(source))
        {
            model.DocumentationSourceList.Add(source);
        }

        if (!model.DocumentationImportBatchList.Contains(batch))
        {
            model.DocumentationImportBatchList.Add(batch);
        }

        var normalized = MetaDocsImportSession.NormalizeKey(modelName);
        var subject = new DocumentationSubject
        {
            Id = $"source:model:test:model:{normalized}",
            DocumentationSource = source,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "Model"),
            SourceTypeName = "GenericModel",
            NativeId = modelName,
            DisplayName = modelName,
            DisplayPath = $"{modelName} model",
            Summary = $"Model {modelName}.",
            ParentSubject = parentSubject,
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(subject);
    }

    private sealed record PublicReferenceSubjects(
        DocumentationSubject Root,
        DocumentationSubject Meta,
        DocumentationSubject MetaCli,
        DocumentationSubject MetaModels,
        DocumentationSubject MetaBi,
        DocumentationSubject MetaBiCli,
        DocumentationSubject MetaBiModels);

    private static void AddLowLevelDeployProperty(MetaDocsModel model)
    {
        var source = new DocumentationSource
        {
            Id = "source:test:low-level-search",
            DisplayName = "Low-level search test",
            DocumentationSourceType = MetaDocsVocabulary.EnsureSourceType(model, "WorkspaceModel"),
            Status = "Current",
        };
        model.DocumentationSourceList.Add(source);
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:test:low-level-search:property:deployordinal",
            DocumentationSource = source,
            DocumentationSubjectType = MetaDocsVocabulary.EnsureSubjectType(model, "Property"),
            SourceTypeName = "GenericProperty",
            NativeId = "DeployOrdinal",
            DisplayName = "DeployOrdinal",
            DisplayPath = "MetaSql.DeployOrdinal",
            Summary = "Low-level deploy property used for search ranking coverage.",
            Status = "Current",
        });
    }

    private static DocumentationFact FindFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind,
        string name) =>
        Assert.Single(model.DocumentationFactList, row =>
            row.DocumentationSubject?.Id == subject.Id &&
            IsFactType(row, kind) &&
            row.Name == name);

    private static DocumentationNarrative FindNarrative(
        MetaDocsModel model,
        DocumentationSubject subject,
        string slot) =>
        Assert.Single(model.DocumentationNarrativeList, row =>
            row.DocumentationSubject?.Id == subject.Id &&
            row.Slot == slot);

    private static string CreateSourceModelWorkspace(bool includeEmail, bool includeInstances = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-model-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WriteSampleModel(root, includeEmail);
        if (includeInstances)
        {
            WriteSampleInstances(root, includeSecondCustomer: true);
        }

        return root;
    }

    private static void WriteSampleModel(string root, bool includeEmail)
    {
        var emailProperty = includeEmail
            ? """<Property name="Email" isRequired="false" />"""
            : string.Empty;
        File.WriteAllText(
            Path.Combine(root, "model.xml"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Model name="SampleModel">
              <EntityList>
                <Entity name="Customer">
                  <PropertyList>
                    <Property name="Name" />
                    {{emailProperty}}
                  </PropertyList>
                </Entity>
                <Entity name="Order">
                  <PropertyList>
                    <Property name="Amount" dataType="decimal" />
                  </PropertyList>
                  <RelationshipList>
                    <Relationship entity="Customer" />
                  </RelationshipList>
                </Entity>
              </EntityList>
            </Model>
            """);
    }

    private static void WriteSampleInstances(string root, bool includeSecondCustomer)
    {
        var secondCustomer = includeSecondCustomer
            ? """
                  <Customer Id="cust-2">
                    <Name>Bob</Name>
                    <Email>bob@example.test</Email>
                  </Customer>
            """
            : string.Empty;
        Directory.CreateDirectory(Path.Combine(root, "instances"));
        File.WriteAllText(
            Path.Combine(root, "instances", "Sample.xml"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <SampleModel>
              <CustomerList>
                <Customer Id="cust-1">
                  <Name>Ada</Name>
                  <Email>ada@example.test</Email>
                </Customer>
            {{secondCustomer}}
              </CustomerList>
              <OrderList>
                <Order Id="order-1" CustomerId="cust-1">
                  <Amount>42.50</Amount>
                </Order>
              </OrderList>
            </SampleModel>
            """);
    }

    private static string CreateDisplayPathSourceWorkspace(bool includeDisplayPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-display-path-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WriteDisplayPathSourceModel(root, includeDisplayPath);
        WriteDisplayPathSourceInstances(root, includeDisplayPath);
        return root;
    }

    private static void WriteDisplayPathSourceModel(string root, bool includeDisplayPath)
    {
        var displayPathProperty = includeDisplayPath
            ? """<Property name="DisplayPath" />"""
            : string.Empty;
        File.WriteAllText(
            Path.Combine(root, "model.xml"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Model name="PageModel">
              <EntityList>
                <Entity name="Page">
                  <PropertyList>
                    <Property name="Name" />
                    {{displayPathProperty}}
                  </PropertyList>
                </Entity>
              </EntityList>
            </Model>
            """);
    }

    private static void WriteDisplayPathSourceInstances(string root, bool includeDisplayPath)
    {
        var metaDisplayPath = includeDisplayPath
            ? "<DisplayPath>Reference.Meta.CLI</DisplayPath>"
            : string.Empty;
        var metaBiDisplayPath = includeDisplayPath
            ? "<DisplayPath>Reference.Meta-BI.CLI</DisplayPath>"
            : string.Empty;
        Directory.CreateDirectory(Path.Combine(root, "instances"));
        File.WriteAllText(
            Path.Combine(root, "instances", "Page.xml"),
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <PageModel>
              <PageList>
                <Page Id="page-meta-cli">
                  <Name>CLI</Name>
                  {{metaDisplayPath}}
                </Page>
                <Page Id="page-meta-bi-cli">
                  <Name>CLI</Name>
                  {{metaBiDisplayPath}}
                </Page>
              </PageList>
            </PageModel>
            """);
    }

    private static MetaCliModel CreateMetaApp() =>
        CreateCliApp(
            "meta",
            "Core metadata model + instance engine.",
            new TestCliCommand("status", "Show workspace summary."));

    private static MetaCliModel CreateMetaDocsApp() =>
        CreateCliApp(
            "meta-docs",
            commands: new TestCliCommand(
                "validate",
                "Validate documentation workspace.",
                new TestCliOption("--workspace", "<path>", "Source schema workspace.")));

    private static MetaCliModel CreateMetaSqlApp() =>
        CreateCliApp(
            "meta-sql",
            "SQL workspace extraction, deploy planning, and execution tooling.",
            new TestCliCommand(
                "deploy",
                "Apply a deploy manifest after source and live fingerprint validation.",
                new TestCliOption("--connection-env", "<value>", "Environment variable containing the SQL Server connection string."),
                new TestCliOption("--manifest-workspace", "<path>", "Deploy manifest workspace created by deploy-plan."),
                new TestCliOption("--source-workspace", "<path>", "Source MetaSql workspace used to create the manifest.")),
            new TestCliCommand(
                "deploy-plan",
                "Create a deploy manifest from a source MetaSql workspace and a live SQL Server database."),
            new TestCliCommand(
                "execute",
                "Execute a SQL Server file or query for demo, bootstrap, and verification scripts."),
            new TestCliCommand(
                "extract sqlserver",
                "Extract SQL Server database objects into a MetaSql workspace."));

    private static MetaCliModel CreateSimpleApp(string name) =>
        CreateCliApp(
            name,
            commands: new TestCliCommand("run", "Run the command."));

    private static MetaCliModel CreateBindingApp(string summary, bool includeInspect = false)
    {
        var commands = new List<TestCliCommand>
        {
            new(
                "bind",
                summary,
                new TestCliOption("--source-schema", "<path>", "Source schema workspace."))
        };
        if (includeInspect)
        {
            commands.Add(new TestCliCommand(
                "inspect",
                "Inspect binding findings.",
                new TestCliOption("--workspace", "<path>", "MetaDocs workspace.")));
        }

        return CreateCliApp(
            "meta-transform-binding",
            "Bind transform scripts against schema contracts.",
            commands.ToArray());
    }

    private static MetaCliModel CreateSameNamedApp() =>
        CreateCliApp(
            "same-cli",
            commands: new TestCliCommand("run", "Run the command."));

    private static MetaCliModel CreateCliApp(
        string executableName,
        string description = "",
        params TestCliCommand[] commands)
    {
        var model = MetaCliModel.CreateEmpty();
        var arityNone = new ValueArity
        {
            Id = "arity-none",
            Name = "None",
            MinValueCount = "0",
            MaxValueCount = "0",
        };
        var arityOne = new ValueArity
        {
            Id = "arity-one",
            Name = "One",
            MinValueCount = "1",
            MaxValueCount = "1",
        };
        var shapeFlag = new ValueShape
        {
            Id = "shape-flag",
            Name = "Flag",
            ValueArity = arityNone,
        };
        var shapeText = new ValueShape
        {
            Id = "shape-text",
            Name = "Text",
            ValueLabel = "<value>",
            ValueArity = arityOne,
        };
        var shapePath = new ValueShape
        {
            Id = "shape-path",
            Name = "Path",
            ValueLabel = "<path>",
            AllowsOptionLikeValue = "true",
            ValueArity = arityOne,
        };
        model.ValueArityList.AddRange([arityNone, arityOne]);
        model.ValueShapeList.AddRange([shapeFlag, shapeText, shapePath]);

        var app = new Application
        {
            Id = $"app-{NormalizeTestId(executableName)}",
            Name = executableName,
            ExecutableName = executableName,
            Description = description,
        };
        model.ApplicationList.Add(app);

        foreach (var commandSpec in commands)
        {
            var command = new Command
            {
                Id = $"cmd-{NormalizeTestId(commandSpec.Name)}",
                Application = app,
                Name = commandSpec.Name,
                Token = commandSpec.Name,
                Description = commandSpec.Description,
            };
            var executable = new ExecutableCommand
            {
                Id = commandSpec.Name,
                Command = command,
            };
            model.CommandList.Add(command);
            model.ExecutableCommandList.Add(executable);

            foreach (var optionSpec in commandSpec.Options)
            {
                var valueShape = optionSpec.ValueLabel.Length == 0
                    ? shapeFlag
                    : string.Equals(optionSpec.ValueLabel, "<path>", StringComparison.Ordinal)
                        ? shapePath
                        : shapeText;
                var optionName = optionSpec.Token.TrimStart('-');
                var parameter = new Parameter
                {
                    Id = $"param-{NormalizeTestId(commandSpec.Name)}-{NormalizeTestId(optionName)}",
                    Name = optionName,
                    Description = optionSpec.Description,
                    ValueShape = valueShape,
                };
                var option = new Option
                {
                    Id = $"option-{NormalizeTestId(commandSpec.Name)}-{NormalizeTestId(optionName)}",
                    Parameter = parameter,
                };
                var token = new OptionToken
                {
                    Id = $"token-{NormalizeTestId(commandSpec.Name)}-{NormalizeTestId(optionName)}",
                    Option = option,
                    Token = optionSpec.Token,
                };
                model.ParameterList.Add(parameter);
                model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
                {
                    Id = $"{executable.Id}:parameter:{parameter.Id}",
                    ExecutableCommand = executable,
                    Parameter = parameter,
                });
                model.OptionList.Add(option);
                model.OptionTokenList.Add(token);
            }
        }

        return model;
    }

    private static string NormalizeTestId(string value) =>
        MetaDocsImportSession.NormalizeKey(value);

    private sealed record TestCliCommand(
        string Name,
        string Description,
        params TestCliOption[] Options);

    private sealed record TestCliOption(
        string Token,
        string ValueLabel,
        string Description);
}
