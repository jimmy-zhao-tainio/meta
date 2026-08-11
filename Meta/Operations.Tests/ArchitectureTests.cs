using Meta.Operations;

namespace Meta.Operations.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void OperationsAssembly_DoesNotReferenceLegacyMetaCore()
    {
        var references = typeof(Operation).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain("Meta.Core", references);
    }
}
