using System.Diagnostics;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaSchema.Core;
using MetaType.Core;
using MetaWeave.Core;

namespace MetaWeave.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsCheckCommand()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MetaWeave CLI", result.Output);
        Assert.Contains("check", result.Output);
    }

    [Fact]
    public void Check_Help_ShowsWorkspaceOption()
    {
        var result = RunCli("check --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace <path>", result.Output);
    }

    [Fact]
    public async Task Check_Resolves_MetaSchema_Field_TypeId_To_MetaType_Type_Id()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-tests", Guid.NewGuid().ToString("N"));
        var metaTypePath = Path.Combine(root, "MetaType");
        var metaSchemaPath = Path.Combine(root, "MetaSchema");
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var workspaceService = new WorkspaceService();

            var metaType = MetaTypeWorkspaces.CreateMetaTypeWorkspace(metaTypePath);
            await workspaceService.SaveAsync(metaType);

            var metaSchema = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(metaSchemaPath);
            metaSchema.Instance.GetOrCreateEntityRecords("System").Add(new GenericRecord
            {
                Id = "sqlserver:system:test",
                Values = { ["Name"] = "TestSystem" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Schema").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo",
                Values = { ["Name"] = "dbo" },
                RelationshipIds = { ["SystemId"] = "sqlserver:system:test" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Table").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube",
                Values = { ["Name"] = "Cube", ["ObjectType"] = "Table" },
                RelationshipIds = { ["SchemaId"] = "sqlserver:test:schema:dbo" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Field").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube:field:CubeName",
                Values =
                {
                    ["Name"] = "CubeName",
                    ["TypeId"] = "sqlserver:type:nvarchar",
                    ["Ordinal"] = "1",
                    ["IsNullable"] = "false"
                },
                RelationshipIds = { ["TableId"] = "sqlserver:test:schema:dbo:table:Cube" }
            });
            await workspaceService.SaveAsync(metaSchema);

            var weave = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(metaWeavePath);
            weave.Instance.GetOrCreateEntityRecords("ModelRef").Add(new GenericRecord
            {
                Id = "1",
                Values =
                {
                    ["Alias"] = "MetaSchema",
                    ["WorkspacePath"] = "..\\MetaSchema",
                    ["ModelName"] = "MetaSchema"
                }
            });
            weave.Instance.GetOrCreateEntityRecords("ModelRef").Add(new GenericRecord
            {
                Id = "2",
                Values =
                {
                    ["Alias"] = "MetaType",
                    ["WorkspacePath"] = "..\\MetaType",
                    ["ModelName"] = "MetaType"
                }
            });
            weave.Instance.GetOrCreateEntityRecords("PropertyBinding").Add(new GenericRecord
            {
                Id = "1",
                Values =
                {
                    ["Name"] = "MetaSchema.Field.TypeId -> MetaType.Type.Id",
                    ["SourceEntity"] = "Field",
                    ["SourceProperty"] = "TypeId",
                    ["TargetEntity"] = "Type",
                    ["TargetProperty"] = "Id"
                },
                RelationshipIds =
                {
                    ["SourceModelId"] = "1",
                    ["TargetModelId"] = "2"
                }
            });
            await workspaceService.SaveAsync(weave);

            var result = RunCli($"check --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: weave check", result.Output);
            Assert.Contains("Bindings: 1", result.Output);
            Assert.Contains("Errors: 0", result.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.Combine(repoRoot, "MetaWeave.Cli", "MetaWeave.Cli.csproj")}\" -- {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-weave CLI process.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
