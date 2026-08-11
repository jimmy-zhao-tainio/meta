using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.Operations.Tests;

public sealed class GenericModelTests
{
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var model = new GenericModel
        {
            Name = "People",
            Entities =
            {
                new GenericEntity
                {
                    Name = "Person",
                    Properties =
                    {
                        new GenericProperty { Name = "Name", IsNullable = false },
                    },
                    Relationships =
                    {
                        new GenericRelationship { Entity = "Team", Role = "PrimaryTeam" },
                    },
                },
            },
        };

        var clone = model.Clone();
        clone.Name = "PeopleClone";
        clone.Entities[0].Name = "PersonClone";
        clone.Entities[0].Properties[0].Name = "DisplayName";
        clone.Entities[0].Relationships[0].Role = "SecondaryTeam";

        Assert.Equal("People", model.Name);
        Assert.Equal("Person", model.Entities[0].Name);
        Assert.Equal("Name", model.Entities[0].Properties[0].Name);
        Assert.Equal("PrimaryTeam", model.Entities[0].Relationships[0].Role);
    }

    [Fact]
    public void ComputeContractSignature_IsCanonicalAcrossOrdering()
    {
        var left = new GenericModel
        {
            Name = "People",
            Entities =
            {
                new GenericEntity
                {
                    Name = "Person",
                    Properties =
                    {
                        new GenericProperty { Name = "Age", IsNullable = false },
                        new GenericProperty { Name = "Name", IsNullable = true },
                    },
                    Relationships =
                    {
                        new GenericRelationship { Entity = "Team", Role = "PrimaryTeam" },
                    },
                },
                new GenericEntity
                {
                    Name = "Team",
                },
            },
        };

        var right = new GenericModel
        {
            Name = "People",
            Entities =
            {
                new GenericEntity
                {
                    Name = "Team",
                },
                new GenericEntity
                {
                    Name = "Person",
                    Properties =
                    {
                        new GenericProperty { Name = "Name", IsNullable = true },
                        new GenericProperty { Name = "Age", IsNullable = false },
                    },
                    Relationships =
                    {
                        new GenericRelationship { Entity = "Team", Role = "PrimaryTeam" },
                    },
                },
            },
        };

        Assert.Equal(left.ComputeContractSignature(), right.ComputeContractSignature());
    }

    [Fact]
    public void ComputeContractSignature_ChangesWithRelationshipNullability()
    {
        var required = BuildRelationshipModel(isNullable: false);
        var optional = BuildRelationshipModel(isNullable: true);

        Assert.NotEqual(
            required.ComputeContractSignature(),
            optional.ComputeContractSignature());
    }

    private static GenericModel BuildRelationshipModel(bool isNullable)
    {
        var model = new GenericModel
        {
            Name = "People",
            Entities =
            {
                new GenericEntity { Name = "Team" },
            },
        };
        var person = new GenericEntity { Name = "Person" };
        person.Relationships.Add(new GenericRelationship
        {
            Entity = "Team",
            Role = "PrimaryTeam",
            IsNullable = isNullable,
        });
        model.Entities.Add(person);
        return model;
    }
}
