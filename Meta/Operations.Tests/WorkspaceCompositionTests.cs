using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.Operations.Tests;

public sealed class WorkspaceCompositionTests
{
    [Fact]
    public async Task MaterializeModel_ComposesStructureWithoutReadingRecords()
    {
        var workspace = BuildWorkspace();
        var source = new StructureOnlySource(
            new InMemoryWorkspaceSource(workspace));

        var result = await WorkspaceComposition.MaterializeModelAsync(source);

        Assert.Equal(workspace.Model.Name, result.Name);
        Assert.Equal(
            workspace.Model.Entities.Select(entity => entity.Name),
            result.Entities.Select(entity => entity.Name));
        Assert.False(source.RecordsRead);
    }

    [Fact]
    public async Task Materialize_ComposesPrimitiveReadsAndMutationOperations()
    {
        var source = BuildWorkspace();

        var result = await WorkspaceComposition.MaterializeAsync(
            new InMemoryWorkspaceSource(source));

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(source, result));
    }

    [Fact]
    public async Task Materialize_OrdersRequiredReferencesAndDefersOptionalReferences()
    {
        var source = BuildWorkspace();
        source.Instance.RecordsByEntity["Person"].Reverse();
        source.Instance.RecordsByEntity["Team"].Reverse();

        var result = await WorkspaceComposition.MaterializeAsync(
            new InMemoryWorkspaceSource(source));

        Assert.Null(InMemoryWorkspaceComparer.FindDifference(source, result));
        var first = Assert.Single(
            result.Instance.RecordsByEntity["Person"],
            record => record.Id == "person-a");
        Assert.Equal("person-b", first.RelationshipIds["PreviousPersonId"]);
    }

    [Fact]
    public async Task Merge_ComposesDistinctSourcesUnderOneModelName()
    {
        var people = BuildWorkspace();
        var inventoryModel = new GenericModel { Name = "Inventory" };
        var asset = new GenericEntity { Name = "Asset" };
        asset.Properties.Add(new GenericProperty
        {
            Name = "Label",
            IsNullable = false,
        });
        inventoryModel.Entities.Add(asset);
        var inventoryInstance = new GenericInstance { ModelName = "Inventory" };
        var assetRecord = new GenericRecord { Id = "asset-a" };
        assetRecord.Values["Label"] = "Server";
        inventoryInstance.GetOrCreateEntityRecords("Asset").Add(assetRecord);
        var inventory = new InMemoryWorkspace(
            inventoryModel,
            inventoryInstance);

        var result = await WorkspaceComposition.MergeAsync(
            "Enterprise",
            [
                new InMemoryWorkspaceSource(people),
                new InMemoryWorkspaceSource(inventory),
            ]);

        Assert.Equal("Enterprise", result.Model.Name);
        Assert.Equal("Enterprise", result.Instance.ModelName);
        Assert.Equal(
            ["Team", "Person", "Asset"],
            result.Model.Entities.Select(entity => entity.Name));
        Assert.Equal(
            "Server",
            Assert.Single(result.Instance.RecordsByEntity["Asset"])
                .Values["Label"]);
    }

    [Fact]
    public async Task PrimitiveReadsExposeStructureAndStreamRecordsSeparately()
    {
        var workspace = BuildWorkspace();
        workspace.Instance.RecordsByEntity["Person"].Reverse();
        IMetaWorkspaceSource source = new InMemoryWorkspaceSource(
            workspace);

        Assert.Equal("People", await source.ReadModelNameAsync());
        Assert.Equal(
            ["Team", "Person"],
            await CollectAsync(source.ReadEntityNamesAsync()));
        Assert.Equal(
            [new PropertyDefinition("Name", IsRequired: true)],
            await CollectAsync(source.ReadPropertiesAsync("Team")));
        Assert.Equal(
            [
                new RelationshipDefinition(
                    "Team",
                    Role: null,
                    IsRequired: true),
                new RelationshipDefinition(
                    "Person",
                    "PreviousPerson",
                    IsRequired: false),
            ],
            await CollectAsync(source.ReadRelationshipsAsync("Person")));

        var people = await CollectAsync(source.ReadRecordsAsync("Person"));
        Assert.Equal(2, people.Count);
        Assert.Equal(["person-a", "person-b"], people.Select(item => item.Id));
        Assert.Equal("Alice", people[0].Values["Name"]);
        Assert.Equal("team-a", people[0].RelationshipIds["TeamId"]);
        Assert.Equal(2, await source.CountRecordsAsync("Person"));

        var query = await source.QueryRecordsAsync(
            "Person",
            new RecordQuery(
                1,
                new RecordCondition.Contains("Name", "o"),
                new RecordCondition.Equal("Team", "TEAM-B")));
        Assert.Equal(1, query.TotalCount);
        Assert.Equal("person-b", Assert.Single(query.Records).Id);

        var bounded = await source.QueryRecordsAsync(
            "Person",
            new RecordQuery(
                1,
                new RecordCondition.Contains("Id", "person-")));
        Assert.Equal(2, bounded.TotalCount);
        Assert.Equal("person-a", Assert.Single(bounded.Records).Id);

        var person = await source.ReadRecordAsync("Person", "PERSON-A");
        Assert.NotNull(person);
        Assert.Equal("person-a", person!.Id);
        Assert.Equal("Alice", person.Values["Name"]);
        Assert.Null(await source.ReadRecordAsync("Person", "missing"));
    }

    private sealed class StructureOnlySource(IMetaWorkspaceSource source) : IMetaWorkspaceSource
    {
        public bool RecordsRead { get; private set; }

        public ValueTask<string> ReadModelNameAsync(CancellationToken cancellationToken = default) =>
            source.ReadModelNameAsync(cancellationToken);

        public IAsyncEnumerable<string> ReadEntityNamesAsync(CancellationToken cancellationToken = default) =>
            source.ReadEntityNamesAsync(cancellationToken);

        public IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(string entityName, CancellationToken cancellationToken = default) =>
            source.ReadPropertiesAsync(entityName, cancellationToken);

        public IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(string entityName, CancellationToken cancellationToken = default) =>
            source.ReadRelationshipsAsync(entityName, cancellationToken);

        public async IAsyncEnumerable<RecordData> ReadRecordsAsync(string entityName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RecordsRead = true;
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<long> CountRecordsAsync(string entityName, CancellationToken cancellationToken = default) =>
            source.CountRecordsAsync(entityName, cancellationToken);

        public ValueTask<RecordQueryResult> QueryRecordsAsync(string entityName, RecordQuery query, CancellationToken cancellationToken = default) =>
            source.QueryRecordsAsync(entityName, query, cancellationToken);

        public ValueTask<RecordData?> ReadRecordAsync(string entityName, string id, CancellationToken cancellationToken = default) =>
            source.ReadRecordAsync(entityName, id, cancellationToken);
    }

    private static async Task<List<T>> CollectAsync<T>(
        IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static InMemoryWorkspace BuildWorkspace()
    {
        var model = new GenericModel
        {
            Name = "People",
        };
        var team = new GenericEntity
        {
            Name = "Team",
        };
        team.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        model.Entities.Add(team);

        var person = new GenericEntity
        {
            Name = "Person",
        };
        person.Properties.Add(new GenericProperty
        {
            Name = "Name",
            IsNullable = false,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Team",
            IsNullable = false,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Person",
            Role = "PreviousPerson",
            IsNullable = true,
        });
        model.Entities.Add(person);

        var instance = new GenericInstance
        {
            ModelName = model.Name,
        };
        instance.GetOrCreateEntityRecords("Team").AddRange(
        [
            Record("team-a", "Alpha"),
            Record("team-b", "Beta"),
        ]);
        instance.GetOrCreateEntityRecords("Person").AddRange(
        [
            Record(
                "person-a",
                "Alice",
                new Dictionary<string, string>
                {
                    ["TeamId"] = "team-a",
                    ["PreviousPersonId"] = "person-b",
                }),
            Record(
                "person-b",
                "Bob",
                new Dictionary<string, string>
                {
                    ["TeamId"] = "team-b",
                }),
        ]);

        return new InMemoryWorkspace(model, instance);
    }

    private static GenericRecord Record(
        string id,
        string name,
        IReadOnlyDictionary<string, string>? relationships = null)
    {
        var record = new GenericRecord
        {
            Id = id,
        };
        record.Values["Name"] = name;
        if (relationships != null)
        {
            foreach (var relationship in relationships)
            {
                record.RelationshipIds.Add(
                    relationship.Key,
                    relationship.Value);
            }
        }

        return record;
    }
}
