using System.Linq;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Core.Tests;

public sealed class WorkspaceValidatorTests
{
    [Fact]
    public void Validate_InvalidIdentifiers_AreErrors()
    {
        var workspace = BuildWorkspace(
            modelName: "Bad Name",
            entityName: "Entity$",
            propertyName: "Property-Name");

        var diagnostics = Validate(workspace);

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

        var diagnostics = Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "model.entity.collision");
        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Validate_PropertyRelationshipNameCollision_IsError()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        var cube = new GenericEntity { Name = "Cube" };
        workspace.Model.Entities.Add(cube);

        var measure = new GenericEntity { Name = "Measure" };
        measure.Properties.Add(new GenericProperty { Name = "CubeId", IsNullable = false });
        measure.Relationships.Add(new GenericRelationship { Entity = "Cube" });
        workspace.Model.Entities.Add(measure);

        var diagnostics = Validate(workspace);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "entity.member.collision");
    }

    [Fact]
    public void Validate_RelationshipCycle_IsError()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        var entityA = new GenericEntity { Name = "EntityA" };
        entityA.Relationships.Add(new GenericRelationship { Entity = "EntityB" });

        var entityB = new GenericEntity { Name = "EntityB" };
        entityB.Relationships.Add(new GenericRelationship { Entity = "EntityA" });

        workspace.Model.Entities.Add(entityA);
        workspace.Model.Entities.Add(entityB);

        var diagnostics = Validate(workspace);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "relationship.cycle");
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Validate_OptionalSelfRelationship_DoesNotCreateModelCycle()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        var command = new GenericEntity { Name = "Command" };
        command.Relationships.Add(new GenericRelationship
        {
            Entity = "Command",
            Role = "ParentCommand",
            IsNullable = true,
        });
        workspace.Model.Entities.Add(command);

        var diagnostics = Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "relationship.cycle");
        Assert.False(diagnostics.HasErrors);
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

        var diagnostics = Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues,
            issue => issue.Code == "instance.required.missing" && issue.Location.EndsWith("/Purpose"));
    }

    [Fact]
    public void Validate_NullableRelationship_AllowsMissingValue()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        workspace.Model.Entities.Add(new GenericEntity { Name = "Attribute" });

        var measure = new GenericEntity { Name = "Measure" };
        measure.Relationships.Add(new GenericRelationship
        {
            Entity = "Attribute",
            Role = "SemiAdditiveByAttribute",
            IsNullable = true,
        });
        workspace.Model.Entities.Add(measure);

        workspace.Instance.GetOrCreateEntityRecords("Measure").Add(new GenericRecord { Id = "SalesAmount" });

        var diagnostics = Validate(workspace);

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "instance.relationship.missing");
        Assert.False(diagnostics.HasErrors);
    }

    private static InMemoryWorkspace BuildWorkspace(string modelName, string entityName, string propertyName)
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = modelName,
            },
            new GenericInstance
            {
                ModelName = modelName,
            });

        var entity = new GenericEntity
        {
            Name = entityName,
        };
        entity.Properties.Add(new GenericProperty
        {
            Name = propertyName,
            IsNullable = false,
        });

        workspace.Model.Entities.Add(entity);
        return workspace;
    }

    private static WorkspaceDiagnostics Validate(InMemoryWorkspace workspace) =>
        WorkspaceValidator.Validate(workspace.Model, workspace.Instance);
}


