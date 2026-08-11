using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

public static class MetaSqlReader
{
    public static async Task<InMemoryWorkspace> ReadAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken = default)
    {
        await using var source = await SqlSchemaSource.OpenAsync(
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
