namespace Meta.Operations;

public interface IOperationTarget
{
    RenameModelResult Apply(Operation.RenameModel operation);
    OperationResult Apply(Operation.AddEntity operation);
    OperationResult Apply(Operation.RemoveEntity operation);
    RenameEntityResult Apply(Operation.RenameEntity operation);
    OperationResult Apply(Operation.AddProperty operation);
    OperationResult Apply(Operation.RemoveProperty operation);
    OperationResult Apply(Operation.RenameProperty operation);
    OperationResult Apply(Operation.SetPropertyRequired operation);
    OperationResult Apply(Operation.AddRelationship operation);
    OperationResult Apply(Operation.RemoveRelationship operation);
    RenameRelationshipResult Apply(Operation.RenameRelationship operation);
    OperationResult Apply(Operation.RetargetRelationship operation);
    OperationResult Apply(Operation.SetRelationshipRequired operation);
    OperationResult Apply(Operation.InsertRecord operation);
    OperationResult Apply(Operation.DeleteRecord operation);
    RenameRecordResult Apply(Operation.RenameRecord operation);
    OperationResult Apply(Operation.SetProperty operation);
    OperationResult Apply(Operation.ClearProperty operation);
    OperationResult Apply(Operation.SetRelationship operation);
    OperationResult Apply(Operation.ClearRelationship operation);
    PropertyToRelationshipResult Apply(
        Operation.PropertyToRelationship operation);
    RelationshipToPropertyResult Apply(
        Operation.RelationshipToProperty operation);
}
