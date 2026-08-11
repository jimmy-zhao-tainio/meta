using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces.Xml;

namespace Meta.Core.Tests;

public sealed class MetaXmlCodecTests
{
    [Fact]
    public void ReadWrite_PreservesSemanticState()
    {
        var state = WorkspaceTestData.BuildState();

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
        var source = WorkspaceTestData.BuildState();
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
        var state = WorkspaceTestData.BuildState();
        state.Instance.RecordsByEntity["Node"][0]
            .Values.Add("Unknown", "value");

        var exception = Assert.Throws<InvalidOperationException>(
            () => MetaXmlCodec.Write(state));

        Assert.Contains(
            "instance.property.unknown",
            exception.Message,
            StringComparison.Ordinal);
    }

}
