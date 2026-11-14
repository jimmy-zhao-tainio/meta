using Meta.Integration;
using Meta.Operations;
using Meta.Operations.Domain;
using Meta.TypedModels;
using MetaCli.Core;

namespace MetaCli.Tests;

public sealed partial class MetaCliRuntimeTests
{
    [Theory]
    [InlineData("xml", null)]
    [InlineData("xml", "")]
    [InlineData("csharp", "Åsa äter räksmörgås.")]
    [InlineData("csharp", " \t\r\n ")]
    public async Task Runtime_ExplicitBindingPersistsThroughSelectedContract(string representation, string? text)
    {
        using var temp = TempDirectory.Create();
        var schema = await TypedWorkspaceModelMapper.LoadStateAsync(
            System.IO.Path.Combine(FindRepositoryRoot(), "MetaCli", "Workspace"));
        var binding = new RecordingBinding();
        var commands = System.IO.Path.Combine(temp.Path, "commands");
        await WorkspaceSurface.CreateAsync(
            binding.Write(CreateRuntimeModel(), schema.Model), commands, representation);
        var domain = new InMemoryWorkspace(schema.Model.Clone(), new GenericInstance { ModelName = schema.Model.Name });
        domain = InMemoryOperations.Apply(domain,
            new Operation.InsertRecord("Application", "app-domain", new Dictionary<string, string> { ["Name"] = "domain" }));
        var path = System.IO.Path.Combine(temp.Path, "domain");
        await WorkspaceSurface.CreateAsync(domain, path, representation);
        var exitCode = -1;
        var error = new StringWriter();
        var runtime = new MetaCliRuntime<MetaCliModel>(commands,
                error: error, setExitCode: code => exitCode = code, workspaceBinding: binding)
            .Bind("exec-add-property", (MetaCliInvocation _, MetaCliModel model) =>
            {
                model.ApplicationList.Single().Description = text;
            });

        runtime.Run("model", "add-property", "--workspace", path, "Customer", "Name");

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Equal(1, binding.ReadCount);
        Assert.Equal(2, binding.WriteCount); // command fixture, then persisted domain change
        var saved = await TypedWorkspaceModelMapper.LoadStateAsync(path);
        var row = Assert.Single(saved.Instance.RecordsByEntity["Application"]);
        if (text is null) Assert.False(row.Values.ContainsKey("Description"));
        else Assert.Equal(text, row.Values["Description"]);
        Assert.Equal(text, new MetaCliWorkspaceBinding().Read(saved).ApplicationList.Single().Description);
    }

    [Fact]
    public async Task Runtime_ExplicitBindingRejectsChangedContractBeforeHandlerOrWrite()
    {
        using var temp = TempDirectory.Create();
        var schema = await TypedWorkspaceModelMapper.LoadStateAsync(
            System.IO.Path.Combine(FindRepositoryRoot(), "MetaCli", "Workspace"));
        var binding = new RecordingBinding();
        var commands = System.IO.Path.Combine(temp.Path, "commands");
        await WorkspaceSurface.CreateAsync(binding.Write(CreateRuntimeModel(), schema.Model), commands, "xml");
        var changed = new InMemoryWorkspace(schema.Model.Clone(), new GenericInstance { ModelName = schema.Model.Name });
        changed = InMemoryOperations.Apply(changed, new Operation.AddProperty("Application", "Unexpected", false));
        var path = System.IO.Path.Combine(temp.Path, "domain");
        await WorkspaceSurface.CreateAsync(changed, path, "xml");
        var ran = false;
        var exitCode = -1;
        var runtime = new MetaCliRuntime<MetaCliModel>(commands,
                error: new StringWriter(), setExitCode: code => exitCode = code, workspaceBinding: binding)
            .Bind("exec-add-property", (MetaCliInvocation _, MetaCliModel _) => ran = true);

        runtime.Run("model", "add-property", "--workspace", path, "Customer", "Name");

        Assert.Equal(4, exitCode);
        Assert.False(ran);
        Assert.Equal(1, binding.ReadCount);
        Assert.Equal(1, binding.WriteCount);
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(changed, await TypedWorkspaceModelMapper.LoadStateAsync(path)));
    }

    private sealed class RecordingBinding : ITypedWorkspaceBinding<MetaCliModel>
    {
        private readonly MetaCliWorkspaceBinding binding = new();
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public MetaCliModel Read(InMemoryWorkspace state)
        {
            ReadCount++;
            return binding.Read(state);
        }
        public InMemoryWorkspace Write(MetaCliModel model, GenericModel contract)
        {
            WriteCount++;
            return binding.Write(model, contract);
        }
    }
}
