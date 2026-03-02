using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Meta.Adapters;

namespace Meta.Core.Tests;

public sealed class ListNamingTests
{
    [Fact]
    public async Task WorkspaceService_UsesEntityListContainers_ForModelAndInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "metadata-list-tests", Guid.NewGuid().ToString("N"));
        var metadataRoot = Path.Combine(root, "metadata");
        var instanceRoot = Path.Combine(metadataRoot, "instance");
        Directory.CreateDirectory(instanceRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(metadataRoot, "model.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Model name="ListModel">
                  <EntityList>
                    <Entity name="Cube">
                      <PropertyList>
                        <Property name="Name" />
                      </PropertyList>
                    </Entity>
                    <Entity name="Person">
                      <PropertyList>
                        <Property name="Name" />
                      </PropertyList>
                    </Entity>
                  </EntityList>
                </Model>
                """);

            File.WriteAllText(
                Path.Combine(instanceRoot, "Cube.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <ListModel>
                  <CubeList>
                    <Cube Id="1">
                      <Name>Sales</Name>
                    </Cube>
                  </CubeList>
                </ListModel>
                """);

            File.WriteAllText(
                Path.Combine(instanceRoot, "Person.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <ListModel>
                  <PersonList>
                    <Person Id="1">
                      <Name>Alex</Name>
                    </Person>
                  </PersonList>
                </ListModel>
                """);

            var services = new ServiceCollection();
            var workspace = await services.WorkspaceService.LoadAsync(root, searchUpward: false);

            var cube = workspace.Model.FindEntity("Cube");
            var person = workspace.Model.FindEntity("Person");
            Assert.NotNull(cube);
            Assert.NotNull(person);
            Assert.Equal("CubeList", cube!.GetListName());
            Assert.Equal("PersonList", person!.GetListName());

            await services.WorkspaceService.SaveAsync(workspace);

            var savedModel = XDocument.Load(Path.Combine(metadataRoot, "model.xml"));
            var cubeEntity = savedModel.Root!.Element("EntityList")!.Elements("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Cube", StringComparison.OrdinalIgnoreCase));
            var personEntity = savedModel.Root!.Element("EntityList")!.Elements("Entity")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Person", StringComparison.OrdinalIgnoreCase));

            Assert.Null(cubeEntity.Attribute("plural"));
            Assert.Null(personEntity.Attribute("plural"));

            var savedCubeShard = XDocument.Load(Path.Combine(instanceRoot, "Cube.xml"));
            var savedPersonShard = XDocument.Load(Path.Combine(instanceRoot, "Person.xml"));
            Assert.NotNull(savedCubeShard.Root!.Element("CubeList"));
            Assert.NotNull(savedPersonShard.Root!.Element("PersonList"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
