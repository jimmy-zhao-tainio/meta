using System;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class GraphStatsServiceTests
{
    [Fact]
    public void Compute_DagGraph_ReturnsExpectedMetrics()
    {
        var model = new GenericModel
        {
            Name = "GraphModel",
        };

        model.Entities.Add(Entity("A", "B", "C"));
        model.Entities.Add(Entity("B", "C"));
        model.Entities.Add(Entity("C"));
        model.Entities.Add(Entity("D"));

        var stats = GraphStatsService.Compute(model, topN: 2, cycleSampleLimit: 2);

        Assert.Equal(4, stats.NodeCount);
        Assert.Equal(3, stats.EdgeCount);
        Assert.Equal(3, stats.UniqueEdgeCount);
        Assert.Equal(0, stats.DuplicateEdgeCount);
        Assert.Equal(0, stats.MissingTargetEdgeCount);
        Assert.Equal(2, stats.WeaklyConnectedComponents);
        Assert.Equal(2, stats.RootCount);
        Assert.Equal(2, stats.SinkCount);
        Assert.Equal(1, stats.IsolatedCount);
        Assert.False(stats.HasCycles);
        Assert.Equal(0, stats.CycleCount);
        Assert.Equal(2, stats.DagMaxDepth);
        Assert.Empty(stats.CycleSamples);
        Assert.Equal("A", stats.TopOutDegree[0].Entity);
        Assert.Equal(2, stats.TopOutDegree[0].Degree);
        Assert.Equal("C", stats.TopInDegree[0].Entity);
        Assert.Equal(2, stats.TopInDegree[0].Degree);
    }

    [Fact]
    public void Compute_CycleAndDataQualityIssues_AreReported()
    {
        var model = new GenericModel
        {
            Name = "GraphModel",
        };

        model.Entities.Add(Entity("A", "B", "B"));
        model.Entities.Add(Entity("B", "A"));
        model.Entities.Add(Entity("C", "MissingX"));

        var stats = GraphStatsService.Compute(model, topN: 3, cycleSampleLimit: 3);

        Assert.Equal(3, stats.NodeCount);
        Assert.Equal(4, stats.EdgeCount);
        Assert.Equal(3, stats.UniqueEdgeCount);
        Assert.Equal(1, stats.DuplicateEdgeCount);
        Assert.Equal(1, stats.MissingTargetEdgeCount);
        Assert.Equal(2, stats.WeaklyConnectedComponents);
        Assert.True(stats.HasCycles);
        Assert.Equal(1, stats.CycleCount);
        Assert.Null(stats.DagMaxDepth);
        Assert.NotEmpty(stats.CycleSamples);
        Assert.Contains("A", stats.CycleSamples[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("B", stats.CycleSamples[0], StringComparison.OrdinalIgnoreCase);
    }

    private static GenericEntity Entity(string name, params string[] relationships)
    {
        var entity = new GenericEntity
        {
            Name = name,
        };

        foreach (var relationship in relationships)
        {
            entity.Relationships.Add(new GenericRelationship
            {
                Entity = relationship,
            });
        }

        return entity;
    }
}


