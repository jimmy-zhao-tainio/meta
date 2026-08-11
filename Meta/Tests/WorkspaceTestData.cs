using Meta.Operations.Domain;

namespace Meta.Tests;

internal static class WorkspaceTestData
{
    internal static InMemoryWorkspace BuildState()
    {
        var model = new GenericModel { Name = "RoundTrip" };
        var node = new GenericEntity { Name = "Node" };
        node.Properties.Add(new GenericProperty { Name = "RequiredText" });
        node.Properties.Add(new GenericProperty { Name = "OptionalText", IsNullable = true });
        node.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            Role = "Parent",
            IsNullable = true,
        });
        model.Entities.Add(node);

        var instance = new GenericInstance { ModelName = model.Name };
        var root = new GenericRecord { Id = "Root" };
        root.Values.Add("RequiredText", "Unicode \u00e5\u00e4\u00f6 <xml> \"C#\" 'SQL'\nline two");
        root.Values.Add("OptionalText", string.Empty);
        instance.GetOrCreateEntityRecords("Node").Add(root);

        var child = new GenericRecord { Id = "child" };
        child.Values.Add("RequiredText", "Child");
        child.RelationshipIds.Add("ParentId", "ROOT");
        instance.GetOrCreateEntityRecords("Node").Add(child);
        return new InMemoryWorkspace(model, instance);
    }
}
