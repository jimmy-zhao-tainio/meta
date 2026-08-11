using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Integration;
using Meta.Surfaces.CSharp;
using Meta.Surfaces.Sql;
using Meta.Surfaces.Xml;

namespace Meta.Core.Tests;

public sealed class MetaIdentityTests
{
    [Fact]
    public void Identity_PreservesCasingAndComparesCaseInsensitively()
    {
        const string authored = "Customer-North";

        var accepted = MetaIdentity.Require(authored, "Record Id.");

        Assert.Equal(authored, accepted);
        Assert.True(MetaIdentity.Comparer.Equals(authored, "customer-north"));
    }

    [Fact]
    public void Identity_EnforcesTheFourHundredFiftyCharacterBoundary()
    {
        Assert.Equal(450, MetaIdentity.MaximumLength);
        Assert.True(MetaIdentity.IsValid(new string('x', MetaIdentity.MaximumLength)));
        Assert.False(MetaIdentity.IsValid(new string('x', MetaIdentity.MaximumLength + 1)));
        Assert.False(MetaIdentity.IsValid(" padded"));
        Assert.False(MetaIdentity.IsValid("padded "));
    }

    [Fact]
    public void Identity_AcceptsPrintableAsciiAndRejectsOtherCharacters()
    {
        for (var character = ' '; character <= '~'; character++)
        {
            Assert.True(
                MetaIdentity.IsValid($"A{character}Z"),
                $"Printable ASCII character {(int)character} should be accepted.");
        }

        Assert.False(MetaIdentity.IsValid("tab\tcharacter"));
        Assert.False(MetaIdentity.IsValid("greek-\u03c3"));
        Assert.False(MetaIdentity.IsValid("sharp-\u00df"));
    }

    [Fact]
    public void Name_EnforcesTheOneHundredTwentyEightCharacterBoundary()
    {
        Assert.Equal(128, MetaName.MaximumLength);
        Assert.True(MetaName.IsValid("MixedCase_1"));
        Assert.True(MetaName.IsValid(new string('x', MetaName.MaximumLength)));
        Assert.False(MetaName.IsValid(new string('x', MetaName.MaximumLength + 1)));
        Assert.False(MetaName.IsValid("not-a-name"));
        Assert.True(MetaName.Comparer.Equals("MixedCase", "mixedcase"));
    }

    [Fact]
    public void SqlConstraintNames_StayWithinTheSharedNameBoundary()
    {
        var entity = new string('E', MetaName.MaximumLength);
        var relationship = new string('R', MetaName.MaximumLength);

        var primaryKey = SqlWorkspaceNames.PrimaryKey(entity);
        var foreignKey = SqlWorkspaceNames.ForeignKey(
            entity,
            entity,
            relationship);

        Assert.True(primaryKey.Length <= MetaName.MaximumLength);
        Assert.True(foreignKey.Length <= MetaName.MaximumLength);
        Assert.Equal(primaryKey, SqlWorkspaceNames.PrimaryKey(entity));
        Assert.Equal(
            foreignKey,
            SqlWorkspaceNames.ForeignKey(entity, entity, relationship));
    }

    [Fact]
    public void XmlInstanceLoad_RejectsIdentityBeyondTheSharedBoundary()
    {
        var model = BuildModel();
        var document = new XDocument(
            new XElement(
                "IdentityModel",
                new XElement(
                    "ItemList",
                    new XElement(
                        "Item",
                        new XAttribute("Id", new string('x', MetaIdentity.MaximumLength + 1))))));

        var exception = Assert.Throws<InvalidDataException>(
            () => InstanceXmlCodec.MergeDocument(
                new GenericInstance { ModelName = model.Name },
                document,
                model));

        Assert.Contains(
            MetaIdentity.MaximumLength.ToString(),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_RejectsCaseInsensitiveIdentityCollision()
    {
        var model = BuildModel();
        var workspace = new InMemoryWorkspace(
            model,
            new GenericInstance
            {
                ModelName = model.Name,
            });
        workspace.Instance.GetOrCreateEntityRecords("Item").Add(new GenericRecord { Id = "MixedCase" });
        workspace.Instance.GetOrCreateEntityRecords("Item").Add(new GenericRecord { Id = "mixedcase" });

        var diagnostics = WorkspaceValidator.Validate(
            workspace.Model,
            workspace.Instance);

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "instance.id.duplicate");
    }

    [Fact]
    public void Validation_RejectsMemberNameMatchingItsEntity()
    {
        var propertyModel = BuildModel();
        propertyModel.FindEntity("Item")!.Properties.Add(
            new GenericProperty
            {
                Name = "item",
            });
        var propertyDiagnostics = WorkspaceValidator.Validate(
            propertyModel,
            new GenericInstance
            {
                ModelName = propertyModel.Name,
            });

        var relationshipModel = BuildModel();
        relationshipModel.FindEntity("Item")!.Relationships.Add(
            new GenericRelationship
            {
                Entity = "Item",
                IsNullable = true,
            });
        var relationshipDiagnostics = WorkspaceValidator.Validate(
            relationshipModel,
            new GenericInstance
            {
                ModelName = relationshipModel.Name,
            });

        Assert.Contains(
            propertyDiagnostics.Issues,
            issue => issue.Code == "entity.member.matches-entity");
        Assert.Contains(
            relationshipDiagnostics.Issues,
            issue => issue.Code == "entity.member.matches-entity");
    }

    [Fact]
    public void Validation_RejectsPropertyAndRelationshipNavigationCollision()
    {
        var model = BuildModel();
        var entity = model.FindEntity("Item")!;
        entity.Properties.Add(new GenericProperty
        {
            Name = "Parent",
        });
        entity.Relationships.Add(new GenericRelationship
        {
            Entity = "Item",
            Role = "Parent",
            IsNullable = true,
        });

        var diagnostics = WorkspaceValidator.Validate(
            model,
            new GenericInstance
            {
                ModelName = model.Name,
            });

        Assert.Contains(
            diagnostics.Issues,
            issue => issue.Code == "entity.member.collision");
    }

    private static GenericModel BuildModel()
    {
        var model = new GenericModel
        {
            Name = "IdentityModel",
        };
        model.Entities.Add(new GenericEntity { Name = "Item" });
        return model;
    }
}
