using System;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class BulkRelationshipResolverTests
{
    [Fact]
    public void ResolveRelationshipIds_PreservesOpaqueRelationshipIds()
    {
        var workspace = BuildWorkspace();
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.BulkUpsertRows,
            EntityName = "Measure",
            RowPatches =
            {
                new RowPatch
                {
                    Id = "1",
                    Values = { ["MeasureName"] = "Orders" },
                    RelationshipIds = { ["CubeId"] = "2" },
                },
                new RowPatch
                {
                    Id = "2",
                    Values = { ["MeasureName"] = "Revenue" },
                    RelationshipIds = { ["CubeId"] = "Cube#1" },
                },
            },
        };

        BulkRelationshipResolver.ResolveRelationshipIds(workspace, operation);

        Assert.Equal("2", operation.RowPatches[0].RelationshipIds["CubeId"]);
        Assert.Equal("Cube#1", operation.RowPatches[1].RelationshipIds["CubeId"]);
    }

    private static Workspace BuildWorkspace()
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

        var cubeRows = workspace.Instance.GetOrCreateEntityRecords("Cube");
        cubeRows.Add(new GenericRecord
        {
            Id = "1",
            Values =
            {
                ["CubeName"] = "Sales",
            },
        });
        cubeRows.Add(new GenericRecord
        {
            Id = "2",
            Values =
            {
                ["CubeName"] = "Finance",
            },
        });

        return workspace;
    }
}


