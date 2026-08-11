using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Xml;

public static class XmlWorkspaceOperationEffects
{
    internal static void Apply(
        Meta.Surfaces.Configuration.MetaWorkspace configuration,
        XmlWorkspaceLayout layout,
        IReadOnlyList<OperationResult> results)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(results);

        foreach (var result in results)
        {
            switch (result)
            {
                case RenameRecordResult renamedRecord:
                    layout.RenameRecord(
                        renamedRecord.EntityName,
                        renamedRecord.OldId,
                        renamedRecord.NewId);
                    break;

                case RenameEntityResult renamedEntity:
                    layout.RenameEntity(
                        renamedEntity.OldName,
                        renamedEntity.NewName);
                    foreach (var entityStorage in configuration.EntityStorage.Where(item =>
                                 MetaName.Comparer.Equals(
                                     item.EntityName,
                                     renamedEntity.OldName)))
                    {
                        entityStorage.EntityName = renamedEntity.NewName;
                    }

                    break;
            }
        }
    }
}
