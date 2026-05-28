using Meta.Core.Domain;

namespace Meta.Core.Tests;

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
                        new GenericProperty { Name = "Name", DataType = "string", IsNullable = false },
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
                        new GenericProperty { Name = "Age", DataType = "int", IsNullable = false },
                        new GenericProperty { Name = "Name", DataType = "string", IsNullable = true },
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
                        new GenericProperty { Name = "Name", DataType = "string", IsNullable = true },
                        new GenericProperty { Name = "Age", DataType = "int", IsNullable = false },
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
}
