using Meta.Core.Services;

namespace Meta.Core.Operations;

public sealed partial class MetaOperationInterpreter
{
    private readonly IValidationService _validationService;

    public MetaOperationInterpreter(IValidationService? validationService = null)
    {
        _validationService = validationService ?? new ValidationService();
    }

    public GenericMetaOperationResult Apply(
        GenericMetadataState source,
        MetaOperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);

        EnsureConforming(source, "Source metadata state is invalid.");
        var working = source.Clone();

        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];
            try
            {
                ApplyOperation(working, operation);
            }
            catch (Exception exception) when (exception is not MetaOperationException)
            {
                throw new MetaOperationException(
                    $"Operation {index + 1} ({operation.GetType().Name}) failed. {exception.Message}",
                    index,
                    operation,
                    exception);
            }
        }

        EnsureConforming(working, "Operation plan produced invalid metadata.");
        return new GenericMetaOperationResult(working, plan.Operations.Count);
    }

    private static void ApplyOperation(
        GenericMetadataState state,
        MetaOperation operation)
    {
        switch (operation)
        {
            case AddEntityOperation addEntity:
                ApplyAddEntity(state, addEntity);
                return;
            case RemoveEntityOperation removeEntity:
                ApplyRemoveEntity(state, removeEntity);
                return;
            case AddPropertyOperation addProperty:
                ApplyAddProperty(state, addProperty);
                return;
            case RemovePropertyOperation removeProperty:
                ApplyRemoveProperty(state, removeProperty);
                return;
            case RenamePropertyOperation renameProperty:
                ApplyRenameProperty(state, renameProperty);
                return;
            case SetPropertyRequiredOperation setPropertyRequired:
                ApplySetPropertyRequired(state, setPropertyRequired);
                return;
            case AddRelationshipOperation addRelationship:
                ApplyAddRelationship(state, addRelationship);
                return;
            case RemoveRelationshipOperation removeRelationship:
                ApplyRemoveRelationship(state, removeRelationship);
                return;
            case InsertRecordOperation insertRecord:
                ApplyInsertRecord(state, insertRecord);
                return;
            case SetPropertyOperation setProperty:
                ApplySetProperty(state, setProperty);
                return;
            case ClearPropertyOperation clearProperty:
                ApplyClearProperty(state, clearProperty);
                return;
            case SetRelationshipOperation setRelationship:
                ApplySetRelationship(state, setRelationship);
                return;
            case ClearRelationshipOperation clearRelationship:
                ApplyClearRelationship(state, clearRelationship);
                return;
            case DeleteRecordOperation deleteRecord:
                ApplyDeleteRecord(state, deleteRecord);
                return;
            default:
                throw new NotSupportedException(
                    $"Operation '{operation.GetType().Name}' is not supported by the generic interpreter.");
        }
    }
}
