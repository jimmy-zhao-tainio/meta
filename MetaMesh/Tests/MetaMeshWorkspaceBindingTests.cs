using Meta.Integration;
using MetaMesh.Core;

namespace MetaMesh.Tests;

public sealed class MetaMeshWorkspaceBindingTests
{
    [Theory]
    [InlineData("xml")]
    [InlineData("csharp")]
    public void OwnerLoadsPersistedCustomValuesAndCanonicalStepReferences(string representation)
    {
        var path = Path.Combine(Path.GetTempPath(), "metamesh-explicit-binding", Guid.NewGuid().ToString("N"));
        var model = MetaMeshModel.CreateEmpty();
        var mesh = new Mesh { Id = "mesh:custom", Name = "  名 Ω  ", Description = "", RootPath = null };
        var operation = new Operation { Id = "operation:custom", Name = "custom", Mesh = mesh, Description = "  retained  " };
        var first = new OperationStep { Id = "step:first", Name = "first", Executable = "tool.exe", Operation = operation, Arguments = "" };
        var last = new OperationStep { Id = "step:last", Name = "last", Executable = "tool.exe", Operation = operation, PreviousStep = first, Arguments = null };
        model.MeshList.Add(mesh);
        model.OperationList.Add(operation);
        model.OperationStepList.AddRange([first, last]);
        try
        {
            TypedWorkspaceModelMapper.Create(model, MetaMeshWorkspaceContract.LoadModel(), new MetaMeshWorkspaceBinding(), path, representation);

            var loaded = new MetaMeshWorkspaceService().Load(path);

            var loadedMesh = Assert.Single(loaded.MeshList);
            var loadedOperation = Assert.Single(loaded.OperationList);
            var loadedFirst = Assert.Single(loaded.OperationStepList, step => step.Id == first.Id);
            var loadedLast = Assert.Single(loaded.OperationStepList, step => step.Id == last.Id);
            Assert.Equal("  名 Ω  ", loadedMesh.Name);
            Assert.Equal("", loadedMesh.Description);
            Assert.Null(loadedMesh.RootPath);
            Assert.Equal("  retained  ", loadedOperation.Description);
            Assert.Equal("", loadedFirst.Arguments);
            Assert.Null(loadedFirst.PreviousStep);
            Assert.Null(loadedLast.Arguments);
            Assert.Same(loadedMesh, loadedOperation.Mesh);
            Assert.Same(loadedOperation, loadedFirst.Operation);
            Assert.Same(loadedOperation, loadedLast.Operation);
            Assert.Same(loadedFirst, loadedLast.PreviousStep);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
