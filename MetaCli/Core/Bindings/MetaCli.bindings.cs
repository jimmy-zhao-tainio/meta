// Derived from the modeled contract. Keep outside authored workspace source sets.
#nullable enable
namespace MetaCli;
public sealed class MetaCliWorkspaceBinding : global::Meta.TypedModels.ITypedWorkspaceBinding<global::MetaCli.MetaCliModel>
{
    private const string ContractSignature = "6d6f64656c7c4d657461436c690a656e746974797c416c6c6f77656456616c75657c416c6c6f77656456616c75654c6973740a70726f70657274797c416c6c6f77656456616c75657c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c416c6c6f77656456616c75657c56616c75657c72657175697265640a72656c6174696f6e736869707c416c6c6f77656456616c75657c56616c756553686170657c56616c7565536861706549647c72657175697265640a656e746974797c4170706c69636174696f6e7c4170706c69636174696f6e4c6973740a70726f70657274797c4170706c69636174696f6e7c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c4170706c69636174696f6e7c45786563757461626c654e616d657c6e756c6c61626c650a70726f70657274797c4170706c69636174696f6e7c4e616d657c72657175697265640a70726f70657274797c4170706c69636174696f6e7c56657273696f6e7c6e756c6c61626c650a656e746974797c4170706c69636174696f6e44656661756c74436f6d6d616e647c4170706c69636174696f6e44656661756c74436f6d6d616e644c6973740a72656c6174696f6e736869707c4170706c69636174696f6e44656661756c74436f6d6d616e647c4170706c69636174696f6e7c4170706c69636174696f6e49647c72657175697265640a72656c6174696f6e736869707c4170706c69636174696f6e44656661756c74436f6d6d616e647c45786563757461626c65436f6d6d616e647c45786563757461626c65436f6d6d616e6449647c72657175697265640a656e746974797c4170706c69636174696f6e506172616d657465727c4170706c69636174696f6e506172616d657465724c6973740a72656c6174696f6e736869707c4170706c69636174696f6e506172616d657465727c4170706c69636174696f6e7c4170706c69636174696f6e49647c72657175697265640a72656c6174696f6e736869707c4170706c69636174696f6e506172616d657465727c506172616d657465727c506172616d6574657249647c72657175697265640a656e746974797c436f6d6d616e647c436f6d6d616e644c6973740a70726f70657274797c436f6d6d616e647c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c436f6d6d616e647c4e616d657c72657175697265640a70726f70657274797c436f6d6d616e647c546f6b656e7c6e756c6c61626c650a72656c6174696f6e736869707c436f6d6d616e647c4170706c69636174696f6e7c4170706c69636174696f6e49647c72657175697265640a72656c6174696f6e736869707c436f6d6d616e647c436f6d6d616e647c506172656e74436f6d6d616e6449647c6e756c6c61626c650a656e746974797c45786563757461626c65436f6d6d616e647c45786563757461626c65436f6d6d616e644c6973740a72656c6174696f6e736869707c45786563757461626c65436f6d6d616e647c436f6d6d616e647c436f6d6d616e6449647c72657175697265640a656e746974797c45786563757461626c65436f6d6d616e64506172616d657465727c45786563757461626c65436f6d6d616e64506172616d657465724c6973740a72656c6174696f6e736869707c45786563757461626c65436f6d6d616e64506172616d657465727c45786563757461626c65436f6d6d616e647c45786563757461626c65436f6d6d616e6449647c72657175697265640a72656c6174696f6e736869707c45786563757461626c65436f6d6d616e64506172616d657465727c506172616d657465727c506172616d6574657249647c72657175697265640a656e746974797c4f7074696f6e7c4f7074696f6e4c6973740a72656c6174696f6e736869707c4f7074696f6e7c506172616d657465727c506172616d6574657249647c72657175697265640a656e746974797c4f7074696f6e546f6b656e7c4f7074696f6e546f6b656e4c6973740a70726f70657274797c4f7074696f6e546f6b656e7c546f6b656e7c72657175697265640a72656c6174696f6e736869707c4f7074696f6e546f6b656e7c4f7074696f6e7c4f7074696f6e49647c72657175697265640a72656c6174696f6e736869707c4f7074696f6e546f6b656e7c4f7074696f6e546f6b656e7c50726576696f7573546f6b656e49647c6e756c6c61626c650a656e746974797c506172616d657465727c506172616d657465724c6973740a70726f70657274797c506172616d657465727c44656661756c7456616c75657c6e756c6c61626c650a70726f70657274797c506172616d657465727c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c506172616d657465727c497352657065617461626c657c6e756c6c61626c650a70726f70657274797c506172616d657465727c497352657175697265647c6e756c6c61626c650a70726f70657274797c506172616d657465727c4e616d657c72657175697265640a72656c6174696f6e736869707c506172616d657465727c56616c756553686170657c56616c7565536861706549647c72657175697265640a656e746974797c506172616d6574657247726f75707c506172616d6574657247726f75704c6973740a70726f70657274797c506172616d6574657247726f75707c416c6c6f77734d756c7469706c657c6e756c6c61626c650a70726f70657274797c506172616d6574657247726f75707c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c506172616d6574657247726f75707c497352657175697265647c6e756c6c61626c650a70726f70657274797c506172616d6574657247726f75707c4e616d657c72657175697265640a72656c6174696f6e736869707c506172616d6574657247726f75707c45786563757461626c65436f6d6d616e647c45786563757461626c65436f6d6d616e6449647c72657175697265640a656e746974797c506172616d6574657247726f75704d656d6265727c506172616d6574657247726f75704d656d6265724c6973740a72656c6174696f6e736869707c506172616d6574657247726f75704d656d6265727c506172616d6574657247726f75707c506172616d6574657247726f757049647c72657175697265640a72656c6174696f6e736869707c506172616d6574657247726f75704d656d6265727c506172616d657465727c506172616d6574657249647c72657175697265640a72656c6174696f6e736869707c506172616d6574657247726f75704d656d6265727c506172616d6574657247726f75704d656d6265727c50726576696f75734d656d62657249647c6e756c6c61626c650a656e746974797c506f736974696f6e616c417267756d656e747c506f736974696f6e616c417267756d656e744c6973740a72656c6174696f6e736869707c506f736974696f6e616c417267756d656e747c506172616d657465727c506172616d6574657249647c72657175697265640a72656c6174696f6e736869707c506f736974696f6e616c417267756d656e747c506f736974696f6e616c417267756d656e747c50726576696f7573417267756d656e7449647c6e756c6c61626c650a656e746974797c56616c756541726974797c56616c756541726974794c6973740a70726f70657274797c56616c756541726974797c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c56616c756541726974797c4d617856616c7565436f756e747c6e756c6c61626c650a70726f70657274797c56616c756541726974797c4d696e56616c7565436f756e747c72657175697265640a70726f70657274797c56616c756541726974797c4e616d657c72657175697265640a656e746974797c56616c756553686170657c56616c756553686170654c6973740a70726f70657274797c56616c756553686170657c416c6c6f77734f7074696f6e4c696b6556616c75657c6e756c6c61626c650a70726f70657274797c56616c756553686170657c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c56616c756553686170657c4e616d657c72657175697265640a70726f70657274797c56616c756553686170657c56616c75654c6162656c7c6e756c6c61626c650a72656c6174696f6e736869707c56616c756553686170657c56616c756541726974797c56616c7565417269747949647c7265717569726564";
    public global::MetaCli.MetaCliModel Read(global::Meta.Operations.Domain.InMemoryWorkspace state)
    {
        global::System.ArgumentNullException.ThrowIfNull(state);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(state.Model, ContractSignature);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        var typed = new global::MetaCli.MetaCliModel();
        var index0 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.AllowedValue>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "AllowedValue"))
        {
            var row = new global::MetaCli.AllowedValue
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Value = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Value");
            typed.AllowedValueList.Add(row);
            index0.Add(source.Id, row);
        }

        var index1 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.Application>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Application"))
        {
            var row = new global::MetaCli.Application
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.ExecutableName = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "ExecutableName");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.Version = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Version");
            typed.ApplicationList.Add(row);
            index1.Add(source.Id, row);
        }

        var index2 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ApplicationDefaultCommand>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ApplicationDefaultCommand"))
        {
            var row = new global::MetaCli.ApplicationDefaultCommand
            {
                Id = source.Id
            };
            typed.ApplicationDefaultCommandList.Add(row);
            index2.Add(source.Id, row);
        }

        var index3 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ApplicationParameter>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ApplicationParameter"))
        {
            var row = new global::MetaCli.ApplicationParameter
            {
                Id = source.Id
            };
            typed.ApplicationParameterList.Add(row);
            index3.Add(source.Id, row);
        }

        var index4 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.Command>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Command"))
        {
            var row = new global::MetaCli.Command
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.Token = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Token");
            typed.CommandList.Add(row);
            index4.Add(source.Id, row);
        }

        var index5 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ExecutableCommand>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ExecutableCommand"))
        {
            var row = new global::MetaCli.ExecutableCommand
            {
                Id = source.Id
            };
            typed.ExecutableCommandList.Add(row);
            index5.Add(source.Id, row);
        }

        var index6 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ExecutableCommandParameter>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ExecutableCommandParameter"))
        {
            var row = new global::MetaCli.ExecutableCommandParameter
            {
                Id = source.Id
            };
            typed.ExecutableCommandParameterList.Add(row);
            index6.Add(source.Id, row);
        }

        var index7 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.Option>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Option"))
        {
            var row = new global::MetaCli.Option
            {
                Id = source.Id
            };
            typed.OptionList.Add(row);
            index7.Add(source.Id, row);
        }

        var index8 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.OptionToken>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "OptionToken"))
        {
            var row = new global::MetaCli.OptionToken
            {
                Id = source.Id
            };
            row.Token = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Token");
            typed.OptionTokenList.Add(row);
            index8.Add(source.Id, row);
        }

        var index9 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.Parameter>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Parameter"))
        {
            var row = new global::MetaCli.Parameter
            {
                Id = source.Id
            };
            row.DefaultValue = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "DefaultValue");
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.IsRepeatable = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "IsRepeatable");
            row.IsRequired = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "IsRequired");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.ParameterList.Add(row);
            index9.Add(source.Id, row);
        }

        var index10 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ParameterGroup>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ParameterGroup"))
        {
            var row = new global::MetaCli.ParameterGroup
            {
                Id = source.Id
            };
            row.AllowsMultiple = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "AllowsMultiple");
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.IsRequired = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "IsRequired");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.ParameterGroupList.Add(row);
            index10.Add(source.Id, row);
        }

        var index11 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ParameterGroupMember>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ParameterGroupMember"))
        {
            var row = new global::MetaCli.ParameterGroupMember
            {
                Id = source.Id
            };
            typed.ParameterGroupMemberList.Add(row);
            index11.Add(source.Id, row);
        }

        var index12 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.PositionalArgument>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "PositionalArgument"))
        {
            var row = new global::MetaCli.PositionalArgument
            {
                Id = source.Id
            };
            typed.PositionalArgumentList.Add(row);
            index12.Add(source.Id, row);
        }

        var index13 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ValueArity>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ValueArity"))
        {
            var row = new global::MetaCli.ValueArity
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.MaxValueCount = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "MaxValueCount");
            row.MinValueCount = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "MinValueCount");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.ValueArityList.Add(row);
            index13.Add(source.Id, row);
        }

        var index14 = new global::System.Collections.Generic.Dictionary<string, global::MetaCli.ValueShape>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ValueShape"))
        {
            var row = new global::MetaCli.ValueShape
            {
                Id = source.Id
            };
            row.AllowsOptionLikeValue = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "AllowsOptionLikeValue");
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.ValueLabel = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "ValueLabel");
            typed.ValueShapeList.Add(row);
            index14.Add(source.Id, row);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "AllowedValue"))
        {
            var row = index0[source.Id];
            row.ValueShape = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ValueShapeId", index14);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ApplicationDefaultCommand"))
        {
            var row = index2[source.Id];
            row.Application = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ApplicationId", index1);
            row.ExecutableCommand = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ExecutableCommandId", index5);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ApplicationParameter"))
        {
            var row = index3[source.Id];
            row.Application = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ApplicationId", index1);
            row.Parameter = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterId", index9);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Command"))
        {
            var row = index4[source.Id];
            row.Application = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ApplicationId", index1);
            row.ParentCommand = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "ParentCommandId", index4);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ExecutableCommand"))
        {
            var row = index5[source.Id];
            row.Command = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "CommandId", index4);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ExecutableCommandParameter"))
        {
            var row = index6[source.Id];
            row.ExecutableCommand = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ExecutableCommandId", index5);
            row.Parameter = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterId", index9);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Option"))
        {
            var row = index7[source.Id];
            row.Parameter = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterId", index9);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "OptionToken"))
        {
            var row = index8[source.Id];
            row.Option = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "OptionId", index7);
            row.PreviousToken = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "PreviousTokenId", index8);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Parameter"))
        {
            var row = index9[source.Id];
            row.ValueShape = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ValueShapeId", index14);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ParameterGroup"))
        {
            var row = index10[source.Id];
            row.ExecutableCommand = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ExecutableCommandId", index5);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ParameterGroupMember"))
        {
            var row = index11[source.Id];
            row.ParameterGroup = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterGroupId", index10);
            row.Parameter = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterId", index9);
            row.PreviousMember = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "PreviousMemberId", index11);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "PositionalArgument"))
        {
            var row = index12[source.Id];
            row.Parameter = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ParameterId", index9);
            row.PreviousArgument = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "PreviousArgumentId", index12);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "ValueShape"))
        {
            var row = index14[source.Id];
            row.ValueArity = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "ValueArityId", index13);
        }

        return typed;
    }

    public global::Meta.Operations.Domain.InMemoryWorkspace Write(global::MetaCli.MetaCliModel model, global::Meta.Operations.Domain.GenericModel contract)
    {
        global::System.ArgumentNullException.ThrowIfNull(model);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(contract, ContractSignature);
        var instance = new global::Meta.Operations.Domain.GenericInstance
        {
            ModelName = contract.Name
        };
        var index0 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.AllowedValueList, row => row.Id, "AllowedValue");
        var index1 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ApplicationList, row => row.Id, "Application");
        var index2 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ApplicationDefaultCommandList, row => row.Id, "ApplicationDefaultCommand");
        var index3 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ApplicationParameterList, row => row.Id, "ApplicationParameter");
        var index4 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.CommandList, row => row.Id, "Command");
        var index5 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ExecutableCommandList, row => row.Id, "ExecutableCommand");
        var index6 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ExecutableCommandParameterList, row => row.Id, "ExecutableCommandParameter");
        var index7 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.OptionList, row => row.Id, "Option");
        var index8 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.OptionTokenList, row => row.Id, "OptionToken");
        var index9 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ParameterList, row => row.Id, "Parameter");
        var index10 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ParameterGroupList, row => row.Id, "ParameterGroup");
        var index11 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ParameterGroupMemberList, row => row.Id, "ParameterGroupMember");
        var index12 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.PositionalArgumentList, row => row.Id, "PositionalArgument");
        var index13 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ValueArityList, row => row.Id, "ValueArity");
        var index14 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.ValueShapeList, row => row.Id, "ValueShape");
        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index0))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Value is not null)
                target.Values.Add("Value", row.Value);
            if (row.ValueShape is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ValueShape, row.ValueShape.Id, index14, "AllowedValue.ValueShape");
                target.RelationshipIds.Add("ValueShapeId", row.ValueShape.Id);
            }

            instance.GetOrCreateEntityRecords("AllowedValue").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index1))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.ExecutableName is not null)
                target.Values.Add("ExecutableName", row.ExecutableName);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.Version is not null)
                target.Values.Add("Version", row.Version);
            instance.GetOrCreateEntityRecords("Application").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index2))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Application is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Application, row.Application.Id, index1, "ApplicationDefaultCommand.Application");
                target.RelationshipIds.Add("ApplicationId", row.Application.Id);
            }

            if (row.ExecutableCommand is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ExecutableCommand, row.ExecutableCommand.Id, index5, "ApplicationDefaultCommand.ExecutableCommand");
                target.RelationshipIds.Add("ExecutableCommandId", row.ExecutableCommand.Id);
            }

            instance.GetOrCreateEntityRecords("ApplicationDefaultCommand").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index3))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Application is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Application, row.Application.Id, index1, "ApplicationParameter.Application");
                target.RelationshipIds.Add("ApplicationId", row.Application.Id);
            }

            if (row.Parameter is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parameter, row.Parameter.Id, index9, "ApplicationParameter.Parameter");
                target.RelationshipIds.Add("ParameterId", row.Parameter.Id);
            }

            instance.GetOrCreateEntityRecords("ApplicationParameter").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index4))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.Token is not null)
                target.Values.Add("Token", row.Token);
            if (row.Application is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Application, row.Application.Id, index1, "Command.Application");
                target.RelationshipIds.Add("ApplicationId", row.Application.Id);
            }

            if (row.ParentCommand is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ParentCommand, row.ParentCommand.Id, index4, "Command.ParentCommand");
                target.RelationshipIds.Add("ParentCommandId", row.ParentCommand.Id);
            }

            instance.GetOrCreateEntityRecords("Command").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index5))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Command is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Command, row.Command.Id, index4, "ExecutableCommand.Command");
                target.RelationshipIds.Add("CommandId", row.Command.Id);
            }

            instance.GetOrCreateEntityRecords("ExecutableCommand").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index6))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.ExecutableCommand is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ExecutableCommand, row.ExecutableCommand.Id, index5, "ExecutableCommandParameter.ExecutableCommand");
                target.RelationshipIds.Add("ExecutableCommandId", row.ExecutableCommand.Id);
            }

            if (row.Parameter is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parameter, row.Parameter.Id, index9, "ExecutableCommandParameter.Parameter");
                target.RelationshipIds.Add("ParameterId", row.Parameter.Id);
            }

            instance.GetOrCreateEntityRecords("ExecutableCommandParameter").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index7))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Parameter is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parameter, row.Parameter.Id, index9, "Option.Parameter");
                target.RelationshipIds.Add("ParameterId", row.Parameter.Id);
            }

            instance.GetOrCreateEntityRecords("Option").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index8))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Token is not null)
                target.Values.Add("Token", row.Token);
            if (row.Option is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Option, row.Option.Id, index7, "OptionToken.Option");
                target.RelationshipIds.Add("OptionId", row.Option.Id);
            }

            if (row.PreviousToken is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.PreviousToken, row.PreviousToken.Id, index8, "OptionToken.PreviousToken");
                target.RelationshipIds.Add("PreviousTokenId", row.PreviousToken.Id);
            }

            instance.GetOrCreateEntityRecords("OptionToken").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index9))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.DefaultValue is not null)
                target.Values.Add("DefaultValue", row.DefaultValue);
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.IsRepeatable is not null)
                target.Values.Add("IsRepeatable", row.IsRepeatable);
            if (row.IsRequired is not null)
                target.Values.Add("IsRequired", row.IsRequired);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.ValueShape is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ValueShape, row.ValueShape.Id, index14, "Parameter.ValueShape");
                target.RelationshipIds.Add("ValueShapeId", row.ValueShape.Id);
            }

            instance.GetOrCreateEntityRecords("Parameter").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index10))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.AllowsMultiple is not null)
                target.Values.Add("AllowsMultiple", row.AllowsMultiple);
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.IsRequired is not null)
                target.Values.Add("IsRequired", row.IsRequired);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.ExecutableCommand is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ExecutableCommand, row.ExecutableCommand.Id, index5, "ParameterGroup.ExecutableCommand");
                target.RelationshipIds.Add("ExecutableCommandId", row.ExecutableCommand.Id);
            }

            instance.GetOrCreateEntityRecords("ParameterGroup").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index11))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.ParameterGroup is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ParameterGroup, row.ParameterGroup.Id, index10, "ParameterGroupMember.ParameterGroup");
                target.RelationshipIds.Add("ParameterGroupId", row.ParameterGroup.Id);
            }

            if (row.Parameter is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parameter, row.Parameter.Id, index9, "ParameterGroupMember.Parameter");
                target.RelationshipIds.Add("ParameterId", row.Parameter.Id);
            }

            if (row.PreviousMember is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.PreviousMember, row.PreviousMember.Id, index11, "ParameterGroupMember.PreviousMember");
                target.RelationshipIds.Add("PreviousMemberId", row.PreviousMember.Id);
            }

            instance.GetOrCreateEntityRecords("ParameterGroupMember").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index12))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Parameter is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Parameter, row.Parameter.Id, index9, "PositionalArgument.Parameter");
                target.RelationshipIds.Add("ParameterId", row.Parameter.Id);
            }

            if (row.PreviousArgument is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.PreviousArgument, row.PreviousArgument.Id, index12, "PositionalArgument.PreviousArgument");
                target.RelationshipIds.Add("PreviousArgumentId", row.PreviousArgument.Id);
            }

            instance.GetOrCreateEntityRecords("PositionalArgument").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index13))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.MaxValueCount is not null)
                target.Values.Add("MaxValueCount", row.MaxValueCount);
            if (row.MinValueCount is not null)
                target.Values.Add("MinValueCount", row.MinValueCount);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            instance.GetOrCreateEntityRecords("ValueArity").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index14))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.AllowsOptionLikeValue is not null)
                target.Values.Add("AllowsOptionLikeValue", row.AllowsOptionLikeValue);
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.ValueLabel is not null)
                target.Values.Add("ValueLabel", row.ValueLabel);
            if (row.ValueArity is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.ValueArity, row.ValueArity.Id, index13, "ValueShape.ValueArity");
                target.RelationshipIds.Add("ValueArityId", row.ValueArity.Id);
            }

            instance.GetOrCreateEntityRecords("ValueShape").Add(target);
        }

        var state = new global::Meta.Operations.Domain.InMemoryWorkspace(contract.Clone(), instance);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        return state;
    }
}