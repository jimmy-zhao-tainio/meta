using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Adapters;

public static class MetaSqlReader
{
    public static async Task<InMemoryWorkspace> ReadAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken = default)
    {
        await using var source = await SqlWorkspaceSource.OpenAsync(
                connectionString,
                schema,
                cancellationToken)
            .ConfigureAwait(false);
        return await WorkspaceComposition.MaterializeAsync(
                source,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
