using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using MetaDocs;
using MetaDocs.Core;

internal static class Program
{
    private const string AppName = "meta-docs";

    private static readonly ConsolePresenter Presenter = new();
    private static readonly CliAppDefinition Cli = new(
        AppName,
        new[]
        {
            "meta-docs <command> [options]"
        },
        new[]
        {
            new CliCommandDefinition(
                "help",
                "Show this help.",
                new[] { "meta-docs help" }),
            new CliCommandDefinition(
                "author-page",
                "Add or update an authored documentation page in a MetaDocs workspace.",
                new[]
                {
                    "meta-docs author-page (--workspace <path> | --new-workspace <path>) --id <id> --title <text> --summary <text> --body <text> [--kind <name>] [--path <text>] [--parent <id>] [--ordinal <n>] [--slot <name>] [--source-id <id>] [--source-name <text>]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--new-workspace <path>", "New MetaDocs workspace to create."),
                    new CliOptionDefinition("--id <id>", "Required. Stable documentation subject id."),
                    new CliOptionDefinition("--title <text>", "Required. Page title and subject display name."),
                    new CliOptionDefinition("--summary <text>", "Required. Short subject summary."),
                    new CliOptionDefinition("--body <text>", "Required. Authored narrative body."),
                    new CliOptionDefinition("--kind <name>", "Optional subject kind. Default: Guide."),
                    new CliOptionDefinition("--path <text>", "Optional display path. Default: title."),
                    new CliOptionDefinition("--parent <id>", "Optional parent subject id."),
                    new CliOptionDefinition("--ordinal <n>", "Optional page ordering number. Default: 100."),
                    new CliOptionDefinition("--slot <name>", "Optional narrative slot. Default: Summary."),
                    new CliOptionDefinition("--source-id <id>", "Optional authored documentation source id."),
                    new CliOptionDefinition("--source-name <text>", "Optional authored documentation source display name.")
                },
                new[]
                {
                    "This writes DocumentationSubject, DocumentationNarrative, and DocumentationViewNode rows.",
                    "It is meant for the durable authored spine around generated reference facts."
                }),
            new CliCommandDefinition(
                "import-cli",
                "Import a structured CLI definition into a MetaDocs workspace.",
                new[]
                {
                    "meta-docs import-cli --assembly <path> --type <name> [--method <name>] (--workspace <path> | --new-workspace <path>) [--group <name>] [--ordinal <n>] [--source-id <id>]"
                },
                new[]
                {
                    new CliOptionDefinition("--assembly <path>", "Required. Assembly that exposes a public static CliAppDefinition factory method."),
                    new CliOptionDefinition("--type <name>", "Required. Fully qualified type name containing the factory method."),
                    new CliOptionDefinition("--method <name>", "Optional. Public static factory method name. Default: CreateAppDefinition."),
                    new CliOptionDefinition("--workspace <path>", "Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--new-workspace <path>", "New MetaDocs workspace to create."),
                    new CliOptionDefinition("--group <name>", "Optional rendered group name, for example meta-bi."),
                    new CliOptionDefinition("--ordinal <n>", "Optional application ordering number. Default: 100."),
                    new CliOptionDefinition("--source-id <id>", "Optional stable documentation source id.")
                },
                new[]
                {
                    "CLI facts are refreshed from the definition assembly.",
                    "Authored narratives in the MetaDocs workspace are preserved where matching subjects still exist.",
                    "--workspace and --new-workspace are mutually exclusive."
                }),
            new CliCommandDefinition(
                "import-command-prose",
                "Import command-oriented markdown prose into existing CLI documentation subjects.",
                new[]
                {
                    "meta-docs import-command-prose --workspace <path> --source-root <path> [--source-id <id>]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. Existing MetaDocs workspace that already contains imported CLI subjects."),
                    new CliOptionDefinition("--source-root <path>", "Required. Markdown file or directory to scan for command prose."),
                    new CliOptionDefinition("--source-id <id>", "Optional stable documentation source id for imported markdown prose.")
                },
                new[]
                {
                    "Markdown headings are matched to CLI application and command names.",
                    "Imported prose is stored as ImportedMarkdown DocumentationNarrative rows and does not replace Authored narratives."
                }),
            new CliCommandDefinition(
                "import-workspace-model",
                "Import generic model documentation from a Meta workspace.",
                new[]
                {
                    "meta-docs import-workspace-model --source-workspace <path> (--workspace <path> | --new-workspace <path>) [--source-id <id>] [--display-name <name>] [--ordinal <n>]"
                },
                new[]
                {
                    new CliOptionDefinition("--source-workspace <path>", "Required. Source Meta workspace whose model should be documented."),
                    new CliOptionDefinition("--workspace <path>", "Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--new-workspace <path>", "New MetaDocs workspace to create."),
                    new CliOptionDefinition("--source-id <id>", "Optional stable documentation source id."),
                    new CliOptionDefinition("--display-name <name>", "Optional source display name."),
                    new CliOptionDefinition("--ordinal <n>", "Optional root subject ordering number. Default: 100.")
                },
                new[]
                {
                    "This imports model structure only: workspace, model, entities, properties, and relationships.",
                    "Instance documentation is intentionally separate and opt-in."
                }),
            new CliCommandDefinition(
                "import-workspace-instances",
                "Import selected workspace instance documentation from a modeled opt-in policy.",
                new[]
                {
                    "meta-docs import-workspace-instances --source-workspace <path> --workspace <path> [--source-id <id>] [--model-source-id <id>] [--display-name <name>] [--ordinal <n>]"
                },
                new[]
                {
                    new CliOptionDefinition("--source-workspace <path>", "Required. Source Meta workspace whose allowed instances should be documented."),
                    new CliOptionDefinition("--workspace <path>", "Required. Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--source-id <id>", "Optional stable documentation source id for imported instance docs."),
                    new CliOptionDefinition("--model-source-id <id>", "Optional source id used by import-workspace-model, for linking instance docs to model docs."),
                    new CliOptionDefinition("--display-name <name>", "Optional source display name."),
                    new CliOptionDefinition("--ordinal <n>", "Optional root subject ordering number. Default: 200.")
                },
                new[]
                {
                    "Default policy imports no instance data.",
                    "Entities, properties, and relationships must be explicitly included in the MetaDocs workspace policy."
                }),
            new CliCommandDefinition(
                "include-instance-entity",
                "Add or update an opt-in entity policy for workspace instance documentation.",
                new[]
                {
                    "meta-docs include-instance-entity --workspace <path> --entity <name> [--source-id <id>] [--display-name-property <name>] [--summary-property <name>] [--ordinal <n>]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--entity <name>", "Required. Source workspace entity name to include."),
                    new CliOptionDefinition("--source-id <id>", "Optional model documentation source id this policy applies to. Omit for a global policy."),
                    new CliOptionDefinition("--display-name-property <name>", "Optional property used only as the imported instance display name."),
                    new CliOptionDefinition("--summary-property <name>", "Optional property used only as the imported instance summary."),
                    new CliOptionDefinition("--ordinal <n>", "Optional policy ordering number. Default: 100.")
                },
                new[]
                {
                    "This writes modeled MetaDocs policy rows. It does not import instance values by itself."
                }),
            new CliCommandDefinition(
                "include-instance-property",
                "Add or update an opt-in property policy for workspace instance documentation.",
                new[]
                {
                    "meta-docs include-instance-property --workspace <path> --entity <name> --property <name> [--source-id <id>] [--ordinal <n>]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--entity <name>", "Required. Source workspace entity name."),
                    new CliOptionDefinition("--property <name>", "Required. Property name to import as a generated fact."),
                    new CliOptionDefinition("--source-id <id>", "Optional model documentation source id this policy applies to. Omit for a global policy."),
                    new CliOptionDefinition("--ordinal <n>", "Optional policy ordering number. Default: 100.")
                }),
            new CliCommandDefinition(
                "include-instance-relationship",
                "Add or update an opt-in relationship policy for workspace instance documentation.",
                new[]
                {
                    "meta-docs include-instance-relationship --workspace <path> --entity <name> --relationship <name> [--source-id <id>] [--ordinal <n>]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. Existing MetaDocs workspace to update in place."),
                    new CliOptionDefinition("--entity <name>", "Required. Source workspace entity name."),
                    new CliOptionDefinition("--relationship <name>", "Required. Relationship selector, navigation name, or relationship column name."),
                    new CliOptionDefinition("--source-id <id>", "Optional model documentation source id this policy applies to. Omit for a global policy."),
                    new CliOptionDefinition("--ordinal <n>", "Optional policy ordering number. Default: 100.")
                }),
            new CliCommandDefinition(
                "merge",
                "Merge multiple MetaDocs workspaces into a suite workspace.",
                new[]
                {
                    "meta-docs merge --include <workspace> [--include <workspace> ...] --new-workspace <path>"
                },
                new[]
                {
                    new CliOptionDefinition("--include <workspace>", "Required. MetaDocs workspace to include. Repeat for many workspaces."),
                    new CliOptionDefinition("--new-workspace <path>", "Required. Suite workspace to create or replace.")
                },
                new[]
                {
                    "Source ids are preserved. Duplicate default theme/view rows are canonicalized in the suite workspace.",
                    "Display names are not used as merge identity."
                }),
            new CliCommandDefinition(
                "validate",
                "Validate a MetaDocs workspace lifecycle and reference integrity.",
                new[]
                {
                    "meta-docs validate --workspace <path> [--warnings-as-errors]"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaDocs workspace to validate."),
                    new CliOptionDefinition("--warnings-as-errors", "Optional. Treat warnings as failing diagnostics.")
                },
                new[]
                {
                    "Validation checks source/documentation references, view references, authored prose review status, duplicate display paths, and modeled theme assets/templates.",
                    "Warnings do not fail by default."
                }),
            new CliCommandDefinition(
                "render-site",
                "Render one merged metametabi docs page from a MetaDocs workspace.",
                new[]
                {
                    "meta-docs render-site --workspace <path> --out <dir>"
                },
                new[]
                {
                    new CliOptionDefinition("--workspace <path>", "Required. MetaDocs workspace to render."),
                    new CliOptionDefinition("--out <dir>", "Required. Output directory. The merged page is written as docs.html.")
                },
                new[]
                {
                    "The renderer consumes DocumentationSubject, DocumentationFact, DocumentationNarrative, DocumentationView, and modeled theme assets.",
                    "Rendered HTML is disposable output, not the durable documentation source."
                })
        },
        Next: "meta-docs render-site --help",
        Examples: new[]
        {
            "meta-docs import-cli --assembly .\\MetaTransformBinding.CliDefinition.dll --type MetaTransform.Binding.CliDefinition.MetaTransformBindingCliDefinitions --new-workspace .\\Docs\\BindingCli --group meta-bi --ordinal 80",
            "meta-docs import-command-prose --workspace .\\Docs\\BindingCli --source-root ..\\meta-bi\\README.md --source-id source:markdown:meta-bi-readme",
            "meta-docs import-workspace-model --source-workspace .\\SourceWS --new-workspace .\\Docs\\SourceModel --source-id source:workspace-model:source",
            "meta-docs merge --include .\\Docs\\BindingCli --include .\\Docs\\SourceModel --new-workspace .\\Docs\\SuiteWorkspace",
            "meta-docs validate --workspace .\\Docs\\SuiteWorkspace",
            "meta-docs render-site --workspace .\\Docs\\SuiteWorkspace --out .\\Docs\\Site"
        });

    internal static CliAppDefinition CreateAppDefinition() => Cli;

    private static async Task<int> Main(string[] args)
    {
        if (CliVersion.TryWriteVersion(Presenter, Cli.Name, args, out var versionExitCode))
        {
            return versionExitCode;
        }

        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "author-page", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAuthorPageAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "import-cli", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportCliAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "import-command-prose", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportCommandProseAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "import-workspace-model", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportWorkspaceModelAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "import-workspace-instances", StringComparison.OrdinalIgnoreCase))
        {
            return await RunImportWorkspaceInstancesAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "include-instance-entity", StringComparison.OrdinalIgnoreCase))
        {
            return await RunIncludeInstanceEntityAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "include-instance-property", StringComparison.OrdinalIgnoreCase))
        {
            return await RunIncludeInstancePropertyAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "include-instance-relationship", StringComparison.OrdinalIgnoreCase))
        {
            return await RunIncludeInstanceRelationshipAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "merge", StringComparison.OrdinalIgnoreCase))
        {
            return await RunMergeAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
        {
            return await RunValidateAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "render-site", StringComparison.OrdinalIgnoreCase))
        {
            return await RunRenderSiteAsync(args, startIndex: 1).ConfigureAwait(false);
        }

        return Fail($"unknown command '{args[0]}'.", $"{AppName} help");
    }

    private static async Task<int> RunAuthorPageAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("author-page");
            return 0;
        }

        var parse = ParseAuthorPageArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs author-page --help");
        }

        try
        {
            var model = !string.IsNullOrWhiteSpace(parse.WorkspacePath)
                ? await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false)
                : MetaDocsModel.CreateEmpty();
            var page = new MetaDocsAuthoredPage(
                parse.Id,
                parse.Title,
                parse.Summary,
                parse.Body,
                parse.Kind,
                parse.DisplayPath,
                parse.ParentKey,
                parse.Ordinal,
                parse.Slot,
                parse.NarrativeTitle,
                parse.NarrativeOrdinal,
                parse.SourceId,
                parse.SourceName);
            var subject = new MetaDocsAuthoringService().UpsertPage(model, page);
            var workspacePath = ResolveOutputWorkspacePath(parse.WorkspacePath, parse.NewWorkspacePath);
            model.SaveToXmlWorkspace(workspacePath);
            Presenter.WriteInfo($"Authored page: {subject.DisplayName}.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot author MetaDocs page.",
                "check the MetaDocs workspace and authored page options.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(ResolveOutputWorkspacePath(parse.WorkspacePath, parse.NewWorkspacePath))}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunImportCliAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("import-cli");
            return 0;
        }

        var parse = ParseImportCliArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs import-cli --help");
        }

        try
        {
            var model = !string.IsNullOrWhiteSpace(parse.WorkspacePath)
                ? await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false)
                : MetaDocsModel.CreateEmpty();
            var app = new CliAppDefinitionReflectionLoader().Load(
                parse.AssemblyPath,
                parse.TypeName,
                parse.MethodName);
            var application = new MetaDocsCliImporter().ImportApplication(
                model,
                app,
                groupName: parse.GroupName,
                ordinal: parse.Ordinal,
                sourceId: parse.SourceId);
            var workspacePath = ResolveOutputWorkspacePath(parse.WorkspacePath, parse.NewWorkspacePath);
            model.SaveToXmlWorkspace(workspacePath);
            var commandCount = CountCurrentChildren(model, application, "CliCommand");
            Presenter.WriteInfo($"Imported {application.DisplayName}: {commandCount} command(s).");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot import CLI documentation.",
                "check the definition assembly, type, method, and workspace path.",
                4,
                new[]
                {
                    $"  Assembly: {Path.GetFullPath(parse.AssemblyPath)}",
                    $"  Type: {parse.TypeName}",
                    $"  Method: {parse.MethodName}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunImportCommandProseAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("import-command-prose");
            return 0;
        }

        var parse = ParseImportCommandProseArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs import-command-prose --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaDocsMarkdownCommandProseImporter().ImportCommandProseAsync(
                model,
                parse.SourceRoot,
                parse.SourceId).ConfigureAwait(false);
            model.SaveToXmlWorkspace(parse.WorkspacePath);
            Presenter.WriteInfo($"Imported command prose from {result.SourceFileCount} markdown file(s): {result.MatchedApplicationCount} app(s), {result.MatchedCommandCount} command(s), {result.ImportedNarrativeCount} narrative(s).");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot import command prose.",
                "check the MetaDocs workspace and markdown source path.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  SourceRoot: {Path.GetFullPath(parse.SourceRoot)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunImportWorkspaceModelAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("import-workspace-model");
            return 0;
        }

        var parse = ParseImportWorkspaceModelArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs import-workspace-model --help");
        }

        try
        {
            var model = !string.IsNullOrWhiteSpace(parse.WorkspacePath)
                ? await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false)
                : MetaDocsModel.CreateEmpty();
            var root = await new MetaDocsWorkspaceModelImporter().ImportWorkspaceModelAsync(
                model,
                parse.SourceWorkspacePath,
                parse.SourceId,
                parse.DisplayName,
                parse.Ordinal).ConfigureAwait(false);
            var workspacePath = ResolveOutputWorkspacePath(parse.WorkspacePath, parse.NewWorkspacePath);
            model.SaveToXmlWorkspace(workspacePath);
            var entityCount = CountCurrentChildren(model, FindModelSubject(model, root) ?? root, "Entity");
            Presenter.WriteInfo($"Imported {root.DisplayName}: {entityCount} entity subject(s).");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot import workspace model documentation.",
                "check the source workspace and MetaDocs workspace path.",
                4,
                new[]
                {
                    $"  SourceWorkspace: {Path.GetFullPath(parse.SourceWorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunImportWorkspaceInstancesAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("import-workspace-instances");
            return 0;
        }

        var parse = ParseImportWorkspaceInstancesArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs import-workspace-instances --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var result = await new MetaDocsWorkspaceInstanceImporter().ImportWorkspaceInstancesAsync(
                model,
                parse.SourceWorkspacePath,
                parse.SourceId,
                parse.ModelSourceId,
                parse.DisplayName,
                parse.Ordinal).ConfigureAwait(false);
            model.SaveToXmlWorkspace(parse.WorkspacePath);
            Presenter.WriteInfo($"Imported {result.ImportedInstanceCount} instance subject(s), {result.ImportedPropertyFactCount} property fact(s), {result.ImportedRelationshipCount} relationship(s).");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot import workspace instance documentation.",
                "check the source workspace, MetaDocs workspace, and modeled instance import policy.",
                4,
                new[]
                {
                    $"  SourceWorkspace: {Path.GetFullPath(parse.SourceWorkspacePath)}",
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunIncludeInstanceEntityAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("include-instance-entity");
            return 0;
        }

        var parse = ParseIncludeInstanceEntityArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs include-instance-entity --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeEntity(
                model,
                parse.EntityName,
                parse.SourceId,
                parse.DisplayNameProperty,
                parse.SummaryProperty,
                parse.Ordinal);
            model.SaveToXmlWorkspace(parse.WorkspacePath);
            Presenter.WriteInfo($"Included instance entity policy: {spec.EntityName}.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot update instance entity policy.",
                "check the MetaDocs workspace and policy options.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunIncludeInstancePropertyAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("include-instance-property");
            return 0;
        }

        var parse = ParseIncludeInstanceMemberArgs(args, startIndex, "--property");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs include-instance-property --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeProperty(
                model,
                parse.EntityName,
                parse.MemberName,
                parse.SourceId,
                parse.Ordinal);
            model.SaveToXmlWorkspace(parse.WorkspacePath);
            Presenter.WriteInfo($"Included instance property policy: {parse.EntityName}.{spec.PropertyName}.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot update instance property policy.",
                "check the MetaDocs workspace and policy options.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunIncludeInstanceRelationshipAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("include-instance-relationship");
            return 0;
        }

        var parse = ParseIncludeInstanceMemberArgs(args, startIndex, "--relationship");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs include-instance-relationship --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var spec = new MetaDocsInstanceImportPolicyEditor().IncludeRelationship(
                model,
                parse.EntityName,
                parse.MemberName,
                parse.SourceId,
                parse.Ordinal);
            model.SaveToXmlWorkspace(parse.WorkspacePath);
            Presenter.WriteInfo($"Included instance relationship policy: {parse.EntityName}.{spec.RelationshipSelector}.");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot update instance relationship policy.",
                "check the MetaDocs workspace and policy options.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunMergeAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("merge");
            return 0;
        }

        var parse = ParseMergeArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs merge --help");
        }

        try
        {
            var models = new List<MetaDocsModel>();
            foreach (var include in parse.IncludePaths)
            {
                models.Add(await MetaDocsModel.LoadFromXmlWorkspaceAsync(include, searchUpward: false).ConfigureAwait(false));
            }

            var merged = new MetaDocsSuiteMerger().MergeIntoNew(models);
            merged.SaveToXmlWorkspace(parse.NewWorkspacePath);
            Presenter.WriteInfo($"Merged {models.Count} workspace(s): {merged.DocumentationSourceList.Count} source(s).");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot merge MetaDocs workspaces.",
                "check included workspaces and output path.",
                4,
                new[]
                {
                    $"  Output: {Path.GetFullPath(parse.NewWorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task<int> RunRenderSiteAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("render-site");
            return 0;
        }

        var parse = ParseRenderSiteArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs render-site --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var html = new MetametabiDocsSiteRenderer().RenderSite(model);
            var outputDirectory = Path.GetFullPath(parse.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "docs.html");
            await File.WriteAllTextAsync(outputPath, html, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
            await WriteThemeAssetsAsync(model, outputDirectory).ConfigureAwait(false);
            Presenter.WriteInfo($"Wrote {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot render MetaDocs site.",
                "check the MetaDocs workspace and output directory, then retry.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  Output: {Path.GetFullPath(parse.OutputDirectory)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static async Task WriteThemeAssetsAsync(MetaDocsModel model, string outputDirectory)
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
            await File.WriteAllTextAsync(
                outputPath,
                asset.Content!,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunValidateAsync(string[] args, int startIndex)
    {
        if (args.Length == startIndex || IsHelpToken(args[startIndex]))
        {
            PrintCommandHelp("validate");
            return 0;
        }

        var parse = ParseValidateArgs(args, startIndex);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, "meta-docs validate --help");
        }

        try
        {
            var model = await MetaDocsModel.LoadFromXmlWorkspaceAsync(parse.WorkspacePath, searchUpward: false).ConfigureAwait(false);
            var result = new MetaDocsValidationService().Validate(model);
            PrintValidationResult(result);
            return result.HasErrors(parse.WarningsAsErrors) ? 2 : 0;
        }
        catch (Exception ex)
        {
            return Fail(
                "Cannot validate MetaDocs workspace.",
                "check the workspace path, then retry.",
                4,
                new[]
                {
                    $"  Workspace: {Path.GetFullPath(parse.WorkspacePath)}",
                    $"  {ex.Message}"
                });
        }
    }

    private static (
        bool Ok,
        string WorkspacePath,
        string NewWorkspacePath,
        string Id,
        string Title,
        string Summary,
        string Body,
        string Kind,
        string DisplayPath,
        string ParentKey,
        int Ordinal,
        string Slot,
        string NarrativeTitle,
        int NarrativeOrdinal,
        string SourceId,
        string SourceName,
        string ErrorMessage) ParseAuthorPageArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var newWorkspacePath = string.Empty;
        var id = string.Empty;
        var title = string.Empty;
        var summary = string.Empty;
        var body = string.Empty;
        var kind = "Guide";
        var displayPath = string.Empty;
        var parentKey = string.Empty;
        var ordinal = 100;
        var slot = "Summary";
        var narrativeTitle = string.Empty;
        var narrativeOrdinal = 10;
        var sourceId = "source:authored:metametabi-docs";
        var sourceName = "Authored MetaDocs pages";

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--new-workspace", ref newWorkspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--id", ref id, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--title", ref title, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--summary", ref summary, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--body", ref body, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--kind", ref kind, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--path", ref displayPath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--parent", ref parentKey, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--slot", ref slot, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-name", ref sourceName, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath) == string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return FailParse("provide exactly one of --workspace <path> or --new-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(id)) return FailParse("missing required option --id <id>.");
        if (string.IsNullOrWhiteSpace(title)) return FailParse("missing required option --title <text>.");
        if (string.IsNullOrWhiteSpace(summary)) return FailParse("missing required option --summary <text>.");
        if (string.IsNullOrWhiteSpace(body)) return FailParse("missing required option --body <text>.");
        return (true, workspacePath, newWorkspacePath, id, title, summary, body, kind, displayPath, parentKey, ordinal, slot, narrativeTitle, narrativeOrdinal, sourceId, sourceName, string.Empty);

        (bool Ok, string WorkspacePath, string NewWorkspacePath, string Id, string Title, string Summary, string Body, string Kind, string DisplayPath, string ParentKey, int Ordinal, string Slot, string NarrativeTitle, int NarrativeOrdinal, string SourceId, string SourceName, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, newWorkspacePath, id, title, summary, body, kind, displayPath, parentKey, ordinal, slot, narrativeTitle, narrativeOrdinal, sourceId, sourceName, message);
    }

    private static (
        bool Ok,
        string AssemblyPath,
        string TypeName,
        string MethodName,
        string WorkspacePath,
        string NewWorkspacePath,
        string GroupName,
        int Ordinal,
        string SourceId,
        string ErrorMessage) ParseImportCliArgs(string[] args, int startIndex)
    {
        var assemblyPath = string.Empty;
        var typeName = string.Empty;
        var methodName = "CreateAppDefinition";
        var workspacePath = string.Empty;
        var newWorkspacePath = string.Empty;
        var groupName = string.Empty;
        var ordinal = 100;
        var sourceId = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--assembly", ref assemblyPath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--type", ref typeName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--method", ref methodName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--new-workspace", ref newWorkspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--group", ref groupName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(assemblyPath)) return FailParse("missing required option --assembly <path>.");
        if (string.IsNullOrWhiteSpace(typeName)) return FailParse("missing required option --type <name>.");
        if (string.IsNullOrWhiteSpace(methodName)) return FailParse("missing required option --method <name>.");
        if (string.IsNullOrWhiteSpace(workspacePath) == string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return FailParse("provide exactly one of --workspace <path> or --new-workspace <path>.");
        }

        return (true, assemblyPath, typeName, methodName, workspacePath, newWorkspacePath, groupName, ordinal, sourceId, string.Empty);

        (bool Ok, string AssemblyPath, string TypeName, string MethodName, string WorkspacePath, string NewWorkspacePath, string GroupName, int Ordinal, string SourceId, string ErrorMessage) FailParse(string message) =>
            (false, assemblyPath, typeName, methodName, workspacePath, newWorkspacePath, groupName, ordinal, sourceId, message);
    }

    private static (
        bool Ok,
        string WorkspacePath,
        string SourceRoot,
        string SourceId,
        string ErrorMessage) ParseImportCommandProseArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var sourceRoot = string.Empty;
        var sourceId = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-root", ref sourceRoot, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        if (string.IsNullOrWhiteSpace(sourceRoot)) return FailParse("missing required option --source-root <path>.");
        return (true, workspacePath, sourceRoot, sourceId, string.Empty);

        (bool Ok, string WorkspacePath, string SourceRoot, string SourceId, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, sourceRoot, sourceId, message);
    }

    private static (
        bool Ok,
        string SourceWorkspacePath,
        string WorkspacePath,
        string NewWorkspacePath,
        string SourceId,
        string DisplayName,
        int Ordinal,
        string ErrorMessage) ParseImportWorkspaceModelArgs(string[] args, int startIndex)
    {
        var sourceWorkspacePath = string.Empty;
        var workspacePath = string.Empty;
        var newWorkspacePath = string.Empty;
        var sourceId = string.Empty;
        var displayName = string.Empty;
        var ordinal = 100;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--source-workspace", ref sourceWorkspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--new-workspace", ref newWorkspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--display-name", ref displayName, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(sourceWorkspacePath)) return FailParse("missing required option --source-workspace <path>.");
        if (string.IsNullOrWhiteSpace(workspacePath) == string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return FailParse("provide exactly one of --workspace <path> or --new-workspace <path>.");
        }

        return (true, sourceWorkspacePath, workspacePath, newWorkspacePath, sourceId, displayName, ordinal, string.Empty);

        (bool Ok, string SourceWorkspacePath, string WorkspacePath, string NewWorkspacePath, string SourceId, string DisplayName, int Ordinal, string ErrorMessage) FailParse(string message) =>
            (false, sourceWorkspacePath, workspacePath, newWorkspacePath, sourceId, displayName, ordinal, message);
    }

    private static (
        bool Ok,
        string SourceWorkspacePath,
        string WorkspacePath,
        string SourceId,
        string ModelSourceId,
        string DisplayName,
        int Ordinal,
        string ErrorMessage) ParseImportWorkspaceInstancesArgs(string[] args, int startIndex)
    {
        var sourceWorkspacePath = string.Empty;
        var workspacePath = string.Empty;
        var sourceId = string.Empty;
        var modelSourceId = string.Empty;
        var displayName = string.Empty;
        var ordinal = 200;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--source-workspace", ref sourceWorkspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--model-source-id", ref modelSourceId, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--display-name", ref displayName, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(sourceWorkspacePath)) return FailParse("missing required option --source-workspace <path>.");
        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        return (true, sourceWorkspacePath, workspacePath, sourceId, modelSourceId, displayName, ordinal, string.Empty);

        (bool Ok, string SourceWorkspacePath, string WorkspacePath, string SourceId, string ModelSourceId, string DisplayName, int Ordinal, string ErrorMessage) FailParse(string message) =>
            (false, sourceWorkspacePath, workspacePath, sourceId, modelSourceId, displayName, ordinal, message);
    }

    private static (
        bool Ok,
        string WorkspacePath,
        string EntityName,
        string SourceId,
        string DisplayNameProperty,
        string SummaryProperty,
        int Ordinal,
        string ErrorMessage) ParseIncludeInstanceEntityArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var entityName = string.Empty;
        var sourceId = string.Empty;
        var displayNameProperty = string.Empty;
        var summaryProperty = string.Empty;
        var ordinal = 100;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--entity", ref entityName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--display-name-property", ref displayNameProperty, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--summary-property", ref summaryProperty, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        if (string.IsNullOrWhiteSpace(entityName)) return FailParse("missing required option --entity <name>.");
        return (true, workspacePath, entityName, sourceId, displayNameProperty, summaryProperty, ordinal, string.Empty);

        (bool Ok, string WorkspacePath, string EntityName, string SourceId, string DisplayNameProperty, string SummaryProperty, int Ordinal, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, entityName, sourceId, displayNameProperty, summaryProperty, ordinal, message);
    }

    private static (
        bool Ok,
        string WorkspacePath,
        string EntityName,
        string MemberName,
        string SourceId,
        int Ordinal,
        string ErrorMessage) ParseIncludeInstanceMemberArgs(string[] args, int startIndex, string memberOption)
    {
        var workspacePath = string.Empty;
        var entityName = string.Empty;
        var memberName = string.Empty;
        var sourceId = string.Empty;
        var ordinal = 100;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--entity", ref entityName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, memberOption, ref memberName, out error)) { if (error != null) return FailParse(error); continue; }
            if (ReadStringOption(args, ref i, "--source-id", ref sourceId, out error)) { if (error != null) return FailParse(error); continue; }

            if (string.Equals(arg, "--ordinal", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --ordinal.");
                if (!int.TryParse(args[++i], out ordinal)) return FailParse("--ordinal must be an integer.");
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        if (string.IsNullOrWhiteSpace(entityName)) return FailParse("missing required option --entity <name>.");
        if (string.IsNullOrWhiteSpace(memberName)) return FailParse($"missing required option {memberOption} <name>.");
        return (true, workspacePath, entityName, memberName, sourceId, ordinal, string.Empty);

        (bool Ok, string WorkspacePath, string EntityName, string MemberName, string SourceId, int Ordinal, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, entityName, memberName, sourceId, ordinal, message);
    }

    private static (
        bool Ok,
        IReadOnlyList<string> IncludePaths,
        string NewWorkspacePath,
        string ErrorMessage) ParseMergeArgs(string[] args, int startIndex)
    {
        var includes = new List<string>();
        var newWorkspacePath = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--include", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) return FailParse("missing value for --include.");
                includes.Add(args[++i]);
                continue;
            }

            if (ReadStringOption(args, ref i, "--new-workspace", ref newWorkspacePath, out var error))
            {
                if (error != null) return FailParse(error);
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (includes.Count == 0) return FailParse("provide at least one --include <workspace>.");
        if (string.IsNullOrWhiteSpace(newWorkspacePath)) return FailParse("missing required option --new-workspace <path>.");
        return (true, includes, newWorkspacePath, string.Empty);

        (bool Ok, IReadOnlyList<string> IncludePaths, string NewWorkspacePath, string ErrorMessage) FailParse(string message) =>
            (false, includes, newWorkspacePath, message);
    }

    private static (
        bool Ok,
        string WorkspacePath,
        bool WarningsAsErrors,
        string ErrorMessage) ParseValidateArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var warningsAsErrors = false;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error))
            {
                if (error != null) return FailParse(error);
                continue;
            }

            if (string.Equals(arg, "--warnings-as-errors", StringComparison.OrdinalIgnoreCase))
            {
                warningsAsErrors = true;
                continue;
            }

            return FailParse($"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        return (true, workspacePath, warningsAsErrors, string.Empty);

        (bool Ok, string WorkspacePath, bool WarningsAsErrors, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, warningsAsErrors, message);
    }

    private static (
        bool Ok,
        string WorkspacePath,
        string OutputDirectory,
        string ErrorMessage) ParseRenderSiteArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var outputDirectory = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            if (ReadStringOption(args, ref i, "--workspace", ref workspacePath, out var error))
            {
                if (error != null) return FailParse(error);
                continue;
            }

            if (ReadStringOption(args, ref i, "--out", ref outputDirectory, out error))
            {
                if (error != null) return FailParse(error);
                continue;
            }

            return FailParse($"unknown option '{args[i]}'.");
        }

        if (string.IsNullOrWhiteSpace(workspacePath)) return FailParse("missing required option --workspace <path>.");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return FailParse("missing required option --out <dir>.");
        return (true, workspacePath, outputDirectory, string.Empty);

        (bool Ok, string WorkspacePath, string OutputDirectory, string ErrorMessage) FailParse(string message) =>
            (false, workspacePath, outputDirectory, message);
    }

    private static bool ReadStringOption(
        string[] args,
        ref int index,
        string option,
        ref string target,
        out string? error)
    {
        error = null;
        if (!string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (index + 1 >= args.Length)
        {
            error = $"missing value for {option}.";
            return true;
        }

        if ((string.Equals(option, "--workspace", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option, "--new-workspace", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option, "--assembly", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option, "--type", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(target))
        {
            error = $"{option} can only be provided once.";
            return true;
        }

        target = args[++index];
        return true;
    }

    private static string ResolveOutputWorkspacePath(string workspacePath, string newWorkspacePath) =>
        !string.IsNullOrWhiteSpace(workspacePath) ? workspacePath : newWorkspacePath;

    private static int CountCurrentChildren(MetaDocsModel model, DocumentationSubject parent, string kind) =>
        model.DocumentationSubjectList.Count(row =>
            string.Equals(row.ParentKey ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase));

    private static DocumentationSubject? FindModelSubject(MetaDocsModel model, DocumentationSubject root) =>
        model.DocumentationSubjectList.FirstOrDefault(row =>
            string.Equals(row.ParentKey ?? string.Empty, root.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row.Kind, "Model", StringComparison.OrdinalIgnoreCase));

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        CliHelpRenderer.WriteAppHelp(Presenter, Cli);
    }

    private static void PrintCommandHelp(string commandName)
    {
        CliHelpRenderer.WriteCommandHelp(Presenter, Cli, Cli.GetCommand(commandName));
    }

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

    private static int Fail(string message, string next, int exitCode = 1, IEnumerable<string>? details = null)
    {
        var renderedDetails = new List<string>();
        if (details != null)
        {
            renderedDetails.AddRange(details.Where(static detail => !string.IsNullOrWhiteSpace(detail)));
        }

        renderedDetails.Add($"Next: {next}");
        Presenter.WriteFailure(message, renderedDetails);
        return exitCode;
    }
}
