using System.Text;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class MetaOperationInterpreterTests
{
    [Fact]
    public void Apply_ExecutesAllOperationFamiliesInOrder()
    {
        var source = BuildState();
        var sourceBefore = Canonicalize(source);
        var plan = BuildPlan();

        Assert.IsAssignableFrom<ModelOperation>(plan.Operations[0]);
        Assert.IsAssignableFrom<ModelInstanceRefactor>(plan.Operations[1]);
        Assert.IsAssignableFrom<InstanceOperation>(plan.Operations[2]);

        var result = new MetaOperationInterpreter().Apply(source, plan);

        Assert.Equal(plan.Operations.Count, result.AppliedOperationCount);
        Assert.Equal(sourceBefore, Canonicalize(source));

        var audit = result.State.Model.FindEntity("Audit");
        Assert.NotNull(audit);

        var person = result.State.Model.FindEntity("Person");
        Assert.NotNull(person);
        Assert.DoesNotContain(
            person!.Properties,
            property => string.Equals(
                property.Name,
                "LegacyName",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            person.Properties,
            property => string.Equals(
                property.Name,
                "DisplayName",
                StringComparison.OrdinalIgnoreCase));

        var people = result.State.Instance.RecordsByEntity["Person"];
        var first = Assert.Single(people, record => record.Id == "person-a");
        Assert.Equal("Updated", first.Values["DisplayName"]);
        Assert.False(first.Values.ContainsKey("Note"));
        Assert.Equal("team-b", first.RelationshipIds["TeamId"]);

        var second = Assert.Single(people, record => record.Id == "person-b");
        Assert.Equal("Beta", second.Values["DisplayName"]);
        Assert.False(second.RelationshipIds.ContainsKey("TeamId"));

        var auditRows = result.State.Instance.RecordsByEntity["Audit"];
        Assert.Equal("audit-1", Assert.Single(auditRows).Id);
    }

    [Fact]
    public void Apply_CanonicalizesRelationshipTargetIdentity()
    {
        var source = BuildState();
        var plan = MetaOperationPlan.Create(
            new SetRelationshipOperation(
                "Person",
                "PERSON-A",
                "Team",
                "TEAM-B"));

        var result = new MetaOperationInterpreter().Apply(source, plan);

        var person = Assert.Single(result.State.Instance.RecordsByEntity["Person"]);
        Assert.Equal("team-b", person.RelationshipIds["TeamId"]);
    }

    [Fact]
    public void Apply_RejectsCaseInsensitiveDuplicateIdentityWithoutChangingSource()
    {
        var source = BuildState();
        var before = Canonicalize(source);
        var plan = MetaOperationPlan.Create(
            new InsertRecordOperation(
                "Person",
                "PERSON-A",
                new Dictionary<string, string>
                {
                    ["LegacyName"] = "Duplicate",
                }));

        var exception = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(source, plan));

        Assert.Equal(0, exception.OperationIndex);
        Assert.IsType<InsertRecordOperation>(exception.Operation);
        Assert.Equal(before, Canonicalize(source));
    }

    [Fact]
    public void Apply_RejectsTemporarilyIncompleteRecordAtTheInsertOperation()
    {
        var source = BuildState();
        var before = Canonicalize(source);
        var plan = MetaOperationPlan.Create(
            new InsertRecordOperation(
                "Person",
                "person-b"),
            new SetPropertyOperation(
                "Person",
                "person-b",
                "LegacyName",
                "Too late"));

        var exception = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(source, plan));

        Assert.Equal(0, exception.OperationIndex);
        Assert.IsType<InsertRecordOperation>(exception.Operation);
        Assert.NotNull(exception.Diagnostics);
        Assert.Contains(
            exception.Diagnostics.Issues,
            issue => issue.Code == "instance.required.missing");
        Assert.Equal(before, Canonicalize(source));
    }

    [Fact]
    public void Apply_RequiresReferencesToBeClearedBeforeDeletingTheirTarget()
    {
        var source = BuildState();
        var rejected = MetaOperationPlan.Create(
            new DeleteRecordOperation("Team", "team-a"),
            new ClearRelationshipOperation("Person", "person-a", "Team"));

        var exception = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(source, rejected));

        Assert.Equal(0, exception.OperationIndex);
        Assert.IsType<DeleteRecordOperation>(exception.Operation);

        var accepted = MetaOperationPlan.Create(
            new ClearRelationshipOperation("Person", "person-a", "Team"),
            new DeleteRecordOperation("Team", "team-a"));
        var result = new MetaOperationInterpreter().Apply(source, accepted);

        Assert.DoesNotContain(
            result.State.Instance.RecordsByEntity["Team"],
            record => record.Id == "team-a");
    }

    [Fact]
    public void Apply_AllowsDeletingARecordThatReferencesItself()
    {
        var source = BuildState();
        var plan = MetaOperationPlan.Create(
            new AddRelationshipOperation(
                "Person",
                "Person",
                "Manager",
                isRequired: false),
            new SetRelationshipOperation(
                "Person",
                "person-a",
                "Manager",
                "person-a"),
            new DeleteRecordOperation(
                "Person",
                "person-a"));

        var result = new MetaOperationInterpreter().Apply(source, plan);

        Assert.Empty(
            result.State.Instance.RecordsByEntity["Person"]);
    }

    [Fact]
    public void Apply_DoesNotRescanTheInstanceAfterEveryInsert()
    {
        var source = BuildState();
        var validation = new RecordingValidationService();
        var operations = Enumerable.Range(0, 100)
            .Select(index => (MetaOperation)new InsertRecordOperation(
                "Person",
                $"person-{index + 100}",
                new Dictionary<string, string>
                {
                    ["LegacyName"] = $"Person {index}",
                }))
            .ToArray();

        var result = new MetaOperationInterpreter(validation).Apply(
            source,
            MetaOperationPlan.Create(operations));

        Assert.Equal(
            101,
            result.State.Instance.RecordsByEntity["Person"].Count);
        Assert.Equal([3, 103], validation.RecordCounts);
    }

    [Fact]
    public void Apply_RejectsModelInvalidityAtTheIntroducingOperation()
    {
        var source = BuildState();
        var exception = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(
                source,
                MetaOperationPlan.Create(
                    new AddRelationshipOperation(
                        "Person",
                        "Person",
                        "Manager",
                        isRequired: true,
                        existingRecordTargetId: "person-a"))));

        Assert.Equal(0, exception.OperationIndex);
        Assert.IsType<AddRelationshipOperation>(exception.Operation);
        Assert.NotNull(exception.Diagnostics);
        Assert.Contains(
            exception.Diagnostics.Issues,
            issue => issue.Code == "relationship.cycle");
    }

    [Fact]
    public void Session_RejectedPlanLeavesPriorPendingStateUnchanged()
    {
        var session = new InMemoryMetaOperationSession(BuildState());
        session.Apply(MetaOperationPlan.Create(
            new SetPropertyOperation(
                "Person",
                "person-a",
                "LegacyName",
                "Pending")));
        var beforeRejectedPlan = Canonicalize(session.Snapshot());

        var rejectedPlan = MetaOperationPlan.Create(
            new SetPropertyOperation(
                "Person",
                "person-a",
                "LegacyName",
                "MustNotPublish"),
            new InsertRecordOperation(
                "Person",
                "PERSON-A",
                new Dictionary<string, string>
                {
                    ["LegacyName"] = "Duplicate",
                }));

        Assert.Throws<MetaOperationException>(() => session.Apply(rejectedPlan));
        Assert.Equal(beforeRejectedPlan, Canonicalize(session.Snapshot()));

        session.Discard();
        var restored = Assert.Single(
            session.Snapshot().Instance.RecordsByEntity["Person"]);
        Assert.Equal("Original", restored.Values["LegacyName"]);
    }

    [Fact]
    public void Apply_ExecutesSchemaRefactorsWithTheirInstanceChanges()
    {
        var source = BuildState();
        var plan = BuildSchemaRefactorPlan();

        var result = new MetaOperationInterpreter().Apply(source, plan);

        Assert.Null(result.State.Model.FindEntity("Temporary"));
        var person = result.State.Model.FindEntity("Person");
        Assert.NotNull(person);
        var note = Assert.Single(
            person!.Properties,
            property => property.Name == "Note");
        Assert.False(note.IsNullable);
        Assert.DoesNotContain(
            person.Properties,
            property => property.Name == "Code");
        Assert.DoesNotContain(
            person.Relationships,
            relationship => relationship.GetRoleOrDefault() == "PreferredTeam");

        var people = result.State.Instance.RecordsByEntity["Person"];
        Assert.Equal("Remove me", Assert.Single(
            people,
            record => record.Id == "person-a").Values["Note"]);
        Assert.Equal("(none)", Assert.Single(
            people,
            record => record.Id == "person-b").Values["Note"]);
    }

    internal static MetaOperationPlan BuildSchemaRefactorPlan()
    {
        return MetaOperationPlan.Create(
            new AddEntityOperation("Temporary"),
            new AddPropertyOperation(
                "Temporary",
                "Name",
                isRequired: true),
            new InsertRecordOperation(
                "Temporary",
                "temporary-1",
                new Dictionary<string, string>
                {
                    ["Name"] = "Temporary",
                }),
            new InsertRecordOperation(
                "Person",
                "person-b",
                new Dictionary<string, string>
                {
                    ["LegacyName"] = "Second",
                }),
            new AddPropertyOperation(
                "Person",
                "Code",
                isRequired: true,
                existingRecordValue: "seed"),
            new SetPropertyRequiredOperation(
                "Person",
                "Note",
                isRequired: true,
                missingRecordValue: "(none)"),
            new AddRelationshipOperation(
                "Person",
                "Team",
                "PreferredTeam",
                isRequired: true,
                existingRecordTargetId: "TEAM-A"),
            new RemoveRelationshipOperation(
                "Person",
                "PreferredTeam"),
            new RemovePropertyOperation(
                "Person",
                "Code"),
            new DeleteRecordOperation(
                "Temporary",
                "temporary-1"),
            new RemovePropertyOperation(
                "Temporary",
                "Name"),
            new RemoveEntityOperation("Temporary"));
    }

    [Fact]
    public void Apply_RejectsRequiredSchemaAdditionsWithoutExistingRecordValues()
    {
        var source = BuildState();
        var before = Canonicalize(source);

        var propertyException = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(
                source,
                MetaOperationPlan.Create(
                    new AddPropertyOperation(
                        "Person",
                        "RequiredValue",
                        isRequired: true))));
        Assert.Equal(0, propertyException.OperationIndex);

        var relationshipException = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(
                source,
                MetaOperationPlan.Create(
                    new AddRelationshipOperation(
                        "Person",
                        "Team",
                        "RequiredTeam",
                        isRequired: true))));
        Assert.Equal(0, relationshipException.OperationIndex);
        Assert.Equal(before, Canonicalize(source));
    }

    [Fact]
    public void Apply_CarriesStructuredDiagnosticsForAnInvalidResult()
    {
        var source = BuildState();
        var before = Canonicalize(source);

        var exception = Assert.Throws<MetaOperationException>(
            () => new MetaOperationInterpreter().Apply(
                source,
                MetaOperationPlan.Create(
                    new DeleteRecordOperation(
                        "Team",
                        "team-a"))));

        Assert.NotNull(exception.Diagnostics);
        Assert.Contains(
            exception.Diagnostics.Issues,
            issue => string.Equals(
                issue.Code,
                "instance.relationship.orphan",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, Canonicalize(source));
    }

    internal static GenericMetadataState BuildState(string modelName = "OperationProof")
    {
        var model = new GenericModel
        {
            Name = modelName,
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
            Name = "LegacyName",
            IsNullable = false,
        });
        person.Properties.Add(new GenericProperty
        {
            Name = "Note",
            IsNullable = true,
        });
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Team",
            IsNullable = true,
        });
        model.Entities.Add(person);

        var instance = new GenericInstance
        {
            ModelName = modelName,
        };
        instance.GetOrCreateEntityRecords("Team").AddRange(
        [
            new GenericRecord
            {
                Id = "team-a",
                Values =
                {
                    ["Name"] = "Alpha",
                },
            },
            new GenericRecord
            {
                Id = "team-b",
                Values =
                {
                    ["Name"] = "Beta",
                },
            },
        ]);
        instance.GetOrCreateEntityRecords("Person").Add(
            new GenericRecord
            {
                Id = "person-a",
                Values =
                {
                    ["LegacyName"] = "Original",
                    ["Note"] = "Remove me",
                },
                RelationshipIds =
                {
                    ["TeamId"] = "team-a",
                },
            });

        return new GenericMetadataState(model, instance);
    }

    internal static MetaOperationPlan BuildPlan()
    {
        return MetaOperationPlan.Create(
            new AddEntityOperation("Audit"),
            new RenamePropertyOperation(
                "Person",
                "LegacyName",
                "DisplayName"),
            new InsertRecordOperation(
                "Audit",
                "audit-1"),
            new InsertRecordOperation(
                "Person",
                "person-b",
                new Dictionary<string, string>
                {
                    ["DisplayName"] = "Beta",
                    ["Note"] = "Temporary",
                },
                new Dictionary<string, string>
                {
                    ["Team"] = "TEAM-A",
                }),
            new SetPropertyOperation(
                "Person",
                "person-a",
                "DisplayName",
                "Updated"),
            new ClearPropertyOperation(
                "Person",
                "person-a",
                "Note"),
            new SetRelationshipOperation(
                "Person",
                "person-a",
                "TeamId",
                "team-b"),
            new ClearRelationshipOperation(
                "Person",
                "person-b",
                "Team"),
            new InsertRecordOperation(
                "Audit",
                "transient"),
            new DeleteRecordOperation(
                "Audit",
                "transient"));
    }

    internal static string Canonicalize(GenericMetadataState state)
    {
        var builder = new StringBuilder();
        Append(builder, "model", state.Model.Name);

        foreach (var entity in state.Model.Entities
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, "entity", entity.Name);
            foreach (var property in entity.Properties
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Name, StringComparer.Ordinal))
            {
                Append(
                    builder,
                    "property",
                    entity.Name,
                    property.Name,
                    property.IsNullable ? "optional" : "required");
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.GetColumnName(), StringComparer.Ordinal))
            {
                Append(
                    builder,
                    "relationship",
                    entity.Name,
                    relationship.Entity,
                    relationship.Role,
                    relationship.IsNullable ? "optional" : "required");
            }

            var records = state.Instance.RecordsByEntity.TryGetValue(entity.Name, out var entityRecords)
                ? entityRecords
                : [];
            foreach (var record in records
                         .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                Append(builder, "record", entity.Name, record.Id);
                foreach (var value in record.Values
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Key, StringComparer.Ordinal))
                {
                    Append(builder, "value", entity.Name, record.Id, value.Key, value.Value);
                }

                foreach (var relationship in record.RelationshipIds
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Key, StringComparer.Ordinal))
                {
                    Append(
                        builder,
                        "target",
                        entity.Name,
                        record.Id,
                        relationship.Key,
                        relationship.Value);
                }
            }
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, params string[] values)
    {
        foreach (var value in values)
        {
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
        }

        builder.AppendLine();
    }

    private sealed class RecordingValidationService : IValidationService
    {
        private readonly ValidationService _inner = new();

        public List<int> RecordCounts { get; } = [];

        public WorkspaceDiagnostics Validate(Workspace workspace)
        {
            RecordCounts.Add(
                workspace.Instance.RecordsByEntity.Values.Sum(
                    records => records.Count));
            return _inner.Validate(workspace);
        }
    }
}
