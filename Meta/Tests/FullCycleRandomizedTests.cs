using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Services;
using Xunit.Abstractions;

namespace Meta.Core.Tests;

public sealed class FullCycleRandomizedTests
{
    private readonly ITestOutputHelper output;

    public FullCycleRandomizedTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task FullCycle_100Entities_RandomizedGraphAndData_IsValidAndDeterministic()
    {
        var seed = ReadInt("Meta_FULLCYCLE_SEED", 20260213);
        var entityCount = 100;
        var generated = BuildRandomWorkspace(
            entityCount: entityCount,
            seed: seed,
            minAdditionalProperties: 2,
            maxAdditionalProperties: 10,
            maxRelationshipsPerEntity: 4,
            minRowsPerEntity: 1,
            maxRowsPerEntity: 30);

        output.WriteLine(
            $"seed={seed} entities={entityCount} maxDepth={generated.MaxDepth} relationships={generated.TotalRelationships} rows={generated.TotalRows} propsRange={generated.MinPropertyCountPerEntity}..{generated.MaxPropertyCountPerEntity}");

        Assert.Equal(entityCount, generated.Workspace.Model.Entities.Count);
        Assert.True(generated.MaxDepth >= 4, "Expected meaningful relationship depth.");
        Assert.True(generated.TotalRelationships > 0, "Expected randomized relationships.");
        Assert.True(generated.TotalRows >= entityCount, "Expected at least one row per entity.");
        Assert.True(generated.MinPropertyCountPerEntity < generated.MaxPropertyCountPerEntity,
            "Expected randomized property counts across entities.");

        var services = new ServiceCollection();
        var preSaveDiagnostics = services.ValidationService.Validate(generated.Workspace);
        Assert.False(preSaveDiagnostics.HasErrors, BuildDiagnosticsMessage(preSaveDiagnostics));

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"));
        var exportRoot = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"));
        var sqlOutA = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "sql-a");
        var sqlOutB = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "sql-b");
        var csOutA = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "cs-a");
        var csOutB = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "cs-b");
        var ssdtOut = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "ssdt");

        try
        {
            generated.Workspace.WorkspaceRootPath = tempRoot;
            generated.Workspace.MetadataRootPath = Path.Combine(tempRoot, "metadata");
            var hashBeforeSave = services.WorkspaceService.CalculateHash(generated.Workspace);

            await services.WorkspaceService.SaveAsync(generated.Workspace);
            var loaded = await services.WorkspaceService.LoadAsync(tempRoot);

            var hashAfterLoad = services.WorkspaceService.CalculateHash(loaded);
            Assert.Equal(hashBeforeSave, hashAfterLoad);
            Assert.Equal(entityCount, loaded.Model.Entities.Count);
            Assert.Equal(generated.TotalRows, loaded.Instance.RecordsByEntity.Values.Sum(rows => rows.Count));

            var postLoadDiagnostics = services.ValidationService.Validate(loaded);
            Assert.False(postLoadDiagnostics.HasErrors, BuildDiagnosticsMessage(postLoadDiagnostics));

            await services.ExportService.ExportXmlAsync(loaded, exportRoot);
            var exportedLoaded = await services.WorkspaceService.LoadAsync(exportRoot);
            var exportedHash = services.WorkspaceService.CalculateHash(exportedLoaded);
            Assert.Equal(hashAfterLoad, exportedHash);

            var sqlManifestA = GenerationService.GenerateSql(loaded, sqlOutA);
            var sqlManifestB = GenerationService.GenerateSql(loaded, sqlOutB);
            Assert.True(GenerationService.AreEquivalent(sqlManifestA, sqlManifestB, out var sqlMessage), sqlMessage);

            var csharpManifestA = GenerationService.GenerateCSharp(loaded, csOutA);
            var csharpManifestB = GenerationService.GenerateCSharp(loaded, csOutB);
            Assert.True(GenerationService.AreEquivalent(csharpManifestA, csharpManifestB, out var csharpMessage), csharpMessage);

            var ssdtManifest = GenerationService.GenerateSsdt(loaded, ssdtOut);
            Assert.Equal(4, ssdtManifest.FileHashes.Count);
            Assert.True(File.Exists(Path.Combine(ssdtOut, "Schema.sql")));
            Assert.True(File.Exists(Path.Combine(ssdtOut, "Data.sql")));
            Assert.True(File.Exists(Path.Combine(ssdtOut, "PostDeploy.sql")));
            Assert.True(File.Exists(Path.Combine(ssdtOut, "Metadata.sqlproj")));
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
            DeleteDirectoryIfExists(exportRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(sqlOutA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(sqlOutB)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(csOutA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(csOutB)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(ssdtOut)!);
        }
    }

    private static GeneratedWorkspace BuildRandomWorkspace(
        int entityCount,
        int seed,
        int minAdditionalProperties,
        int maxAdditionalProperties,
        int maxRelationshipsPerEntity,
        int minRowsPerEntity,
        int maxRowsPerEntity)
    {
        if (entityCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entityCount));
        }

        var random = new Random(seed);
        var modelName = "RandomModel_" + seed;
        var workspace = new Workspace
        {
            Model = new GenericModel
            {
                Name = modelName,
            },
            Instance = new GenericInstance
            {
                ModelName = modelName,
            },
        };

        var depthBucketCount = Math.Min(entityCount, random.Next(8, 20));
        var entitiesByDepth = Enumerable.Range(0, depthBucketCount)
            .Select(_ => new List<GenericEntity>())
            .ToList();
        var entityDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var orderedEntities = new List<GenericEntity>(entityCount);

        for (var index = 0; index < entityCount; index++)
        {
            var depth = index < depthBucketCount ? index : random.Next(0, depthBucketCount);
            var entity = new GenericEntity
            {
                Name = $"Entity{index:D4}",
            };

            var additionalCount = random.Next(minAdditionalProperties, maxAdditionalProperties + 1);
            for (var propertyIndex = 1; propertyIndex <= additionalCount; propertyIndex++)
            {
                var propertyName = $"P{propertyIndex:D2}";
                entity.Properties.Add(new GenericProperty
                {
                    Name = propertyName,
                    DataType = "string",
                    IsNullable = random.NextDouble() >= 0.35d,
                });
            }

            workspace.Model.Entities.Add(entity);
            orderedEntities.Add(entity);
            entitiesByDepth[depth].Add(entity);
            entityDepths[entity.Name] = depth;
        }

        var maxDepth = entitiesByDepth.FindLastIndex(bucket => bucket.Count > 0);
        foreach (var entity in orderedEntities)
        {
            var entityDepth = entityDepths[entity.Name];
            if (entityDepth <= 0)
            {
                continue;
            }

            var candidates = orderedEntities
                .Where(candidate => entityDepths[candidate.Name] < entityDepth)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var relationshipCount = random.Next(1, Math.Min(maxRelationshipsPerEntity, candidates.Count) + 1);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var relationIndex = 0; relationIndex < relationshipCount; relationIndex++)
            {
                var target = candidates[random.Next(candidates.Count)];
                if (!used.Add(target.Name))
                {
                    continue;
                }

                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = target.Name,
                });
            }
        }

        var entitiesForData = orderedEntities
            .OrderBy(entity => entityDepths[entity.Name])
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalRows = 0;
        foreach (var entity in entitiesForData)
        {
            var rowCount = random.Next(minRowsPerEntity, maxRowsPerEntity + 1);
            totalRows += rowCount;

            var rows = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
            rows.Clear();

            for (var rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                var id = rowIndex.ToString();
                var record = new GenericRecord
                {
                    Id = id,
                };

                foreach (var property in entity.Properties.Where(property =>
                             !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.IsNullable && random.NextDouble() < 0.25d)
                    {
                        continue;
                    }

                    record.Values[property.Name] = $"{property.Name}_{rowIndex:D4}_{random.Next(1000, 9999)}";
                }

                foreach (var relationship in entity.Relationships)
                {
                    var targetRows = workspace.Instance.GetOrCreateEntityRecords(relationship.Entity);
                    if (targetRows.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Target entity '{relationship.Entity}' has no rows for relationship assignment.");
                    }

                    var target = targetRows[random.Next(targetRows.Count)];
                    record.RelationshipIds[relationship.GetColumnName()] = target.Id;
                }

                rows.Add(record);
            }
        }

        var propertyCounts = workspace.Model.Entities.Select(entity => entity.Properties.Count).ToList();
        var totalRelationships = workspace.Model.Entities.Sum(entity => entity.Relationships.Count);

        return new GeneratedWorkspace
        {
            Workspace = workspace,
            MaxDepth = maxDepth,
            TotalRelationships = totalRelationships,
            TotalRows = totalRows,
            MinPropertyCountPerEntity = propertyCounts.Min(),
            MaxPropertyCountPerEntity = propertyCounts.Max(),
        };
    }

    private static string BuildDiagnosticsMessage(WorkspaceDiagnostics diagnostics)
    {
        var preview = diagnostics.Issues
            .Take(8)
            .Select(issue => $"{issue.Severity}:{issue.Code}:{issue.Location}:{issue.Message}");
        return string.Join(" | ", preview);
    }

    private static int ReadInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class GeneratedWorkspace
    {
        public Workspace Workspace { get; set; } = new();
        public int MaxDepth { get; set; }
        public int TotalRelationships { get; set; }
        public int TotalRows { get; set; }
        public int MinPropertyCountPerEntity { get; set; }
        public int MaxPropertyCountPerEntity { get; set; }
    }
}


