using System;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class NormalizationServiceTests
{
    [Fact]
    public void BuildNormalizeOperations_DropUnknown_RemovesUnknownValuesAndRelationships()
    {
        var workspace = BuildWorkspaceWithUnknowns();

        var operations = NormalizationService.BuildNormalizeOperations(workspace, new NormalizeOptions
        {
            EntityName = "Measure",
            DropUnknown = true,
        });

        var operation = Assert.Single(operations);
        Assert.Equal(WorkspaceOpTypes.BulkUpsertRows, operation.Type);
        Assert.Equal("Measure", operation.EntityName);
        var patch = Assert.Single(operation.RowPatches);
        Assert.Equal("1", patch.Id);
        Assert.True(patch.ReplaceExisting);
        Assert.True(patch.Values.ContainsKey("MeasureName"));
        Assert.False(patch.Values.ContainsKey("LegacyField"));
        Assert.True(patch.RelationshipIds.ContainsKey("CubeId"));
        Assert.False(patch.RelationshipIds.ContainsKey("LegacyLink"));
    }

    [Fact]
    public void BuildNormalizeOperations_NoChanges_ReturnsEmpty()
    {
        var workspace = BuildWorkspaceNormalized();

        var operations = NormalizationService.BuildNormalizeOperations(workspace, new NormalizeOptions
        {
            EntityName = "Cube",
            DropUnknown = true,
        });

        Assert.Empty(operations);
    }

    [Fact]
    public void BuildNormalizeOperations_UnknownEntity_Throws()
    {
        var workspace = BuildWorkspaceNormalized();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NormalizationService.BuildNormalizeOperations(workspace, new NormalizeOptions
            {
                EntityName = "DoesNotExist",
                DropUnknown = false,
            }));

        Assert.Contains("DoesNotExist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Workspace BuildWorkspaceWithUnknowns()
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = "memory",
            MetadataRootPath = "memory/metadata",
            Model = new GenericModel
            {
                Name = "TestModel",
            },
            Instance = new GenericInstance
            {
                ModelName = "TestModel",
            },
        };

        var cube = new GenericEntity
        {
            Name = "Cube",
        };
        cube.Properties.Add(new GenericProperty { Name = "CubeName", DataType = "string", IsNullable = false });
        workspace.Model.Entities.Add(cube);

        var measure = new GenericEntity
        {
            Name = "Measure",
        };
        measure.Properties.Add(new GenericProperty { Name = "MeasureName", DataType = "string", IsNullable = false });
        measure.Relationships.Add(new GenericRelationship { Entity = "Cube" });
        workspace.Model.Entities.Add(measure);

        workspace.Instance.GetOrCreateEntityRecords("Cube").Add(new GenericRecord
        {
            Id = "10",
            Values =
            {
                ["CubeName"] = "Sales",
            },
        });

        workspace.Instance.GetOrCreateEntityRecords("Measure").Add(new GenericRecord
        {
            Id = "1",
            Values =
            {
                ["MeasureName"] = "Orders",
                ["LegacyField"] = "deprecated",
            },
            RelationshipIds =
            {
                ["CubeId"] = "10",
                ["LegacyLink"] = "xyz",
            },
        });

        return workspace;
    }

    private static Workspace BuildWorkspaceNormalized()
    {
        var workspace = BuildWorkspaceWithUnknowns();
        var measure = workspace.Instance.RecordsByEntity["Measure"].Single();
        measure.Values.Remove("LegacyField");
        measure.RelationshipIds.Remove("LegacyLink");
        return workspace;
    }
}


