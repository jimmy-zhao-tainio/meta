using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class CliStrictModeTests
{
    private static string? cliExecutablePath;

    [Fact]
    public void CommandExamples_DoNotContainLegacyHumanErrorTokens()
    {
        var repoRoot = FindRepositoryRoot();
        var content = string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(repoRoot, "COMMANDS.md")),
            File.ReadAllText(Path.Combine(repoRoot, "README.md")));

        Assert.DoesNotContain("Where:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Hint:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("instance.relationship.orphan", content, StringComparison.Ordinal);
        Assert.DoesNotContain("contains(Id,'')", content, StringComparison.Ordinal);
        Assert.DoesNotContain("--where", content, StringComparison.Ordinal);
        Assert.DoesNotContain("contains(", content, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandDocs_DoNotContainWhereDslTokens()
    {
        var repoRoot = FindRepositoryRoot();
        var commands = File.ReadAllText(Path.Combine(repoRoot, "COMMANDS.md"));
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var combined = commands + Environment.NewLine + readme;

        Assert.DoesNotContain("--where", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("contains(", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void SurfaceDocs_DoNotAdvertiseIdSwitch_ForRowTargeting()
    {
        var repoRoot = FindRepositoryRoot();
        var commands = File.ReadAllText(Path.Combine(repoRoot, "COMMANDS.md"));
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var cliProgram = File.ReadAllText(Path.Combine(repoRoot, Path.Combine("Meta", "Cli"), "Program.cs"));
        var combined = string.Join(Environment.NewLine, new[] { commands, readme, cliProgram });

        Assert.DoesNotMatch(@"(?im)^.*Usage:.*--id(\s|$).*$", combined);
        Assert.DoesNotMatch(@"(?im)^.*Next:.*--id(\s|$).*$", combined);
        Assert.DoesNotMatch(@"(?im)^.*example:.*--id(\s|$).*$", combined);
        Assert.DoesNotMatch(@"(?im)^.*meta\s+.*--id(\s|$).*$", combined);
    }

    [Fact]
    public void SurfaceDocs_DoNotAdvertiseSetId_ForRowTargeting()
    {
        var repoRoot = FindRepositoryRoot();
        var commands = File.ReadAllText(Path.Combine(repoRoot, "COMMANDS.md"));
        var readme = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        var combined = string.Join(Environment.NewLine, new[] { commands, readme });

        Assert.DoesNotContain("--set Id=", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HumanFailures_DoNotLeakDiagnosticKeyValueTokens_AndUseSingleNext()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var nonEmptyImportTarget = Path.Combine(Path.GetTempPath(), "metadata-import-target", Guid.NewGuid().ToString("N"));
        var brokenWorkspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-broken", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(nonEmptyImportTarget);
        await File.WriteAllTextAsync(Path.Combine(nonEmptyImportTarget, "placeholder.txt"), "x");

        Directory.CreateDirectory(Path.Combine(brokenWorkspaceRoot, "metadata", "instance"));
        await File.WriteAllTextAsync(
            Path.Combine(brokenWorkspaceRoot, "workspace.xml"),
            "<MetaWorkspace><WorkspaceList><Workspace Id=\"1\" WorkspaceLayoutId=\"1\" EncodingId=\"1\" NewlinesId=\"1\" EntitiesOrderId=\"1\" PropertiesOrderId=\"1\" RelationshipsOrderId=\"1\" RowsOrderId=\"2\" AttributesOrderId=\"3\"><Name>Workspace</Name><FormatVersion>1.0</FormatVersion></Workspace></WorkspaceList><WorkspaceLayoutList><WorkspaceLayout Id=\"1\"><ModelFilePath>metadata/model.xml</ModelFilePath><InstanceDirPath>metadata/instance</InstanceDirPath></WorkspaceLayout></WorkspaceLayoutList><EncodingList><Encoding Id=\"1\"><Name>utf-8-no-bom</Name></Encoding></EncodingList><NewlinesList><Newlines Id=\"1\"><Name>lf</Name></Newlines></NewlinesList><CanonicalOrderList><CanonicalOrder Id=\"1\"><Name>name-ordinal</Name></CanonicalOrder><CanonicalOrder Id=\"2\"><Name>id-ordinal</Name></CanonicalOrder><CanonicalOrder Id=\"3\"><Name>id-first-then-name-ordinal</Name></CanonicalOrder></CanonicalOrderList><EntityStorageList /></MetaWorkspace>");
        await File.WriteAllTextAsync(
            Path.Combine(brokenWorkspaceRoot, "metadata", "model.xml"),
            "<Model name=\"Broken\"><EntityList><Entity name=\"X\"></EntityList></Model>");

        try
        {
            var outputs = new[]
            {
                (await RunCliAsync("view", "entity", "MissingEntity", "--workspace", workspaceRoot)).CombinedOutput,
                (await RunCliAsync("init", nonEmptyImportTarget)).CombinedOutput,
                (await RunCliAsync("status", "--workspace", brokenWorkspaceRoot)).CombinedOutput,
            };

            var bannedTokens = new[]
            {
                "endPos=",
                "startPos=",
                "file=",
                "workspace=",
                "entity=",
                "occurrences=",
                "entries=",
                "sampleEntries=",
            };

            foreach (var output in outputs)
            {
                foreach (var banned in bannedTokens)
                {
                    Assert.DoesNotContain(banned, output, StringComparison.Ordinal);
                }

                var nextCount = Regex.Matches(output, @"(?m)^Next:\s").Count;
                Assert.True(nextCount <= 1, $"Expected at most one Next line, got {nextCount}:{Environment.NewLine}{output}");
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(nonEmptyImportTarget);
            DeleteDirectorySafe(brokenWorkspaceRoot);
        }
    }

    [Fact]
    public async Task NotFoundMessages_UseCanonicalTemplates_AcrossCommandFamilies()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var missingEntity = await RunCliAsync("view", "entity", "MissingEntity", "--workspace", workspaceRoot);
            var missingRow = await RunCliAsync("view", "instance", "Cube", "999", "--workspace", workspaceRoot);
            var missingProperty = await RunCliAsync(
                "query",
                "Cube",
                "--contains",
                "MissingField",
                "Value",
                "--workspace",
                workspaceRoot);
            var missingRelationship = await RunCliAsync(
                "instance",
                "relationship",
                "set",
                "Cube",
                "1",
                "--to",
                "System",
                "1",
                "--workspace",
                workspaceRoot);

            Assert.Contains("Entity 'MissingEntity' was not found.", missingEntity.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Instance 'Cube 999' was not found.", missingRow.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Property 'Cube.MissingField' was not found.", missingProperty.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Relationship 'Cube->System' was not found.", missingRelationship.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task XmlParseErrors_UseSameEnvelope_InStatusAndGenerate()
    {
        var brokenWorkspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-broken", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(Path.GetTempPath(), "metadata-generate-out", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(brokenWorkspaceRoot, "metadata", "instance"));
        await File.WriteAllTextAsync(
            Path.Combine(brokenWorkspaceRoot, "workspace.xml"),
            "<MetaWorkspace><WorkspaceList><Workspace Id=\"1\" WorkspaceLayoutId=\"1\" EncodingId=\"1\" NewlinesId=\"1\" EntitiesOrderId=\"1\" PropertiesOrderId=\"1\" RelationshipsOrderId=\"1\" RowsOrderId=\"2\" AttributesOrderId=\"3\"><Name>Workspace</Name><FormatVersion>1.0</FormatVersion></Workspace></WorkspaceList><WorkspaceLayoutList><WorkspaceLayout Id=\"1\"><ModelFilePath>metadata/model.xml</ModelFilePath><InstanceDirPath>metadata/instance</InstanceDirPath></WorkspaceLayout></WorkspaceLayoutList><EncodingList><Encoding Id=\"1\"><Name>utf-8-no-bom</Name></Encoding></EncodingList><NewlinesList><Newlines Id=\"1\"><Name>lf</Name></Newlines></NewlinesList><CanonicalOrderList><CanonicalOrder Id=\"1\"><Name>name-ordinal</Name></CanonicalOrder><CanonicalOrder Id=\"2\"><Name>id-ordinal</Name></CanonicalOrder><CanonicalOrder Id=\"3\"><Name>id-first-then-name-ordinal</Name></CanonicalOrder></CanonicalOrderList><EntityStorageList /></MetaWorkspace>");
        await File.WriteAllTextAsync(
            Path.Combine(brokenWorkspaceRoot, "metadata", "model.xml"),
            "<Model name=\"Broken\"><EntityList><Entity name=\"X\"></EntityList></Model>");

        try
        {
            var status = await RunCliAsync("status", "--workspace", brokenWorkspaceRoot);
            var generate = await RunCliAsync(
                "generate",
                "sql",
                "--out",
                outputRoot,
                "--workspace",
                brokenWorkspaceRoot);

            Assert.Equal(4, status.ExitCode);
            Assert.Equal(4, generate.ExitCode);

            Assert.Contains("Cannot parse metadata/model.xml.", status.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Cannot parse metadata/model.xml.", generate.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Location: line 1, position", status.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Location: line 1, position", generate.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Next: meta check", status.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Next: meta check", generate.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Usage:", generate.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(brokenWorkspaceRoot);
            DeleteDirectorySafe(outputRoot);
        }
    }

    [Fact]
    public async Task ModelWithDisplayKeyAttribute_FailsWithClearError()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            var firstEntity = model.Descendants("Entity").First();
            firstEntity.SetAttributeValue("displayKey", "Name");
            model.Save(modelPath);

            var status = await RunCliAsync("status", "--workspace", workspaceRoot);
            Assert.Equal(4, status.ExitCode);
            Assert.Contains("unsupported attribute 'displayKey'", status.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task InvalidInstanceMissingRequiredRelationship_FailsAtLoad_ForCheckAndRelationshipToProperty()
    {
        var workspaceRoot = CreateTempWorkspaceWithMissingRequiredRelationship();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-invalid-load-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(4, check.ExitCode);
            Assert.Contains("Error: Entity 'Order' row 'ORD-001' is missing required relationship 'WarehouseId'.", check.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", check.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Stack trace", check.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);

            var refactor = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--workspace",
                workspaceRoot);
            Assert.Equal(4, refactor.ExitCode);
            Assert.Contains("Error: Entity 'Order' row 'ORD-001' is missing required relationship 'WarehouseId'.", refactor.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", refactor.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Stack trace", refactor.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task Help_OverviewAndCommandHelp_AreAvailable()
    {
        var overview = await RunCliAsync("help");
        Assert.Equal(0, overview.ExitCode);
        Assert.DoesNotContain("Meta CLI", overview.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Version:", overview.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workspace", overview.StdOut, StringComparison.Ordinal);
        Assert.Contains("Model", overview.StdOut, StringComparison.Ordinal);
        Assert.Contains("Instance", overview.StdOut, StringComparison.Ordinal);
        Assert.Contains("Pipeline", overview.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("Utility", overview.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("random", overview.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Command  Description", overview.StdOut, StringComparison.Ordinal);
        foreach (var line in overview.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            Assert.Equal(line.TrimEnd(), line);
        }
        Assert.Contains("Next: meta <command> help", overview.StdOut, StringComparison.Ordinal);

        var commandHelp = await RunCliAsync("model", "--help");
        Assert.Equal(0, commandHelp.ExitCode);
        Assert.Contains("model", commandHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("Usage:", commandHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("Next: meta model <subcommand> help", commandHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("rename-entity", commandHelp.StdOut, StringComparison.OrdinalIgnoreCase);

        var suggestHelp = await RunCliAsync("model", "suggest", "--help");
        Assert.Equal(0, suggestHelp.ExitCode);
        Assert.Contains("meta model suggest", suggestHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--show-keys", suggestHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--explain", suggestHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--print-commands", suggestHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--workspace", suggestHelp.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("concepts", suggestHelp.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirm", suggestHelp.StdOut, StringComparison.OrdinalIgnoreCase);

        var refactorHelp = await RunCliAsync("model", "refactor", "property-to-relationship", "--help");
        Assert.Equal(0, refactorHelp.ExitCode);
        Assert.Contains("meta model refactor property-to-relationship", refactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--source <Entity.Property>", refactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--target <Entity>", refactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--lookup <Property>", refactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--preserve-property", refactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--workspace <path>", refactorHelp.StdOut, StringComparison.Ordinal);

        var inverseRefactorHelp = await RunCliAsync("model", "refactor", "relationship-to-property", "--help");
        Assert.Equal(0, inverseRefactorHelp.ExitCode);
        Assert.Contains("meta model refactor relationship-to-property", inverseRefactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--source <Entity>", inverseRefactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--target <Entity>", inverseRefactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--role <Role>", inverseRefactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--property <PropertyName>", inverseRefactorHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--workspace <path>", inverseRefactorHelp.StdOut, StringComparison.Ordinal);

        var renameEntityHelp = await RunCliAsync("model", "rename-entity", "--help");
        Assert.Equal(0, renameEntityHelp.ExitCode);
        Assert.Contains("meta model rename-entity <Old> <New> [--workspace <path>]", renameEntityHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--workspace <path>", renameEntityHelp.StdOut, StringComparison.Ordinal);

        var renameIdHelp = await RunCliAsync("instance", "rename-id", "--help");
        Assert.Equal(0, renameIdHelp.ExitCode);
        Assert.Contains("meta instance rename-id <Entity> <OldId> <NewId>", renameIdHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--workspace <path>", renameIdHelp.StdOut, StringComparison.Ordinal);

        var renameModelHelp = await RunCliAsync("model", "rename-model", "--help");
        Assert.Equal(0, renameModelHelp.ExitCode);
        Assert.Contains("meta model rename-model <Old> <New> [--workspace <path>]", renameModelHelp.StdOut, StringComparison.Ordinal);

        var relationshipSetHelp = await RunCliAsync("instance", "relationship", "set", "--help");
        Assert.Equal(0, relationshipSetHelp.ExitCode);
        Assert.Contains("meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId>", relationshipSetHelp.StdOut, StringComparison.Ordinal);

        var renameRelationshipHelp = await RunCliAsync("model", "rename-relationship", "--help");
        Assert.Equal(0, renameRelationshipHelp.ExitCode);
        Assert.Contains("meta model rename-relationship <FromEntity> <ToEntity> [--role <Role>] [--workspace <path>]", renameRelationshipHelp.StdOut, StringComparison.Ordinal);

        var setPropertyRequiredHelp = await RunCliAsync("model", "set-property-required", "--help");
        Assert.Equal(0, setPropertyRequiredHelp.ExitCode);
        Assert.Contains("meta model set-property-required <Entity> <Property> --required true|false", setPropertyRequiredHelp.StdOut, StringComparison.Ordinal);
        Assert.Contains("--default-value", setPropertyRequiredHelp.StdOut, StringComparison.Ordinal);

        var exportCsvHelp = await RunCliAsync("export", "csv", "--help");
        Assert.Equal(0, exportCsvHelp.ExitCode);
        Assert.Contains("meta export csv <Entity> --out <file> [--workspace <path>]", exportCsvHelp.StdOut, StringComparison.Ordinal);

        Assert.Contains("target entity, relationship role, or implied relationship field name", relationshipSetHelp.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModelSuggest_DefaultOutput_IsActionableOnly()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var result = await RunCliAsync("model", "suggest", "--workspace", workspaceRoot);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: model suggest", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Workspace:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Model:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Suggestions: 3", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Relationship suggestions", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("  1) Order.ProductId -> Product (lookup: Product.Id)", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("  2) Order.SupplierId -> Supplier (lookup: Supplier.Id)", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("  3) Order.WarehouseId -> Warehouse (lookup: Warehouse.Id)", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("meta model suggest", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Proposed refactor:", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Summary", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Candidate business keys", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Blocked relationship candidates", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Evidence:", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Stats:", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Why:", result.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("score=", result.StdOut, StringComparison.OrdinalIgnoreCase);

            var normalized = result.StdOut.Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Contains("Suggestions: 3\n\nRelationship suggestions", normalized, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSuggest_ShowKeysFlag_IsOptIn()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var showKeys = await RunCliAsync("model", "suggest", "--show-keys", "--workspace", workspaceRoot);
            Assert.Equal(0, showKeys.ExitCode);
            Assert.Contains("Candidate business keys", showKeys.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSuggest_Explain_ShowsPlanBlocks()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var explainOnly = await RunCliAsync("model", "suggest", "--explain", "--workspace", workspaceRoot);
            Assert.Equal(0, explainOnly.ExitCode);
            Assert.Contains("Plan:", explainOnly.StdOut, StringComparison.Ordinal);
            Assert.Contains("Add relationship Order -> Product", explainOnly.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Evidence:", explainOnly.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Stats:", explainOnly.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Why:", explainOnly.StdOut, StringComparison.Ordinal);

            var explainKeys = await RunCliAsync(
                "model",
                "suggest",
                "--show-keys",
                "--explain",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, explainKeys.ExitCode);
            Assert.Contains("Candidate business keys", explainKeys.StdOut, StringComparison.Ordinal);
            Assert.Contains("Details:", explainKeys.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSuggest_PrintCommands_EmitsRefactorCommands()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var result = await RunCliAsync("model", "suggest", "--print-commands", "--workspace", workspaceRoot);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Suggested commands", result.StdOut, StringComparison.Ordinal);
            Assert.Contains(
                $"meta model refactor property-to-relationship --workspace {workspaceRoot}",
                result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains(
                "--source Order.ProductId --target Product --lookup Id",
                result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains(
                "--source Order.SupplierId --target Supplier --lookup Id",
                result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains(
                "--source Order.WarehouseId --target Warehouse --lookup Id",
                result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSuggest_Output_IsDeterministic_PerMode()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            await AssertModeDeterministic("model", "suggest", "--workspace", workspaceRoot);
            await AssertModeDeterministic("model", "suggest", "--show-keys", "--workspace", workspaceRoot);
            await AssertModeDeterministic("model", "suggest", "--explain", "--workspace", workspaceRoot);
            await AssertModeDeterministic("model", "suggest", "--print-commands", "--workspace", workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    private static async Task AssertModeDeterministic(params string[] args)
    {
        var first = await RunCliAsync(args);
        var second = await RunCliAsync(args);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(first.StdOut, second.StdOut);
    }

    [Fact]
    public async Task ModelSuggest_UnknownFlag_ReturnsArgumentError()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync("model", "suggest", "--wat", "--workspace", workspaceRoot);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Error: unknown option --wat.", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSuggest_SubcommandsAreNotSupported()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var concepts = await RunCliAsync("model", "suggest", "concepts", "--workspace", workspaceRoot);
            Assert.Equal(1, concepts.ExitCode);
            Assert.Contains("Unknown command 'model suggest concepts'.", concepts.CombinedOutput, StringComparison.Ordinal);

            var confirm = await RunCliAsync(
                "model", "suggest", "confirm", "--concept", "DisplayName", "--labels", "CubeName", "--workspace", workspaceRoot);
            Assert.Equal(1, confirm.ExitCode);
            Assert.Contains("Unknown command 'model suggest confirm'.", confirm.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task GenerateCSharpHelp_IncludesToolingOption()
    {
        InvalidateCliAssemblyCache();
        var result = await RunCliAsync("generate", "csharp", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta generate csharp", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("--tooling", result.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateSql_RejectsToolingOption()
    {
        InvalidateCliAssemblyCache();
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var outputRoot = Path.Combine(Path.GetTempPath(), "metadata-generate-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await RunCliAsync(
                "generate",
                "sql",
                "--tooling",
                "--out",
                outputRoot,
                "--workspace",
                workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("--tooling is only supported for 'generate csharp'.", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(outputRoot);
        }
    }

    [Fact]
    public async Task ArgumentError_IncludesUsageAndNext()
    {
        var result = await RunCliAsync("model", "add-entity");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("missing required argument <Name>", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Usage: meta model add-entity <Name> [--workspace <path>]", result.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Next: meta model add-entity help", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Where:", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Hint:", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentError_UsesSameUsageAsCommandHelp()
    {
        var help = await RunCliAsync("model", "rename-entity", "--help");
        var error = await RunCliAsync("model", "rename-entity");

        Assert.Equal(0, help.ExitCode);
        Assert.Equal(1, error.ExitCode);

        var helpUsage = ExtractUsageClause(help.StdOut);
        var errorUsage = ExtractUsageClause(error.CombinedOutput);

        Assert.Equal(helpUsage, errorUsage);
        Assert.Contains("Next: meta model rename-entity help", error.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_RejectsScopeOption()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync("check", "--scope", "all", "--workspace", workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("unknown option --scope", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddEntity_AppliesByDefault()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync("model", "add-entity", "SmokeEntity", "--workspace", workspaceRoot);
            Assert.True(result.ExitCode == 0, result.CombinedOutput);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            Assert.True(File.Exists(modelPath), "Expected metadata/model.xml to be written.");

            var modelDocument = XDocument.Load(modelPath);
            Assert.NotNull(modelDocument
                .Descendants("Entity")
                .SingleOrDefault(element => string.Equals((string?)element.Attribute("name"), "SmokeEntity", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RandomCreate_IsNotExposed_OnCliSurface()
    {
        var result = await RunCliAsync("random", "create");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'random'.", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceCommand_IsNotExposed_OnCliSurface()
    {
        var result = await RunCliAsync("workspace", "migrate");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'workspace", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphStats_ReturnsTextOutput()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "graph",
                "stats",
                "--workspace",
                workspaceRoot,
                "--top",
                "3",
                "--cycles",
                "2");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Graph:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Nodes:", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Top out-degree", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Top in-degree", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ViewRow_AcceptsOpaqueIdWithoutSpecialParsing()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var opaqueId = string.Concat("Cube", "#", "1");
            var result = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                opaqueId,
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Instance 'Cube Cube#1' was not found.", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RowTargetCommands_RejectLegacyIdSwitch()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var cases = new[]
            {
                new[] { "view", "instance", "Cube", "1", "--id", "1", "--workspace", workspaceRoot },
                new[] { "insert", "Cube", "10", "--id", "10", "--set", "CubeName=Test", "--workspace", workspaceRoot },
                new[] { "instance", "update", "Cube", "1", "--id", "1", "--set", "RefreshMode=Manual", "--workspace", workspaceRoot },
                new[] { "delete", "Cube", "1", "--id", "1", "--workspace", workspaceRoot },
                new[] { "instance", "relationship", "set", "Measure", "--id", "1", "--to", "Cube", "1", "--workspace", workspaceRoot },
            };

            foreach (var command in cases)
            {
                var result = await RunCliAsync(command);
                Assert.Equal(1, result.ExitCode);
                Assert.Contains("unknown option", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Usage:", result.CombinedOutput, StringComparison.Ordinal);
                Assert.Contains("Next:", result.CombinedOutput, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RowTargetCommands_RejectSetIdAsRowIdentifier()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var cases = new[]
            {
                new[] { "insert", "Cube", "10", "--set", "Id=10", "--set", "CubeName=Bad", "--workspace", workspaceRoot },
                new[] { "instance", "update", "Cube", "1", "--set", "Id=2", "--workspace", workspaceRoot },
            };

            foreach (var command in cases)
            {
                var result = await RunCliAsync(command);
                Assert.Equal(1, result.ExitCode);
                Assert.Contains("do not use --set Id", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Usage:", result.CombinedOutput, StringComparison.Ordinal);
                Assert.Contains("Next:", result.CombinedOutput, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task Insert_AutoId_CreatesNextNumericRow()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "--auto-id",
                "--set",
                "CubeName=Auto Id Cube",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, insertResult.ExitCode);
            Assert.Contains("created Cube 3", insertResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var viewResult = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "3",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, viewResult.ExitCode);
            Assert.Contains("Instance: Cube 3", viewResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task Insert_AutoId_CannotBeCombinedWithPositionalId()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "insert",
                "Cube",
                "10",
                "--auto-id",
                "--set",
                "CubeName=Conflicting",
                "--workspace",
                workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("cannot be combined", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Usage:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Next:", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task Insert_AutoId_UsesNextNumericId()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var autoIdInsert = await RunCliAsync(
                "insert",
                "Cube",
                "--auto-id",
                "--set",
                "CubeName=Auto Generated",
                "--set",
                "Purpose=Auto id test",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, autoIdInsert.ExitCode);

            var viewResult = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "3",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, viewResult.ExitCode);
            Assert.Contains("Auto Generated", viewResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task BulkInsert_AutoId_CreatesNextNumericRows()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var tsvPath = Path.Combine(workspaceRoot, "bulk-auto-id.tsv");
        await File.WriteAllLinesAsync(
            tsvPath,
            new[]
            {
                "CubeName\tPurpose\tRefreshMode",
                "Auto Cube A\tAuto row A\tManual",
                "Auto Cube B\tAuto row B\tScheduled",
            });

        try
        {
            var result = await RunCliAsync(
                "bulk-insert",
                "Cube",
                "--from",
                "tsv",
                "--file",
                tsvPath,
                "--auto-id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);

            var viewThree = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "3",
                "--workspace",
                workspaceRoot);
            var viewFour = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "4",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, viewThree.ExitCode);
            Assert.Equal(0, viewFour.ExitCode);
            Assert.Contains("Instance: Cube 3", viewThree.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Instance: Cube 4", viewFour.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task BulkInsert_AutoId_RejectsKeyCombination()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var tsvPath = Path.Combine(workspaceRoot, "bulk-auto-id-conflict.tsv");
        await File.WriteAllLinesAsync(
            tsvPath,
            new[]
            {
                "CubeName\tPurpose\tRefreshMode",
                "Auto Cube Conflict\tConflict row\tManual",
            });

        try
        {
            var result = await RunCliAsync(
                "bulk-insert",
                "Cube",
                "--from",
                "tsv",
                "--file",
                tsvPath,
                "--auto-id",
                "--key",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("cannot be combined", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Usage:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Next:", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task UnknownFieldOrColumnErrors_ProvideSingleNextAction()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var tsvPath = Path.Combine(workspaceRoot, "bad-bulk-insert.tsv");
        await File.WriteAllLinesAsync(
            tsvPath,
            new[]
            {
                "Id\tUnknownColumn",
                "1\tBadValue",
            });

        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "10",
                "--set",
                "MissingField=WillFail",
                "--workspace",
                workspaceRoot);
            var rowUpdateResult = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "MissingField=BadValue",
                "--workspace",
                workspaceRoot);
            var bulkInsertResult = await RunCliAsync(
                "bulk-insert",
                "Cube",
                "--from",
                "tsv",
                "--file",
                tsvPath,
                "--key",
                "Id",
                "--workspace",
                workspaceRoot);

            foreach (var result in new[] { insertResult, rowUpdateResult, bulkInsertResult })
            {
                Assert.Equal(4, result.ExitCode);
                Assert.Contains("Property 'Cube.", result.CombinedOutput, StringComparison.Ordinal);
                Assert.Contains("' was not found.", result.CombinedOutput, StringComparison.Ordinal);
                var nextMatches = Regex.Matches(result.CombinedOutput, @"(?m)^Next:\s+meta list properties Cube\s*$");
                Assert.Single(nextMatches.Cast<Match>());
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportSql_RequiresNewWorkspaceOption()
    {
        var result = await RunCliAsync(
            "import",
            "sql",
            "Server=localhost;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False",
            "dbo");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("import requires --new-workspace", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportCsv_WithoutWorkspaceFlags_UsesDefaultWorkspaceDiscovery()
    {
        var csvPath = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"), "landing.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Name,Status",
            "1,A,Open",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Landing");

            Assert.Equal(4, result.ExitCode);
            Assert.DoesNotContain("requires --workspace <path> or --new-workspace <path>", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Could not find model.xml", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(Path.GetDirectoryName(csvPath)!);
        }
    }

    [Fact]
    public async Task ImportCsv_NewWorkspace_FailsWithoutIdColumn()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "landing.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Display Name,Active",
            "Alice,true",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Order Items",
                "--new-workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("CSV file must include Id column 'Id'.", result.CombinedOutput, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(workspaceRoot, "metadata")));
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_RejectsIdColumnOverrideOption()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "landing.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Display Name",
            "101,Alice",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Order Items",
                "--id-column",
                "RowId",
                "--new-workspace",
                workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("unknown option", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--id-column", result.CombinedOutput, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(workspaceRoot, "metadata")));
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_NewWorkspace_FailsWhenIdsDuplicateOrMissing()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var duplicateCsvPath = Path.Combine(csvRoot, "duplicate.csv");
        var missingCsvPath = Path.Combine(csvRoot, "missing.csv");
        var duplicateWorkspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        var missingWorkspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(duplicateCsvPath, new[]
        {
            "Id,Display Name",
            "101,Alice",
            "101,Bob",
        });
        await File.WriteAllLinesAsync(missingCsvPath, new[]
        {
            "Id,Display Name",
            "101,Alice",
            ",Bob",
        });

        try
        {
            var duplicateResult = await RunCliAsync(
                "import",
                "csv",
                duplicateCsvPath,
                "--entity",
                "Order Items",
                "--new-workspace",
                duplicateWorkspaceRoot);
            Assert.Equal(4, duplicateResult.ExitCode);
            Assert.Contains("CSV contains duplicate Id '101' in column 'Id'.", duplicateResult.CombinedOutput, StringComparison.Ordinal);

            var missingResult = await RunCliAsync(
                "import",
                "csv",
                missingCsvPath,
                "--entity",
                "Order Items",
                "--new-workspace",
                missingWorkspaceRoot);
            Assert.Equal(4, missingResult.ExitCode);
            Assert.Contains("CSV row 3 is missing required Id value from column 'Id'.", missingResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(duplicateWorkspaceRoot);
            DeleteDirectorySafe(missingWorkspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_NewWorkspace_CreatesEntityRowsAndInferredTypes()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "landing.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Display Name,Active,Count,BigCount,Amount,CreatedAt,Status,Status",
            "101,Alice,true,42,2147483648,10.50,2024-01-01T10:20:30Z,Open,Primary",
            "205,Bob,false,7,922337203685477580,0.00,2024-02-02T00:00:00Z,Closed,Secondary",
            "333,,,,,,,Open,",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Order Items",
                "--new-workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("imported csv", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            var entity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Order_Items", StringComparison.Ordinal));

            var properties = entity
                .Element("PropertyList")?
                .Elements("Property")
                .ToList() ?? new List<XElement>();

            Assert.Contains(properties, property => string.Equals((string?)property.Attribute("name"), "Display_Name", StringComparison.Ordinal));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "Active", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("dataType"), "bool", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "Count", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("dataType"), "int", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "BigCount", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("dataType"), "long", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "Amount", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("dataType"), "decimal", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "CreatedAt", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("dataType"), "datetime", StringComparison.OrdinalIgnoreCase) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(properties, property => string.Equals((string?)property.Attribute("name"), "Status", StringComparison.Ordinal));
            Assert.Contains(properties, property =>
                string.Equals((string?)property.Attribute("name"), "Status_2", StringComparison.Ordinal) &&
                string.Equals((string?)property.Attribute("isRequired"), "false", StringComparison.OrdinalIgnoreCase));

            var rows = LoadEntityRows(workspaceRoot, "Order_Items");
            Assert.Equal(3, rows.Count);
            Assert.Equal("101", (string?)rows[0].Attribute("Id"));
            Assert.Equal("205", (string?)rows[1].Attribute("Id"));
            Assert.Equal("333", (string?)rows[2].Attribute("Id"));
            Assert.Equal("Alice", rows[0].Element("Display_Name")?.Value);
            Assert.Null(rows[2].Element("Display_Name"));
            Assert.Equal("Open", rows[2].Element("Status")?.Value);
            Assert.Null(rows[2].Element("Status_2"));

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_NewWorkspace_AllowsExplicitPlural()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "categories.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,CategoryName",
            "CAT-001,Cycles",
            "CAT-002,Accessories",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Category",
                "--new-workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            var entity = model.Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Category", StringComparison.Ordinal));
            Assert.Null(entity.Attribute("plural"));

            var instance = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "instance", "Category.xml"));
            Assert.NotNull(instance.Root?.Element("CategoryList"));
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_NewWorkspace_AllowsOpaqueStringIds()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "landing.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Label",
            "sku-a,Alpha",
            "sku-b,Beta",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Landing",
                "--new-workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);

            var rows = LoadEntityRows(workspaceRoot, "Landing")
                .OrderBy(row => (string?)row.Attribute("Id"), StringComparer.Ordinal)
                .ToList();
            Assert.Equal(new[] { "sku-a", "sku-b" }, rows.Select(row => (string?)row.Attribute("Id")));
            Assert.Equal("Alpha", rows[0].Element("Label")?.Value);
            Assert.Equal("Beta", rows[1].Element("Label")?.Value);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_UpsertsByIdAndPreservesRows()
    {
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "landing.csv");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Label,IsActive",
            "10,Alpha,true",
            "20,Beta,false",
        });

        try
        {
            var initialImport = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Landing",
                "--new-workspace",
                workspaceRoot);
            Assert.Equal(0, initialImport.ExitCode);

            await File.WriteAllLinesAsync(csvPath, new[]
            {
                "Id,Label,IsActive",
                "10,Alpha Updated,false",
                "30,Gamma,true",
            });

            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Landing",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);

            var rows = LoadEntityRows(workspaceRoot, "Landing")
                .OrderBy(row => (string?)row.Attribute("Id"), StringComparer.Ordinal)
                .ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "10", "20", "30" }, rows.Select(row => (string?)row.Attribute("Id")));
            Assert.Equal("Alpha Updated", rows[0].Element("Label")?.Value);
            Assert.Equal("false", rows[0].Element("IsActive")?.Value);
            Assert.Equal("Beta", rows[1].Element("Label")?.Value);
            Assert.Equal("Gamma", rows[2].Element("Label")?.Value);
        }
        finally
        {
            DeleteDirectorySafe(csvRoot);
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_UpsertsRelationshipColumnsAfterRefactor()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "orders-update.csv");
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,OrderNumber,ProductId,SupplierId,WarehouseId,StatusText",
            "ORD-001,ORD-1001,PRD-002,SUP-001,WH-001,Released",
            "ORD-002,ORD-1002,PRD-002,SUP-002,WH-001,Released",
            "ORD-003,ORD-1003,PRD-003,SUP-003,WH-002,Held",
            "ORD-004,ORD-1004,PRD-004,SUP-004,WH-003,Closed",
            "ORD-005,ORD-1005,PRD-001,SUP-002,WH-001,Released",
        });

        try
        {
            var refactor = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.ProductId",
                "--target",
                "Product",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, refactor.ExitCode);

            var import = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Order",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, import.ExitCode);

            var rows = LoadEntityRows(workspaceRoot, "Order")
                .OrderBy(row => (string?)row.Attribute("Id"), StringComparer.Ordinal)
                .ToList();
            Assert.Equal(new[] { "ORD-001", "ORD-002", "ORD-003", "ORD-004", "ORD-005" }, rows.Select(row => (string?)row.Attribute("Id")));
            Assert.Equal("PRD-002", (string?)rows[0].Attribute("ProductId"));
            Assert.Null(rows[0].Element("ProductId"));

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            var orderEntity = model.Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Order", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                orderEntity.Element("RelationshipList")?.Elements("Relationship") ?? Enumerable.Empty<XElement>(),
                relationship => string.Equals((string?)relationship.Attribute("entity"), "Product", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                orderEntity.Element("PropertyList")?.Elements("Property") ?? Enumerable.Empty<XElement>(),
                property => string.Equals((string?)property.Attribute("name"), "ProductId", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(csvRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_FailsEarlyWhenRequiredPropertyIsBlank()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "cube-update.csv");
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,CubeName,Purpose,RefreshMode",
            "1,,Sales metrics,Manual",
        });

        try
        {
            var before = LoadEntityRows(workspaceRoot, "Cube")
                .Single(row => string.Equals((string?)row.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            var beforeName = before.Element("CubeName")?.Value;

            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Cube",
                "--workspace",
                workspaceRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "leaves required property 'CubeName' blank on entity 'Cube'",
                result.CombinedOutput,
                StringComparison.Ordinal);

            var after = LoadEntityRows(workspaceRoot, "Cube")
                .Single(row => string.Equals((string?)row.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(beforeName, after.Element("CubeName")?.Value);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(csvRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_FailsEarlyWhenRequiredRelationshipIsBlank()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "orders-update.csv");
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,OrderNumber,ProductId,SupplierId,WarehouseId,StatusText",
            "ORD-001,ORD-1001,,SUP-001,WH-001,Released",
        });

        try
        {
            var refactor = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.ProductId",
                "--target",
                "Product",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, refactor.ExitCode);

            var before = LoadEntityRows(workspaceRoot, "Order")
                .Single(row => string.Equals((string?)row.Attribute("Id"), "ORD-001", StringComparison.OrdinalIgnoreCase));
            var beforeProductId = (string?)before.Attribute("ProductId");

            var import = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Order",
                "--workspace",
                workspaceRoot);

            Assert.NotEqual(0, import.ExitCode);
            Assert.Contains(
                "leaves required relationship 'ProductId' blank on entity 'Order'",
                import.CombinedOutput,
                StringComparison.Ordinal);

            var after = LoadEntityRows(workspaceRoot, "Order")
                .Single(row => string.Equals((string?)row.Attribute("Id"), "ORD-001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(beforeProductId, (string?)after.Attribute("ProductId"));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(csvRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_FailsEarlyWhenNewRowOmitsRequiredColumns()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var csvRoot = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(csvRoot, "cube-new-row.csv");
        Directory.CreateDirectory(csvRoot);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Purpose,RefreshMode",
            "999,Ad hoc analysis,Manual",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Cube",
                "--workspace",
                workspaceRoot);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "cannot create new 'Cube' because required property 'CubeName' is missing from the import columns",
                result.CombinedOutput,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                LoadEntityRows(workspaceRoot, "Cube"),
                row => string.Equals((string?)row.Attribute("Id"), "999", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(csvRoot);
        }
    }

    [Fact]
    public async Task ImportCsv_ExistingWorkspace_AddsNewEntityAndRows()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var csvPath = Path.Combine(Path.GetTempPath(), "metadata-import-csv", Guid.NewGuid().ToString("N"), "landing.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
        await File.WriteAllLinesAsync(csvPath, new[]
        {
            "Id,Label,IsActive",
            "10,Alpha,true",
            "20,Beta,false",
        });

        try
        {
            var result = await RunCliAsync(
                "import",
                "csv",
                csvPath,
                "--entity",
                "Landing",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("imported csv", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Entity: Landing", result.CombinedOutput, StringComparison.Ordinal);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            Assert.Contains(
                model.Descendants("Entity"),
                entity => string.Equals((string?)entity.Attribute("name"), "Landing", StringComparison.Ordinal));

            var rows = LoadEntityRows(workspaceRoot, "Landing");
            Assert.Equal(2, rows.Count);
            Assert.Equal("Alpha", rows[0].Element("Label")?.Value);
            Assert.Equal("true", rows[0].Element("IsActive")?.Value);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(Path.GetDirectoryName(csvPath)!);
        }
    }

    [Fact]
    public async Task ModelDropRelationship_RewritesInstanceUsageAndSucceeds()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "drop-relationship",
                "Measure",
                "Cube",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("relationship removed", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            var measureEntity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Measure", StringComparison.OrdinalIgnoreCase));
            var measureRelationships = measureEntity
                .Element("RelationshipList")?
                .Elements("Relationship") ?? Enumerable.Empty<XElement>();
            Assert.DoesNotContain(
                measureRelationships,
                relationship => string.Equals((string?)relationship.Attribute("entity"), "Cube", StringComparison.OrdinalIgnoreCase));

            var measureRows = LoadEntityRows(workspaceRoot, "Measure");
            foreach (var row in measureRows)
            {
                Assert.Null(row.Attribute("CubeId"));
            }

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelDropEntity_FailsWhenEntityHasRows()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "drop-entity",
                "Cube",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Cannot drop entity Cube", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Cube has", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("contains(Id,'')", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Where:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Hint:", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelDropEntity_FailsWhenInboundRelationshipsExist()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync("init", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-entity", "Parent", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-entity", "Child", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-property", "Child", "Name", "--required", "true", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("insert", "Child", "1", "--set", "Name=Default Child", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-relationship", "Parent", "Child", "--default-id", "1", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("delete", "Child", "1", "--workspace", workspaceRoot)).ExitCode);

            var result = await RunCliAsync(
                "model",
                "drop-entity",
                "Child",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Entity 'Child' has inbound relationships", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Inbound relationships:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Parent", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Where:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Hint:", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddRelationship_BackfillsExistingRows_WithDefaultId()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "add-relationship",
                "Cube",
                "System",
                "--default-id",
                "1",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("relationship added", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Name: SystemId", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("DefaultId: 1", result.CombinedOutput, StringComparison.Ordinal);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            var cubeEntity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Cube", StringComparison.OrdinalIgnoreCase));
            var cubeRelationships = cubeEntity
                .Element("RelationshipList")?
                .Elements("Relationship") ?? Enumerable.Empty<XElement>();
            Assert.Contains(
                cubeRelationships,
                relationship => string.Equals((string?)relationship.Attribute("entity"), "System", StringComparison.OrdinalIgnoreCase));

            var cubeRows = LoadEntityRows(workspaceRoot, "Cube");
            foreach (var row in cubeRows)
            {
                Assert.Equal("1", (string?)row.Attribute("SystemId"));
            }

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddRelationship_AllowsMissingDefaultId_WhenSourceHasNoRows()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync("init", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-entity", "FromEntity", "--workspace", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-entity", "ToEntity", "--workspace", workspaceRoot)).ExitCode);

            var addRelationship = await RunCliAsync(
                "model",
                "add-relationship",
                "FromEntity",
                "ToEntity",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, addRelationship.ExitCode);
            Assert.Contains("relationship added", addRelationship.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DefaultId:", addRelationship.CombinedOutput, StringComparison.Ordinal);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddRelationship_RequiresDefaultId_WhenSourceHasRows()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "add-relationship",
                "Cube",
                "System",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("requires --default-id", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Cube.SystemId", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_RewritesRowsAndOptionallyDropsSourceProperty()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var refactor = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, refactor.ExitCode);
            Assert.Contains("OK: refactor property-to-relationship", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Source: Order.WarehouseId", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Target: Warehouse", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Lookup: Warehouse.Id", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Role: (none)", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Preserve property: no", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows rewritten: 5", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Property dropped: yes", refactor.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            var orderEntity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Order", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                orderEntity
                    .Element("PropertyList")?
                    .Elements("Property") ?? Enumerable.Empty<XElement>(),
                property => string.Equals((string?)property.Attribute("name"), "WarehouseId", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                orderEntity
                    .Element("RelationshipList")?
                    .Elements("Relationship") ?? Enumerable.Empty<XElement>(),
                relationship =>
                    string.Equals((string?)relationship.Attribute("entity"), "Warehouse", StringComparison.OrdinalIgnoreCase) &&
                    relationship.Attribute("role") == null);

            var orderRows = LoadEntityRows(workspaceRoot, "Order");
            foreach (var row in orderRows)
            {
                var warehouseId = (string?)row.Attribute("WarehouseId");
                Assert.False(string.IsNullOrWhiteSpace(warehouseId));
                Assert.Null(row.Element("WarehouseId"));
            }

            var suggestAfter = await RunCliAsync("model", "suggest", "--workspace", workspaceRoot);
            Assert.Equal(0, suggestAfter.ExitCode);
            Assert.Contains("Suggestions: 2", suggestAfter.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Order.WarehouseId -> Warehouse (lookup: Warehouse.Id)",
                suggestAfter.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_FailsWhenTargetLookupHasDuplicates_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            var warehousePath = Path.Combine(workspaceRoot, "metadata", "instance", "Warehouse.xml");
            var warehouseDocument = XDocument.Load(warehousePath);
            var warehouses = warehouseDocument.Descendants("Warehouse").ToList();
            Assert.True(warehouses.Count >= 2);
            warehouses[1].SetAttributeValue("Id", "WH-001");
            warehouseDocument.Save(warehousePath);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Target lookup key is not unique.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_FailsWhenSourceHasBlank_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            var orderPath = Path.Combine(workspaceRoot, "metadata", "instance", "Order.xml");
            var orderDocument = XDocument.Load(orderPath);
            var firstOrder = orderDocument.Descendants("Order")
                .First(element => string.Equals((string?)element.Attribute("Id"), "ORD-001", StringComparison.Ordinal));
            firstOrder.Element("WarehouseId")?.SetValue(string.Empty);
            orderDocument.Save(orderPath);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains(
                "Source contains null/blank; required relationship cannot be created.",
                result.CombinedOutput,
                StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_FailsWhenSourceValueIsUnmatched_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync(
                "instance",
                "update",
                "Order",
                "ORD-001",
                "--set",
                "WarehouseId=WH-404",
                "--workspace",
                workspaceRoot)).ExitCode);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Source values not fully resolvable against target key.", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("WH-404", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_PreserveProperty_WorksWhenRoleAvoidsCollision()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        try
        {
            var refactor = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.ProductId",
                "--target",
                "Product",
                "--lookup",
                "Id",
                "--role",
                "ProductRef",
                "--preserve-property",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, refactor.ExitCode);
            Assert.Contains("Preserve property: yes", refactor.StdOut, StringComparison.Ordinal);
            Assert.Contains("Property dropped: no", refactor.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            var orderEntity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Order", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                orderEntity
                    .Element("PropertyList")?
                    .Elements("Property") ?? Enumerable.Empty<XElement>(),
                property => string.Equals((string?)property.Attribute("name"), "ProductId", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                orderEntity
                    .Element("RelationshipList")?
                    .Elements("Relationship") ?? Enumerable.Empty<XElement>(),
                relationship =>
                    string.Equals((string?)relationship.Attribute("entity"), "Product", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)relationship.Attribute("role"), "ProductRef", StringComparison.OrdinalIgnoreCase));

            var orderRows = LoadEntityRows(workspaceRoot, "Order");
            foreach (var row in orderRows)
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)row.Attribute("ProductRefId")));
                Assert.False(string.IsNullOrWhiteSpace(row.Element("ProductId")?.Value));
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_FailsWhenRelationshipAlreadyExists_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var modelDocument = XDocument.Load(modelPath);
            var orderEntity = modelDocument.Descendants("Entity")
                .First(element => string.Equals((string?)element.Attribute("name"), "Order", StringComparison.OrdinalIgnoreCase));
            var relationships = orderEntity.Element("RelationshipList");
            if (relationships == null)
            {
                relationships = new XElement("RelationshipList");
                orderEntity.Add(relationships);
            }

            relationships.Add(new XElement("Relationship",
                new XAttribute("entity", "Warehouse"),
                new XAttribute("role", "WarehouseRef")));
            modelDocument.Save(modelPath);

            var orderPath = Path.Combine(workspaceRoot, "metadata", "instance", "Order.xml");
            var orderDocument = XDocument.Load(orderPath);
            foreach (var row in orderDocument.Descendants("Order"))
            {
                row.SetAttributeValue("WarehouseRefId", row.Element("WarehouseId")?.Value);
            }
            orderDocument.Save(orderPath);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--role",
                "WarehouseRef",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Relationship 'Order.WarehouseRefId' already exists.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_DropSourcePropertyFailsWhenPropertyMissing_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.DoesNotExist",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Property 'Order.DoesNotExist' was not found.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorPropertyToRelationship_PreconditionFailureRollsBackAndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempRefactorCycleWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "B.ACode",
                "--target",
                "A",
                "--lookup",
                "Code",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains(
                "Source does not show reuse; lookup direction is ambiguous.",
                result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRelationshipToProperty_RoundTripsBackToLandingShape()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            Assert.Equal(0, (await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot)).ExitCode);

            var demote = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, demote.ExitCode);
            Assert.Contains("OK: refactor relationship-to-property", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Source: Order", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Target: Warehouse", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Role: (none)", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Property: WarehouseId", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows rewritten: 5", demote.StdOut, StringComparison.Ordinal);
            Assert.Contains("Relationship removed: yes", demote.StdOut, StringComparison.Ordinal);

            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRelationshipToProperty_FailsWhenRelationshipMissing_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Relationship 'Order->Warehouse' was not found.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRelationshipToProperty_FailsWhenPropertyAlreadyExists_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot)).ExitCode);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--property",
                "StatusText",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Property 'Order.StatusText' already exists.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRelationshipToProperty_FailsWhenAnyRowHasMissingFk_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot)).ExitCode);

            var orderPath = Path.Combine(workspaceRoot, "metadata", "instance", "Order.xml");
            var orderDocument = XDocument.Load(orderPath);
            var firstOrder = orderDocument.Descendants("Order")
                .First(element => string.Equals((string?)element.Attribute("Id"), "ORD-001", StringComparison.Ordinal));
            firstOrder.Attribute("WarehouseId")?.Remove();
            orderDocument.Save(orderPath);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Entity 'Order' row 'ORD-001' is missing required relationship 'WarehouseId'.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRelationshipToProperty_FailsWhenPropertyNameCollidesWithExistingRowAttribute_AndDoesNotWrite()
    {
        var workspaceRoot = await CreateTempSuggestDemoWorkspaceAsync();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-refactor-expected", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.WarehouseId",
                "--target",
                "Warehouse",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync(
                "model",
                "refactor",
                "property-to-relationship",
                "--source",
                "Order.SupplierId",
                "--target",
                "Supplier",
                "--lookup",
                "Id",
                "--workspace",
                workspaceRoot)).ExitCode);
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "refactor",
                "relationship-to-property",
                "--source",
                "Order",
                "--target",
                "Warehouse",
                "--property",
                "SupplierId",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains(
                "Cannot demote relationship 'Order.WarehouseId' to property 'SupplierId' because row 'ORD-001' already contains 'SupplierId'.",
                result.CombinedOutput,
                StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRefactorRenameEntity_UpdatesRelationshipsInstanceFieldsAndWorkspaceConfig()
    {
        var workspaceRoot = CreateTempWorkspaceWithEntityStorageRenameFixture();
        try
        {
            var result = await RunCliAsync(
                "model",
                "rename-entity",
                "SystemType",
                "PlatformType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: entity renamed", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("From: SystemType", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("To: PlatformType", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Relationships updated: 1", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("FK fields renamed: 1", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows touched: 2", result.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            Assert.DoesNotContain(
                model.Descendants("Entity"),
                element => string.Equals((string?)element.Attribute("name"), "SystemType", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                model.Descendants("Entity"),
                element => string.Equals((string?)element.Attribute("name"), "PlatformType", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                model.Descendants("Relationship"),
                element => string.Equals((string?)element.Attribute("entity"), "PlatformType", StringComparison.OrdinalIgnoreCase));

            var workspaceConfig = XDocument.Load(Path.Combine(workspaceRoot, "workspace.xml"));
            Assert.Contains(
                workspaceConfig.Descendants("EntityStorage"),
                element => string.Equals((string?)element.Element("EntityName"), "PlatformType", StringComparison.OrdinalIgnoreCase));

            var systemRows = LoadEntityRows(workspaceRoot, "System");
            Assert.All(systemRows, row =>
            {
                Assert.NotNull(row.Attribute("PlatformTypeId"));
                Assert.Null(row.Attribute("SystemTypeId"));
            });

            Assert.True(File.Exists(Path.Combine(workspaceRoot, "metadata", "instance", "PlatformType.xml")));
            Assert.False(File.Exists(Path.Combine(workspaceRoot, "metadata", "instance", "SystemType.xml")));

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRefactorRenameEntity_RoleRelationship_DoesNotRenameRoleField()
    {
        var workspaceRoot = CreateTempWorkspaceWithRoleRenameFixture();
        try
        {
            var result = await RunCliAsync(
                "model",
                "rename-entity",
                "SystemType",
                "PlatformType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Relationships updated: 1", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("FK fields renamed: 0", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows touched: 0", result.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            Assert.Contains(
                model.Descendants("Relationship"),
                element =>
                    string.Equals((string?)element.Attribute("entity"), "PlatformType", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)element.Attribute("role"), "PrimarySystemType", StringComparison.OrdinalIgnoreCase));

            var systemRows = LoadEntityRows(workspaceRoot, "System");
            Assert.All(systemRows, row =>
            {
                Assert.NotNull(row.Attribute("PrimarySystemTypeId"));
                Assert.Null(row.Attribute("PlatformTypeId"));
            });

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRefactorRenameEntity_CollisionFailsAndIsAtomic()
    {
        var workspaceRoot = CreateTempWorkspaceWithRenameCollisionFixture();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-rename-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "rename-entity",
                "SystemType",
                "PlatformType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("row 'System:1' already contains relationship 'PlatformTypeId'", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelRenameRelationship_SetsRoleAndRewritesUsageName()
    {
        InvalidateCliAssemblyCache();
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "rename-relationship",
                "System",
                "SystemType",
                "--role",
                "PrimarySystemType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: relationship renamed", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("From: System.SystemTypeId", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("To: System.PrimarySystemTypeId", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Target: SystemType", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("OldRole: (none)", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("NewRole: PrimarySystemType", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows touched: 2", result.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            Assert.Contains(
                model.Descendants("Relationship"),
                element =>
                    string.Equals((string?)element.Attribute("entity"), "SystemType", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)element.Attribute("role"), "PrimarySystemType", StringComparison.OrdinalIgnoreCase));

            var systemRows = LoadEntityRows(workspaceRoot, "System");
            Assert.All(systemRows, row =>
            {
                Assert.NotNull(row.Attribute("PrimarySystemTypeId"));
                Assert.Null(row.Attribute("SystemTypeId"));
            });

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRenameRelationship_ClearsRoleAndRewritesUsageName()
    {
        InvalidateCliAssemblyCache();
        var workspaceRoot = CreateTempWorkspaceWithRoleRenameFixture();
        try
        {
            var result = await RunCliAsync(
                "model",
                "rename-relationship",
                "System",
                "SystemType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: relationship renamed", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("From: System.PrimarySystemTypeId", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("To: System.SystemTypeId", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("OldRole: PrimarySystemType", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("NewRole: (none)", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Rows touched: 2", result.StdOut, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            Assert.Contains(
                model.Descendants("Relationship"),
                element =>
                    string.Equals((string?)element.Attribute("entity"), "SystemType", StringComparison.OrdinalIgnoreCase) &&
                    element.Attribute("role") == null);

            var systemRows = LoadEntityRows(workspaceRoot, "System");
            Assert.All(systemRows, row =>
            {
                Assert.NotNull(row.Attribute("SystemTypeId"));
                Assert.Null(row.Attribute("PrimarySystemTypeId"));
            });

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRenameRelationship_CollisionFailsAndIsAtomic()
    {
        InvalidateCliAssemblyCache();
        var workspaceRoot = CreateTempWorkspaceWithRelationshipRenameCollisionFixture();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-rename-relationship-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "rename-relationship",
                "System",
                "SystemType",
                "--role",
                "PrimarySystemType",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("property 'System.PrimarySystemTypeId' already exists", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task InstanceRelationshipSet_AllowsRoleSelector()
    {
        var workspaceRoot = CreateTempWorkspaceWithRoleRenameFixture();
        try
        {
            var result = await RunCliAsync(
                "instance",
                "relationship",
                "set",
                "System",
                "1",
                "--to",
                "PrimarySystemType",
                "2",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);

            var systemRows = LoadEntityRows(workspaceRoot, "System");
            var system = systemRows.Single(row => string.Equals((string?)row.Attribute("Id"), "1", StringComparison.Ordinal));
            Assert.Equal("2", (string?)system.Attribute("PrimarySystemTypeId"));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task InstanceRenameId_RenamesRowAndInboundRelationships()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "instance",
                "rename-id",
                "Cube",
                "1",
                "Cube-001",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: instance id renamed", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Entity: Cube", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("From: 1", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("To: Cube-001", result.StdOut, StringComparison.Ordinal);
            Assert.Contains("Relationships updated: 2", result.StdOut, StringComparison.Ordinal);

            var cubeRows = LoadEntityRows(workspaceRoot, "Cube");
            Assert.Contains(cubeRows, row => string.Equals((string?)row.Attribute("Id"), "Cube-001", StringComparison.Ordinal));
            Assert.DoesNotContain(cubeRows, row => string.Equals((string?)row.Attribute("Id"), "1", StringComparison.Ordinal));

            var measureRows = LoadEntityRows(workspaceRoot, "Measure");
            Assert.Contains(measureRows, row => string.Equals((string?)row.Attribute("CubeId"), "Cube-001", StringComparison.Ordinal));
            Assert.DoesNotContain(measureRows, row => string.Equals((string?)row.Attribute("CubeId"), "1", StringComparison.Ordinal));

            var systemCubeRows = LoadEntityRows(workspaceRoot, "SystemCube");
            Assert.Contains(systemCubeRows, row => string.Equals((string?)row.Attribute("CubeId"), "Cube-001", StringComparison.Ordinal));
            Assert.DoesNotContain(systemCubeRows, row => string.Equals((string?)row.Attribute("CubeId"), "1", StringComparison.Ordinal));

            var check = await RunCliAsync("check", "--workspace", workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task InstanceRenameId_FailsOnCollision_AndIsAtomic()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-rename-id-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "instance",
                "rename-id",
                "Cube",
                "1",
                "2",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("because it already exists", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelAddProperty_BackfillsExistingRows_WhenRequiredAndDefaultValueProvided()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "add-property",
                "Cube",
                "Category",
                "--required",
                "true",
                "--default-value",
                "General",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("property added", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DefaultValue: General", result.CombinedOutput, StringComparison.Ordinal);

            var modelPath = Path.Combine(workspaceRoot, "metadata", "model.xml");
            var model = XDocument.Load(modelPath);
            var cubeEntity = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Cube", StringComparison.OrdinalIgnoreCase));
            var cubeProperties = cubeEntity
                .Element("PropertyList")?
                .Elements("Property") ?? Enumerable.Empty<XElement>();
            Assert.Contains(
                cubeProperties,
                property => string.Equals((string?)property.Attribute("name"), "Category", StringComparison.OrdinalIgnoreCase));

            var cubeRows = LoadEntityRows(workspaceRoot, "Cube");
            foreach (var row in cubeRows)
            {
                Assert.Equal("General", row.Element("Category")?.Value);
            }

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddProperty_RequiresDefaultValue_WhenRequiredAndEntityHasRows()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "add-property",
                "Cube",
                "Category",
                "--required",
                "true",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("requires --default-value", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Cube.Category", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelAddProperty_AllowsMissingDefaultValue_WhenEntityHasNoRows()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, (await RunCliAsync("init", workspaceRoot)).ExitCode);
            Assert.Equal(0, (await RunCliAsync("model", "add-entity", "Thing", "--workspace", workspaceRoot)).ExitCode);

            var addProperty = await RunCliAsync(
                "model",
                "add-property",
                "Thing",
                "Name",
                "--required",
                "true",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, addProperty.ExitCode);
            Assert.DoesNotContain("DefaultValue:", addProperty.CombinedOutput, StringComparison.Ordinal);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSetPropertyRequired_RequiresDefaultValue_WhenExistingRowsAreMissingValues()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-set-property-required-expected", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(
                0,
                (await RunCliAsync(
                    "model",
                    "add-property",
                    "Cube",
                    "Category",
                    "--required",
                    "false",
                    "--workspace",
                    workspaceRoot)).ExitCode);

            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "set-property-required",
                "Cube",
                "Category",
                "--required",
                "true",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("requires --default-value", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Cube.Category", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ModelSetPropertyRequired_BackfillsExistingRows_WhenDefaultValueProvided()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            Assert.Equal(
                0,
                (await RunCliAsync(
                    "model",
                    "add-property",
                    "Cube",
                    "Category",
                    "--required",
                    "false",
                    "--workspace",
                    workspaceRoot)).ExitCode);

            var result = await RunCliAsync(
                "model",
                "set-property-required",
                "Cube",
                "Category",
                "--required",
                "true",
                "--default-value",
                "General",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("property requiredness updated", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DefaultValue: General", result.CombinedOutput, StringComparison.Ordinal);

            var model = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            var property = model
                .Descendants("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Cube", StringComparison.OrdinalIgnoreCase))
                .Element("PropertyList")!
                .Elements("Property")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Category", StringComparison.OrdinalIgnoreCase));
            Assert.Null(property.Attribute("isRequired"));

            foreach (var row in LoadEntityRows(workspaceRoot, "Cube"))
            {
                Assert.Equal("General", row.Element("Category")?.Value);
            }
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSetPropertyRequired_AllowsRequiredWithoutDefault_WhenExistingRowsAlreadyHaveValues()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            Assert.Equal(
                0,
                (await RunCliAsync(
                    "model",
                    "add-property",
                    "Cube",
                    "Category",
                    "--required",
                    "false",
                    "--default-value",
                    "General",
                    "--workspace",
                    workspaceRoot)).ExitCode);

            var result = await RunCliAsync(
                "model",
                "set-property-required",
                "Cube",
                "Category",
                "--required",
                "true",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("DefaultValue:", result.CombinedOutput, StringComparison.Ordinal);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelSetPropertyRequired_RejectsDefaultValue_WhenSettingOptional()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "set-property-required",
                "Cube",
                "CubeName",
                "--required",
                "false",
                "--default-value",
                "Ignored",
                "--workspace",
                workspaceRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("--default-value is only valid with --required true", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRenameModel_UpdatesModelAndInstanceRoots()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "model",
                "rename-model",
                "EnterpriseBIPlatform",
                "AnalyticsModel",
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("model renamed", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var modelDocument = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "model.xml"));
            Assert.Equal("AnalyticsModel", modelDocument.Root?.Attribute("name")?.Value);

            var cubeDocument = XDocument.Load(Path.Combine(workspaceRoot, "metadata", "instance", "Cube.xml"));
            Assert.Equal("AnalyticsModel", cubeDocument.Root?.Name.LocalName);

            var check = await RunCliAsync(
                "check",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task ModelRenameModel_FailsWhenWorkspaceModelDoesNotMatchRequestedSource()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var expectedWorkspace = Path.Combine(Path.GetTempPath(), "metadata-rename-model-expected", Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(workspaceRoot, expectedWorkspace);

            var result = await RunCliAsync(
                "model",
                "rename-model",
                "WrongModel",
                "AnalyticsModel",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Workspace model is 'EnterpriseBIPlatform', not 'WrongModel'.", result.CombinedOutput, StringComparison.Ordinal);
            AssertDirectoryBytesEqual(expectedWorkspace, workspaceRoot);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(expectedWorkspace);
        }
    }

    [Fact]
    public async Task ExportCsv_ExportsEntityRows_WithIdRelationshipsAndProperties()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        var csvPath = Path.Combine(Path.GetTempPath(), "metadata-export-csv", Guid.NewGuid().ToString("N"), "systemcube.csv");
        try
        {
            var result = await RunCliAsync(
                "export",
                "csv",
                "SystemCube",
                "--out",
                csvPath,
                "--workspace",
                workspaceRoot);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("exported csv", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(csvPath), "Expected CSV file to be written.");

            var lines = await File.ReadAllLinesAsync(csvPath);
            Assert.NotEmpty(lines);
            Assert.Equal("Id,CubeId,SystemId,ProcessingMode", lines[0]);
            Assert.Contains(lines, line => line.StartsWith("1,1,1,", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.StartsWith("2,2,2,", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(Path.GetDirectoryName(csvPath)!);
        }
    }

    [Fact]
    public async Task Delete_FailsWithHumanBlockers_WhenRelationshipUsageWouldBreak()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "delete",
                "Cube",
                "2",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("Cannot delete Cube 2", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("Blocked by existing relationships (", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("references Cube 2", result.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("SystemCube 2 references Cube 2", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Where:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Hint:", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("instance.relationship.orphan", result.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("contains(Id,'')", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RowRelationship_Set_ReplacesExistingUsageDeterministically()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var listBeforeSet = await RunCliAsync(
                "instance",
                "relationship",
                "list",
                "Measure",
                "1",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, listBeforeSet.ExitCode);
            Assert.Contains("Cube 1", listBeforeSet.StdOut, StringComparison.Ordinal);

            var setResult = await RunCliAsync(
                "instance",
                "relationship",
                "set",
                "Measure",
                "1",
                "--to",
                "Cube",
                "2",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, setResult.ExitCode);

            var listAfterSet = await RunCliAsync(
                "instance",
                "relationship",
                "list",
                "Measure",
                "1",
                "--workspace",
                workspaceRoot);
            Assert.Equal(0, listAfterSet.ExitCode);
            Assert.Contains("Cube 2", listAfterSet.StdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("Cube 1", listAfterSet.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task RowRelationship_Clear_IsNotExposed()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var clearResult = await RunCliAsync(
                "instance",
                "relationship",
                "clear",
                "Measure",
                "1",
                "--workspace",
                workspaceRoot);
            Assert.Equal(1, clearResult.ExitCode);
            Assert.Contains("Unknown command 'instance relationship clear'.", clearResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task Insert_FailsWhenRequiredRelationshipIsMissing()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var result = await RunCliAsync(
                "insert",
                "Measure",
                "99",
                "--set",
                "MeasureName=Missing Cube Relationship",
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("insert is missing required relationship 'CubeId'", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--set CubeId=<Id>", result.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task BulkInsert_FailsWhenRequiredRelationshipColumnIsMissing()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var tsvPath = Path.Combine(workspaceRoot, "measure-missing-cube.tsv");
            await File.WriteAllTextAsync(
                tsvPath,
                "Id\tMeasureName\n99\tMissing Cube Relationship\n");

            var result = await RunCliAsync(
                "bulk-insert",
                "Measure",
                "--from",
                "tsv",
                "--file",
                tsvPath,
                "--workspace",
                workspaceRoot);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("bulk-insert row 1 is missing required relationship 'CubeId'", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Set column 'CubeId' to a target Id", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task InstanceDiff_FailsWhenRightWorkspaceHasMissingRequiredRelationshipUsage()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        try
        {
            var measureShardPath = Path.Combine(rightWorkspace, "metadata", "instance", "Measure.xml");
            var measureShard = XDocument.Load(measureShardPath);
            var measureOne = measureShard
                .Descendants("Measure")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            measureOne.SetAttributeValue("CubeId", null);
            measureShard.Save(measureShardPath);

            var result = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains("missing required relationship 'CubeId'", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
        }
    }

    [Fact]
    public async Task GraphInbound_OutputIsDeterministic()
    {
        var workspaceRoot = CreateTempWorkspaceFromSamples();
        try
        {
            var first = await RunCliAsync(
                "graph",
                "inbound",
                "Cube",
                "--workspace",
                workspaceRoot,
                "--top",
                "20");
            var second = await RunCliAsync(
                "graph",
                "inbound",
                "Cube",
                "--workspace",
                workspaceRoot,
                "--top",
                "20");

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(first.StdOut, second.StdOut);
            Assert.Contains("Measure", first.StdOut, StringComparison.Ordinal);
            Assert.Contains("SystemCube", first.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
        }
    }

    [Fact]
    public async Task WorkspaceDiff_ReturnsDifferencesAndPersistsDiffWorkspace()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        try
        {
            var updateResult = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Diff sample changed",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateResult.ExitCode);

            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Diff sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertResult.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);

            Assert.Equal(1, diffResult.ExitCode);
            Assert.Contains("Instance diff: differences found.", diffResult.StdOut, StringComparison.Ordinal);
            var diffPathMatch = Regex.Match(diffResult.StdOut, @"(?m)^DiffWorkspace:\s*(.+)$");
            Assert.True(diffPathMatch.Success, $"Expected diff workspace path in output:{Environment.NewLine}{diffResult.StdOut}");

            var diffWorkspacePath = diffPathMatch.Groups[1].Value.Trim();
            Assert.True(Directory.Exists(diffWorkspacePath));

            var statusResult = await RunCliAsync("status", "--workspace", diffWorkspacePath);
            Assert.Equal(0, statusResult.ExitCode);
            Assert.Contains("InstanceDiffModelEqual", statusResult.StdOut, StringComparison.Ordinal);

            Assert.NotEmpty(LoadEntityRows(diffWorkspacePath, "ModelLeftPropertyInstanceNotInRight"));
            Assert.NotEmpty(LoadEntityRows(diffWorkspacePath, "ModelRightPropertyInstanceNotInLeft"));
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
        }
    }

    [Fact]
    public async Task InstanceDiff_UsesIdentityIds_AndReferenceColumnsPointToExistingIds()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var updateResult = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Identity shape verification",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateResult.ExitCode);

            var diffResult = await RunCliAsync("instance", "diff", leftWorkspace, rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var modelIds = AssertIdentityIds(diffWorkspacePath, "Model");
            var diffIds = AssertIdentityIds(diffWorkspacePath, "Diff");
            var entityIds = AssertIdentityIds(diffWorkspacePath, "Entity");
            var propertyIds = AssertIdentityIds(diffWorkspacePath, "Property");
            var leftRowIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftEntityInstance");
            var rightRowIds = AssertIdentityIds(diffWorkspacePath, "ModelRightEntityInstance");
            var leftPropertyInstanceIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftPropertyInstance");
            var rightPropertyInstanceIds = AssertIdentityIds(diffWorkspacePath, "ModelRightPropertyInstance");
            var leftEntityNotInRightIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftEntityInstanceNotInRight");
            var rightEntityNotInLeftIds = AssertIdentityIds(diffWorkspacePath, "ModelRightEntityInstanceNotInLeft");
            var leftNotInRightIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftPropertyInstanceNotInRight");
            var rightNotInLeftIds = AssertIdentityIds(diffWorkspacePath, "ModelRightPropertyInstanceNotInLeft");

            _ = leftEntityNotInRightIds;
            _ = rightEntityNotInLeftIds;
            _ = leftNotInRightIds;
            _ = rightNotInLeftIds;

            AssertReferenceValuesExist(diffWorkspacePath, "Diff", "ModelId", modelIds);
            AssertReferenceValuesExist(diffWorkspacePath, "Entity", "ModelId", modelIds);
            AssertReferenceValuesExist(diffWorkspacePath, "Property", "EntityId", entityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftEntityInstance", "DiffId", diffIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightEntityInstance", "DiffId", diffIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftEntityInstance", "EntityId", entityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightEntityInstance", "EntityId", entityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftPropertyInstance", "ModelLeftEntityInstanceId", leftRowIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightPropertyInstance", "ModelRightEntityInstanceId", rightRowIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftPropertyInstance", "PropertyId", propertyIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightPropertyInstance", "PropertyId", propertyIds);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelLeftEntityInstanceNotInRight",
                "ModelLeftEntityInstanceId",
                leftRowIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelRightEntityInstanceNotInLeft",
                "ModelRightEntityInstanceId",
                rightRowIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelLeftPropertyInstanceNotInRight",
                "ModelLeftPropertyInstanceId",
                leftPropertyInstanceIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelRightPropertyInstanceNotInLeft",
                "ModelRightPropertyInstanceId",
                rightPropertyInstanceIds,
                allowEmptyRows: true);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task WorkspaceDiff_FailsHardWhenModelsDiffer()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        try
        {
            var modelResult = await RunCliAsync(
                "model",
                "add-property",
                "Cube",
                "ModelDiffOnlyProperty",
                "--required",
                "false",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, modelResult.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);

            Assert.Equal(4, diffResult.ExitCode);
            Assert.Contains("byte-identical model.xml", diffResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LeftModel:", diffResult.CombinedOutput, StringComparison.Ordinal);
            Assert.Contains("RightModel:", diffResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
        }
    }

    [Fact]
    public async Task WorkspaceMerge_AppliesDiffWorkspace()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Diff sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertResult.ExitCode);

            var cubeIdsBeforeMerge = LoadEntityRows(leftWorkspace, "Cube")
                .Select(row => (string?)row.Attribute("Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            Assert.DoesNotContain("99", cubeIdsBeforeMerge);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            var diffPathMatch = Regex.Match(diffResult.StdOut, @"(?m)^DiffWorkspace:\s*(.+)$");
            Assert.True(diffPathMatch.Success);
            diffWorkspacePath = diffPathMatch.Groups[1].Value.Trim();

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(0, mergeResult.ExitCode);
            Assert.Contains("instance merge applied", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var viewResult = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "99",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, viewResult.ExitCode);
            Assert.Contains("Diff Cube", viewResult.CombinedOutput, StringComparison.Ordinal);

            var cubeIdsAfterMerge = LoadEntityRows(leftWorkspace, "Cube")
                .Select(row => (string?)row.Attribute("Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(
                cubeIdsBeforeMerge.Concat(new[] { "99" }).OrderBy(id => id, StringComparer.Ordinal),
                cubeIdsAfterMerge);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task WorkspaceMerge_FailsOnIncomingIdCollisionWithoutRemapping()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var insertRight = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Diff sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var insertLeft = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Local Cube",
                "--set",
                "Purpose=Target-local row",
                "--set",
                "RefreshMode=Scheduled",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, insertLeft.ExitCode);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(1, mergeResult.ExitCode);
            Assert.Contains("precondition failed", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var viewResult = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "99",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, viewResult.ExitCode);
            Assert.Contains("Local Cube", viewResult.CombinedOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("Diff Cube", viewResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task WorkspaceMerge_FailsOnFingerprintMismatch()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var insertRight = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Diff sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            var diffPathMatch = Regex.Match(diffResult.StdOut, @"(?m)^DiffWorkspace:\s*(.+)$");
            Assert.True(diffPathMatch.Success);
            diffWorkspacePath = diffPathMatch.Groups[1].Value.Trim();

            var mutateLeft = await RunCliAsync(
                "insert",
                "Cube",
                "100",
                "--set",
                "CubeName=Conflict Cube",
                "--set",
                "Purpose=Merge conflict sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, mutateLeft.ExitCode);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(1, mergeResult.ExitCode);
            Assert.Contains("precondition failed", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Next:", mergeResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task WorkspaceMerge_RejectsAutoIdOption()
    {
        var result = await RunCliAsync(
            "instance",
            "merge",
            ".\\LeftWorkspace",
            ".\\RightWorkspace.instance-diff",
            "--auto-id");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error:", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("applied", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceMerge_FailsHardWhenDiffContainsModelDeltas()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Diff sample",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertResult.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var diffShardPath = Path.Combine(diffWorkspacePath, "metadata", "instance", "Diff.xml");
            var diffShard = XDocument.Load(diffShardPath);
            var summaryRow = diffShard.Descendants("Diff").Single();
            summaryRow.SetAttributeValue("ModelId", null);
            summaryRow.Element("ModelId")?.Remove();
            diffShard.Save(diffShardPath);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);

            Assert.Equal(4, mergeResult.ExitCode);
            Assert.Contains("missing required relationship", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ModelId", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task Insert_AllowsOpaqueStringIds()
    {
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "b|a",
                "--set",
                "CubeName=Pipe Cube",
                "--set",
                "Purpose=Opaque id test",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertResult.ExitCode);

            var viewResult = await RunCliAsync(
                "view",
                "instance",
                "Cube",
                "b|a",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, viewResult.ExitCode);
            Assert.Contains("Pipe Cube", viewResult.CombinedOutput, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(rightWorkspace);
        }
    }

    [Fact]
    public async Task InstanceDiffAligned_ReturnsCleanWhenMappedSubsetEqual()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            var renamePropertyResult = await RunCliAsync(
                "model",
                "rename-property",
                "Cube",
                "Purpose",
                "BusinessPurpose",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, renamePropertyResult.ExitCode);

            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    ("Purpose", "BusinessPurpose"),
                    ("RefreshMode", "RefreshMode"),
                });

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(0, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);
            Assert.Contains("no differences", diffResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceDiffAligned_UsesIdentityIds_AndReferenceColumnsPointToExistingIds()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            var renamePropertyResult = await RunCliAsync(
                "model",
                "rename-property",
                "Cube",
                "Purpose",
                "BusinessPurpose",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, renamePropertyResult.ExitCode);

            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    ("Purpose", "BusinessPurpose"),
                    ("RefreshMode", "RefreshMode"),
                });

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(0, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var modelIds = AssertIdentityIds(diffWorkspacePath, "Model");
            var modelLeftIds = AssertIdentityIds(diffWorkspacePath, "ModelLeft");
            var modelRightIds = AssertIdentityIds(diffWorkspacePath, "ModelRight");
            var alignmentIds = AssertIdentityIds(diffWorkspacePath, "Alignment");
            var modelLeftEntityIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftEntity");
            var modelRightEntityIds = AssertIdentityIds(diffWorkspacePath, "ModelRightEntity");
            var modelLeftPropertyIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftProperty");
            var modelRightPropertyIds = AssertIdentityIds(diffWorkspacePath, "ModelRightProperty");
            var entityMapIds = AssertIdentityIds(diffWorkspacePath, "EntityMap");
            var propertyMapIds = AssertIdentityIds(diffWorkspacePath, "PropertyMap");
            var leftRowIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftEntityInstance");
            var rightRowIds = AssertIdentityIds(diffWorkspacePath, "ModelRightEntityInstance");
            var leftPropertyInstanceIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftPropertyInstance");
            var rightPropertyInstanceIds = AssertIdentityIds(diffWorkspacePath, "ModelRightPropertyInstance");
            var leftEntityNotInRightIds = AssertIdentityIds(diffWorkspacePath, "ModelLeftEntityInstanceNotInRight");
            var rightEntityNotInLeftIds = AssertIdentityIds(diffWorkspacePath, "ModelRightEntityInstanceNotInLeft");

            _ = alignmentIds;
            _ = entityMapIds;
            _ = propertyMapIds;
            _ = leftEntityNotInRightIds;
            _ = rightEntityNotInLeftIds;

            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeft", "ModelId", modelIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRight", "ModelId", modelIds);
            AssertReferenceValuesExist(diffWorkspacePath, "Alignment", "ModelLeftId", modelLeftIds);
            AssertReferenceValuesExist(diffWorkspacePath, "Alignment", "ModelRightId", modelRightIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftEntity", "ModelLeftId", modelLeftIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightEntity", "ModelRightId", modelRightIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftProperty", "ModelLeftEntityId", modelLeftEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightProperty", "ModelRightEntityId", modelRightEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "EntityMap", "ModelLeftEntityId", modelLeftEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "EntityMap", "ModelRightEntityId", modelRightEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "PropertyMap", "ModelLeftPropertyId", modelLeftPropertyIds);
            AssertReferenceValuesExist(diffWorkspacePath, "PropertyMap", "ModelRightPropertyId", modelRightPropertyIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftEntityInstance", "ModelLeftEntityId", modelLeftEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightEntityInstance", "ModelRightEntityId", modelRightEntityIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftPropertyInstance", "ModelLeftEntityInstanceId", leftRowIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightPropertyInstance", "ModelRightEntityInstanceId", rightRowIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelLeftPropertyInstance", "ModelLeftPropertyId", modelLeftPropertyIds);
            AssertReferenceValuesExist(diffWorkspacePath, "ModelRightPropertyInstance", "ModelRightPropertyId", modelRightPropertyIds);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelLeftEntityInstanceNotInRight",
                "ModelLeftEntityInstanceId",
                leftRowIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelRightEntityInstanceNotInLeft",
                "ModelRightEntityInstanceId",
                rightRowIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelLeftPropertyInstanceNotInRight",
                "ModelLeftPropertyInstanceId",
                leftPropertyInstanceIds,
                allowEmptyRows: true);
            AssertReferenceValuesExist(
                diffWorkspacePath,
                "ModelRightPropertyInstanceNotInLeft",
                "ModelRightPropertyInstanceId",
                rightPropertyInstanceIds,
                allowEmptyRows: true);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task WorkspaceDiff_FailsOnBlankAndDuplicateIds()
    {
        var blankLeftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var blankRightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var duplicateLeftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var duplicateRightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        try
        {
            var blankInstancePath = Path.Combine(blankLeftWorkspace, "metadata", "instance", "Cube.xml");
            var blankInstance = XDocument.Load(blankInstancePath);
            blankInstance.Descendants("Cube").First().SetAttributeValue("Id", string.Empty);
            blankInstance.Save(blankInstancePath);

            var blankDiffResult = await RunCliAsync(
                "instance",
                "diff",
                blankLeftWorkspace,
                blankRightWorkspace);
            Assert.Equal(4, blankDiffResult.ExitCode);
            Assert.Contains("missing valid Id", blankDiffResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);

            var duplicateInstancePath = Path.Combine(duplicateLeftWorkspace, "metadata", "instance", "Cube.xml");
            var duplicateInstance = XDocument.Load(duplicateInstancePath);
            var cubes = duplicateInstance.Descendants("Cube").Take(2).ToList();
            Assert.True(cubes.Count == 2);
            cubes[1].SetAttributeValue("Id", (string?)cubes[0].Attribute("Id") ?? string.Empty);
            duplicateInstance.Save(duplicateInstancePath);

            var duplicateDiffResult = await RunCliAsync(
                "instance",
                "diff",
                duplicateLeftWorkspace,
                duplicateRightWorkspace);
            Assert.Equal(4, duplicateDiffResult.ExitCode);
            Assert.Contains("duplicate Id", duplicateDiffResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(blankLeftWorkspace);
            DeleteDirectorySafe(blankRightWorkspace);
            DeleteDirectorySafe(duplicateLeftWorkspace);
            DeleteDirectorySafe(duplicateRightWorkspace);
        }
    }

    [Fact]
    public async Task WorkspaceMerge_RejectsMultipleSummaryRows()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var insertResult = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Diff Cube",
                "--set",
                "Purpose=Summary-row test",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertResult.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var summaryShardPath = Path.Combine(diffWorkspacePath, "metadata", "instance", "Diff.xml");
            var summaryShard = XDocument.Load(summaryShardPath);
            var summaryRow = summaryShard.Descendants("Diff").Single();
            var duplicateSummaryRow = new XElement(summaryRow);
            duplicateSummaryRow.SetAttributeValue("Id", "2");
            summaryRow.Parent!.Add(duplicateSummaryRow);
            summaryShard.Save(summaryShardPath);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(4, mergeResult.ExitCode);
            Assert.Contains("must contain exactly one 'Diff' row", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceDiff_TreatsMissingAndExplicitEmptyAsDifferent()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        const string propertyName = "MissingVsEmptyProp";
        try
        {
            var addLeftProperty = await RunCliAsync(
                "model",
                "add-property",
                "Cube",
                propertyName,
                "--required",
                "false",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, addLeftProperty.ExitCode);

            var addRightProperty = await RunCliAsync(
                "model",
                "add-property",
                "Cube",
                propertyName,
                "--required",
                "false",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, addRightProperty.ExitCode);

            var setRightEmpty = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                propertyName + "=",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, setRightEmpty.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            Assert.NotEmpty(LoadEntityRows(diffWorkspacePath, "ModelRightPropertyInstanceNotInLeft"));
            Assert.Empty(LoadEntityRows(diffWorkspacePath, "ModelLeftPropertyInstanceNotInRight"));

            var propertyShardPath = Path.Combine(diffWorkspacePath, "metadata", "instance", "Property.xml");
            var rightPropertyShardPath = Path.Combine(diffWorkspacePath, "metadata", "instance", "ModelRightPropertyInstance.xml");
            var propertyShard = XDocument.Load(propertyShardPath);
            var optionalPropertyId = propertyShard
                .Descendants("Property")
                .Single(element => string.Equals(GetFieldValue(element, "Name"), propertyName, StringComparison.OrdinalIgnoreCase))
                .Attribute("Id")?.Value;
            Assert.False(string.IsNullOrWhiteSpace(optionalPropertyId));

            var rightPropertyShard = XDocument.Load(rightPropertyShardPath);
            Assert.Contains(
                rightPropertyShard.Descendants("ModelRightPropertyInstance"),
                element =>
                    string.Equals(GetFieldValue(element, "PropertyId"), optionalPropertyId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(GetFieldValue(element, "Value"), string.Empty, StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceMerge_PreservesMissingVsExplicitEmptyInBothDirections()
    {
        var missingWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var emptyWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var reverseMissingWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffToEmptyWorkspace = null;
        string? diffToMissingWorkspace = null;
        const string propertyName = "MissingVsEmptyProp";
        try
        {
            foreach (var workspacePath in new[] { missingWorkspace, emptyWorkspace, reverseMissingWorkspace })
            {
                var addProperty = await RunCliAsync(
                    "model",
                    "add-property",
                    "Cube",
                    propertyName,
                    "--required",
                    "false",
                    "--workspace",
                    workspacePath);
                Assert.Equal(0, addProperty.ExitCode);
            }

            var setEmpty = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                propertyName + "=",
                "--workspace",
                emptyWorkspace);
            Assert.Equal(0, setEmpty.ExitCode);

            var diffToEmpty = await RunCliAsync(
                "instance",
                "diff",
                missingWorkspace,
                emptyWorkspace);
            Assert.Equal(1, diffToEmpty.ExitCode);
            diffToEmptyWorkspace = ExtractDiffWorkspacePath(diffToEmpty.StdOut);

            var mergeToEmpty = await RunCliAsync(
                "instance",
                "merge",
                missingWorkspace,
                diffToEmptyWorkspace);
            Assert.Equal(0, mergeToEmpty.ExitCode);

            var missingCubeShard = XDocument.Load(Path.Combine(missingWorkspace, "metadata", "instance", "Cube.xml"));
            var missingCubeOne = missingCubeShard
                .Descendants("Cube")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(string.Empty, missingCubeOne.Element(propertyName)?.Value);

            var diffToMissing = await RunCliAsync(
                "instance",
                "diff",
                emptyWorkspace,
                reverseMissingWorkspace);
            Assert.Equal(1, diffToMissing.ExitCode);
            diffToMissingWorkspace = ExtractDiffWorkspacePath(diffToMissing.StdOut);

            var mergeToMissing = await RunCliAsync(
                "instance",
                "merge",
                emptyWorkspace,
                diffToMissingWorkspace);
            Assert.Equal(0, mergeToMissing.ExitCode);

            var emptyCubeShard = XDocument.Load(Path.Combine(emptyWorkspace, "metadata", "instance", "Cube.xml"));
            var emptyCubeOne = emptyCubeShard
                .Descendants("Cube")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            Assert.Null(emptyCubeOne.Element(propertyName));
        }
        finally
        {
            DeleteDirectorySafe(missingWorkspace);
            DeleteDirectorySafe(emptyWorkspace);
            DeleteDirectorySafe(reverseMissingWorkspace);
            if (!string.IsNullOrWhiteSpace(diffToEmptyWorkspace))
            {
                DeleteDirectorySafe(diffToEmptyWorkspace);
            }

            if (!string.IsNullOrWhiteSpace(diffToMissingWorkspace))
            {
                DeleteDirectorySafe(diffToMissingWorkspace);
            }
        }
    }

    [Fact]
    public async Task InstanceMerge_RejectsDiffWhenModelRightPropertyInstanceValueIsMissing()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        string? diffWorkspacePath = null;
        try
        {
            var updateRight = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Tampered diff test",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var rightPropertyShardPath = Path.Combine(
                diffWorkspacePath,
                "metadata",
                "instance",
                "ModelRightPropertyInstance.xml");
            var rightPropertyShard = XDocument.Load(rightPropertyShardPath);
            var tampered = rightPropertyShard
                .Descendants("ModelRightPropertyInstance")
                .FirstOrDefault(element => element.Element("Value") != null);
            Assert.NotNull(tampered);
            tampered!.Element("Value")!.Remove();
            rightPropertyShard.Save(rightPropertyShardPath);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(4, mergeResult.ExitCode);
            Assert.Contains("missing required value 'Value'", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceMergeAligned_AppliesMappedRightSnapshot()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    ("Purpose", "Purpose"),
                    ("RefreshMode", "RefreshMode"),
                });

            var updateRight = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "2",
                "--set",
                "Purpose=Aligned merge update",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateRight.ExitCode);

            var insertRight = await RunCliAsync(
                "insert",
                "Cube",
                "99",
                "--set",
                "CubeName=Aligned Merge Cube",
                "--set",
                "Purpose=Added by merge-aligned",
                "--set",
                "RefreshMode=Manual",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, insertRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge-aligned",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(0, mergeResult.ExitCode);

            var mergedCubeShard = XDocument.Load(Path.Combine(leftWorkspace, "metadata", "instance", "Cube.xml"));
            var updatedCube = mergedCubeShard
                .Descendants("Cube")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "2", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Aligned merge update", GetFieldValue(updatedCube, "Purpose"));

            var insertedCube = mergedCubeShard
                .Descendants("Cube")
                .SingleOrDefault(element => string.Equals((string?)element.Attribute("Id"), "99", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(insertedCube);
            Assert.Equal("Aligned Merge Cube", GetFieldValue(insertedCube!, "CubeName"));
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceMergeAligned_FailsWhenTargetDoesNotMatchLeftSnapshot()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    ("Purpose", "Purpose"),
                });

            var updateRight = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Right snapshot value",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var driftTarget = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Target drift",
                "--workspace",
                leftWorkspace);
            Assert.Equal(0, driftTarget.ExitCode);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge-aligned",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(1, mergeResult.ExitCode);
            Assert.Contains("precondition failed", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceMergeAligned_RejectsMalformedDiffWorkspace()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    ("Purpose", "Purpose"),
                });

            var updateRight = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Tampered aligned diff",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateRight.ExitCode);

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var alignmentShardPath = Path.Combine(diffWorkspacePath, "metadata", "instance", "Alignment.xml");
            var alignmentShard = XDocument.Load(alignmentShardPath);
            var alignmentRow = alignmentShard.Descendants("Alignment").Single();
            alignmentRow.Attribute("ModelRightId")?.Remove();
            alignmentRow.Element("ModelRightId")?.Remove();
            alignmentShard.Save(alignmentShardPath);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge-aligned",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(4, mergeResult.ExitCode);
            Assert.Contains("ModelRightId", mergeResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceDiff_RepeatedRuns_AreByteDeterministic()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var firstSnapshot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        try
        {
            var updateRight = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                "Purpose=Deterministic diff content",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, updateRight.ExitCode);

            var firstDiff = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, firstDiff.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(firstDiff.StdOut);
            CopyDirectory(diffWorkspacePath, firstSnapshot);

            var secondDiff = await RunCliAsync(
                "instance",
                "diff",
                leftWorkspace,
                rightWorkspace);
            Assert.Equal(1, secondDiff.ExitCode);

            AssertDirectoryBytesEqual(firstSnapshot, diffWorkspacePath);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(firstSnapshot);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    [Fact]
    public async Task InstanceDiffAligned_RejectsIdPropertyMappings()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("Id", "Id"),
                    ("CubeName", "CubeName"),
                });

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(4, diffResult.ExitCode);
            Assert.Contains("missing aligned property 'Id'", diffResult.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
        }
    }

    [Fact]
    public async Task InstanceMergeAligned_PreservesMissingVsExplicitEmpty()
    {
        var leftWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var rightWorkspace = await CreateTempCanonicalWorkspaceFromSamplesAsync();
        var alignmentWorkspace = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        string? diffWorkspacePath = null;
        const string propertyName = "AlignedMissingVsEmpty";
        try
        {
            foreach (var workspacePath in new[] { leftWorkspace, rightWorkspace })
            {
                var addProperty = await RunCliAsync(
                    "model",
                    "add-property",
                    "Cube",
                    propertyName,
                    "--required",
                    "false",
                    "--workspace",
                    workspacePath);
                Assert.Equal(0, addProperty.ExitCode);
            }

            var setRightEmpty = await RunCliAsync(
                "instance",
                "update",
                "Cube",
                "1",
                "--set",
                propertyName + "=",
                "--workspace",
                rightWorkspace);
            Assert.Equal(0, setRightEmpty.ExitCode);

            await CreateAlignmentWorkspaceAsync(
                alignmentWorkspace,
                modelLeftName: "EnterpriseBIPlatform",
                modelRightName: "EnterpriseBIPlatform",
                leftEntityName: "Cube",
                rightEntityName: "Cube",
                propertyMappings: new (string Left, string Right)[]
                {
                    ("CubeName", "CubeName"),
                    (propertyName, propertyName),
                });

            var diffResult = await RunCliAsync(
                "instance",
                "diff-aligned",
                leftWorkspace,
                rightWorkspace,
                alignmentWorkspace);
            Assert.Equal(1, diffResult.ExitCode);
            diffWorkspacePath = ExtractDiffWorkspacePath(diffResult.StdOut);

            var mergeResult = await RunCliAsync(
                "instance",
                "merge-aligned",
                leftWorkspace,
                diffWorkspacePath);
            Assert.Equal(0, mergeResult.ExitCode);

            var cubeShard = XDocument.Load(Path.Combine(leftWorkspace, "metadata", "instance", "Cube.xml"));
            var cubeOne = cubeShard
                .Descendants("Cube")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(cubeOne.Element(propertyName));
            Assert.Equal(string.Empty, cubeOne.Element(propertyName)!.Value);
        }
        finally
        {
            DeleteDirectorySafe(leftWorkspace);
            DeleteDirectorySafe(rightWorkspace);
            DeleteDirectorySafe(alignmentWorkspace);
            if (!string.IsNullOrWhiteSpace(diffWorkspacePath))
            {
                DeleteDirectorySafe(diffWorkspacePath);
            }
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr, string CombinedOutput)> RunCliAsync(
        params string[] arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = ResolveCliExecutablePath(repoRoot);

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await WaitForProcessExitAsync(process, startInfo, TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        return (process.ExitCode, stdOut, stdErr, stdOut + Environment.NewLine + stdErr);
    }

    private static void InvalidateCliAssemblyCache()
    {
        cliExecutablePath = null;
    }

    private static string ResolveCliExecutablePath(string repoRoot)
    {
        if (!string.IsNullOrWhiteSpace(cliExecutablePath) && File.Exists(cliExecutablePath))
        {
            return cliExecutablePath;
        }

        var targetFramework = ResolveCliTargetFramework(repoRoot);
        var candidate = Path.Combine(repoRoot, Path.Combine("Meta", "Cli"), "bin", "Debug", targetFramework, "meta.exe");
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException($"Could not find compiled Meta CLI at '{candidate}'. Build Meta.Cli before running CLI tests.");
        }

        cliExecutablePath = candidate;
        return candidate;
    }

    private static async Task WaitForProcessExitAsync(
        Process process,
        ProcessStartInfo startInfo,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            TryKillProcessTree(process);
            process.WaitForExit();
            throw new TimeoutException(
                $"Timed out waiting for process: {startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}",
                exception);
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKillProcessTree(process);
                process.WaitForExit();
            }
        }
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

    private static string ResolveCliTargetFramework(string repoRoot)
    {
        var cliProject = Path.Combine(repoRoot, Path.Combine("Meta", "Cli"), "Meta.Cli.csproj");
        if (!File.Exists(cliProject))
        {
            throw new FileNotFoundException($"Could not find CLI project at '{cliProject}'.");
        }

        var document = XDocument.Load(cliProject, LoadOptions.None);
        var targetFramework = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new InvalidOperationException($"Project '{cliProject}' does not define <TargetFramework>.");
        }

        return targetFramework;
    }

    private static HashSet<string> AssertIdentityIds(string workspacePath, string entityName)
    {
        var rows = LoadEntityRows(workspacePath, entityName);
        var parsedIds = rows
            .Select(row =>
            {
                var idText = (string?)row.Attribute("Id");
                Assert.False(string.IsNullOrWhiteSpace(idText), $"{entityName} row is missing Id.");
                Assert.True(int.TryParse(idText, out _), $"{entityName} row Id '{idText}' is not numeric.");
                return int.Parse(idText!, System.Globalization.CultureInfo.InvariantCulture);
            })
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(Enumerable.Range(1, parsedIds.Length), parsedIds);
        return rows
            .Select(row => (string)row.Attribute("Id")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertReferenceValuesExist(
        string workspacePath,
        string entityName,
        string referenceAttributeName,
        IReadOnlySet<string> targetIds,
        bool allowEmptyRows = false)
    {
        var rows = LoadEntityRows(workspacePath, entityName);
        if (!rows.Any())
        {
            Assert.True(allowEmptyRows, $"{entityName} has no rows but rows were expected.");
            return;
        }

        foreach (var row in rows)
        {
            var reference = GetFieldValue(row, referenceAttributeName);
            Assert.False(
                string.IsNullOrWhiteSpace(reference),
                $"{entityName} row '{(string?)row.Attribute("Id")}' is missing '{referenceAttributeName}'.");
            Assert.Contains(reference!, targetIds);
        }
    }

    private static string? GetFieldValue(XElement row, string fieldName)
    {
        var attributeValue = (string?)row.Attribute(fieldName);
        if (!string.IsNullOrWhiteSpace(attributeValue) || row.Attribute(fieldName) != null)
        {
            return attributeValue;
        }

        var element = row.Element(fieldName);
        return element?.Value;
    }

    private static IReadOnlyList<XElement> LoadEntityRows(string workspacePath, string entityName)
    {
        var shardPath = Path.Combine(workspacePath, "metadata", "instance", entityName + ".xml");
        if (!File.Exists(shardPath))
        {
            return Array.Empty<XElement>();
        }

        var document = XDocument.Load(shardPath);
        return document.Descendants(entityName).ToList();
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        Directory.CreateDirectory(destinationPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void AssertDirectoryBytesEqual(string expectedPath, string actualPath)
    {
        var expectedFiles = Directory.GetFiles(expectedPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(expectedPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actualFiles = Directory.GetFiles(actualPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(actualPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expectedFiles, actualFiles);

        foreach (var relativePath in expectedFiles)
        {
            var expectedBytes = File.ReadAllBytes(Path.Combine(expectedPath, relativePath));
            var actualBytes = File.ReadAllBytes(Path.Combine(actualPath, relativePath));
            Assert.True(
                expectedBytes.AsSpan().SequenceEqual(actualBytes),
                $"File '{relativePath}' differs between expected and actual directories.");
        }
    }

    private static async Task CreateAlignmentWorkspaceAsync(
        string workspaceRoot,
        string modelLeftName,
        string modelRightName,
        string leftEntityName,
        string rightEntityName,
        IReadOnlyList<(string Left, string Right)> propertyMappings)
    {
        if (propertyMappings.Count == 0)
        {
            throw new InvalidOperationException("Alignment test helper requires at least one property mapping.");
        }

        var repoRoot = FindRepositoryRoot();
        var metadataRoot = Path.Combine(workspaceRoot, "metadata");
        var instanceRoot = Path.Combine(metadataRoot, "instance");
        Directory.CreateDirectory(instanceRoot);
        File.Copy(
            Path.Combine(repoRoot, "Meta.Core.Workspaces", "InstanceDiff.Alignment", "metadata", "model.xml"),
            Path.Combine(metadataRoot, "model.xml"),
            overwrite: true);

        var services = new ServiceCollection();
        var workspace = await services.WorkspaceService
            .LoadAsync(workspaceRoot, searchUpward: false)
            .ConfigureAwait(false);
        workspace.WorkspaceRootPath = workspaceRoot;
        workspace.MetadataRootPath = metadataRoot;
        workspace.IsDirty = true;
        workspace.Instance.ModelName = workspace.Model.Name;

        AddRow("Model", "1", ("Name", modelLeftName));
        AddRow("Model", "2", ("Name", modelRightName));
        AddRow("ModelLeft", "1", ("ModelId", "1"));
        AddRow("ModelRight", "1", ("ModelId", "2"));
        AddRow(
            "Alignment",
            "1",
            ("Name", "TestAlignment"),
            ("ModelLeftId", "1"),
            ("ModelRightId", "1"));

        AddRow(
            "ModelLeftEntity",
            "1",
            ("Name", leftEntityName),
            ("ModelLeftId", "1"));
        AddRow(
            "ModelRightEntity",
            "1",
            ("Name", rightEntityName),
            ("ModelRightId", "1"));
        AddRow(
            "EntityMap",
            "1",
            ("ModelLeftEntityId", "1"),
            ("ModelRightEntityId", "1"));

        for (var index = 0; index < propertyMappings.Count; index++)
        {
            var ordinal = (index + 1).ToString();
            var leftPropertyId = ordinal;
            var rightPropertyId = ordinal;
            AddRow(
                "ModelLeftProperty",
                leftPropertyId,
                ("Name", propertyMappings[index].Left),
                ("ModelLeftEntityId", "1"));
            AddRow(
                "ModelRightProperty",
                rightPropertyId,
                ("Name", propertyMappings[index].Right),
                ("ModelRightEntityId", "1"));
            AddRow(
                "PropertyMap",
                ordinal,
                ("ModelLeftPropertyId", leftPropertyId),
                ("ModelRightPropertyId", rightPropertyId));
        }

        await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

        void AddRow(
            string entityName,
            string id,
            params (string Key, string Value)[] values)
        {
            var entity = workspace.Model.FindEntity(entityName)
                         ?? throw new InvalidOperationException($"Alignment helper is missing entity '{entityName}'.");
            var propertyNames = entity.Properties
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relationshipNames = entity.Relationships
                .Select(item => item.GetColumnName())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var row = new GenericRecord
            {
                Id = id,
            };
            foreach (var (key, value) in values)
            {
                if (relationshipNames.Contains(key))
                {
                    row.RelationshipIds[key] = value;
                    continue;
                }

                if (propertyNames.Contains(key))
                {
                    row.Values[key] = value;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Alignment helper cannot map field '{key}' for entity '{entityName}'.");
            }

            workspace.Instance.GetOrCreateEntityRecords(entityName).Add(row);
        }
    }

    private static async Task<string> CreateTempSuggestDemoWorkspaceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadata-suggest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        DeleteDirectorySafe(root);
        return await TestWorkspaceFactory.CreateTempSuggestDemoWorkspaceAsync().ConfigureAwait(false);
    }

    private static async Task<string> CreateTempRefactorCycleWorkspaceAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadata-refactor-cycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        Assert.Equal(0, (await RunCliAsync("init", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-entity", "A", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-entity", "B", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-property", "A", "Code", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-property", "B", "Code", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-property", "B", "ACode", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("insert", "B", "1", "--set", "Code=B1", "--set", "ACode=A1", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("model", "add-relationship", "A", "B", "--default-id", "1", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("insert", "A", "1", "--set", "Code=A1", "--set", "BId=1", "--workspace", root)).ExitCode);
        Assert.Equal(0, (await RunCliAsync("check", "--workspace", root)).ExitCode);

        return root;
    }

    private static string CreateTempWorkspaceWithEntityStorageRenameFixture()
    {
        var root = CreateTempWorkspaceFromSamples();
        var workspaceConfigPath = Path.Combine(root, "workspace.xml");
        var workspaceConfig = XDocument.Load(workspaceConfigPath);
        var entityStorages = workspaceConfig.Root?.Element("EntityStorageList");
        Assert.NotNull(entityStorages);
        entityStorages!.Add(
            new XElement("EntityStorage",
                new XAttribute("Id", "1"),
                new XAttribute("WorkspaceId", "1"),
                new XElement("EntityName", "SystemType"),
                new XElement("StorageKind", "Sharded"),
                new XElement("FilePath", "metadata/instance/SystemType.xml")));
        workspaceConfig.Save(workspaceConfigPath);
        return root;
    }

    private static string CreateTempWorkspaceWithRoleRenameFixture()
    {
        var root = CreateTempWorkspaceFromSamples();

        var modelPath = Path.Combine(root, "metadata", "model.xml");
        var modelDocument = XDocument.Load(modelPath);
        var systemEntity = modelDocument
            .Descendants("Entity")
            .Single(element => string.Equals((string?)element.Attribute("name"), "System", StringComparison.OrdinalIgnoreCase));
        var relationship = systemEntity
            .Descendants("Relationship")
            .Single(element => string.Equals((string?)element.Attribute("entity"), "SystemType", StringComparison.OrdinalIgnoreCase));
        relationship.SetAttributeValue("role", "PrimarySystemType");
        modelDocument.Save(modelPath);

        var systemPath = Path.Combine(root, "metadata", "instance", "System.xml");
        var systemDocument = XDocument.Load(systemPath);
        foreach (var row in systemDocument.Descendants("System"))
        {
            var current = (string?)row.Attribute("SystemTypeId");
            row.SetAttributeValue("PrimarySystemTypeId", current);
            row.Attribute("SystemTypeId")?.Remove();
        }

        systemDocument.Save(systemPath);
        return root;
    }

    private static string CreateTempWorkspaceWithRenameCollisionFixture()
    {
        var root = CreateTempWorkspaceFromSamples();

        var modelPath = Path.Combine(root, "metadata", "model.xml");
        var modelDocument = XDocument.Load(modelPath);
        var systemEntity = modelDocument
            .Descendants("Entity")
            .Single(element => string.Equals((string?)element.Attribute("name"), "System", StringComparison.OrdinalIgnoreCase));
        var relationships = systemEntity.Element("RelationshipList");
        Assert.NotNull(relationships);
        relationships!.Add(new XElement("Relationship",
            new XAttribute("entity", "Cube"),
            new XAttribute("role", "PlatformType")));
        modelDocument.Save(modelPath);

        var systemPath = Path.Combine(root, "metadata", "instance", "System.xml");
        var systemDocument = XDocument.Load(systemPath);
        foreach (var row in systemDocument.Descendants("System"))
        {
            row.SetAttributeValue("PlatformTypeId", "1");
        }

        systemDocument.Save(systemPath);
        return root;
    }

    private static string CreateTempWorkspaceWithRelationshipRenameCollisionFixture()
    {
        var root = CreateTempWorkspaceFromSamples();

        var modelPath = Path.Combine(root, "metadata", "model.xml");
        var modelDocument = XDocument.Load(modelPath);
        var systemEntity = modelDocument
            .Descendants("Entity")
            .Single(element => string.Equals((string?)element.Attribute("name"), "System", StringComparison.OrdinalIgnoreCase));
        var properties = systemEntity.Element("PropertyList");
        Assert.NotNull(properties);
        properties!.Add(new XElement("Property", new XAttribute("name", "PrimarySystemTypeId")));
        modelDocument.Save(modelPath);

        return root;
    }

    private static string CreateTempWorkspaceWithMissingRequiredRelationship()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadata-invalid-load-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "metadata", "instance"));

        File.WriteAllText(
            Path.Combine(root, "workspace.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <MetaWorkspace>
              <WorkspaceList>
                <Workspace Id="1" WorkspaceLayoutId="1" EncodingId="1" NewlinesId="1" EntitiesOrderId="1" PropertiesOrderId="1" RelationshipsOrderId="1" RowsOrderId="2" AttributesOrderId="3">
                  <Name>Workspace</Name>
                  <FormatVersion>1.0</FormatVersion>
                </Workspace>
              </WorkspaceList>
              <WorkspaceLayoutList>
                <WorkspaceLayout Id="1">
                  <ModelFilePath>metadata/model.xml</ModelFilePath>
                  <InstanceDirPath>metadata/instance</InstanceDirPath>
                </WorkspaceLayout>
              </WorkspaceLayoutList>
              <EncodingList>
                <Encoding Id="1">
                  <Name>utf-8-no-bom</Name>
                </Encoding>
              </EncodingList>
              <NewlinesList>
                <Newlines Id="1">
                  <Name>lf</Name>
                </Newlines>
              </NewlinesList>
              <CanonicalOrderList>
                <CanonicalOrder Id="1">
                  <Name>name-ordinal</Name>
                </CanonicalOrder>
                <CanonicalOrder Id="2">
                  <Name>id-ordinal</Name>
                </CanonicalOrder>
                <CanonicalOrder Id="3">
                  <Name>id-first-then-name-ordinal</Name>
                </CanonicalOrder>
              </CanonicalOrderList>
              <EntityStorageList />
            </MetaWorkspace>
            """);

        File.WriteAllText(
            Path.Combine(root, "metadata", "model.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Model name="Mini">
              <EntityList>
                <Entity name="Warehouse" />
                <Entity name="Order">
                  <RelationshipList>
                    <Relationship entity="Warehouse" />
                  </RelationshipList>
                </Entity>
              </EntityList>
            </Model>
            """);

        File.WriteAllText(
            Path.Combine(root, "metadata", "instance", "Warehouse.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Mini>
              <WarehouseList>
                <Warehouse Id="WH-001" />
              </WarehouseList>
            </Mini>
            """);

        File.WriteAllText(
            Path.Combine(root, "metadata", "instance", "Order.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Mini>
              <OrderList>
                <Order Id="ORD-001" />
              </OrderList>
            </Mini>
            """);

        return root;
    }

    private static string CreateTempWorkspaceFromSamples()
    {
        return TestWorkspaceFactory.CreateTempWorkspaceFromCanonicalSample();
    }

    private static async Task<string> CreateTempCanonicalWorkspaceFromSamplesAsync()
    {
        return await TestWorkspaceFactory.CreateTempCanonicalWorkspaceFromCanonicalSampleAsync().ConfigureAwait(false);
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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

            var parent = Directory.GetParent(directory);
            if (parent == null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static string ExtractDiffWorkspacePath(string output)
    {
        var diffPathMatch = Regex.Match(output, @"(?m)^DiffWorkspace:\s*(.+)$");
        Assert.True(diffPathMatch.Success, $"Expected diff workspace path in output:{Environment.NewLine}{output}");
        return diffPathMatch.Groups[1].Value.Trim();
    }

    private static string ExtractUsageClause(string output)
    {
        var normalized = Regex.Replace(output, @"\s+", " ").Trim();
        var usageMatch = Regex.Match(normalized, @"Usage:\s+meta\s+.+?(?=\s+Options:|\s+Examples:|\s+Next:|$)");
        Assert.True(usageMatch.Success, $"Expected usage clause in output:{Environment.NewLine}{output}");
        return usageMatch.Value.Trim();
    }
}








