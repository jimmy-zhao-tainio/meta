using System.Linq;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Core.Services;

public sealed class WorkspaceMergeService : IWorkspaceMergeService
{
    public async Task<WorkspaceMergePlan> MergeAsync(
        IReadOnlyList<IMetaWorkspaceSource> sourceWorkspaces,
        WorkspaceMergeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);
        ValidateOptions(sourceWorkspaces.Count, options);
        await ValidateEntityCollisionsAsync(
                sourceWorkspaces,
                cancellationToken)
            .ConfigureAwait(false);

        var merged = await WorkspaceComposition.MergeAsync(
                options.MergedModelName,
                sourceWorkspaces,
                cancellationToken)
            .ConfigureAwait(false);
        var result = new WorkspaceMergeResult(
            SourceWorkspaceCount: sourceWorkspaces.Count,
            EntitiesMerged: merged.Model.Entities.Count,
            RowsMerged: merged.Instance.RecordsByEntity.Values.Sum(
                records => records.Count),
            MergedModelName: options.MergedModelName);
        return new WorkspaceMergePlan(merged, result);
    }

    private static async Task ValidateEntityCollisionsAsync(
        IReadOnlyList<IMetaWorkspaceSource> sourceWorkspaces,
        CancellationToken cancellationToken)
    {
        var sourceByEntity = new Dictionary<string, int>(MetaName.Comparer);
        for (var sourceIndex = 0; sourceIndex < sourceWorkspaces.Count; sourceIndex++)
        {
            var source = sourceWorkspaces[sourceIndex]
                ?? throw new ArgumentNullException(
                    nameof(sourceWorkspaces),
                    $"Source workspace {sourceIndex + 1} is null.");
            await foreach (var entityName in source
                               .ReadEntityNamesAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (sourceByEntity.TryGetValue(
                        entityName,
                        out var existingSourceIndex))
                {
                    throw new InvalidOperationException(
                        $"Cannot merge source workspace {sourceIndex + 1} because entity '{entityName}' already exists in source workspace {existingSourceIndex + 1}.");
                }

                sourceByEntity[entityName] = sourceIndex;
            }
        }
    }

    private static void ValidateOptions(
        int sourceWorkspaceCount,
        WorkspaceMergeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.MergedModelName))
        {
            throw new InvalidOperationException("Merged model name is required.");
        }

        if (sourceWorkspaceCount < 2)
        {
            throw new InvalidOperationException(
                "Workspace merge requires at least two source workspaces.");
        }

        if (!MetaName.IsValid(options.MergedModelName))
        {
            throw new InvalidOperationException(
                $"Model '{options.MergedModelName}' is invalid. Names must use [A-Za-z_][A-Za-z0-9_]* and cannot exceed {MetaName.MaximumLength} characters.");
        }
    }

}
