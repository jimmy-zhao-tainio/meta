using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;
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

        var preSaveDiagnostics = WorkspaceValidator.Validate(
            generated.Workspace.Model,
            generated.Workspace.Instance);
        Assert.False(preSaveDiagnostics.HasErrors, BuildDiagnosticsMessage(preSaveDiagnostics));

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"));
        var exportRoot = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"));
        var sqlOutA = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "sql-a");
        var sqlOutB = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "sql-b");
        var csOutA = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "cs-a");
        var csOutB = Path.Combine(Path.GetTempPath(), "metadata-fullcycle-tests", Guid.NewGuid().ToString("N"), "cs-b");

        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(generated.Workspace, tempRoot);
            var loaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var hashAfterLoad = loaded.Fingerprint;
            Assert.Equal(entityCount, loaded.Model.Entities.Count);
            Assert.Equal(generated.TotalRows, loaded.Instance.RecordsByEntity.Values.Sum(rows => rows.Count));

            var postLoadDiagnostics = WorkspaceValidator.Validate(
                loaded.Model,
                loaded.Instance);
            Assert.False(postLoadDiagnostics.HasErrors, BuildDiagnosticsMessage(postLoadDiagnostics));

            await XmlWorkspaceWriter.WriteNewAsync(loaded.State, exportRoot);
            var exportedLoaded = await XmlWorkspaceReader.OpenAsync(exportRoot);
            var exportedHash = exportedLoaded.Fingerprint;
            Assert.Equal(hashAfterLoad, exportedHash);

            var sqlManifestA = GenerationService.GenerateSql(loaded.State, sqlOutA);
            var sqlManifestB = GenerationService.GenerateSql(loaded.State, sqlOutB);
            AssertEquivalent(sqlManifestA, sqlManifestB);

            var csharpManifestA = GenerationService.GenerateCSharp(loaded.State, csOutA);
            var csharpManifestB = GenerationService.GenerateCSharp(loaded.State, csOutB);
            AssertEquivalent(csharpManifestA, csharpManifestB);

        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
            DeleteDirectoryIfExists(exportRoot);
            DeleteDirectoryIfExists(Path.GetDirectoryName(sqlOutA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(sqlOutB)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(csOutA)!);
            DeleteDirectoryIfExists(Path.GetDirectoryName(csOutB)!);
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
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = modelName,
            },
            new GenericInstance
            {
                ModelName = modelName,
            });

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

    private static void AssertEquivalent(GenerationManifest left, GenerationManifest right)
    {
        Assert.Equal(left.FileHashes.Count, right.FileHashes.Count);
        foreach (var file in left.FileHashes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(right.FileHashes.TryGetValue(file.Key, out var otherHash), $"Missing file in right manifest: {file.Key}.");
            Assert.Equal(file.Value, otherHash);
        }

        Assert.Equal(left.CombinedHash, right.CombinedHash);
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
        public required InMemoryWorkspace Workspace { get; init; }
        public int MaxDepth { get; set; }
        public int TotalRelationships { get; set; }
        public int TotalRows { get; set; }
        public int MinPropertyCountPerEntity { get; set; }
        public int MaxPropertyCountPerEntity { get; set; }
    }
}


