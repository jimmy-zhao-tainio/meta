using Meta.Integration;
using Meta.Operations.Domain;
using MetaDocs.Core;

namespace MetaDocs.Tests;

public sealed class MetaDocsWorkspaceBindingTests
{
    [Theory]
    [InlineData("xml")]
    [InlineData("csharp")]
    public void PersistedCustomVocabularyPreservesExactValues(string representation)
    {
        var path = Path.Combine(Path.GetTempPath(), "metadocs-explicit-binding", Guid.NewGuid().ToString("N"));
        var model = MetaDocsModel.CreateEmpty();
        model.DocumentationFactTypeList.AddRange(
        [
            new DocumentationFactType { Id = "custom:empty", Name = " 名 Ω ", Description = "" },
            new DocumentationFactType { Id = "custom:null", Name = "Other", Description = null },
            new DocumentationFactType { Id = "custom:spaces", Name = "Spaces", Description = "  retained  " }
        ]);
        try
        {
            TypedWorkspaceModelMapper.Create(model, MetaDocsModels.CreateMetaDocsModel(), new MetaDocsWorkspaceBinding(), path, representation);

            var loaded = TypedWorkspaceModelMapper.Load(path, new MetaDocsWorkspaceBinding());

            Assert.Equal(" 名 Ω ", Assert.Single(loaded.DocumentationFactTypeList, row => row.Id == "custom:empty").Name);
            Assert.Equal("", Assert.Single(loaded.DocumentationFactTypeList, row => row.Id == "custom:empty").Description);
            Assert.Null(Assert.Single(loaded.DocumentationFactTypeList, row => row.Id == "custom:null").Description);
            Assert.Equal("  retained  ", Assert.Single(loaded.DocumentationFactTypeList, row => row.Id == "custom:spaces").Description);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void PackagedModelReadsHaveIndependentMutableState()
    {
        var first = MetaDocsModels.CreateMetaDocsModel();
        first.Entities[0].Properties.Add(new GenericProperty { Name = "CallerOnly" });

        var second = MetaDocsModels.CreateMetaDocsModel();

        Assert.NotSame(first, second);
        Assert.DoesNotContain(second.Entities.SelectMany(entity => entity.Properties), property => property.Name == "CallerOnly");
    }
}
