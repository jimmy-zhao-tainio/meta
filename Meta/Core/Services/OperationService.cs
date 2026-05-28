using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Services;

public sealed class OperationService : IOperationService
{
    private readonly ConditionalWeakTable<Workspace, OperationHistory> _histories = new();

    public void Execute(Workspace workspace, WorkspaceOp operation)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (operation == null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var history = GetHistory(workspace);
        var before = WorkspaceSnapshotCloner.Capture(workspace);
        WorkspaceOperationApplier.Apply(workspace, operation);
        var after = WorkspaceSnapshotCloner.Capture(workspace);

        history.UndoStack.Push(new OperationHistoryEntry(operation, before, after));
        history.RedoStack.Clear();
    }

    public bool CanUndo(Workspace workspace)
    {
        return GetHistory(workspace).UndoStack.Count > 0;
    }

    public bool CanRedo(Workspace workspace)
    {
        return GetHistory(workspace).RedoStack.Count > 0;
    }

    public void Undo(Workspace workspace)
    {
        if (!CanUndo(workspace))
        {
            return;
        }

        var history = GetHistory(workspace);
        var entry = history.UndoStack.Pop();
        WorkspaceSnapshotCloner.Restore(workspace, entry.Before);
        history.RedoStack.Push(entry);
    }

    public void Redo(Workspace workspace)
    {
        if (!CanRedo(workspace))
        {
            return;
        }

        var history = GetHistory(workspace);
        var entry = history.RedoStack.Pop();
        WorkspaceSnapshotCloner.Restore(workspace, entry.After);
        history.UndoStack.Push(entry);
    }

    public void ApplyWithoutHistory(Workspace workspace, WorkspaceOp operation)
    {
        WorkspaceOperationApplier.Apply(workspace, operation);
    }

    public IReadOnlyCollection<WorkspaceOp> GetUndoOperations(Workspace workspace)
    {
        var history = GetHistory(workspace);
        var list = new List<WorkspaceOp>();
        foreach (var entry in history.UndoStack)
        {
            list.Add(entry.Operation);
        }

        return list;
    }

    private OperationHistory GetHistory(Workspace workspace)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        return _histories.GetValue(workspace, _ => new OperationHistory());
    }

    private sealed class OperationHistory
    {
        public Stack<OperationHistoryEntry> UndoStack { get; } = new();
        public Stack<OperationHistoryEntry> RedoStack { get; } = new();
    }

    private sealed class OperationHistoryEntry
    {
        public OperationHistoryEntry(WorkspaceOp operation, WorkspaceSnapshot before, WorkspaceSnapshot after)
        {
            Operation = operation;
            Before = before;
            After = after;
        }

        public WorkspaceOp Operation { get; }
        public WorkspaceSnapshot Before { get; }
        public WorkspaceSnapshot After { get; }
    }
}
