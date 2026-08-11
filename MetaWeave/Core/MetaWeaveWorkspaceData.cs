using Meta.Operations.Domain;
using Meta.Operations;

namespace MetaWeave.Core;

internal static class MetaWeaveWorkspaceData
{
    public static IReadOnlyList<GenericRecord> ReadRecords(
        InMemoryWorkspace workspace,
        string entityName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Instance.RecordsByEntity.TryGetValue(
            entityName,
            out var records)
            ? records
            : Array.Empty<GenericRecord>();
    }
}
