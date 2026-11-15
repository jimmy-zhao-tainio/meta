using Meta.Integration;
using Meta.Operations.Domain;

namespace MetaMesh.Core;

public static class MetaMeshWorkspaceContract
{
    public static GenericModel LoadModel() =>
        TypedWorkspaceModelMapper.LoadStateAsync(Path.Combine(AppContext.BaseDirectory, "MetaMesh"))
            .GetAwaiter().GetResult().Model;
}
