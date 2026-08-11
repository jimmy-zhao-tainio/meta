using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Core.Services;

public sealed partial class InstanceDiffService : IInstanceDiffService
{
    private const string InstanceDiffEqualModelName = "InstanceDiffModelEqual";
    private const string InstanceDiffAlignedModelName = "InstanceDiffModelAligned";
    private const string InstanceDiffAlignmentModelName = "InstanceDiffModelAlignment";

    private const string DiffEntityName = "Diff";
    private const string ModelEntityName = "Model";
    private const string EntityEntityName = "Entity";
    private const string PropertyEntityName = "Property";
    private const string ModelLeftEntityInstanceEntityName = "ModelLeftEntityInstance";
    private const string ModelRightEntityInstanceEntityName = "ModelRightEntityInstance";
    private const string ModelLeftPropertyInstanceEntityName = "ModelLeftPropertyInstance";
    private const string ModelRightPropertyInstanceEntityName = "ModelRightPropertyInstance";
    private const string ModelLeftEntityInstanceNotInRightEntityName = "ModelLeftEntityInstanceNotInRight";
    private const string ModelRightEntityInstanceNotInLeftEntityName = "ModelRightEntityInstanceNotInLeft";
    private const string ModelLeftPropertyInstanceNotInRightEntityName = "ModelLeftPropertyInstanceNotInRight";
    private const string ModelRightPropertyInstanceNotInLeftEntityName = "ModelRightPropertyInstanceNotInLeft";

    private const string AlignmentEntityName = "Alignment";
    private const string ModelLeftEntityName = "ModelLeft";
    private const string ModelRightEntityName = "ModelRight";
    private const string ModelLeftEntityEntityName = "ModelLeftEntity";
    private const string ModelRightEntityEntityName = "ModelRightEntity";
    private const string ModelLeftPropertyEntityName = "ModelLeftProperty";
    private const string ModelRightPropertyEntityName = "ModelRightProperty";
    private const string EntityMapEntityName = "EntityMap";
    private const string PropertyMapEntityName = "PropertyMap";

    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffEqualWorkspaceDefinition =
        new(() => new InstanceDiffWorkspaceDefinition(CreateEqualModel()));
    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffAlignedWorkspaceDefinition =
        new(() => new InstanceDiffWorkspaceDefinition(CreateAlignedModel()));
    private static readonly Lazy<InstanceDiffWorkspaceDefinition> InstanceDiffAlignmentWorkspaceDefinition =
        new(() => new InstanceDiffWorkspaceDefinition(CreateAlignmentModel()));

    private static readonly Lazy<string> InstanceDiffEqualModelSignature =
        new(() => InstanceDiffEqualWorkspaceDefinition.Value.Model.ComputeContractSignature());
    private static readonly Lazy<string> InstanceDiffAlignedModelSignature =
        new(() => InstanceDiffAlignedWorkspaceDefinition.Value.Model.ComputeContractSignature());
    private static readonly Lazy<string> InstanceDiffAlignmentModelSignature =
        new(() => InstanceDiffAlignmentWorkspaceDefinition.Value.Model.ComputeContractSignature());

    private sealed record EqualEntityCatalog(
        string EntityId,
        string EntityName,
        GenericEntity ModelEntity,
        IReadOnlyDictionary<string, string> PropertyIdByName,
        IReadOnlyList<string> OrderedPropertyNames);

    private sealed record EqualSideData(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityRowPropertyKey,
        int RowCount,
        int PropertyCount);

    private sealed record EqualDiffData(
        IReadOnlyDictionary<string, EqualEntityCatalog> EntityCatalogByName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> OrderedPropertiesByEntity,
        IReadOnlyCollection<string> LeftRowSet,
        IReadOnlyCollection<string> RightRowSet,
        IReadOnlyCollection<string> LeftPropertySet,
        IReadOnlyCollection<string> RightPropertySet,
        IReadOnlyDictionary<string, string> RightValueByEntityRowPropertyKey,
        string DiffId);

    private sealed record AlignmentCatalog(
        string AlignmentId,
        string AlignmentName,
        string ModelLeftId,
        string ModelRightId,
        string ModelLeftName,
        string ModelRightName,
        IReadOnlyDictionary<string, string> LeftEntityNameById,
        IReadOnlyDictionary<string, string> RightEntityNameById,
        IReadOnlyDictionary<string, string> LeftPropertyNameById,
        IReadOnlyDictionary<string, string> RightPropertyNameById,
        IReadOnlyDictionary<string, string> LeftPropertyEntityIdByPropertyId,
        IReadOnlyDictionary<string, string> RightPropertyEntityIdByPropertyId,
        IReadOnlyDictionary<string, (string ModelLeftEntityId, string ModelRightEntityId)> EntityMapById,
        IReadOnlyDictionary<string, (string ModelLeftPropertyId, string ModelRightPropertyId)> PropertyMapById,
        IReadOnlyDictionary<string, string> EntityMapIdByLeftEntityId,
        IReadOnlyDictionary<string, string> EntityMapIdByRightEntityId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> PropertyMapIdsByEntityMapId);

    private sealed record AlignedSideData(
        IReadOnlyCollection<string> RowSet,
        IReadOnlyDictionary<string, string> EntityInstanceIdByRowKey,
        IReadOnlyCollection<string> PropertySet,
        IReadOnlyDictionary<string, string> PropertyInstanceIdByTupleKey,
        IReadOnlyDictionary<string, string> ValueByEntityMapRowPropertyMapKey,
        int RowCount,
        int PropertyCount);

    private sealed record AlignedDiffData(
        AlignmentCatalog Alignment,
        IReadOnlyCollection<string> LeftRowSet,
        IReadOnlyCollection<string> RightRowSet,
        IReadOnlyCollection<string> LeftPropertySet,
        IReadOnlyCollection<string> RightPropertySet,
        IReadOnlyDictionary<string, string> RightValueByEntityMapRowPropertyMapKey);

    private sealed record InstanceDiffWorkspaceDefinition(GenericModel Model);

    public InstanceDiffBuildResult BuildEqualDiffWorkspace(
        InMemoryWorkspace leftWorkspace,
        InMemoryWorkspace rightWorkspace)
    {
        ArgumentNullException.ThrowIfNull(leftWorkspace);
        ArgumentNullException.ThrowIfNull(rightWorkspace);
        return BuildEqualInstanceDiffWorkspace(leftWorkspace, rightWorkspace);
    }

    public InstanceDiffBuildResult BuildAlignedDiffWorkspace(
        InMemoryWorkspace leftWorkspace,
        InMemoryWorkspace rightWorkspace,
        InMemoryWorkspace alignmentWorkspace)
    {
        ArgumentNullException.ThrowIfNull(leftWorkspace);
        ArgumentNullException.ThrowIfNull(rightWorkspace);
        ArgumentNullException.ThrowIfNull(alignmentWorkspace);
        var alignment = ParseAlignmentCatalog(
            alignmentWorkspace,
            InstanceDiffAlignmentModelName,
            InstanceDiffAlignmentModelSignature.Value);
        ValidateWorkspaceMatchesAlignment(
            leftWorkspace,
            alignment.ModelLeftName,
            alignment.LeftEntityNameById,
            alignment.LeftPropertyNameById,
            alignment.LeftPropertyEntityIdByPropertyId);
        ValidateWorkspaceMatchesAlignment(
            rightWorkspace,
            alignment.ModelRightName,
            alignment.RightEntityNameById,
            alignment.RightPropertyNameById,
            alignment.RightPropertyEntityIdByPropertyId);

        return BuildAlignedInstanceDiffWorkspace(
            leftWorkspace,
            rightWorkspace,
            alignmentWorkspace,
            alignment);
    }

    public IReadOnlyList<Operation> PlanEqualDiffMerge(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace diffWorkspace)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(diffWorkspace);

        var diffData = ParseEqualDiffWorkspace(diffWorkspace);
        var preSnapshot = BuildWorkspaceSnapshotForEqualDiff(targetWorkspace, diffData);
        if (!preSnapshot.RowSet.SetEquals(diffData.LeftRowSet) ||
            !preSnapshot.PropertySet.SetEquals(diffData.LeftPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge precondition failed: target does not match the diff left snapshot.");
        }

        var current = targetWorkspace;
        var desired = current.Clone();
        ApplyEqualRightSnapshotToWorkspace(desired, diffData);
        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            current,
            desired);

        var postSnapshot = BuildWorkspaceSnapshotForEqualDiff(desired, diffData);
        if (!postSnapshot.RowSet.SetEquals(diffData.RightRowSet) ||
            !postSnapshot.PropertySet.SetEquals(diffData.RightPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge postcondition failed: target does not match the diff right snapshot.");
        }

        return operations;
    }

    public IReadOnlyList<Operation> PlanAlignedDiffMerge(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace diffWorkspace)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(diffWorkspace);

        var diffData = ParseAlignedDiffWorkspace(diffWorkspace);
        ValidateWorkspaceMatchesAlignment(
            targetWorkspace,
            diffData.Alignment.ModelLeftName,
            diffData.Alignment.LeftEntityNameById,
            diffData.Alignment.LeftPropertyNameById,
            diffData.Alignment.LeftPropertyEntityIdByPropertyId);

        var preSnapshot = BuildWorkspaceSnapshotForAlignedDiff(targetWorkspace, diffData.Alignment);
        if (!preSnapshot.RowSet.SetEquals(diffData.LeftRowSet) ||
            !preSnapshot.PropertySet.SetEquals(diffData.LeftPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge-aligned precondition failed: target does not match the diff left snapshot.");
        }

        var current = targetWorkspace;
        var desired = current.Clone();
        ApplyAlignedRightSnapshotToWorkspace(desired, diffData);
        var operations = WorkspaceSynchronization.PlanInstanceChanges(
            current,
            desired);

        var postSnapshot = BuildWorkspaceSnapshotForAlignedDiff(desired, diffData.Alignment);
        if (!postSnapshot.RowSet.SetEquals(diffData.RightRowSet) ||
            !postSnapshot.PropertySet.SetEquals(diffData.RightPropertySet))
        {
            throw new InvalidOperationException(
                "instance merge-aligned postcondition failed: target does not match the diff right snapshot.");
        }

        return operations;
    }

}
