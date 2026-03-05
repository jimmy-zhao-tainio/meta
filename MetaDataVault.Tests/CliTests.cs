using System.Diagnostics;
using Meta.Core.Services;
using MetaSchema.Core;

namespace MetaDataVault.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsFromMetaSchemaCommand()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MetaDataVault CLI", result.Output);
        Assert.Contains("from-metaschema", result.Output);
    }

    [Fact]
    public void FromMetaSchema_Help_ShowsRequiredOptions()
    {
        var result = RunCli("from-metaschema --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--source-workspace <path>", result.Output);
        Assert.Contains("--new-workspace <path>", result.Output);
    }

    [Fact]
    public async Task FromMetaSchema_CreatesRawDataVaultWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadatavault-tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "MetaSchemaSource");
        var targetPath = Path.Combine(root, "RawDataVault");

        try
        {
            Directory.CreateDirectory(sourcePath);
            var source = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(sourcePath);
            SeedMetaSchema(source);
            await new WorkspaceService().SaveAsync(source);

            var result = RunCli($"from-metaschema --source-workspace \"{sourcePath}\" --new-workspace \"{targetPath}\"");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: raw datavault generated from metaschema", result.Output);
            Assert.True(File.Exists(Path.Combine(targetPath, "workspace.xml")));
            Assert.True(File.Exists(Path.Combine(targetPath, "metadata", "model.xml")));

            var hubXml = File.ReadAllText(Path.Combine(targetPath, "metadata", "instance", "RawHub.xml"));
            Assert.Contains("<RawHub ", hubXml);
            Assert.Contains("<Name>Order</Name>", hubXml);

            var satXml = File.ReadAllText(Path.Combine(targetPath, "metadata", "instance", "RawSatellite.xml"));
            Assert.Contains("<RawSatellite ", satXml);
            Assert.Contains("<Name>OrderSat</Name>", satXml);

            var linkXml = File.ReadAllText(Path.Combine(targetPath, "metadata", "instance", "RawLink.xml"));
            Assert.Contains("<RawLink ", linkXml);
            Assert.Contains("<Name>FK_Order_Customer</Name>", linkXml);

            var linkEndXml = File.ReadAllText(Path.Combine(targetPath, "metadata", "instance", "RawLinkEnd.xml"));
            Assert.Contains("<RawLinkEnd ", linkEndXml);
            Assert.Contains("<RoleName>Source</RoleName>", linkEndXml);
            Assert.Contains("<RoleName>Target</RoleName>", linkEndXml);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static void SeedMetaSchema(Meta.Core.Domain.Workspace workspace)
    {
        var systems = workspace.Instance.GetOrCreateEntityRecords("System");
        systems.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "1",
            SourceShardFileName = "System.xml",
            Values =
            {
                ["Name"] = "Sales"
            }
        });

        var schemas = workspace.Instance.GetOrCreateEntityRecords("Schema");
        schemas.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "1",
            SourceShardFileName = "Schema.xml",
            Values =
            {
                ["Name"] = "dbo"
            },
            RelationshipIds =
            {
                ["SystemId"] = "1"
            }
        });

        var tables = workspace.Instance.GetOrCreateEntityRecords("Table");
        tables.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "1",
            SourceShardFileName = "Table.xml",
            Values =
            {
                ["Name"] = "Order"
            },
            RelationshipIds =
            {
                ["SchemaId"] = "1"
            }
        });
        tables.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "2",
            SourceShardFileName = "Table.xml",
            Values =
            {
                ["Name"] = "Customer"
            },
            RelationshipIds =
            {
                ["SchemaId"] = "1"
            }
        });

        var fields = workspace.Instance.GetOrCreateEntityRecords("Field");
        fields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "1",
            SourceShardFileName = "Field.xml",
            Values =
            {
                ["Name"] = "OrderId",
                ["TypeId"] = "sqlserver:type:int",
                ["Ordinal"] = "1"
            },
            RelationshipIds =
            {
                ["TableId"] = "1"
            }
        });
        fields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "2",
            SourceShardFileName = "Field.xml",
            Values =
            {
                ["Name"] = "OrderNumber",
                ["TypeId"] = "sqlserver:type:nvarchar",
                ["Ordinal"] = "2"
            },
            RelationshipIds =
            {
                ["TableId"] = "1"
            }
        });
        fields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "3",
            SourceShardFileName = "Field.xml",
            Values =
            {
                ["Name"] = "CustomerId",
                ["TypeId"] = "sqlserver:type:int",
                ["Ordinal"] = "3"
            },
            RelationshipIds =
            {
                ["TableId"] = "1"
            }
        });
        fields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "4",
            SourceShardFileName = "Field.xml",
            Values =
            {
                ["Name"] = "CustomerId",
                ["TypeId"] = "sqlserver:type:int",
                ["Ordinal"] = "1"
            },
            RelationshipIds =
            {
                ["TableId"] = "2"
            }
        });
        fields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "5",
            SourceShardFileName = "Field.xml",
            Values =
            {
                ["Name"] = "CustomerName",
                ["TypeId"] = "sqlserver:type:nvarchar",
                ["Ordinal"] = "2"
            },
            RelationshipIds =
            {
                ["TableId"] = "2"
            }
        });

        var tableRelationships = workspace.Instance.GetOrCreateEntityRecords("TableRelationship");
        tableRelationships.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "rel:1",
            SourceShardFileName = "TableRelationship.xml",
            Values =
            {
                ["Name"] = "FK_Order_Customer",
                ["TargetSchemaName"] = "dbo",
                ["TargetTableName"] = "Customer"
            },
            RelationshipIds =
            {
                ["SourceTableId"] = "1"
            }
        });

        var tableRelationshipFields = workspace.Instance.GetOrCreateEntityRecords("TableRelationshipField");
        tableRelationshipFields.Add(new Meta.Core.Domain.GenericRecord
        {
            Id = "relf:1",
            SourceShardFileName = "TableRelationshipField.xml",
            Values =
            {
                ["Ordinal"] = "1",
                ["SourceFieldName"] = "CustomerId",
                ["TargetFieldName"] = "CustomerId"
            },
            RelationshipIds =
            {
                ["TableRelationshipId"] = "rel:1",
                ["SourceFieldId"] = "3"
            }
        });
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.Combine(repoRoot, "MetaDataVault.Cli", "MetaDataVault.Cli.csproj")}\" -- {arguments}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-datavault CLI process.");
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
