// Derived from the modeled contract. Keep outside authored workspace source sets.
#nullable enable
namespace MetaMesh;
public sealed class MetaMeshWorkspaceBinding : global::Meta.TypedModels.ITypedWorkspaceBinding<global::MetaMesh.MetaMeshModel>
{
    private const string ContractSignature = "6d6f64656c7c4d6574614d6573680a656e746974797c435368617270576f726b73706163657c435368617270576f726b73706163654c6973740a70726f70657274797c435368617270576f726b73706163657c506174687c72657175697265640a72656c6174696f6e736869707c435368617270576f726b73706163657c576f726b73706163657c576f726b737061636549647c72657175697265640a656e746974797c4d6573687c4d6573684c6973740a70726f70657274797c4d6573687c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c4d6573687c4e616d657c72657175697265640a70726f70657274797c4d6573687c526f6f74506174687c6e756c6c61626c650a656e746974797c4f7065726174696f6e7c4f7065726174696f6e4c6973740a70726f70657274797c4f7065726174696f6e7c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c4f7065726174696f6e7c4e616d657c72657175697265640a72656c6174696f6e736869707c4f7065726174696f6e7c4d6573687c4d65736849647c72657175697265640a656e746974797c4f7065726174696f6e537465707c4f7065726174696f6e537465704c6973740a70726f70657274797c4f7065726174696f6e537465707c417267756d656e74737c6e756c6c61626c650a70726f70657274797c4f7065726174696f6e537465707c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c4f7065726174696f6e537465707c45786563757461626c657c72657175697265640a70726f70657274797c4f7065726174696f6e537465707c457870656374656445786974436f64657c6e756c6c61626c650a70726f70657274797c4f7065726174696f6e537465707c4e616d657c72657175697265640a70726f70657274797c4f7065726174696f6e537465707c576f726b696e674469726563746f72797c6e756c6c61626c650a72656c6174696f6e736869707c4f7065726174696f6e537465707c4f7065726174696f6e7c4f7065726174696f6e49647c72657175697265640a72656c6174696f6e736869707c4f7065726174696f6e537465707c4f7065726174696f6e537465707c50726576696f75735374657049647c6e756c6c61626c650a656e746974797c53716c576f726b73706163657c53716c576f726b73706163654c6973740a70726f70657274797c53716c576f726b73706163657c436f6e6e656374696f6e456e7669726f6e6d656e745661726961626c657c72657175697265640a72656c6174696f6e736869707c53716c576f726b73706163657c576f726b73706163657c576f726b737061636549647c72657175697265640a656e746974797c576f726b73706163657c576f726b73706163654c6973740a70726f70657274797c576f726b73706163657c4465736372697074696f6e7c6e756c6c61626c650a70726f70657274797c576f726b73706163657c4d6f64656c4e616d657c6e756c6c61626c650a70726f70657274797c576f726b73706163657c4e616d657c72657175697265640a72656c6174696f6e736869707c576f726b73706163657c4d6573687c4d65736849647c72657175697265640a656e746974797c586d6c576f726b73706163657c586d6c576f726b73706163654c6973740a70726f70657274797c586d6c576f726b73706163657c506174687c72657175697265640a72656c6174696f6e736869707c586d6c576f726b73706163657c576f726b73706163657c576f726b737061636549647c7265717569726564";
    public global::MetaMesh.MetaMeshModel Read(global::Meta.Operations.Domain.InMemoryWorkspace state)
    {
        global::System.ArgumentNullException.ThrowIfNull(state);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(state.Model, ContractSignature);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        var typed = new global::MetaMesh.MetaMeshModel();
        var index0 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.CSharpWorkspace>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "CSharpWorkspace"))
        {
            var row = new global::MetaMesh.CSharpWorkspace
            {
                Id = source.Id
            };
            row.Path = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Path");
            typed.CSharpWorkspaceList.Add(row);
            index0.Add(source.Id, row);
        }

        var index1 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.Mesh>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Mesh"))
        {
            var row = new global::MetaMesh.Mesh
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.RootPath = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "RootPath");
            typed.MeshList.Add(row);
            index1.Add(source.Id, row);
        }

        var index2 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.Operation>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Operation"))
        {
            var row = new global::MetaMesh.Operation
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.OperationList.Add(row);
            index2.Add(source.Id, row);
        }

        var index3 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.OperationStep>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "OperationStep"))
        {
            var row = new global::MetaMesh.OperationStep
            {
                Id = source.Id
            };
            row.Arguments = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Arguments");
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.Executable = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Executable");
            row.ExpectedExitCode = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "ExpectedExitCode");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            row.WorkingDirectory = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "WorkingDirectory");
            typed.OperationStepList.Add(row);
            index3.Add(source.Id, row);
        }

        var index4 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.SqlWorkspace>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "SqlWorkspace"))
        {
            var row = new global::MetaMesh.SqlWorkspace
            {
                Id = source.Id
            };
            row.ConnectionEnvironmentVariable = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "ConnectionEnvironmentVariable");
            typed.SqlWorkspaceList.Add(row);
            index4.Add(source.Id, row);
        }

        var index5 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.Workspace>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Workspace"))
        {
            var row = new global::MetaMesh.Workspace
            {
                Id = source.Id
            };
            row.Description = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "Description");
            row.ModelName = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalValue(source, "ModelName");
            row.Name = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Name");
            typed.WorkspaceList.Add(row);
            index5.Add(source.Id, row);
        }

        var index6 = new global::System.Collections.Generic.Dictionary<string, global::MetaMesh.XmlWorkspace>(global::Meta.Operations.Domain.MetaIdentity.Comparer);
        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "XmlWorkspace"))
        {
            var row = new global::MetaMesh.XmlWorkspace
            {
                Id = source.Id
            };
            row.Path = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredValue(source, "Path");
            typed.XmlWorkspaceList.Add(row);
            index6.Add(source.Id, row);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "CSharpWorkspace"))
        {
            var row = index0[source.Id];
            row.Workspace = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "WorkspaceId", index5);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Operation"))
        {
            var row = index2[source.Id];
            row.Mesh = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "MeshId", index1);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "OperationStep"))
        {
            var row = index3[source.Id];
            row.Operation = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "OperationId", index2);
            row.PreviousStep = global::Meta.TypedModels.TypedWorkspaceBindingValidation.OptionalTarget(source, "PreviousStepId", index3);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "SqlWorkspace"))
        {
            var row = index4[source.Id];
            row.Workspace = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "WorkspaceId", index5);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "Workspace"))
        {
            var row = index5[source.Id];
            row.Mesh = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "MeshId", index1);
        }

        foreach (var source in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Records(state, "XmlWorkspace"))
        {
            var row = index6[source.Id];
            row.Workspace = global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequiredTarget(source, "WorkspaceId", index5);
        }

        return typed;
    }

    public global::Meta.Operations.Domain.InMemoryWorkspace Write(global::MetaMesh.MetaMeshModel model, global::Meta.Operations.Domain.GenericModel contract)
    {
        global::System.ArgumentNullException.ThrowIfNull(model);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireContract(contract, ContractSignature);
        var instance = new global::Meta.Operations.Domain.GenericInstance
        {
            ModelName = contract.Name
        };
        var index0 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.CSharpWorkspaceList, row => row.Id, "CSharpWorkspace");
        var index1 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.MeshList, row => row.Id, "Mesh");
        var index2 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.OperationList, row => row.Id, "Operation");
        var index3 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.OperationStepList, row => row.Id, "OperationStep");
        var index4 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.SqlWorkspaceList, row => row.Id, "SqlWorkspace");
        var index5 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.WorkspaceList, row => row.Id, "Workspace");
        var index6 = global::Meta.TypedModels.TypedWorkspaceBindingValidation.Index(model.XmlWorkspaceList, row => row.Id, "XmlWorkspace");
        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index0))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Path is not null)
                target.Values.Add("Path", row.Path);
            if (row.Workspace is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Workspace, row.Workspace.Id, index5, "CSharpWorkspace.Workspace");
                target.RelationshipIds.Add("WorkspaceId", row.Workspace.Id);
            }

            instance.GetOrCreateEntityRecords("CSharpWorkspace").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index1))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.RootPath is not null)
                target.Values.Add("RootPath", row.RootPath);
            instance.GetOrCreateEntityRecords("Mesh").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index2))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.Mesh is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Mesh, row.Mesh.Id, index1, "Operation.Mesh");
                target.RelationshipIds.Add("MeshId", row.Mesh.Id);
            }

            instance.GetOrCreateEntityRecords("Operation").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index3))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Arguments is not null)
                target.Values.Add("Arguments", row.Arguments);
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.Executable is not null)
                target.Values.Add("Executable", row.Executable);
            if (row.ExpectedExitCode is not null)
                target.Values.Add("ExpectedExitCode", row.ExpectedExitCode);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.WorkingDirectory is not null)
                target.Values.Add("WorkingDirectory", row.WorkingDirectory);
            if (row.Operation is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Operation, row.Operation.Id, index2, "OperationStep.Operation");
                target.RelationshipIds.Add("OperationId", row.Operation.Id);
            }

            if (row.PreviousStep is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.PreviousStep, row.PreviousStep.Id, index3, "OperationStep.PreviousStep");
                target.RelationshipIds.Add("PreviousStepId", row.PreviousStep.Id);
            }

            instance.GetOrCreateEntityRecords("OperationStep").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index4))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.ConnectionEnvironmentVariable is not null)
                target.Values.Add("ConnectionEnvironmentVariable", row.ConnectionEnvironmentVariable);
            if (row.Workspace is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Workspace, row.Workspace.Id, index5, "SqlWorkspace.Workspace");
                target.RelationshipIds.Add("WorkspaceId", row.Workspace.Id);
            }

            instance.GetOrCreateEntityRecords("SqlWorkspace").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index5))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Description is not null)
                target.Values.Add("Description", row.Description);
            if (row.ModelName is not null)
                target.Values.Add("ModelName", row.ModelName);
            if (row.Name is not null)
                target.Values.Add("Name", row.Name);
            if (row.Mesh is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Mesh, row.Mesh.Id, index1, "Workspace.Mesh");
                target.RelationshipIds.Add("MeshId", row.Mesh.Id);
            }

            instance.GetOrCreateEntityRecords("Workspace").Add(target);
        }

        foreach (var row in global::Meta.TypedModels.TypedWorkspaceBindingValidation.Ordered(index6))
        {
            var target = new global::Meta.Operations.Domain.GenericRecord
            {
                Id = row.Id
            };
            if (row.Path is not null)
                target.Values.Add("Path", row.Path);
            if (row.Workspace is not null)
            {
                global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireCanonical(row.Workspace, row.Workspace.Id, index5, "XmlWorkspace.Workspace");
                target.RelationshipIds.Add("WorkspaceId", row.Workspace.Id);
            }

            instance.GetOrCreateEntityRecords("XmlWorkspace").Add(target);
        }

        var state = new global::Meta.Operations.Domain.InMemoryWorkspace(contract.Clone(), instance);
        global::Meta.TypedModels.TypedWorkspaceBindingValidation.RequireValid(state);
        return state;
    }
}