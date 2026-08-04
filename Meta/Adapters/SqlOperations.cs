using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Adapters;

public static class SqlOperations
{
    public static void Apply(
        string connectionString,
        string schema,
        params Operation[] operations)
    {
        Apply(
            connectionString,
            schema,
            (IReadOnlyList<Operation>)operations);
    }

    public static void Apply(
        string connectionString,
        string schema,
        IReadOnlyList<Operation> operations)
    {
        Execute(connectionString, schema, operations);
    }

    public static IReadOnlyList<OperationResult> Execute(
        string connectionString,
        string schema,
        params Operation[] operations)
    {
        return Execute(
            connectionString,
            schema,
            (IReadOnlyList<Operation>)operations);
    }

    public static IReadOnlyList<OperationResult> Execute(
        string connectionString,
        string schema,
        IReadOnlyList<Operation> operations)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "Connection string is required.",
                nameof(connectionString));
        }

        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Any(operation => operation == null))
        {
            throw new ArgumentException(
                "Operations cannot contain null.",
                nameof(operations));
        }

        if (operations.Count == 0)
        {
            return [];
        }

        var effectiveSchema = string.IsNullOrWhiteSpace(schema)
            ? "dbo"
            : MetaName.Require(schema, "Schema name.");

        if (operations.Any(operation => operation is Operation.RenameModel))
        {
            if (operations.Count != 1 ||
                operations[0] is not Operation.RenameModel renameModel)
            {
                throw new InvalidOperationException(
                    "SQL model rename must be applied as one operation because SQL Server database rename cannot participate in a transaction with other workspace operations.");
            }

            try
            {
                var result = SqlOperationTarget.RenameDatabase(
                    connectionString,
                    effectiveSchema,
                    renameModel);
                return [result];
            }
            catch (Exception exception)
            {
                throw new MetaOperationException(
                    0,
                    renameModel,
                    exception);
            }
        }

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        MetaName.Require(connection.Database, "Database name.");

        using var transaction = connection.BeginTransaction(
            IsolationLevel.Serializable);
        SqlWorkspaceModelReader.Read(
            connection,
            transaction,
            effectiveSchema);
        var target = new SqlOperationTarget(
            connection,
            transaction,
            effectiveSchema);
        var results = new List<OperationResult>(operations.Count);

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            try
            {
                results.Add(operation.ApplyTo(target));
                if (operation is Operation.ModelOperation or
                    Operation.RefactorOperation)
                {
                    SqlWorkspaceModelReader.Read(
                        connection,
                        transaction,
                        effectiveSchema);
                }
            }
            catch (MetaOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MetaOperationException(
                    index,
                    operation,
                    exception);
            }
        }

        transaction.Commit();
        return results;
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

    RenameModelResult IOperationTarget.Apply(Operation.RenameModel operation) =>
        throw new InvalidOperationException(
            "SQL model rename is executed outside the transactional operation target.");

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
