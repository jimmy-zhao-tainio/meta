using Meta.Core.Serialization;
using Meta.Core.Services;

namespace Meta.Core.Operations;

public sealed class TypedXmlMetaOperationSession<TModel>
    : ITypedMetaOperationSession<TModel>
    where TModel : class, IMetaWorkspaceModel<TModel>
{
    private readonly XmlMetaOperationSession _xmlSession;
    private readonly TypedMetaOperationSession<TModel> _typedSession;
    private readonly List<MetaOperationPlan> _acceptedPlans = [];
    private bool _faulted;

    private TypedXmlMetaOperationSession(
        XmlMetaOperationSession xmlSession,
        TModel model,
        MetaOperationInterpreter interpreter)
    {
        _xmlSession = xmlSession;
        _typedSession = new TypedMetaOperationSession<TModel>(
            model,
            interpreter);
        EnsureSynchronized();
    }

    public TModel Model => _typedSession.Model;

    public string WorkspacePath => _xmlSession.WorkspacePath;

    public static async Task<TypedXmlMetaOperationSession<TModel>> OpenExistingAsync(
        string workspacePath,
        IWorkspaceService? workspaceService = null,
        MetaOperationInterpreter? interpreter = null,
        CancellationToken cancellationToken = default)
    {
        var operationInterpreter = interpreter ?? new MetaOperationInterpreter();
        var xmlSession = await XmlMetaOperationSession.OpenExistingAsync(
                workspacePath,
                workspaceService,
                operationInterpreter,
                cancellationToken)
            .ConfigureAwait(false);
        var model = TModel.CreateEmpty();
        TypedWorkspaceXmlSerializer.RestoreOperationState(
            model,
            xmlSession.Snapshot());
        return new TypedXmlMetaOperationSession<TModel>(
            xmlSession,
            model,
            operationInterpreter);
    }

    public static async Task<TypedXmlMetaOperationSession<TModel>> OpenLoadedAsync(
        TModel model,
        string workspacePath,
        IWorkspaceService? workspaceService = null,
        MetaOperationInterpreter? interpreter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var operationInterpreter = interpreter ?? new MetaOperationInterpreter();
        var xmlSession = await XmlMetaOperationSession.OpenExistingAsync(
                workspacePath,
                workspaceService,
                operationInterpreter,
                cancellationToken)
            .ConfigureAwait(false);
        return new TypedXmlMetaOperationSession<TModel>(
            xmlSession,
            model,
            operationInterpreter);
    }

    public TypedMetaOperationResult Apply(
        Action<TypedMetaOperationPlanBuilder<TModel>> configure)
    {
        return Apply(TypedMetaOperationPlan<TModel>.Create(configure));
    }

    public TypedMetaOperationResult Apply(TypedMetaOperationPlan<TModel> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureSynchronized();
        return _typedSession.Apply(plan, ApplyBackingPlan);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureSynchronized();
        await _xmlSession.CommitAsync(cancellationToken).ConfigureAwait(false);
        _typedSession.Commit();
        _acceptedPlans.Clear();
    }

    public void Discard()
    {
        EnsureSynchronized();
        _typedSession.Discard();
        _xmlSession.Discard();
        _acceptedPlans.Clear();
    }

    private GenericMetaOperationResult ApplyBackingPlan(
        MetaOperationPlan plan,
        GenericMetadataState expectedBefore,
        GenericMetadataState expectedAfter)
    {
        EnsureEqual(
            _xmlSession.Snapshot(),
            expectedBefore,
            "The XML operation session changed outside the typed session.");

        var result = _xmlSession.Apply(plan);
        var difference = GenericMetadataStateComparer.FindDifference(
            result.State,
            expectedAfter);
        if (difference != null)
        {
            try
            {
                RestoreBackingSession();
            }
            catch (Exception restoreException)
            {
                _faulted = true;
                throw new InvalidOperationException(
                    "The XML operation session diverged from the generated model and could not be restored.",
                    restoreException);
            }

            throw new InvalidOperationException(
                $"The XML operation session produced a different state from the typed operation session. {difference}");
        }

        _acceptedPlans.Add(plan);
        return result;
    }

    private void EnsureSynchronized()
    {
        if (_faulted)
        {
            throw new InvalidOperationException(
                "The typed XML operation session is faulted and cannot continue.");
        }

        EnsureEqual(
            _xmlSession.Snapshot(),
            _typedSession.Snapshot(),
            "The generated model and XML operation session represent different metadata states.");
    }

    private void RestoreBackingSession()
    {
        _xmlSession.Discard();
        foreach (var plan in _acceptedPlans)
        {
            _xmlSession.Apply(plan);
        }
    }

    private static void EnsureEqual(
        GenericMetadataState left,
        GenericMetadataState right,
        string message)
    {
        var difference = GenericMetadataStateComparer.FindDifference(
            left,
            right);
        if (difference != null)
        {
            throw new InvalidOperationException($"{message} {difference}");
        }
    }
}
