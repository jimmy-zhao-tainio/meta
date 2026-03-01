using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.Adapters;
using Meta.Core.Domain;
using Xunit.Abstractions;

namespace Meta.Core.Tests;

public sealed class LargeWorkspacePerformanceTests
{
    private readonly ITestOutputHelper output;

    public LargeWorkspacePerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task SaveAndLoad_100kRows_WithinConfiguredBudgets()
    {
        var rowCount = ReadInt("Meta_PERF_ROWS", 100_000);
        var maxSaveMs = ReadInt("Meta_PERF_MAX_SAVE_MS", 45_000);
        var maxLoadMs = ReadInt("Meta_PERF_MAX_LOAD_MS", 45_000);
        var maxAllocatedMb = ReadInt("Meta_PERF_MAX_ALLOCATED_MB", 2_048);

        await RunScenarioAsync(
            scenarioName: "100k-default",
            rowCount: rowCount,
            maxSaveMs: maxSaveMs,
            maxLoadMs: maxLoadMs,
            maxAllocatedMb: maxAllocatedMb);
    }

    [Fact]
    public async Task SaveAndLoad_1MRows_WithinConfiguredBudgets_WhenEnabled()
    {
        if (!ReadBool("Meta_ENABLE_1M_PERF_TEST", defaultValue: false))
        {
            output.WriteLine("Skipping 1M-row perf test. Set Meta_ENABLE_1M_PERF_TEST=1 to enable.");
            return;
        }

        var maxSaveMs = ReadInt("Meta_PERF_1M_MAX_SAVE_MS", 300_000);
        var maxLoadMs = ReadInt("Meta_PERF_1M_MAX_LOAD_MS", 300_000);
        var maxAllocatedMb = ReadInt("Meta_PERF_1M_MAX_ALLOCATED_MB", 6_144);

        await RunScenarioAsync(
            scenarioName: "1m-opt-in",
            rowCount: 1_000_000,
            maxSaveMs: maxSaveMs,
            maxLoadMs: maxLoadMs,
            maxAllocatedMb: maxAllocatedMb);
    }

    private async Task RunScenarioAsync(
        string scenarioName,
        int rowCount,
        int maxSaveMs,
        int maxLoadMs,
        int maxAllocatedMb)
    {
        var services = new ServiceCollection();
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync(services);
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));

        try
        {
            MaterializeLargeCubeDataset(workspace, rowCount);
            workspace.WorkspaceRootPath = tempRoot;
            workspace.MetadataRootPath = Path.Combine(tempRoot, "metadata");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBeforeSave = GC.GetTotalAllocatedBytes(precise: true);
            var saveWatch = Stopwatch.StartNew();
            await services.WorkspaceService.SaveAsync(workspace);
            saveWatch.Stop();
            var allocatedAfterSave = GC.GetTotalAllocatedBytes(precise: true);
            var saveAllocatedMb = BytesToMb(allocatedAfterSave - allocatedBeforeSave);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBeforeLoad = GC.GetTotalAllocatedBytes(precise: true);
            var loadWatch = Stopwatch.StartNew();
            var reloaded = await services.WorkspaceService.LoadAsync(tempRoot);
            loadWatch.Stop();
            var allocatedAfterLoad = GC.GetTotalAllocatedBytes(precise: true);
            var loadAllocatedMb = BytesToMb(allocatedAfterLoad - allocatedBeforeLoad);

            var totalAllocatedMb = saveAllocatedMb + loadAllocatedMb;

            var actualCubeRows = reloaded.Instance
                .GetOrCreateEntityRecords("Cube")
                .Count;
            Assert.Equal(rowCount, actualCubeRows);

            output.WriteLine(
                $"scenario={scenarioName} rows={rowCount} saveMs={saveWatch.ElapsedMilliseconds} loadMs={loadWatch.ElapsedMilliseconds} allocatedMb={totalAllocatedMb:F2}");
            output.WriteLine(
                $"budgets saveMs<={maxSaveMs} loadMs<={maxLoadMs} allocatedMb<={maxAllocatedMb}");

            Assert.True(
                saveWatch.ElapsedMilliseconds <= maxSaveMs,
                $"Save exceeded budget. scenario={scenarioName}, rows={rowCount}, actualMs={saveWatch.ElapsedMilliseconds}, budgetMs={maxSaveMs}.");
            Assert.True(
                loadWatch.ElapsedMilliseconds <= maxLoadMs,
                $"Load exceeded budget. scenario={scenarioName}, rows={rowCount}, actualMs={loadWatch.ElapsedMilliseconds}, budgetMs={maxLoadMs}.");
            Assert.True(
                totalAllocatedMb <= maxAllocatedMb,
                $"Allocation exceeded budget. scenario={scenarioName}, rows={rowCount}, actualMb={totalAllocatedMb:F2}, budgetMb={maxAllocatedMb}.");
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void MaterializeLargeCubeDataset(Workspace workspace, int rowCount)
    {
        var cubeEntity = workspace.Model.FindEntity("Cube");
        if (cubeEntity == null)
        {
            throw new InvalidOperationException("Sample model must contain entity 'Cube'.");
        }

        var requiredPropertyNames = cubeEntity.Properties
            .Where(property => !property.IsNullable)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requiredPropertyNames.Contains("CubeName"))
        {
            throw new InvalidOperationException("Entity 'Cube' must require CubeName for this test.");
        }

        var cubeRecords = workspace.Instance.GetOrCreateEntityRecords("Cube");
        cubeRecords.Clear();
        cubeRecords.Capacity = rowCount;

        for (var i = 1; i <= rowCount; i++)
        {
            var id = i.ToString();
            var record = new GenericRecord
            {
                Id = id,
            };
            record.Values["CubeName"] = "Cube_" + id;
            record.Values["Purpose"] = "Purpose_" + id;
            record.Values["RefreshMode"] = (i % 2 == 0) ? "Scheduled" : "Manual";
            cubeRecords.Add(record);
        }
    }

    private static int ReadInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ReadBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static double BytesToMb(long bytes)
    {
        return bytes / 1024d / 1024d;
    }
}


