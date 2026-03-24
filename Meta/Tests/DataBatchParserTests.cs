using System;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class DataBatchParserTests
{
    [Fact]
    public void ParseBulkUpsert_ParsesPropertiesAndRelationships()
    {
        var entity = BuildMeasureEntity();
        var input = "Id\tMeasureName\tCubeId\n1\tOrders\t10\n2\tRevenue\t11";

        var operation = DataBatchParser.ParseBulkUpsert("Measure", entity, input);

        Assert.Equal(WorkspaceOpTypes.BulkUpsertRows, operation.Type);
        Assert.Equal("Measure", operation.EntityName);
        Assert.Equal(2, operation.RowPatches.Count);
        Assert.Equal("1", operation.RowPatches[0].Id);
        Assert.Equal("Orders", operation.RowPatches[0].Values["MeasureName"]);
        Assert.Equal("10", operation.RowPatches[0].RelationshipIds["CubeId"]);
        Assert.Equal("2", operation.RowPatches[1].Id);
        Assert.Equal("Revenue", operation.RowPatches[1].Values["MeasureName"]);
        Assert.Equal("11", operation.RowPatches[1].RelationshipIds["CubeId"]);
    }

    [Fact]
    public void ParseBulkUpsert_ThrowsOnUnknownColumn()
    {
        var entity = BuildMeasureEntity();
        var input = "Id,MeasureName,UnknownColumn\n1,Orders,bad";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataBatchParser.ParseBulkUpsert("Measure", entity, input));
        Assert.Contains("UnknownColumn", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseDeleteRows_RemovesHeaderAndDeduplicates()
    {
        var operation = DataBatchParser.ParseDeleteRows("Measure", "Id\n3\n1\n2\n3");

        Assert.Equal(WorkspaceOpTypes.DeleteRows, operation.Type);
        Assert.Equal("Measure", operation.EntityName);
        Assert.Equal(3, operation.Ids.Count);
        Assert.Equal(["1", "2", "3"], operation.Ids.ToArray());
    }

    private static GenericEntity BuildMeasureEntity()
    {
        var entity = new GenericEntity
        {
            Name = "Measure",
        };

        entity.Properties.Add(new GenericProperty
        {
            Name = "MeasureName",
            DataType = "string",
            IsNullable = false,
        });
        entity.Relationships.Add(new GenericRelationship
        {
            Entity = "Cube",
        });

        return entity;
    }
}

