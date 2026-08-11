using Meta.Operations.Domain;
using Meta.Integration;
using Meta.Surfaces.CSharp;
using Meta.Operations;
using Meta.Surfaces;
using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class CSharpWorkspaceContractTests
{
    [Fact]
    public void MetaCliModel_IsProducedAndConsumedByTheCSharpSurfaceContract()
    {
        var typed = new MetaCliWorkspaceService().CreateWorkspace(
            "demo",
            standardCliShapes: true,
            defaultHelp: true);
        var sourceState = TypedWorkspaceModelMapper.ToInMemoryWorkspace(typed);

        var csharp = MetaCSharpWriter.Write(sourceState);
        var source = Assert.Single(csharp.Sources.Values);
        Assert.Contains("public sealed partial class MetaCliModel", source);
        Assert.Contains("CreateEmpty()", source);
        Assert.Contains("List<Application> ApplicationList", source);
        Assert.Contains("MetaCliInstance", source);
        Assert.Contains("CreateBuiltIn()", source);
        Assert.Contains("BuiltIn", source);

        var readState = MetaCSharpReader.Read(csharp);
        var consumed = TypedWorkspaceModelMapper.FromInMemoryWorkspace(
            readState,
            static () => new MetaCliModel());

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            sourceState,
            readState));
        Assert.Equal(
            typed.ApplicationList.Single().Name,
            consumed.ApplicationList.Single().Name);
        Assert.Equal(
            typed.CommandList.Select(command => command.Name),
            consumed.CommandList.Select(command => command.Name));
    }
}
