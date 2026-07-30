using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

namespace Meta.Adapters;

public sealed partial class SqlServerMetaOperationSession : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly string _schema;
    private GenericModel _model;
    private int _savepointSequence;
    private bool _completed;
    private bool _faulted;

    private SqlServerMetaOperationSession(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericModel model)
    {
        _connection = connection;
        _transaction = transaction;
        _schema = schema;
        _model = model;
    }

    public static async Task<SqlServerMetaOperationSession> OpenExistingAsync(
        string connectionString,
        string schema = "dbo",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "Connection string is required.",
                nameof(connectionString));
        }

        var effectiveSchema = SqlServerMetaModelReader.NormalizeSchema(schema);
        var connection = new SqlConnection(connectionString);
        SqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken)
                .ConfigureAwait(false);
            var model = await SqlServerMetaModelReader.LoadAsync(
                    connection,
                    effectiveSchema,
                    cancellationToken,
                    transaction)
                .ConfigureAwait(false);
            await SqlServerMetaStorageValidator.ValidateAsync(
                    connection,
                    transaction,
                    effectiveSchema,
                    model,
                    cancellationToken)
                .ConfigureAwait(false);
            var diagnostics = ValidateModel(model);
            if (diagnostics.HasErrors)
            {
                throw new InvalidOperationException(
                    "SQL operation sessions require a conforming Meta model. " +
                    FormatErrors(diagnostics));
            }

            return new SqlServerMetaOperationSession(
                connection,
                transaction,
                effectiveSchema,
                model);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public GenericModel SnapshotModel()
    {
        return _model.Clone();
    }

    public async Task<MetaOperationResult> ApplyAsync(
        MetaOperationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureActive();

        var modelBefore = _model.Clone();
        var savepoint = $"MetaOperation{++_savepointSequence}";
        _transaction.Save(savepoint);

        for (var index = 0; index < plan.Operations.Count; index++)
        {
            var operation = plan.Operations[index];
            try
            {
                await ApplyOperationAsync(operation, cancellationToken)
                    .ConfigureAwait(false);
                if (operation is not InstanceOperation)
                {
                    var diagnostics = ValidateModel(_model);
                    if (diagnostics.HasErrors)
                    {
                        throw new MetaOperationException(
                            $"Operation {index + 1} ({operation.GetType().Name}) produced an invalid model. {FormatErrors(diagnostics)}",
                            diagnostics: diagnostics);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                RollbackPlan(savepoint, modelBefore);
                throw;
            }
            catch (MetaOperationException exception)
            {
                RollbackPlan(savepoint, modelBefore);
                throw new MetaOperationException(
                    exception.Message,
                    index,
                    operation,
                    exception,
                    exception.Diagnostics);
            }
            catch (Exception exception)
            {
                RollbackPlan(savepoint, modelBefore);
                throw new MetaOperationException(
                    $"Operation {index + 1} ({operation.GetType().Name}) failed. {exception.Message}",
                    index,
                    operation,
                    exception);
            }
        }

        return new MetaOperationResult(plan.Operations.Count);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Disposal cannot restore a connection whose transaction has already failed.
            }
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        _completed = true;
    }

    private async Task ApplyOperationAsync(
        MetaOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case AddEntityOperation addEntity:
                await AddEntityAsync(addEntity, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RemoveEntityOperation removeEntity:
                await RemoveEntityAsync(removeEntity, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case AddPropertyOperation addProperty:
                await AddPropertyAsync(addProperty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RemovePropertyOperation removeProperty:
                await RemovePropertyAsync(removeProperty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RenamePropertyOperation renameProperty:
                await RenamePropertyAsync(renameProperty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case SetPropertyRequiredOperation setPropertyRequired:
                await SetPropertyRequiredAsync(
                        setPropertyRequired,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case AddRelationshipOperation addRelationship:
                await AddRelationshipAsync(
                        addRelationship,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case RemoveRelationshipOperation removeRelationship:
                await RemoveRelationshipAsync(
                        removeRelationship,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case InsertRecordOperation insertRecord:
                await InsertRecordAsync(insertRecord, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case SetPropertyOperation setProperty:
                await SetPropertyAsync(setProperty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case ClearPropertyOperation clearProperty:
                await ClearPropertyAsync(clearProperty, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case SetRelationshipOperation setRelationship:
                await SetRelationshipAsync(setRelationship, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case ClearRelationshipOperation clearRelationship:
                await ClearRelationshipAsync(clearRelationship, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case DeleteRecordOperation deleteRecord:
                await DeleteRecordAsync(deleteRecord, cancellationToken)
                    .ConfigureAwait(false);
                return;
            default:
                throw new NotSupportedException(
                    $"Operation '{operation.GetType().Name}' is not supported by the SQL Server interpreter.");
        }
    }

    private void RollbackPlan(
        string savepoint,
        GenericModel modelBefore)
    {
        try
        {
            _transaction.Rollback(savepoint);
            _model = modelBefore;
        }
        catch
        {
            _faulted = true;
            throw;
        }
    }

    private void EnsureActive()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "The SQL Server metadata session has already completed.");
        }

        if (_faulted)
        {
            throw new InvalidOperationException(
                "The SQL Server metadata session cannot continue after a failed rollback.");
        }
    }

    private static WorkspaceDiagnostics ValidateModel(GenericModel model)
    {
        return new ValidationService().Validate(new Workspace
        {
            Model = model,
            Instance = new GenericInstance
            {
                ModelName = model.Name,
            },
        });
    }

    private static string FormatErrors(WorkspaceDiagnostics diagnostics)
    {
        return string.Join(
            " | ",
            diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue =>
                    $"{issue.Code} {issue.Location} - {issue.Message}"));
    }
}
