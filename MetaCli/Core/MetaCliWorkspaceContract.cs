using Meta.Integration;
using Meta.Operations.Domain;

namespace MetaCli.Core;

/// <summary>Loads the packaged authored MetaCli model for creation of a new workspace.</summary>
public static class MetaCliWorkspaceContract
{
    public static GenericModel LoadModel() => TypedWorkspaceModelMapper
        .LoadStateAsync(Path.Combine(AppContext.BaseDirectory, "MetaCli"))
        .GetAwaiter().GetResult().Model;
}
