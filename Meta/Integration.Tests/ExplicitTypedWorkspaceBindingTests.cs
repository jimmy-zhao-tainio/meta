using Meta.Integration;
using Meta.Operations;
using Meta.Operations.Domain;
using Meta.TypedModels;

namespace Meta.Core.Tests;

public sealed class ExplicitTypedWorkspaceBindingTests
{
    private const string ExactText = " \t\r\n\u00c5sa \u6f22\u5b57 \U0001f642\r\nsecond\nthird ";

    [Theory]
    [InlineData("xml")]
    [InlineData("csharp")]
    public async Task ExplicitBinding_PersistsScalarsCanonicalReferencesAndIndependentGraphs(string representation)
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "workspace");
            var binding = new FixtureBinding();
            var graph = CreateGraph();
            TypedWorkspaceModelMapper.Create(graph, CreateContract(), binding, path, representation);

            var state = await TypedWorkspaceModelMapper.LoadStateAsync(path);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(CreateInitialState(), state));
            var first = await TypedWorkspaceModelMapper.LoadAsync(path, binding);
            var second = TypedWorkspaceModelMapper.Load(path, binding);
            var firstOne = first.Items.Single(item => item.Key == "one");
            var firstTwo = first.Items.Single(item => item.Key == "two");
            var firstThree = first.Items.Single(item => item.Key == "three");
            Assert.NotSame(first, second);
            Assert.NotSame(firstOne, second.Items.Single(item => item.Key == "one"));
            Assert.NotSame(first.Owners[0], second.Owners[0]);
            Assert.Same(first.Owners[0], firstOne.Owner);
            Assert.Same(first.Owners[0], firstTwo.Owner);
            Assert.Null(firstOne.Note);
            Assert.Equal(string.Empty, firstTwo.Note);
            Assert.Equal(ExactText, firstThree.Note);

            var projected = TypedWorkspaceModelMapper.FromInMemoryWorkspace(state, binding);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(state,
                TypedWorkspaceModelMapper.ToInMemoryWorkspace(projected, state.Model, binding)));

            var newOwner = new FixtureOwner("new-owner", "New owner");
            first.Owners.Add(newOwner);
            firstOne.Owner = newOwner;
            firstOne.Note = ExactText;
            firstTwo.Note = null;
            first.Items.Remove(firstThree);
            await TypedWorkspaceModelMapper.SaveAsync(first, path, binding);
            Assert.NotNull(binding.LastWrittenContract);
            Assert.Equal(state.Model.ComputeContractSignature(), binding.LastWrittenContract.ComputeContractSignature());

            var restored = TypedWorkspaceModelMapper.Load(path, binding);
            Assert.Equal(2, restored.Items.Count);
            Assert.Equal(2, restored.Owners.Count);
            var restoredOne = restored.Items.Single(item => item.Key == "one");
            Assert.Same(restored.Owners.Single(owner => owner.Key == "new-owner"), restoredOne.Owner);
            Assert.Equal(ExactText, restoredOne.Note);
            Assert.Null(restored.Items.Single(item => item.Key == "two").Note);
            Assert.Null(second.Items.Single(item => item.Key == "one").Note);
            Assert.Equal(string.Empty, second.Items.Single(item => item.Key == "two").Note);
            var saved = await TypedWorkspaceModelMapper.LoadStateAsync(path);
            var savedItems = saved.Instance.RecordsByEntity["Item"];
            Assert.Equal("new-owner", savedItems.Single(item => item.Id == "one").RelationshipIds["OwnerId"]);
            Assert.Equal(ExactText, savedItems.Single(item => item.Id == "one").Values["Note"]);
            Assert.False(savedItems.Single(item => item.Id == "two").Values.ContainsKey("Note"));
            Assert.DoesNotContain(savedItems, item => item.Id == "three");

            restoredOne.Note = string.Empty;
            TypedWorkspaceModelMapper.Save(restored, path, binding);
            var final = await TypedWorkspaceModelMapper.LoadAsync(path, binding);
            Assert.Equal(string.Empty, final.Items.Single(item => item.Key == "one").Note);
            Assert.Null(final.Items.Single(item => item.Key == "two").Note);
            Assert.True(binding.ReadCount >= 5);
            Assert.Equal(4, binding.WriteCount);

            var reads = binding.ReadCount;
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                TypedWorkspaceModelMapper.LoadAsync(path, binding, searchUpward: true));
            Assert.Equal(reads, binding.ReadCount);
        }
        finally { DeleteDirectoryIfExists(root); }
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("csharp")]
    public async Task ExplicitBinding_RejectsPersistedContractMismatchWithoutChangingWorkspace(string representation)
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "workspace");
            var wrong = CreateInitialState();
            wrong.Model.Entities[0].Properties.Add(new GenericProperty { Name = "Added", IsNullable = true });
            await WorkspaceSurface.CreateAsync(wrong, path, representation);
            var before = await TypedWorkspaceModelMapper.LoadStateAsync(path);
            var binding = new FixtureBinding();

            var loadError = Assert.Throws<InvalidOperationException>(() =>
                TypedWorkspaceModelMapper.Load(path, binding));
            Assert.Contains("binding contract", loadError.Message);
            var saveError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TypedWorkspaceModelMapper.SaveAsync(CreateGraph(), path, binding));
            Assert.Contains("binding contract", saveError.Message);
            Assert.NotNull(binding.LastWrittenContract);
            Assert.Equal(before.Model.ComputeContractSignature(), binding.LastWrittenContract.ComputeContractSignature());
            Assert.Contains(binding.LastWrittenContract.Entities[0].Properties, property => property.Name == "Added");
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(before,
                await TypedWorkspaceModelMapper.LoadStateAsync(path)));

            var rejectedPath = Path.Combine(root, "rejected");
            var createError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TypedWorkspaceModelMapper.CreateAsync(CreateGraph(), wrong.Model, binding, rejectedPath, representation));
            Assert.Contains("binding contract", createError.Message);
            Assert.False(Directory.Exists(rejectedPath));
        }
        finally { DeleteDirectoryIfExists(root); }
    }

    [Theory]
    [InlineData("xml", "noncanonical")]
    [InlineData("csharp", "noncanonical")]
    [InlineData("xml", "required-value")]
    [InlineData("csharp", "required-value")]
    [InlineData("xml", "required-reference")]
    [InlineData("csharp", "required-reference")]
    [InlineData("xml", "duplicate")]
    [InlineData("csharp", "duplicate")]
    public async Task ExplicitBinding_RejectsInvalidGraphBeforePersisting(string representation, string invalidKind)
    {
        var root = CreateTempRoot();
        try
        {
            var path = Path.Combine(root, "workspace");
            var binding = new FixtureBinding();
            await TypedWorkspaceModelMapper.CreateAsync(CreateGraph(), CreateContract(), binding, path, representation);
            var graph = await TypedWorkspaceModelMapper.LoadAsync(path, binding);
            var before = await TypedWorkspaceModelMapper.LoadStateAsync(path);
            switch (invalidKind)
            {
                case "noncanonical": graph.Items[0].Owner = new FixtureOwner("owner", "Owner"); break;
                case "required-value": graph.Items[0].Caption = null!; break;
                case "required-reference": graph.Items[0].Owner = null!; break;
                case "duplicate": graph.Owners.Add(new FixtureOwner("owner", "Duplicate")); break;
            }

            var writes = binding.WriteCount;
            var error = Assert.Throws<InvalidOperationException>(() =>
                TypedWorkspaceModelMapper.Save(graph, path, binding));
            Assert.Contains(invalidKind switch
            {
                "noncanonical" => "canonical object",
                "duplicate" => "Duplicate owner",
                _ => "Invalid bound graph"
            }, error.Message);
            Assert.Equal(writes + 1, binding.WriteCount);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(before,
                await TypedWorkspaceModelMapper.LoadStateAsync(path)));
        }
        finally { DeleteDirectoryIfExists(root); }
    }

    private static GenericModel CreateContract()
    {
        var model = new GenericModel { Name = "ExplicitBindingFixture" };
        var item = new GenericEntity { Name = "Item" };
        item.Properties.Add(new GenericProperty { Name = "Caption" });
        item.Properties.Add(new GenericProperty { Name = "Note", IsNullable = true });
        item.Relationships.Add(new GenericRelationship { Entity = "Owner" });
        var owner = new GenericEntity { Name = "Owner" };
        owner.Properties.Add(new GenericProperty { Name = "Name" });
        model.Entities.Add(item);
        model.Entities.Add(owner);
        return model;
    }

    private static FixtureGraph CreateGraph()
    {
        var owner = new FixtureOwner("owner", "Owner");
        return new FixtureGraph(
            [new FixtureItem("one", "One", null, owner), new FixtureItem("two", "Two", string.Empty, owner),
                new FixtureItem("three", "Three", ExactText, owner)],
            [owner]);
    }

    private static InMemoryWorkspace CreateInitialState()
    {
        var instance = new GenericInstance { ModelName = "ExplicitBindingFixture" };
        var owner = new GenericRecord { Id = "owner" };
        owner.Values.Add("Name", "Owner");
        instance.GetOrCreateEntityRecords("Owner").Add(owner);
        foreach (var (id, caption, note) in new[]
                 { ("one", "One", (string?)null), ("two", "Two", string.Empty), ("three", "Three", ExactText) })
        {
            var item = new GenericRecord { Id = id };
            item.Values.Add("Caption", caption);
            if (note is not null) item.Values.Add("Note", note);
            item.RelationshipIds.Add("OwnerId", "owner");
            instance.GetOrCreateEntityRecords("Item").Add(item);
        }
        return new InMemoryWorkspace(CreateContract(), instance);
    }

    // The typed root has no parameterless constructor or model-shaped collection properties.
    // Access therefore has to use the explicitly supplied binding rather than the legacy mapper.
    private sealed class FixtureGraph(List<FixtureItem> items, List<FixtureOwner> owners)
    {
        public List<FixtureItem> Items { get; } = items;
        public List<FixtureOwner> Owners { get; } = owners;
    }

    private sealed class FixtureItem(string key, string caption, string? note, FixtureOwner owner)
    {
        public string Key { get; } = key;
        public string Caption { get; set; } = caption;
        public string? Note { get; set; } = note;
        public FixtureOwner Owner { get; set; } = owner;
    }

    private sealed class FixtureOwner(string key, string name)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
    }

    private sealed class FixtureBinding : ITypedWorkspaceBinding<FixtureGraph>
    {
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public GenericModel? LastWrittenContract { get; private set; }

        public FixtureGraph Read(InMemoryWorkspace workspace)
        {
            ReadCount++;
            RequireContract(workspace.Model);
            RequireValid(workspace);
            var owners = Rows(workspace, "Owner")
                .ToDictionary(row => row.Id, row => new FixtureOwner(row.Id, row.Values["Name"]), MetaIdentity.Comparer);
            var items = Rows(workspace, "Item")
                .Select(row => new FixtureItem(row.Id, row.Values["Caption"],
                    row.Values.TryGetValue("Note", out var note) ? note : null, owners[row.RelationshipIds["OwnerId"]]))
                .ToList();
            return new FixtureGraph(items, owners.Values.ToList());
        }

        public InMemoryWorkspace Write(FixtureGraph graph, GenericModel contract)
        {
            WriteCount++;
            LastWrittenContract = contract;
            RequireContract(contract);
            var instance = new GenericInstance { ModelName = contract.Name };
            var owners = new Dictionary<string, FixtureOwner>(MetaIdentity.Comparer);
            foreach (var owner in graph.Owners)
            {
                if (!owners.TryAdd(owner.Key, owner)) throw new InvalidOperationException("Duplicate owner identity.");
                var row = new GenericRecord { Id = owner.Key };
                row.Values.Add("Name", owner.Name);
                instance.GetOrCreateEntityRecords("Owner").Add(row);
            }
            foreach (var item in graph.Items)
            {
                var row = new GenericRecord { Id = item.Key };
                if (item.Caption is not null) row.Values.Add("Caption", item.Caption);
                if (item.Note is not null) row.Values.Add("Note", item.Note);
                if (item.Owner is not null)
                {
                    if (!owners.TryGetValue(item.Owner.Key, out var canonical) || !ReferenceEquals(canonical, item.Owner))
                        throw new InvalidOperationException("Owner reference is not the canonical object.");
                    row.RelationshipIds.Add("OwnerId", item.Owner.Key);
                }
                instance.GetOrCreateEntityRecords("Item").Add(row);
            }
            var state = new InMemoryWorkspace(contract.Clone(), instance);
            RequireValid(state);
            return state;
        }

        private static void RequireContract(GenericModel contract)
        {
            if (contract.ComputeContractSignature() != CreateContract().ComputeContractSignature())
                throw new InvalidOperationException("Workspace differs from the explicit binding contract.");
        }

        private static IEnumerable<GenericRecord> Rows(InMemoryWorkspace workspace, string entity) =>
            workspace.Instance.RecordsByEntity.TryGetValue(entity, out var rows) ? rows : [];

        private static void RequireValid(InMemoryWorkspace workspace)
        {
            var diagnostics = WorkspaceValidator.Validate(workspace.Model, workspace.Instance);
            if (diagnostics.HasErrors) throw new InvalidOperationException("Invalid bound graph: " +
                string.Join(" | ", diagnostics.Issues.Where(issue => issue.Severity == IssueSeverity.Error).Select(issue => issue.Message)));
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "meta-explicit-binding-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
