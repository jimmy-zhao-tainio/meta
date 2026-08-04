using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;

namespace Meta.Core.Tests;

public sealed class MetaXmlCodecTests
{
    [Fact]
    public void ReadWrite_PreservesSemanticState()
    {
        var state = BuildState();

        var xml = MetaXmlCodec.Write(state);
        var roundTripped = MetaXmlCodec.Read(
            xml.Model,
            xml.Instance);

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            state,
            roundTripped));
    }

    [Fact]
    public void OperationLaw_HoldsForXml()
    {
        var source = BuildState();
        var sourceXml = MetaXmlCodec.Write(source);
        var operation = new Operation.SetProperty(
            "Node",
            "child",
            "OptionalText",
            string.Empty);
        var expected = InMemoryOperations.Apply(
            source,
            operation);

        var decoded = MetaXmlCodec.Read(
            sourceXml.Model,
            sourceXml.Instance);
        var applied = InMemoryOperations.Apply(
            decoded,
            operation);
        var saved = MetaXmlCodec.Write(applied);
        var reloaded = MetaXmlCodec.Read(
            saved.Model,
            saved.Instance);

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            expected,
            reloaded));
    }

    [Fact]
    public void Write_RejectsInstanceDataOutsideTheModel()
    {
        var state = BuildState();
        state.Instance.RecordsByEntity["Node"][0]
            .Values.Add("Unknown", "value");

        var exception = Assert.Throws<InvalidOperationException>(
            () => MetaXmlCodec.Write(state));

        Assert.Contains(
            "instance.property.unknown",
            exception.Message,
            StringComparison.Ordinal);
    }

    internal static InMemoryWorkspace BuildState()
    {
        var model = new GenericModel
        {
            Name = "RoundTrip",
        };
        var node = new GenericEntity
        {
            Name = "Node",
        };
        node.Properties.Add(new GenericProperty
        {
            Name = "RequiredText",
            IsNullable = false,
        });
        node.Properties.Add(new GenericProperty
        {
            Name = "OptionalText",
            IsNullable = true,
        });
        node.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            Role = "Parent",
            IsNullable = true,
        });
        model.Entities.Add(node);

        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        var root = new GenericRecord
        {
            Id = "Root",
        };
        root.Values.Add(
            "RequiredText",
            "Unicode \u00e5\u00e4\u00f6 <xml> \"C#\" 'SQL'\nline two");
        root.Values.Add("OptionalText", string.Empty);
        instance.GetOrCreateEntityRecords("Node").Add(root);

        var child = new GenericRecord
        {
            Id = "child",
        };
        child.Values.Add("RequiredText", "Child");
        child.RelationshipIds.Add("ParentId", "ROOT");
        instance.GetOrCreateEntityRecords("Node").Add(child);

        return new InMemoryWorkspace(model, instance);
    }
}
