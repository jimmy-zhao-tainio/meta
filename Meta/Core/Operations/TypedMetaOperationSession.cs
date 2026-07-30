using Meta.Core.Serialization;

namespace Meta.Core.Operations;

public sealed class TypedMetaOperationSession<TModel>
    : ITypedMetaOperationSession<TModel>
    where TModel : class, IMetaWorkspaceModel<TModel>
{
    private readonly MetaOperationInterpreter _interpreter;
    private readonly List<Action> _undo = [];
    private GenericMetadataState _baseline;
    private GenericMetadataState _working;
    private bool _faulted;

    public TypedMetaOperationSession(
        TModel model,
        MetaOperationInterpreter? interpreter = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _interpreter = interpreter ?? new MetaOperationInterpreter();
        _baseline = TypedWorkspaceXmlSerializer.CaptureOperationState(Model);
        _working = _baseline.Clone();
    }

    public TModel Model { get; }

    public TypedMetaOperationResult Apply(
        Action<TypedMetaOperationPlanBuilder<TModel>> configure)
    {
        return Apply(TypedMetaOperationPlan<TModel>.Create(configure));
    }

    public TypedMetaOperationResult Apply(TypedMetaOperationPlan<TModel> plan)
    {
        return Apply(plan, applyBackingPlan: null);
    }

    internal TypedMetaOperationResult Apply(
        TypedMetaOperationPlan<TModel> plan,
        Func<
            MetaOperationPlan,
            GenericMetadataState,
            GenericMetadataState,
            GenericMetaOperationResult>? applyBackingPlan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureModelWasNotChangedOutsideSession();

        var beforePlan = _working.Clone();
        var appliedOperationCount = 0;
        var planUndo = new List<Action>();
        var genericOperations = new List<MetaOperation>();
        try
        {
            foreach (var resolve in plan.Operations)
            {
                var resolved = resolve(Model);
                resolved.Apply();
                planUndo.Add(resolved.Revert);
                genericOperations.Add(resolved.Operation);
                appliedOperationCount++;
            }

            if (genericOperations.Count > 0)
            {
                var genericPlan = new MetaOperationPlan(genericOperations);
                var result = _interpreter.Apply(beforePlan, genericPlan);
                var actual =
                    TypedWorkspaceXmlSerializer.CaptureOperationState(Model);
                var difference = GenericMetadataStateComparer.FindDifference(
                    result.State,
                    actual);
                if (difference != null)
                {
                    throw new InvalidOperationException(
                        $"The typed operation plan did not produce the state defined by the generic interpreter. {difference}");
                }

                _working = result.State;
                if (applyBackingPlan != null)
                {
                    var backingResult = applyBackingPlan(
                        genericPlan,
                        beforePlan,
                        _working);
                    difference = GenericMetadataStateComparer.FindDifference(
                        backingResult.State,
                        _working);
                    if (difference != null)
                    {
                        throw new InvalidOperationException(
                            $"The backing operation session produced a different state from the typed operation session. {difference}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            RestoreAfterFailure(planUndo, beforePlan, exception);

            throw;
        }

        _undo.AddRange(planUndo);
        return new TypedMetaOperationResult(appliedOperationCount);
    }

    internal GenericMetadataState Snapshot()
    {
        EnsureModelWasNotChangedOutsideSession();
        return _working.Clone();
    }

    public void Commit()
    {
        EnsureModelWasNotChangedOutsideSession();
        _baseline = _working.Clone();
        _undo.Clear();
    }

    public void Discard()
    {
        EnsureModelWasNotChangedOutsideSession();
        RestoreUndoActions(_undo, _baseline);
        _undo.Clear();
        _working = _baseline.Clone();
    }

    private void EnsureModelWasNotChangedOutsideSession()
    {
        if (_faulted)
        {
            throw new InvalidOperationException(
                "The typed operation session is faulted and cannot continue.");
        }

        var current = TypedWorkspaceXmlSerializer.CaptureOperationState(Model);
        var difference = GenericMetadataStateComparer.FindDifference(
            current,
            _working);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"The typed model changed outside this operation session. {difference}");
        }
    }

    private void RestoreAfterFailure(
        IReadOnlyList<Action> planUndo,
        GenericMetadataState beforePlan,
        Exception operationException)
    {
        try
        {
            RestoreUndoActions(planUndo, beforePlan);
            _working = beforePlan;
        }
        catch (Exception restoreException)
        {
            _faulted = true;
            throw new InvalidOperationException(
                "Typed operation plan failed and its object graph could not be restored.",
                new AggregateException(operationException, restoreException));
        }
    }

    private void RestoreUndoActions(
        IReadOnlyList<Action> actions,
        GenericMetadataState expectedState)
    {
        for (var index = actions.Count - 1; index >= 0; index--)
        {
            actions[index]();
        }

        var actual = TypedWorkspaceXmlSerializer.CaptureOperationState(Model);
        var difference = GenericMetadataStateComparer.FindDifference(
            actual,
            expectedState);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"Typed operation rollback did not restore its source state. {difference}");
        }
    }
}

public readonly record struct TypedMetaOperationResult(
    int AppliedOperationCount);
