using Meta.Operations.Domain;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService
{
    private static GenericModel CreateEqualModel() => CreateModel(
        InstanceDiffEqualModelName,
        Entity("Diff", ["Name", "DiffModelVersion"], [Relationship("Model")]),
        Entity("Model", ["Name"]),
        Entity("Entity", ["Name"], [Relationship("Model")]),
        Entity("Property", ["Name"], [Relationship("Entity")]),
        Entity("ModelLeftEntityInstance", ["EntityInstanceIdentifier"], [Relationship("Diff"), Relationship("Entity")]),
        Entity("ModelRightEntityInstance", ["EntityInstanceIdentifier"], [Relationship("Diff"), Relationship("Entity")]),
        Entity("ModelLeftPropertyInstance", ["Value"], [Relationship("ModelLeftEntityInstance"), Relationship("Property")]),
        Entity("ModelRightPropertyInstance", ["Value"], [Relationship("ModelRightEntityInstance"), Relationship("Property")]),
        Entity("ModelLeftEntityInstanceNotInRight", relationships: [Relationship("ModelLeftEntityInstance")]),
        Entity("ModelRightEntityInstanceNotInLeft", relationships: [Relationship("ModelRightEntityInstance")]),
        Entity("ModelLeftPropertyInstanceNotInRight", relationships: [Relationship("ModelLeftPropertyInstance")]),
        Entity("ModelRightPropertyInstanceNotInLeft", relationships: [Relationship("ModelRightPropertyInstance")]));

    private static GenericModel CreateAlignmentModel() => CreateModel(
        InstanceDiffAlignmentModelName,
        Entity("Alignment", ["Name"], [Relationship("ModelLeft"), Relationship("ModelRight")]),
        Entity("Model", ["Name"]),
        Entity("ModelLeft", relationships: [Relationship("Model")]),
        Entity("ModelRight", relationships: [Relationship("Model")]),
        Entity("ModelLeftEntity", ["Name"], [Relationship("ModelLeft")]),
        Entity("ModelRightEntity", ["Name"], [Relationship("ModelRight")]),
        Entity("ModelLeftProperty", ["Name"], [Relationship("ModelLeftEntity")]),
        Entity("ModelRightProperty", ["Name"], [Relationship("ModelRightEntity")]),
        Entity("EntityMap", relationships: [Relationship("ModelLeftEntity"), Relationship("ModelRightEntity")]),
        Entity("PropertyMap", relationships: [Relationship("ModelLeftProperty"), Relationship("ModelRightProperty")]));

    private static GenericModel CreateAlignedModel()
    {
        var model = CreateAlignmentModel();
        model.Name = InstanceDiffAlignedModelName;
        model.Entities.AddRange(
        [
            Entity("ModelLeftEntityInstance", ["EntityInstanceIdentifier"], [Relationship("ModelLeftEntity")]),
            Entity("ModelRightEntityInstance", ["EntityInstanceIdentifier"], [Relationship("ModelRightEntity")]),
            Entity("ModelLeftPropertyInstance", ["Value"], [Relationship("ModelLeftEntityInstance"), Relationship("ModelLeftProperty")]),
            Entity("ModelRightPropertyInstance", ["Value"], [Relationship("ModelRightEntityInstance"), Relationship("ModelRightProperty")]),
            Entity("ModelLeftEntityInstanceNotInRight", relationships: [Relationship("ModelLeftEntityInstance")]),
            Entity("ModelRightEntityInstanceNotInLeft", relationships: [Relationship("ModelRightEntityInstance")]),
            Entity("ModelLeftPropertyInstanceNotInRight", relationships: [Relationship("ModelLeftPropertyInstance")]),
            Entity("ModelRightPropertyInstanceNotInLeft", relationships: [Relationship("ModelRightPropertyInstance")]),
        ]);
        return model;
    }

    private static GenericModel CreateModel(
        string name,
        params GenericEntity[] entities)
    {
        var model = new GenericModel { Name = name };
        model.Entities.AddRange(entities);
        return model;
    }

    private static GenericEntity Entity(
        string name,
        IReadOnlyList<string>? properties = null,
        IReadOnlyList<GenericRelationship>? relationships = null)
    {
        var entity = new GenericEntity { Name = name };
        if (properties != null)
        {
            entity.Properties.AddRange(properties.Select(property => new GenericProperty { Name = property }));
        }

        if (relationships != null)
        {
            entity.Relationships.AddRange(relationships);
        }

        return entity;
    }

    private static GenericRelationship Relationship(string entity) => new()
    {
        Entity = entity,
    };
}
