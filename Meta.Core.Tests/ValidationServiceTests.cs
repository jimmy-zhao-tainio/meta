using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class ValidationServiceTests
{
    [Fact]
    public void Validate_InvalidIdentifiers_AreErrors()
    {
        var workspace = BuildWorkspace(
            modelName: "Bad Name",
            entityName: "Entity$",
            propertyName: "Property-Name");

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "model.name.invalid");
        Assert.Contains(diagnostics.Issues, issue => issue.Code == "entity.name.invalid");
        Assert.Contains(diagnostics.Issues, issue => issue.Code == "property.name.invalid");
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Validate_ModelAndEntityNameCollision_IsAllowed()
    {
        var workspace = BuildWorkspace(
            modelName: "Cube",
            entityName: "Cube",
            propertyName: "CubeName");

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "model.entity.collision");
        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Validate_PropertyRelationshipNameCollision_IsError()
    {
        var workspace = new Workspace
        {
            Model = new GenericModel
            {
                Name = "MetadataModel",
            },
            Instance = new GenericInstance
            {
                ModelName = "MetadataModel",
            },
        };

        var cube = new GenericEntity { Name = "Cube" };
        workspace.Model.Entities.Add(cube);

        var measure = new GenericEntity { Name = "Measure" };
        measure.Properties.Add(new GenericProperty { Name = "CubeId", DataType = "string", IsNullable = false });
        measure.Relationships.Add(new GenericRelationship { Entity = "Cube" });
        workspace.Model.Entities.Add(measure);

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "entity.member.collision");
    }

    [Fact]
    public void Validate_RelationshipCycle_IsError()
    {
        var workspace = new Workspace
        {
            Model = new GenericModel
            {
                Name = "MetadataModel",
            },
            Instance = new GenericInstance
            {
                ModelName = "MetadataModel",
            },
        };

        var entityA = new GenericEntity { Name = "EntityA" };
        entityA.Relationships.Add(new GenericRelationship { Entity = "EntityB" });

        var entityB = new GenericEntity { Name = "EntityB" };
        entityB.Relationships.Add(new GenericRelationship { Entity = "EntityA" });

        workspace.Model.Entities.Add(entityA);
        workspace.Model.Entities.Add(entityB);

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "relationship.cycle");
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Validate_RequiredStringProperty_AllowsExplicitEmptyValue()
    {
        var workspace = BuildWorkspace(
            modelName: "MetadataModel",
            entityName: "Cube",
            propertyName: "Purpose");
        var record = new GenericRecord { Id = "1" };
        record.Values["Purpose"] = string.Empty;
        workspace.Instance.GetOrCreateEntityRecords("Cube").Add(record);

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues,
            issue => issue.Code == "instance.required.missing" && issue.Location.EndsWith("/Purpose"));
    }

    [Fact]
    public void Validate_RequiredNonStringProperty_RejectsExplicitEmptyValue()
    {
        var workspace = BuildWorkspace(
            modelName: "MetadataModel",
            entityName: "Cube",
            propertyName: "Rank");
        workspace.Model.Entities[0].Properties[0].DataType = "int";
        var record = new GenericRecord { Id = "1" };
        record.Values["Rank"] = string.Empty;
        workspace.Instance.GetOrCreateEntityRecords("Cube").Add(record);

        var diagnostics = new ValidationService().Validate(workspace);

        Assert.Contains(diagnostics.Issues,
            issue => issue.Code == "instance.property.parse" && issue.Location.EndsWith("/Rank"));
        Assert.DoesNotContain(diagnostics.Issues,
            issue => issue.Code == "instance.required.missing" && issue.Location.EndsWith("/Rank"));
    }

    private static Workspace BuildWorkspace(string modelName, string entityName, string propertyName)
    {
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

        var entity = new GenericEntity
        {
            Name = entityName,
        };
        entity.Properties.Add(new GenericProperty
        {
            Name = propertyName,
            DataType = "string",
            IsNullable = false,
        });

        workspace.Model.Entities.Add(entity);
        return workspace;
    }
}


