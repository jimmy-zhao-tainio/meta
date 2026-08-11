using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

public static class SqlOperations
{
    public static void Apply(
        string connectionString,
        params Operation[] operations)
    {
        Apply(
            connectionString,
            (IReadOnlyList<Operation>)operations);
    }

    public static void Apply(
        string connectionString,
        IReadOnlyList<Operation> operations)
    {
        Execute(connectionString, operations);
    }

    public static IReadOnlyList<OperationResult> Execute(
        string connectionString,
        params Operation[] operations)
    {
        return Execute(
            connectionString,
            (IReadOnlyList<Operation>)operations);
    }

    public static IReadOnlyList<OperationResult> Execute(
        string connectionString,
        IReadOnlyList<Operation> operations)
    {
        var workspace = SqlWorkspace.OpenAsync(connectionString)
            .GetAwaiter()
            .GetResult();
        try
        {
            return workspace.ExecuteAsync(operations)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            workspace.DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }
}

internal sealed partial class SqlOperationTarget : IOperationTarget
{
    private static string IdentitySqlType =>
        SqlWorkspaceContract.IdentitySqlType;

    private const string PropertySqlType =
        SqlWorkspaceContract.PropertySqlType;

    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly string _schema;

    public SqlOperationTarget(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema)
    {
        _connection = connection;
        _transaction = transaction;
        _schema = schema;
    }

    RenameModelResult IOperationTarget.Apply(Operation.RenameModel operation) => Apply(operation);

    OperationResult IOperationTarget.Apply(Operation.AddEntity operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveEntity operation) => Complete(operation, Apply);
    RenameEntityResult IOperationTarget.Apply(Operation.RenameEntity operation) => Apply(operation);
    OperationResult IOperationTarget.Apply(Operation.AddProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RenameProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetPropertyRequired operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.AddRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.RemoveRelationship operation) => Complete(operation, Apply);
    RenameRelationshipResult IOperationTarget.Apply(Operation.RenameRelationship operation) => Apply(operation);
    OperationResult IOperationTarget.Apply(Operation.RetargetRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetRelationshipRequired operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.InsertRecord operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.DeleteRecord operation) => Complete(operation, Apply);
    RenameRecordResult IOperationTarget.Apply(Operation.RenameRecord operation) => Apply(operation);
    OperationResult IOperationTarget.Apply(Operation.SetProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.ClearProperty operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.SetRelationship operation) => Complete(operation, Apply);
    OperationResult IOperationTarget.Apply(Operation.ClearRelationship operation) => Complete(operation, Apply);
    PropertyToRelationshipResult IOperationTarget.Apply(
        Operation.PropertyToRelationship operation) => Apply(operation);
    RelationshipToPropertyResult IOperationTarget.Apply(
        Operation.RelationshipToProperty operation) => Apply(operation);

    private static OperationResult Complete<T>(
        T operation,
        Action<T> apply)
    {
        apply(operation);
        return OperationResult.Completed;
    }
}
