using Meta.Integration;
using Meta.Operations.Domain;
using MetaWeaveScript.Execution;

namespace MetaWeave.Core;

public sealed class MetaWeaveScriptDirectionLoader
{
    public MetaWeaveScriptDirection Load(
        string workspacePath,
        string? directionName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

        var model = TypedWorkspaceModelMapper.Load<MetaWeaveModel>(
            Path.GetFullPath(workspacePath),
            searchUpward: false);
        return Load(model, directionName);
    }

    public MetaWeaveScriptDirection Load(
        MetaWeaveModel model,
        string? directionName = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.WeaveList.Count != 1)
        {
            throw new InvalidOperationException(
                $"A MetaWeave workspace requires exactly one Weave but contained {model.WeaveList.Count}.");
        }

        var direction = SelectDirection(model, directionName);
        var transformations = model.TransformationList
            .Where(transformation =>
                ReferenceEquals(transformation.Direction, direction) ||
                MetaName.Comparer.Equals(transformation.Direction?.Id, direction.Id))
            .Select(transformation => new MetaWeaveScriptTransformation(
                MetaWeaveAuthoringService.GetTransformationName(transformation),
                transformation.TargetEntityName,
                transformation.SelectStatement))
            .ToArray();
        var requirements = model.DirectionRequirementList
            .Where(requirement =>
                ReferenceEquals(requirement.Direction, direction) ||
                MetaName.Comparer.Equals(requirement.Direction?.Id, direction.Id))
            .OrderBy(requirement => requirement.Id, StringComparer.OrdinalIgnoreCase)
            .Select(requirement => new MetaWeaveScriptRequirement(
                MetaWeaveAuthoringService.GetRequirementName(requirement),
                requirement.Code,
                requirement.Message,
                requirement.SelectStatement))
            .ToArray();

        return new MetaWeaveScriptDirection(
            direction.Id,
            direction.SourceModelName,
            direction.TargetModelName,
            model,
            transformations,
            requirements);
    }

    private static Direction SelectDirection(
        MetaWeaveModel model,
        string? directionName)
    {
        if (string.IsNullOrWhiteSpace(directionName))
        {
            return model.DirectionList.Count switch
            {
                1 => model.DirectionList[0],
                _ => throw new InvalidOperationException(
                    $"The MetaWeave workspace contains {model.DirectionList.Count} directions; specify --direction.")
            };
        }

        var matches = model.DirectionList.Where(direction =>
            MetaName.Comparer.Equals(direction.Id, directionName.Trim())).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Direction '{directionName.Trim()}' was not found."),
            _ => throw new InvalidOperationException(
                $"Direction '{directionName.Trim()}' is ambiguous.")
        };
    }
}
