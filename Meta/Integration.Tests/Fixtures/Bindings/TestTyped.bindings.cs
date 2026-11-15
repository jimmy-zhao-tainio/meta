// Derived from the modeled contract. Keep outside authored workspace source sets.
#nullable enable
namespace TestTyped;
public sealed class TestTypedWorkspaceBinding : global::Meta.TypedModels.ITypedWorkspaceBinding<global::TestTyped.TestTypedModel>
{
    private const string ContractSignature = "6d6f64656c7c5465737454797065640a656e746974797c416c7068617c416c7068614c6973740a70726f70657274797c416c7068617c4e616d657c72657175697265640a70726f70657274797c416c7068617c4e6f74657c6e756c6c61626c650a72656c6174696f6e736869707c416c7068617c426574617c506172656e7449647c6e756c6c61626c650a656e746974797c426574617c426574614c6973740a70726f70657274797c426574617c4e616d657c7265717569726564";
    public global::TestTyped.TestTypedModel Read(global::Meta.Operations.Domain.InMemoryWorkspace state)
    {
        global::System.ArgumentNullException.ThrowIfNull(state);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(state.Model, ContractSignature);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        var typed = new global::TestTyped.TestTypedModel();
        var index0 = new global::System.Collections.Generic.Dictionary<string, global::TestTyped.Alpha>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Alpha"))
        {
            var row = new global::TestTyped.Alpha
            {
                Id = source.Id
            };
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.Note = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Note");
            typed.AlphaList.Add(row);
            index0.Add(source.Id, row);
        }

        var index1 = new global::System.Collections.Generic.Dictionary<string, global::TestTyped.Beta>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Beta"))
        {
            var row = new global::TestTyped.Beta
            {
                Id = source.Id
            };
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.BetaList.Add(row);
            index1.Add(source.Id, row);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Alpha"))
        {
            var row = index0[source.Id];
            row.Parent = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "ParentId", index1);
        }

        return typed;
    }

    public global::Meta.Operations.Domain.InMemoryWorkspace Write(global::TestTyped.TestTypedModel model, global::Meta.Operations.Domain.GenericModel contract)
    {
        global::System.ArgumentNullException.ThrowIfNull(model);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(contract, ContractSignature);
        var instance = new global::Meta.Operations.Domain.GenericInstance
        {
            ModelName = contract.Name
        };
        var index0 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.AlphaList, row => row.Id, "Alpha");
        var index1 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.BetaList, row => row.Id, "Beta");
        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index0))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.Note is not null)
                target.Values.Add("Note", row.Note);
            if (row.Parent is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parent, row.Parent.Id, index1, "Alpha.Parent");
                target.RelationshipIds.Add("ParentId", row.Parent.Id);
            }

            instance.GetOrCreateEntityRecords("Alpha").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index1))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            instance.GetOrCreateEntityRecords("Beta").Add(target);
        }

        var state = new global::Meta.Operations.Domain.InMemoryWorkspace(contract.Clone(), instance);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        return state;
    }
}