using System;
using System.Linq;
using Meta.Adapters;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Tests;

public sealed class OperationServiceTests
{
    [Fact]
    public void OperationService_RenameProperty_SupportsUndoAndRedo()
    {
        var services = new ServiceCollection();
        var workspace = BuildWorkspace();

        services.OperationService.Execute(workspace, new WorkspaceOp
        {
            Type = WorkspaceOpTypes.RenameProperty,
            EntityName = "Thing",
            PropertyName = "Name",
            NewPropertyName = "DisplayName",
        });

        var entity = workspace.Model.FindEntity("Thing");
        Assert.NotNull(entity);
        Assert.Contains(entity!.Properties, property => string.Equals(property.Name, "DisplayName", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entity.Properties, property => string.Equals(property.Name, "Name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alpha", workspace.Instance.RecordsByEntity["Thing"][0].Values["DisplayName"]);

        services.OperationService.Undo(workspace);
        entity = workspace.Model.FindEntity("Thing");
        Assert.NotNull(entity);
        Assert.Contains(entity!.Properties, property => string.Equals(property.Name, "Name", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entity.Properties, property => string.Equals(property.Name, "DisplayName", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Alpha", workspace.Instance.RecordsByEntity["Thing"][0].Values["Name"]);

        services.OperationService.Redo(workspace);
        entity = workspace.Model.FindEntity("Thing");
        Assert.NotNull(entity);
        Assert.Contains(entity!.Properties, property => string.Equals(property.Name, "DisplayName", StringComparison.OrdinalIgnoreCase));
    }

    private static Workspace BuildWorkspace()
    {
        var workspace = new Workspace
        {
            WorkspaceRootPath = "memory",
            MetadataRootPath = "memory/metadata",
            Model = new GenericModel { Name = "TestModel" },
            Instance = new GenericInstance { ModelName = "TestModel" },
        };

        var entity = new GenericEntity { Name = "Thing" };
        entity.Properties.Add(new GenericProperty { Name = "Name", DataType = "string", IsNullable = false });
        workspace.Model.Entities.Add(entity);

        var records = workspace.Instance.GetOrCreateEntityRecords("Thing");
        records.Add(new GenericRecord
        {
            Id = "1",
            Values =
            {
                ["Name"] = "Alpha",
            },
        });

        return workspace;
    }
}


