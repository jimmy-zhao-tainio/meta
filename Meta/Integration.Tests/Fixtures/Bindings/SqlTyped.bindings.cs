// Derived from the modeled contract. Keep outside authored workspace source sets.
#nullable enable
namespace MetaTypedModelsSqlOrchestrationProof;
public sealed class MetaTypedModelsSqlOrchestrationProofWorkspaceBinding : global::Meta.TypedModels.ITypedWorkspaceBinding<global::MetaTypedModelsSqlOrchestrationProof.MetaTypedModelsSqlOrchestrationProofModel>
{
    private const string ContractSignature = "6d6f64656c7c4d65746154797065644d6f64656c7353716c4f726368657374726174696f6e50726f6f660a656e746974797c4974656d7c4974656d4c6973740a70726f70657274797c4974656d7c4e6f74657c6e756c6c61626c65";
    public global::MetaTypedModelsSqlOrchestrationProof.MetaTypedModelsSqlOrchestrationProofModel Read(global::Meta.Operations.Domain.InMemoryWorkspace state)
    {
        global::System.ArgumentNullException.ThrowIfNull(state);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(state.Model, ContractSignature);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        var typed = new global::MetaTypedModelsSqlOrchestrationProof.MetaTypedModelsSqlOrchestrationProofModel();
        var index0 = new global::System.Collections.Generic.Dictionary<string, global::MetaTypedModelsSqlOrchestrationProof.Item>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Item"))
        {
            var row = new global::MetaTypedModelsSqlOrchestrationProof.Item
            {
                Id = source.Id
            };
            row.Note = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Note");
            typed.ItemList.Add(row);
            index0.Add(source.Id, row);
        }

        return typed;
    }

    public global::Meta.Operations.Domain.InMemoryWorkspace Write(global::MetaTypedModelsSqlOrchestrationProof.MetaTypedModelsSqlOrchestrationProofModel model, global::Meta.Operations.Domain.GenericModel contract)
    {
        global::System.ArgumentNullException.ThrowIfNull(model);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(contract, ContractSignature);
        var instance = new global::Meta.Operations.Domain.GenericInstance
        {
            ModelName = contract.Name
        };
        var index0 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ItemList, row => row.Id, "Item");
        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index0))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Note is not null)
                target.Values.Add("Note", row.Note);
            instance.GetOrCreateEntityRecords("Item").Add(target);
        }

        var state = new global::Meta.Operations.Domain.InMemoryWorkspace(contract.Clone(), instance);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        return state;
    }
}