using Meta.Integration;
using Meta.Operations;
using Meta.Operations.Domain;

namespace MetaWeave.Core;

public static class MetaWeaveWorkspaceContract
{
    public static GenericModel LoadModel() =>
        LoadModelAsync().GetAwaiter().GetResult();

    private static async Task<GenericModel> LoadModelAsync()
    {
        await using var workspace = await WorkspaceSurface.OpenAsync(
                Path.Combine(AppContext.BaseDirectory, "MetaWeave"))
            .ConfigureAwait(false);
        return (await WorkspaceComposition.MaterializeAsync(workspace)
            .ConfigureAwait(false)).Model;
    }
}
