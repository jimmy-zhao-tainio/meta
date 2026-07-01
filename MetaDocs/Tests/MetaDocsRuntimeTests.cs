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
        Assert.Contains("import-cli", appHelp.Output);
        Assert.Contains("render-site", appHelp.Output);

        var commandHelp = RunCli("help import-cli");
        Assert.Equal(0, commandHelp.ExitCode);
        Assert.Contains("meta-docs import-cli", commandHelp.Output);
        Assert.Contains("--source-workspace <path>", commandHelp.Output);
        Assert.Contains("--new-workspace <path>", commandHelp.Output);

        var source = File.ReadAllText(Path.Combine(repoRoot, "MetaDocs", "Cli", "Program.cs"));
        Assert.Contains("MetaCliRuntime<MetaDocsModel>", source);
        Assert.Contains(".UseDefaultHelp()", source);
        Assert.DoesNotContain("ParseAuthorPageArgs", source);
        Assert.DoesNotContain("ReadStringOption", source);
        Assert.DoesNotContain("CliAppDefinition", source);
        Assert.DoesNotContain("CliCommandDefinition", source);
        Assert.DoesNotContain("CliOptionDefinition", source);
        Assert.DoesNotContain("CliHelpRenderer", source);
    }

    [Fact]
    public void AuthoringService_UpsertsPageNarrativeAndViewNode()
    {
        var model = MetaDocsModel.CreateEmpty();
        var page = new MetaDocsAuthoredPage(
            "docs:home",
            "meta + meta-bi",
            "Model-first documentation for meta and meta-bi.",
            "MetaDocs stores authored prose beside refreshable generated facts.");

        var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
        new MetaDocsAuthoringService().UpsertPage(
            model,
            page with { Body = "Updated authored prose.", Summary = "Updated summary." });

        subject = Assert.Single(model.DocumentationSubjectList, row => row.Id == "docs:home");
        Assert.Equal("Guide", subject.Kind);
        Assert.Equal("Updated summary.", subject.Summary);
        Assert.Equal("AuthoredDocumentation", subject.DocumentationSource.Kind);

        var narrative = Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == subject.Id &&
            row.Origin == "Authored");
        Assert.Equal("Updated authored prose.", narrative.Body);
        Assert.Equal("Current", narrative.ReviewStatus);

        Assert.Contains(model.DocumentationViewNodeList, row =>
            row.SubjectKey == subject.Id &&
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
                ]),
            groupName: "meta-bi");

        importer.ImportApplication(
            model,
            CreateBindingApp("Bind transforms after help text changed."),
            groupName: "meta-bi");

        var application = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliApplication");
        Assert.Equal("source:cli:meta-transform-binding:app", application.Id);
        Assert.Equal("Authored application summary.", FindNarrative(model, application, "Summary").Body);
        Assert.Equal("meta-bi", FindFact(model, application, "Cli", "GroupName").Value);

        var command = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliCommand");
        Assert.Equal("meta-transform-binding bind", command.DisplayName);
        Assert.Equal("Bind transforms after help text changed.", command.Summary);
        Assert.Equal("Changed", command.Status);
        Assert.Equal("Authored purpose.", FindNarrative(model, command, "Summary").Body);
        Assert.Equal("Authored when.", FindNarrative(model, command, "Usage").Body);
        Assert.Equal("Authored how.", FindNarrative(model, command, "ImplementationNote").Body);
        Assert.Equal("1", FindFact(model, command, "Cli", "OptionCount").Value);

        var option = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliOption");
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
            row.Kind == "CliCommand" &&
            row.NativeId == "inspect");
        Assert.DoesNotContain(model.DocumentationFactList, row =>
            row.SubjectKey.Contains("inspect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportApplication_SameFingerprintReusesImportBatch()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();

        importer.ImportApplication(model, CreateBindingApp("Bind transforms."));
        var source = Assert.Single(model.DocumentationSourceList, row => row.Kind == "MetaCliWorkspace");
        var batch = Assert.Single(model.DocumentationImportBatchList);
        var importedAt = source.ImportedAt;

        importer.ImportApplication(model, CreateBindingApp("Bind transforms."));

        var reimportedSource = Assert.Single(model.DocumentationSourceList, row => row.Kind == "MetaCliWorkspace");
        var reimportedBatch = Assert.Single(model.DocumentationImportBatchList);
        Assert.Equal(batch.Id, reimportedBatch.Id);
        Assert.Equal(importedAt, reimportedSource.ImportedAt);
        Assert.All(
            model.DocumentationFactList.Where(row => ReferenceEquals(row.DocumentationSource, reimportedSource)),
            fact => Assert.Equal(batch.Id, fact.DocumentationImportBatch.Id));
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

        new MetaDocsCliImporter().ImportApplication(model, cli, groupName: "meta");

        var application = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliApplication");
        Assert.Equal(string.Empty, application.Summary);
        Assert.True(string.IsNullOrWhiteSpace(FindNarrative(model, application, "Summary").Body));

        var command = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliCommand");
        Assert.Equal("1", FindFact(model, command, "Cli", "ParameterGroupCount").Value);

        var mode = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "CliOption" &&
            row.DisplayName == "--mode");
        Assert.Equal("Mode", FindFact(model, mode, "Cli", "ValueShape").Value);
        Assert.Equal("One", FindFact(model, mode, "Cli", "ValueArity").Value);
        Assert.Equal("fast, safe", FindFact(model, mode, "Cli", "AllowedValues").Value);
        Assert.Equal(2, model.DocumentationSubjectList.Count(row =>
            row.Kind == "CliAllowedValue" &&
            row.ParentKey == mode.Id));

        var importedGroup = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliParameterGroup");
        Assert.Equal("IdChoice", importedGroup.DisplayName);
        Assert.Equal("true", FindFact(model, importedGroup, "Cli", "Required").Value);
        Assert.Equal("false", FindFact(model, importedGroup, "Cli", "AllowsMultiple").Value);
        Assert.Equal("auto-id, id", FindFact(model, importedGroup, "Cli", "Members").Value);

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);
        Assert.Contains("&lt;mode&gt;: fast, safe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<p class=\"panel-lead\"></p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCommandProse_MapsMarkdownToCliSubjectsAndPreservesAuthoredNarratives()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."));
        var command = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliCommand");
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = $"{command.Id}:narrative:guidance:040",
            DocumentationSubject = command,
            SubjectKey = command.Id,
            Slot = "Guidance",
            Title = "Guidance",
            Body = "Authored binding guidance.",
            BodyFormat = "PlainText",
            Origin = "Authored",
            ReviewStatus = "Current",
        });
        var markdownPath = WriteBindingCommandMarkdown();

        var result = await new MetaDocsMarkdownCommandProseImporter().ImportCommandProseAsync(
            model,
            markdownPath,
            "source:markdown:test");
        await new MetaDocsMarkdownCommandProseImporter().ImportCommandProseAsync(
            model,
            markdownPath,
            "source:markdown:test");

        Assert.Equal(1, result.SourceFileCount);
        Assert.Equal(1, result.MatchedApplicationCount);
        Assert.Equal(1, result.MatchedCommandCount);
        var application = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliApplication");
        Assert.Contains("binding contract layer", FindNarrative(model, application, "Summary").Body);
        Assert.Equal("ImportedMarkdown", FindNarrative(model, application, "Summary").Origin);
        Assert.Equal("Authored binding guidance.", FindNarrative(model, command, "Guidance").Body);

        var example = FindNarrative(model, command, "Example");
        Assert.Equal("ImportedMarkdown", example.Origin);
        Assert.Contains("meta-transform-binding bind", example.Body);
        Assert.DoesNotContain("Samples\\Demos", FindNarrative(model, command, "Guidance").Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(model.DocumentationNarrativeList, row =>
            row.SubjectKey == command.Id &&
            row.Slot == "Reference");

        var option = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "CliOption" &&
            row.DisplayName == "--source-schema");
        var optionUsage = FindNarrative(model, option, "Usage");
        Assert.Equal("ImportedMarkdown", optionUsage.Origin);
        Assert.Contains("--source-schema", optionUsage.Body);
        Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == command.Id &&
            row.Slot == "Guidance");
        Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == command.Id &&
            row.Slot == "Example");
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

        var workspace = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Workspace");
        Assert.Equal("Sample docs", workspace.DisplayName);
        var source = Assert.Single(model.DocumentationSourceList, row => row.Kind == "WorkspaceModel");
        Assert.NotNull(source.SourceFingerprint);
        Assert.Equal(64, source.SourceFingerprint.Length);
        Assert.All(
            model.DocumentationFactList.Where(row => row.DocumentationSource == source),
            fact =>
            {
                Assert.NotNull(fact.SourceFingerprint);
                Assert.Equal(64, fact.SourceFingerprint.Length);
            });
        var modelSubject = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Model");
        Assert.Equal("SampleModel", modelSubject.DisplayName);
        Assert.Equal("2", FindFact(model, modelSubject, "Model", "EntityCount").Value);

        var customer = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "Entity" &&
            row.DisplayName == "Customer");
        Assert.Equal("2", FindFact(model, customer, "Model", "PropertyCount").Value);

        var email = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "Property" &&
            row.DisplayName == "Email");
        Assert.Equal("True", FindFact(model, email, "Model", "Nullable").Value);

        var orderCustomer = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "Relationship" &&
            row.DisplayName == "Customer");
        Assert.Equal("Customer", FindFact(model, orderCustomer, "Model", "TargetEntity").Value);
        Assert.Contains(model.DocumentationRelationshipList, row =>
            row.FromSubjectKey == orderCustomer.Id &&
            row.Kind == "ReferencesEntity" &&
            row.ToSubjectKey == customer.Id);
    }

    [Fact]
    public async Task ImportWorkspaceModel_ReimportPreservesAuthoredNarrativeMarksRemovedPropertyAndUpdatesFacts()
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
            row.Kind == "Entity" &&
            row.DisplayName == "Customer");
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = $"{customer.Id}:narrative:summary:900",
            DocumentationSubject = customer,
            SubjectKey = customer.Id,
            Slot = "Summary",
            Title = "Summary",
            Body = "Authored customer prose.",
            BodyFormat = "PlainText",
            Origin = "Authored",
            ReviewStatus = "Current",
        });

        WriteSampleModel(sourceWorkspace, includeEmail: false);
        await importer.ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");

        customer = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "Entity" &&
            row.DisplayName == "Customer");
        Assert.Equal("1", FindFact(model, customer, "Model", "PropertyCount").Value);
        Assert.Equal("Authored customer prose.", Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == customer.Id &&
            row.Origin == "Authored").Body);

        var email = Assert.Single(model.DocumentationSubjectList, row =>
            row.Kind == "Property" &&
            row.DisplayName == "Email");
        Assert.Equal("MissingFromSource", email.Status);
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

        Assert.Contains(suite.DocumentationWorkspaceList, row => row.Kind == "SuiteDocumentation");
        Assert.Contains(suite.DocumentationSourceList, row => row.Id == "source:cli:left");
        Assert.Contains(suite.DocumentationSourceList, row => row.Id == "source:cli:right");
        Assert.Equal(2, suite.DocumentationSubjectList.Count(row =>
            row.Kind == "CliApplication" &&
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
        var sourceReference = Assert.Single(source.DocumentationSubjectList, row => row.Kind == "CliApplication");
        var sourceWorkspace = Assert.Single(source.DocumentationWorkspaceList);

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { source });
        var suiteReference = Assert.Single(suite.DocumentationSubjectList, row => row.Kind == "CliApplication");

        Assert.NotSame(sourceReference, suiteReference);
        Assert.Same(sourceWorkspace, source.DocumentationSourceList.Single().DocumentationWorkspace);
        Assert.NotSame(source.DocumentationSourceList.Single(), suite.DocumentationSourceList.Single(row => row.Id == "source:cli:left"));
    }

    [Fact]
    public void SuiteMerge_RepeatedMergeIsDeterministicAndDoesNotDuplicateStableRows()
    {
        var source = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(source, CreateSameNamedApp(), sourceId: "source:cli:left");

        var suite = new MetaDocsSuiteMerger().MergeIntoNew(new[] { source, source });

        Assert.Equal(1, suite.DocumentationSourceList.Count(row => row.Id == "source:cli:left"));
        Assert.Equal(1, suite.DocumentationSubjectList.Count(row => row.Kind == "CliApplication"));
        Assert.Equal(1, suite.DocumentationThemeAssetList.Count(row => row.Id == "theme:metametabi-static:asset:css"));
        var brandMark = Assert.Single(suite.DocumentationThemeAssetList, row => row.Id == "theme:metametabi-static:asset:brand-mark");
        Assert.Equal(string.Empty, brandMark.Href);
        Assert.Contains("<circle cx=\"11\" cy=\"11\" r=\"11\" fill=\"#0a0a0a\"/>", brandMark.Content, StringComparison.Ordinal);
        Assert.Contains("points=\"9.5,8 13.5,11 9.5,14\"", brandMark.Content, StringComparison.Ordinal);
        Assert.Equal(1, suite.DocumentationViewList.Count(row => row.Id == "view:default"));
    }

    [Fact]
    public void PublicReferenceClassifier_ClassifiesProductFamilyAndSurface()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), groupName: "meta");
        importer.ImportApplication(model, CreateMetaDocsApp(), groupName: "meta");
        importer.ImportApplication(model, CreateSimpleApp("meta-cli"), groupName: "meta");
        importer.ImportApplication(model, CreateSimpleApp("meta-mesh"), groupName: "meta");
        importer.ImportApplication(model, CreateSimpleApp("meta-data-type"), groupName: "meta-bi");
        importer.ImportApplication(model, CreateSimpleApp("meta-convert"), groupName: "meta-bi");
        AddModelSubject(model, "MetaDocs");
        AddModelSubject(model, "MetaDataType");

        AssertClassification(model, "meta", MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "meta-docs", MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "meta-cli", MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "meta-mesh", MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "meta-data-type", MetaDocsProductFamily.MetaBi, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "meta-convert", MetaDocsProductFamily.MetaBi, MetaDocsReferenceSurface.Cli);
        AssertClassification(model, "MetaDocs", MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Models);
        AssertClassification(model, "MetaDataType", MetaDocsProductFamily.MetaBi, MetaDocsReferenceSurface.Models);

        var ungrouped = new MetaDocsCliImporter().ImportApplication(model, CreateSimpleApp("meta-ungrouped"));
        Assert.False(MetaDocsPublicReferenceClassifier.TryClassify(model, ungrouped, out _));
    }

    [Fact]
    public void PublicReferenceViewBuilder_RebuildsOnlyCliAndModelPublicView()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateMetaApp(), groupName: "meta");
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."), groupName: "meta-bi");
        AddModelSubject(model, "MetaDocs");
        var authored = new MetaDocsAuthoringService().UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "docs:getting-started",
                "Getting started",
                "Old authored spine.",
                "Old authored spine."));

        MetaDocsPublicReferenceViewBuilder.EnsurePublicReferenceView(model);

        Assert.DoesNotContain(model.DocumentationViewNodeList, row => row.SubjectKey == authored.Id);
        Assert.DoesNotContain(model.DocumentationViewNodeList, row => row.Title == "Getting started");
        Assert.Contains(model.DocumentationViewNodeList, row => row.Title == "Meta" && row.Selection == "ProductFamily:meta");
        Assert.Contains(model.DocumentationViewNodeList, row => row.Title == "Meta-BI" && row.Selection == "ProductFamily:meta-bi");
        Assert.Contains(model.DocumentationViewNodeList, row => row.Title == "CLI" && row.Selection == "ReferenceSurface:meta:cli");
        Assert.Contains(model.DocumentationViewNodeList, row => row.Title == "Models" && row.Selection == "ReferenceSurface:meta:models");
        Assert.Contains(model.DocumentationViewNodeList, row => row.Title == "meta-docs" || row.Title == "MetaDocs");
        Assert.DoesNotContain(new MetaDocsValidationService().Validate(model).Diagnostics, row =>
            row.Id is "MDOC031" or "MDOC032" or "MDOC033" or "MDOC035");
    }

    [Fact]
    public void Validate_ReturnsStableDiagnosticsForBrokenLifecycleState()
    {
        var model = MetaDocsModel.CreateEmpty();
        var source = new DocumentationSource
        {
            Id = "source:test",
            DisplayName = "Source",
            Kind = "MetaCliWorkspace",
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
            Id = "subject:left",
            Key = "same-key",
            DocumentationSource = source,
            Kind = "CliCommand",
            DisplayName = "Left",
            DisplayPath = "Same.Display",
            Status = "Current",
        };
        var right = new DocumentationSubject
        {
            Id = "subject:right",
            Key = "same-key",
            DocumentationSource = source,
            Kind = "CliCommand",
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
            DocumentationSubject = left,
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            SubjectKey = "subject:missing",
            Kind = "Cli",
            Name = "Name",
            Value = "x",
            ValueKind = "String",
            Status = "Current",
        });
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = "narrative:review",
            DocumentationSubject = left,
            SubjectKey = left.Id,
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
            FromSubjectKey = left.Id,
            ToSubjectKey = "subject:missing",
            Kind = "References",
        });
        model.DocumentationViewList.Add(new DocumentationView
        {
            Id = "view:default",
            Name = "Default",
            Kind = "Site",
        });
        model.DocumentationViewNodeList.Add(new DocumentationViewNode
        {
            Id = "view:default:node:missing",
            DocumentationView = model.DocumentationViewList.Single(),
            SubjectKey = "subject:missing",
            Title = "Missing",
        });

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC001" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC002" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC003" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC005" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC006" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC008");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC010" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC011" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.True(result.HasErrors());

        var resultWithProseDiagnostics = new MetaDocsValidationService().Validate(
            model,
            new MetaDocsValidationOptions { IncludeProseDiagnostics = true });
        Assert.Contains(resultWithProseDiagnostics.Diagnostics, diagnostic =>
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
    public void Validate_FlagsPublicViewPolicyIssues()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsAuthoringService().UpsertPage(
            model,
            new MetaDocsAuthoredPage(
                "docs:what-is-meta",
                "What is Meta?",
                "Old guide page.",
                "Old guide page."));
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."), groupName: "meta-bi");

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC031");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC032");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC033");
    }

    [Fact]
    public void Validate_ChecksCliReferenceCompletenessAndParentage()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."));

        var source = Assert.Single(model.DocumentationSourceList, source => source.Kind == "MetaCliWorkspace");
        var batch = Assert.Single(model.DocumentationImportBatchList);
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:empty:app",
            Key = "source:cli:empty:app",
            DocumentationSource = source,
            Kind = "CliApplication",
            NativeKind = "MetaCli.Application",
            NativeId = "empty-cli",
            DisplayName = "empty-cli",
            DisplayPath = "empty-cli",
            Status = "Current",
        });
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:orphan:command:run",
            Key = "source:cli:orphan:command:run",
            DocumentationSource = source,
            Kind = "CliCommand",
            NativeKind = "MetaCli.ExecutableCommand",
            NativeId = "run",
            DisplayName = "orphan run",
            DisplayPath = "orphan run",
            ParentKey = "missing-app",
            Status = "Current",
        });
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = "source:cli:orphan:option:flag",
            Key = "source:cli:orphan:option:flag",
            DocumentationSource = source,
            Kind = "CliOption",
            NativeKind = "MetaCli.Option",
            NativeId = "--flag",
            DisplayName = "--flag",
            DisplayPath = "orphan run --flag",
            ParentKey = "missing-command",
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:orphan:command:run:fact:cli:usagecount",
            DocumentationSubject = model.DocumentationSubjectList.Single(subject => subject.Id == "source:cli:orphan:command:run"),
            DocumentationSource = source,
            DocumentationImportBatch = batch,
            SubjectKey = "source:cli:orphan:command:run",
            Kind = "Cli",
            Name = "UsageCount",
            Value = "1",
            ValueKind = "Number",
            Status = "Current",
        });

        var result = new MetaDocsValidationService().Validate(model);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC024" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC025" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC026" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC028" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC036" && diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "MDOC030");

        var resultWithProseDiagnostics = new MetaDocsValidationService().Validate(
            model,
            new MetaDocsValidationOptions { IncludeProseDiagnostics = true });
        Assert.Contains(resultWithProseDiagnostics.Diagnostics, diagnostic =>
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
            Kind = "WorkspaceModel",
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
        Assert.DoesNotContain(model.DocumentationSubjectList, row => row.Kind == "Instance");
        Assert.DoesNotContain(model.DocumentationFactList, row => row.Kind == "InstancePropertyValue");
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
        var source = Assert.Single(model.DocumentationSourceList, row => row.Kind == "WorkspaceInstances");
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
        Assert.Equal(2, model.DocumentationSubjectList.Count(row => row.Kind == "Instance" && row.NativeKind == "Customer"));
        Assert.DoesNotContain(model.DocumentationFactList, row => row.Kind == "InstancePropertyValue");
        Assert.Contains(model.DocumentationSubjectList, row => row.Kind == "Instance" && row.DisplayName == "Ada");
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

        Assert.Equal(2, model.DocumentationFactList.Count(row => row.Kind == "InstancePropertyValue" && row.Name == "Name"));
        Assert.DoesNotContain(model.DocumentationFactList, row => row.Kind == "InstancePropertyValue" && row.Name == "Email");
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
            row.Kind == "InstanceRelationship:Customer" &&
            row.FromSubjectKey.Contains(":instance:order:", StringComparison.OrdinalIgnoreCase) &&
            row.ToSubjectKey.Contains(":instance:customer:", StringComparison.OrdinalIgnoreCase));
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
        var ada = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Instance" && row.NativeId == "cust-1");
        model.DocumentationNarrativeList.Add(new DocumentationNarrative
        {
            Id = $"{ada.Id}:narrative:summary:900",
            DocumentationSubject = ada,
            SubjectKey = ada.Id,
            Slot = "Summary",
            Title = "Summary",
            Body = "Authored Ada prose.",
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

        ada = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Instance" && row.NativeId == "cust-1");
        Assert.Equal("Authored Ada prose.", Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == ada.Id &&
            row.Origin == "Authored").Body);
        var bob = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Instance" && row.NativeId == "cust-2");
        Assert.Equal("MissingFromSource", bob.Status);
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

        var customerEntity = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Entity" && row.DisplayName == "Customer");
        var ada = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "Instance" && row.NativeId == "cust-1");
        Assert.Equal(customerEntity.Id, ada.ParentKey);
        Assert.Contains(model.DocumentationRelationshipList, row =>
            row.FromSubjectKey == ada.Id &&
            row.Kind == "DocumentsProperty" &&
            row.ToSubjectKey.EndsWith(":property:name", StringComparison.OrdinalIgnoreCase));
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

        Assert.Equal(4, suite.DocumentationSubjectList.Count(row => row.Kind == "Instance"));
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
        Assert.DoesNotContain("Selected safe instance docs", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderSite_UsesModeledThemeAndRendersGenericContent()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), groupName: "meta");
        importer.ImportApplication(model, CreateBindingApp("Bind transforms."), groupName: "meta-bi");

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
        Assert.Contains("href=\"#group-meta-cli\"", html, StringComparison.Ordinal);
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
        var app = new DocumentationSubject
        {
            Id = "source:cli:sample:app",
            Key = "source:cli:sample:app",
            Kind = "CliApplication",
            NativeId = "sample",
            DisplayName = "sample",
            DisplayPath = "sample",
            Summary = "Sample CLI.",
            Status = "Current",
        };
        var parent = new DocumentationSubject
        {
            Id = "source:cli:sample:app:command:parent",
            Key = "source:cli:sample:app:command:parent",
            Kind = "CliCommand",
            NativeId = "parent",
            DisplayName = "sample parent",
            DisplayPath = "sample parent",
            Summary = "Parent command.",
            ParentKey = app.Id,
            Status = "Current",
        };
        var child = new DocumentationSubject
        {
            Id = "source:cli:sample:app:command:parent:command:child",
            Key = "source:cli:sample:app:command:parent:command:child",
            Kind = "CliCommand",
            NativeId = "child",
            DisplayName = "sample parent child",
            DisplayPath = "sample parent child",
            Summary = "Child command.",
            ParentKey = parent.Id,
            Status = "Current",
        };
        model.DocumentationSubjectList.Add(app);
        model.DocumentationSubjectList.Add(parent);
        model.DocumentationSubjectList.Add(child);
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:sample:app:fact:cli:groupname",
            SubjectKey = app.Id,
            Kind = "Cli",
            Name = "GroupName",
            Value = "meta",
            ValueKind = "String",
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:sample:app:command:parent:command:child:fact:cli:commandpath",
            SubjectKey = child.Id,
            Kind = "Cli",
            Name = "CommandPath",
            Value = "sample parent child",
            ValueKind = "String",
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = "source:cli:sample:app:command:parent:command:child:fact:cli:usagecount",
            SubjectKey = child.Id,
            Kind = "Cli",
            Name = "UsageCount",
            Value = "0",
            ValueKind = "Number",
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
        await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
            model,
            sourceWorkspace,
            "source:workspace-model:sample",
            "Sample docs");

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
        new MetaDocsCliImporter().ImportApplication(model, CreateMetaApp(), groupName: "meta");
        var template = Assert.Single(model.DocumentationTemplateList, row => row.Kind == "SiteShell");
        template.Html = "MODELED {{title}} {{css}} {{navigation}} {{content}} {{script}}";
        var css = Assert.Single(model.DocumentationThemeAssetList, row => row.AssetKind == "Css");
        css.Content = "/* modeled css */ .custom{}";

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.StartsWith("MODELED meta + meta-bi &#183; Reference", html, StringComparison.Ordinal);
        Assert.Contains("/* modeled css */", html, StringComparison.Ordinal);
        Assert.Contains("meta status", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderSite_RendersCliReferenceWithMarkdownProseOptionsExamplesAndNoPublicConfessions()
    {
        var model = MetaDocsModel.CreateEmpty();
        new MetaDocsCliImporter().ImportApplication(model, CreateBindingApp("Bind transforms."), groupName: "meta-bi");
        await new MetaDocsMarkdownCommandProseImporter().ImportCommandProseAsync(
            model,
            WriteBindingCommandMarkdown(),
            "source:markdown:test");
        var application = Assert.Single(model.DocumentationSubjectList, row => row.Kind == "CliApplication");
        var hiddenProperty = new DocumentationSubject
        {
            Id = $"{application.Id}:property:sourcefingerprint",
            DocumentationSource = application.DocumentationSource,
            ParentKey = application.Id,
            Kind = "Property",
            NativeKind = "Property",
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
            SubjectKey = application.Id,
            Kind = "Test",
            Name = "SourceFingerprint",
            Value = "first suite outside MetaDocs today",
            ValueKind = "String",
            Status = "Current",
        });
        model.DocumentationFactList.Add(new DocumentationFact
        {
            Id = $"{hiddenProperty.Id}:fact:model:name",
            SubjectKey = hiddenProperty.Id,
            Kind = "Model",
            Name = "Name",
            Value = "SourceFingerprint",
            ValueKind = "String",
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
    public void RenderSite_UsesSinglePanelRoutesAndStrictPublicGrouping()
    {
        var model = MetaDocsModel.CreateEmpty();
        var importer = new MetaDocsCliImporter();
        importer.ImportApplication(model, CreateMetaApp(), groupName: "meta");
        importer.ImportApplication(model, CreateMetaDocsApp(), groupName: "meta");
        importer.ImportApplication(model, CreateSimpleApp("meta-data-type"), groupName: "meta-bi");
        importer.ImportApplication(model, CreateSimpleApp("meta-convert"), groupName: "meta-bi");
        AddModelSubject(model, "MetaDocs");
        AddModelSubject(model, "MetaTransformBinding");

        var html = new MetametabiDocsSiteRenderer().RenderSite(model);

        Assert.Contains("href=\"#cli-meta-docs\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"cli-meta-docs\" data-panel=\"cli-meta-docs\"", html, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Selected safe instance docs", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IntersectionObserver", html, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", html, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
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
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-docs process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

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

    private static void AssertClassification(
        MetaDocsModel model,
        string displayName,
        MetaDocsProductFamily expectedFamily,
        MetaDocsReferenceSurface expectedSurface)
    {
        var subject = Assert.Single(model.DocumentationSubjectList, row =>
            string.Equals(row.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        Assert.True(MetaDocsPublicReferenceClassifier.TryClassify(model, subject, out var classification));
        Assert.Equal(expectedFamily, classification.ProductFamily);
        Assert.Equal(expectedSurface, classification.Surface);
    }

    private static void AddModelSubject(MetaDocsModel model, string modelName)
    {
        var source = model.DocumentationSourceList.FirstOrDefault(row => row.Id == "source:model:test")
            ?? new DocumentationSource
            {
                Id = "source:model:test",
                DisplayName = "Model test",
                Kind = "WorkspaceModel",
                Status = "Current",
            };
        if (!model.DocumentationSourceList.Contains(source))
        {
            model.DocumentationSourceList.Add(source);
        }

        var normalized = MetaDocsImportSession.NormalizeKey(modelName);
        model.DocumentationSubjectList.Add(new DocumentationSubject
        {
            Id = $"source:model:test:model:{normalized}",
            DocumentationSource = source,
            Key = $"source:model:test:model:{normalized}",
            Kind = "Model",
            NativeKind = "GenericModel",
            NativeId = modelName,
            DisplayName = modelName,
            DisplayPath = $"{modelName} model",
            Summary = $"Model {modelName}.",
            Status = "Current",
        });
    }

    private static DocumentationFact FindFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind,
        string name) =>
        Assert.Single(model.DocumentationFactList, row =>
            row.SubjectKey == subject.Id &&
            row.Kind == kind &&
            row.Name == name);

    private static DocumentationNarrative FindNarrative(
        MetaDocsModel model,
        DocumentationSubject subject,
        string slot) =>
        Assert.Single(model.DocumentationNarrativeList, row =>
            row.SubjectKey == subject.Id &&
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

    private static string WriteBindingCommandMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadocs-command-prose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "README.md");
        File.WriteAllText(
            path,
            """
            ### meta-transform-binding

            `MetaTransformBinding` is the binding contract layer on top of `MetaTransformScript`.

            Purpose:
            - bind all transform scripts in a transform workspace into an explicit binding workspace
            - validate source and target contracts against explicit schema workspaces in the same command

            Command surface:
            - `meta-transform-binding bind --transform-workspace <path> --source-schema <path> --target-schema <path> --execute-system <name> --new-workspace <path>`

            Behavior summary:
            - `bind` reads the target SQL identifier from view or mutation transform metadata
            - `--source-schema` can be provided more than once when a corpus has several source schema workspaces
            - scale proof is included in `Samples\Demos\MetaTransformScriptTpcDsCliIntegration\run.cmd`

            Examples:

            ```cmd
            meta-transform-binding bind --transform-workspace .\TransformWS --source-schema .\SourceSchemaWS --target-schema .\TargetSchemaWS --execute-system WarehouseDb --new-workspace .\BindingWS
            ```

            See also:
            - `Samples\Demos\MetaTransformBindingCliIntegration\run.cmd`
            - `Samples\Demos\MetaTransformBindingCliIntegration\README.md`
            """);
        return path;
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
