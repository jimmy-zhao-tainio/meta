using Meta.Operations;
using Meta.Operations.Domain;

namespace MetaWeaveScript.Execution;

public sealed class MetaWeaveScriptExecutionService
{
    public MetaWeaveScriptQueryResult ExecuteQuery(
        MetaWeaveModel model,
        InMemoryWorkspace sourceWorkspace)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sourceWorkspace);

        if (model.SelectStatementList.Count != 1)
        {
            return new MetaWeaveScriptQueryResult(
                null,
                [new MetaWeaveScriptExecutionIssue(
                    "SelectStatementCountInvalid",
                    $"Standalone query execution requires exactly one SelectStatement, but the semantic model contains {model.SelectStatementList.Count}.")]);
        }

        return ExecuteQuery(model, model.SelectStatementList[0], sourceWorkspace);
    }

    public MetaWeaveScriptQueryResult ExecuteQuery(
        MetaWeaveModel model,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, string>? stringParameters = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);

        if (model.SelectStatementList.Count != 1)
        {
            return new MetaWeaveScriptQueryResult(
                null,
                [new MetaWeaveScriptExecutionIssue(
                    "SelectStatementCountInvalid",
                    $"Standalone query execution requires exactly one SelectStatement, but the semantic model contains {model.SelectStatementList.Count}.")]);
        }

        return ExecuteQuery(
            model,
            model.SelectStatementList[0],
            sourceWorkspaces,
            stringParameters);
    }

    public MetaWeaveScriptQueryResult ExecuteQuery(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        InMemoryWorkspace sourceWorkspace)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        return ExecuteQuery(
            model,
            selectStatement,
            new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = sourceWorkspace
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public MetaWeaveScriptQueryResult ExecuteQuery(
        MetaWeaveModel model,
        SelectStatement selectStatement,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, string>? stringParameters = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selectStatement);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);

        var sourceIssues = new List<MetaWeaveScriptExecutionIssue>();
        var normalizedSources = NormalizeSourceWorkspaces(sourceWorkspaces, sourceIssues);
        foreach (var source in normalizedSources)
        {
            sourceIssues.AddRange(ValidateSourceWorkspace(source.Key, source.Value));
        }

        if (sourceIssues.Count > 0)
        {
            return new MetaWeaveScriptQueryResult(null, sourceIssues);
        }

        try
        {
            var snapshots = normalizedSources.ToDictionary(
                source => source.Key,
                source => source.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
            var parameters = NormalizeStringParameters(stringParameters, sourceIssues);
            if (sourceIssues.Count > 0)
            {
                return new MetaWeaveScriptQueryResult(null, sourceIssues);
            }

            var rowset = new MetaWeaveScriptExecutionSession(
                model,
                selectStatement,
                snapshots,
                parameters).Execute();
            return new MetaWeaveScriptQueryResult(
                new MetaWeaveScriptQueryOutput(
                    rowset.Columns.Select(column => new MetaWeaveScriptQueryColumn(column.Name)).ToArray(),
                    rowset.Rows.Select(row => new MetaWeaveScriptQueryRow(row.Values)).ToArray()),
                []);
        }
        catch (MetaWeaveScriptExecutionFault fault)
        {
            return new MetaWeaveScriptQueryResult(
                null,
                [new MetaWeaveScriptExecutionIssue(fault.Code, fault.Message, SyntaxId: fault.SyntaxId)]);
        }
    }

    public MetaWeaveScriptApplicationResult ExecuteDirection(
        MetaWeaveScriptDirection direction,
        InMemoryWorkspace sourceWorkspace,
        InMemoryWorkspace targetWorkspace,
        Action<MetaWeaveScriptExecutionProgress>? progress = null,
        bool includeRelationOutputs = false)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(targetWorkspace);

        if (direction.SourceWorkspaces is null || direction.SourceWorkspaces.Count != 1)
        {
            return new MetaWeaveScriptApplicationResult(
                null,
                [new MetaWeaveScriptExecutionIssue(
                    "SourceWorkspaceCountInvalid",
                    $"The single-source execution overload requires exactly one declared source workspace, but direction '{direction.Name}' declares {direction.SourceWorkspaces?.Count ?? 0}.")]);
        }

        return ExecuteDirection(
            direction,
            new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase)
            {
                [direction.SourceWorkspaces[0].Name] = sourceWorkspace
            },
            targetWorkspace,
            progress: progress,
            includeRelationOutputs: includeRelationOutputs);
    }

    public MetaWeaveScriptApplicationResult ExecuteDirection(
        MetaWeaveScriptDirection direction,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        InMemoryWorkspace targetWorkspace,
        IReadOnlyDictionary<string, string>? stringParameters = null,
        Action<MetaWeaveScriptExecutionProgress>? progress = null,
        bool includeRelationOutputs = false)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);
        ArgumentNullException.ThrowIfNull(targetWorkspace);

        var initialTarget = new InMemoryWorkspace(
            targetWorkspace.Model.Clone(),
            new GenericInstance { ModelName = targetWorkspace.Model.Name });
        var issues = new List<MetaWeaveScriptExecutionIssue>();
        var normalizedSources = NormalizeSourceWorkspaces(sourceWorkspaces, issues);
        var normalizedParameters = NormalizeStringParameters(stringParameters, issues);
        ValidateDirection(
            direction,
            normalizedSources,
            initialTarget,
            normalizedParameters,
            issues);
        var requirements = direction.Requirements ?? [];
        var transformations = direction.Transformations ?? [];
        var relations = direction.Relations ?? [];
        foreach (var source in normalizedSources)
        {
            issues.AddRange(ValidateSourceWorkspace(source.Key, source.Value));
        }
        issues.AddRange(ValidateTargetWorkspace(initialTarget));
        ValidateRequirements(requirements, issues);
        ValidateRelations(relations, issues);
        ValidateTransformations(transformations, initialTarget.Model, issues);
        var orderedTransformations = issues.Count == 0
            ? OrderTransformations(transformations, initialTarget.Model)
            : [];
        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        var totalTaskCount = requirements.Count + relations.Count + orderedTransformations.Count;
        var completedTaskCount = 0;
        NotifyProgress(
            progress,
            new MetaWeaveScriptExecutionProgress(0, totalTaskCount, null, null));

        var sourceSnapshots = normalizedSources.ToDictionary(
            source => source.Key,
            source => source.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        var namedRelations = new RuntimeNamedRelationContext(
            direction.Model,
            relations,
            sourceSnapshots,
            normalizedParameters,
            relation => TaskCompleted(
                MetaWeaveScriptExecutionTaskKind.Relation,
                relation.Name));
        ExecuteRequirements(
            direction.Model,
            requirements,
            sourceSnapshots,
            normalizedParameters,
            namedRelations,
            issues,
            requirement => TaskCompleted(
                MetaWeaveScriptExecutionTaskKind.Requirement,
                requirement.Name));
        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        try
        {
            namedRelations.EvaluateAll();
        }
        catch (MetaWeaveScriptExecutionFault fault)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                fault.Code,
                fault.Message,
                SyntaxId: fault.SyntaxId,
                RelationName: fault.RelationName));
        }
        catch (InvalidOperationException exception)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "NamedRelationEvaluationFailed",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "NamedRelationEvaluationFailed",
                exception.Message));
        }
        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        var currentTarget = initialTarget;

        for (var transformationIndex = 0; transformationIndex < orderedTransformations.Count; transformationIndex++)
        {
            var transformation = orderedTransformations[transformationIndex];
            try
            {
                var rowset = new MetaWeaveScriptExecutionSession(
                    direction.Model,
                    transformation.SelectStatement,
                    sourceSnapshots,
                    normalizedParameters,
                    namedRelations).Execute();
                var instantiation = CreateInstantiationPlan(
                    transformation,
                    rowset,
                    currentTarget.Model);
                if (instantiation.InsertOperations.Count > 0)
                {
                    currentTarget = InMemoryOperations.Execute(
                        currentTarget,
                        instantiation.InsertOperations).Workspace;
                }

                if (instantiation.SelfRelationshipOperations.Count > 0)
                {
                    currentTarget = InMemoryOperations.Execute(
                        currentTarget,
                        instantiation.SelfRelationshipOperations).Workspace;
                }

                TaskCompleted(
                    MetaWeaveScriptExecutionTaskKind.TargetEntity,
                    transformation.TargetEntityName);
            }
            catch (MetaWeaveScriptExecutionFault fault)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    fault.Code,
                    fault.Message,
                    transformation.Name,
                    fault.SyntaxId,
                    RelationName: fault.RelationName));
                break;
            }
            catch (MetaOperationException exception)
            {
                AddOperationIssues(issues, transformation, exception);
                break;
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TargetInstantiationFailed",
                    exception.Message,
                    transformation.Name));
                break;
            }
            catch (ArgumentException exception)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TargetInstantiationFailed",
                    exception.Message,
                    transformation.Name));
                break;
            }
        }

        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        return includeRelationOutputs
            ? new MetaWeaveScriptApplicationResult(currentTarget, [])
            {
                RelationOutputs = namedRelations.ExportOutputs()
            }
            : new MetaWeaveScriptApplicationResult(currentTarget, []);

        void TaskCompleted(MetaWeaveScriptExecutionTaskKind kind, string name)
        {
            completedTaskCount++;
            NotifyProgress(
                progress,
                new MetaWeaveScriptExecutionProgress(
                    completedTaskCount,
                    totalTaskCount,
                    kind,
                    name));
        }
    }

    private static void NotifyProgress(
        Action<MetaWeaveScriptExecutionProgress>? progress,
        MetaWeaveScriptExecutionProgress value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress(value);
        }
        catch
        {
            // Progress is observational and cannot change execution semantics.
        }
    }

    private static void ValidateDirection(
        MetaWeaveScriptDirection direction,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        InMemoryWorkspace targetWorkspace,
        IReadOnlyDictionary<string, MetaWeaveScriptValue> stringParameters,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(direction.Name))
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionNameMissing",
                "A WeaveScript direction requires a name."));
        }

        var declaredSourceNames = new HashSet<string>(MetaName.Comparer);
        if (direction.SourceWorkspaces is null || direction.SourceWorkspaces.Count == 0)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionSourceWorkspacesMissing",
                $"Direction '{direction.Name}' requires at least one source workspace declaration."));
        }
        else
        {
            foreach (var source in direction.SourceWorkspaces)
            {
                if (source is null || !MetaName.IsValid(source.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "SourceWorkspaceNameInvalid",
                        $"Direction '{direction.Name}' contains a source workspace with an invalid name."));
                    continue;
                }

                if (!declaredSourceNames.Add(source.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "SourceWorkspaceNameDuplicate",
                        $"Direction '{direction.Name}' declares source workspace '{source.Name}' more than once."));
                    continue;
                }

                if (!sourceWorkspaces.TryGetValue(source.Name, out var supplied))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "SourceWorkspaceMissing",
                        $"Direction '{direction.Name}' requires source workspace '{source.Name}'."));
                    continue;
                }

                if (!MetaName.Comparer.Equals(source.ModelName, supplied.Model.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "SourceModelMismatch",
                        $"Direction '{direction.Name}' requires model '{source.ModelName}' for source workspace '{source.Name}' but received '{supplied.Model.Name}'."));
                }
            }
        }

        foreach (var suppliedName in sourceWorkspaces.Keys)
        {
            if (!declaredSourceNames.Contains(suppliedName))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "SourceWorkspaceUnexpected",
                    $"Source workspace '{suppliedName}' is not declared by direction '{direction.Name}'."));
            }
        }

        var declaredParameterNames = new HashSet<string>(MetaName.Comparer);
        if (direction.StringParameters is null)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionStringParametersMissing",
                $"Direction '{direction.Name}' has no string-parameter collection."));
        }
        else
        {
            foreach (var parameter in direction.StringParameters)
            {
                if (parameter is null || !MetaName.IsValid(parameter.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "StringParameterNameInvalid",
                        $"Direction '{direction.Name}' contains a string parameter with an invalid name."));
                    continue;
                }

                if (!declaredParameterNames.Add(parameter.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "StringParameterNameDuplicate",
                        $"Direction '{direction.Name}' declares string parameter '{parameter.Name}' more than once."));
                    continue;
                }

                if (!stringParameters.ContainsKey(parameter.Name))
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        "ParameterValueMissing",
                        $"Direction '{direction.Name}' requires string parameter '@{parameter.Name}'."));
                }
            }
        }

        foreach (var suppliedName in stringParameters.Keys)
        {
            if (!declaredParameterNames.Contains(suppliedName))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "ParameterValueUnexpected",
                    $"Parameter '@{suppliedName}' is not declared by direction '{direction.Name}'."));
            }
        }

        if (!MetaName.Comparer.Equals(
                direction.TargetModelName,
                targetWorkspace.Model.Name))
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "TargetModelMismatch",
                $"Direction '{direction.Name}' requires target model '{direction.TargetModelName}' but received '{targetWorkspace.Model.Name}'."));
        }

        if (direction.Transformations is null)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionTransformationsMissing",
                $"Direction '{direction.Name}' has no transformation collection."));
        }

        if (direction.Requirements is null)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionRequirementsMissing",
                $"Direction '{direction.Name}' has no requirement collection."));
        }

        if (direction.Relations is null)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionRelationsMissing",
                $"Direction '{direction.Name}' has no named-relation collection."));
        }

        if (direction.Model is null)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "WeaveModelMissing",
                $"Direction '{direction.Name}' has no containing MetaWeave model."));
        }
    }

    private static void ValidateRequirements(
        IReadOnlyList<MetaWeaveScriptRequirement> requirements,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in requirements)
        {
            if (requirement is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementMissing",
                    "The direction contains a null requirement."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(requirement.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementNameMissing",
                    "Every direction requirement requires a name."));
            }
            else if (!names.Add(requirement.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementNameDuplicate",
                    $"Direction requirement name '{requirement.Name}' is duplicated.",
                    RequirementName: requirement.Name));
            }

            if (string.IsNullOrWhiteSpace(requirement.Code))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementCodeMissing",
                    $"Direction requirement '{requirement.Name}' has no diagnostic code.",
                    RequirementName: requirement.Name));
            }

            if (string.IsNullOrWhiteSpace(requirement.Message))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementMessageMissing",
                    $"Direction requirement '{requirement.Name}' has no diagnostic message.",
                    RequirementName: requirement.Name));
            }

            if (requirement.SelectStatement is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementSelectStatementMissing",
                    $"Direction requirement '{requirement.Name}' has no SELECT root.",
                    RequirementName: requirement.Name));
            }
        }
    }

    private static void ValidateRelations(
        IReadOnlyList<MetaWeaveScriptRelation> relations,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relation in relations)
        {
            if (relation is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "NamedRelationMissing",
                    "The direction contains a null named relation."));
                continue;
            }

            if (!MetaName.IsValid(relation.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "NamedRelationNameInvalid",
                    $"Direction relation name '{relation.Name}' is invalid.",
                    RelationName: relation.Name));
            }
            else if (!names.Add(relation.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "NamedRelationNameDuplicate",
                    $"Direction relation name '{relation.Name}' is duplicated.",
                    RelationName: relation.Name));
            }

            if (relation.SelectStatement is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "NamedRelationSelectStatementMissing",
                    $"Named relation '{relation.Name}' has no SELECT root.",
                    RelationName: relation.Name));
            }
        }
    }

    private static void ExecuteRequirements(
        MetaWeaveModel model,
        IReadOnlyList<MetaWeaveScriptRequirement> requirements,
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        IReadOnlyDictionary<string, MetaWeaveScriptValue> stringParameters,
        RuntimeNamedRelationContext namedRelations,
        ICollection<MetaWeaveScriptExecutionIssue> issues,
        Action<MetaWeaveScriptRequirement>? requirementEvaluated)
    {
        foreach (var requirement in requirements)
        {
            try
            {
                var rowset = new MetaWeaveScriptExecutionSession(
                    model,
                    requirement.SelectStatement,
                    sourceWorkspaces,
                    stringParameters,
                    namedRelations).Execute();
                foreach (var row in rowset.Rows)
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        requirement.Code,
                        FormatRequirementViolation(requirement.Message, rowset.Columns, row),
                        RequirementName: requirement.Name));
                }

                requirementEvaluated?.Invoke(requirement);
            }
            catch (MetaWeaveScriptExecutionFault fault)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    fault.Code,
                    fault.Message,
                    SyntaxId: fault.SyntaxId,
                    RequirementName: requirement.Name,
                    RelationName: fault.RelationName));
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementEvaluationFailed",
                    exception.Message,
                    RequirementName: requirement.Name));
            }
            catch (ArgumentException exception)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "RequirementEvaluationFailed",
                    exception.Message,
                    RequirementName: requirement.Name));
            }
        }
    }

    private static string FormatRequirementViolation(
        string message,
        IReadOnlyList<RuntimeColumn> columns,
        RuntimeRow row)
    {
        var evidence = columns
            .Select((column, index) =>
            {
                var name = string.IsNullOrWhiteSpace(column.Name)
                    ? $"Column{index + 1}"
                    : column.Name;
                return $"{name}={row.Values[index]}";
            })
            .ToArray();
        return evidence.Length == 0
            ? message
            : $"{message} ({string.Join(", ", evidence)})";
    }

    private static IReadOnlyDictionary<string, InMemoryWorkspace> NormalizeSourceWorkspaces(
        IReadOnlyDictionary<string, InMemoryWorkspace> sourceWorkspaces,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        var normalized = new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceWorkspaces)
        {
            if (!MetaName.IsValid(source.Key))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "SourceWorkspaceNameInvalid",
                    $"Supplied source workspace name '{source.Key}' is invalid."));
                continue;
            }

            if (source.Value is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "SourceWorkspaceMissing",
                    $"Supplied source workspace '{source.Key}' has no workspace value."));
                continue;
            }

            if (!normalized.TryAdd(source.Key, source.Value))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "SourceWorkspaceNameDuplicate",
                    $"Source workspace name '{source.Key}' was supplied more than once."));
            }
        }

        if (normalized.Count == 0)
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "SourceWorkspacesMissing",
                "At least one source workspace must be supplied."));
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, MetaWeaveScriptValue> NormalizeStringParameters(
        IReadOnlyDictionary<string, string>? stringParameters,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        var normalized = new Dictionary<string, MetaWeaveScriptValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in stringParameters ?? new Dictionary<string, string>())
        {
            if (!MetaName.IsValid(parameter.Key))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "StringParameterNameInvalid",
                    $"Supplied string parameter name '{parameter.Key}' is invalid."));
                continue;
            }

            if (parameter.Value is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "ParameterValueInvalid",
                    $"String parameter '@{parameter.Key}' has a null runtime value."));
                continue;
            }

            if (!normalized.TryAdd(parameter.Key, MetaWeaveScriptValue.FromString(parameter.Value)))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "StringParameterNameDuplicate",
                    $"String parameter '@{parameter.Key}' was supplied more than once."));
            }
        }

        return normalized;
    }

    private static IReadOnlyList<MetaWeaveScriptExecutionIssue> ValidateSourceWorkspace(
        string sourceName,
        InMemoryWorkspace sourceWorkspace)
    {
        var diagnostics = WorkspaceValidator.Validate(sourceWorkspace.Model, sourceWorkspace.Instance);
        return diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Select(issue => new MetaWeaveScriptExecutionIssue(
                "SourceWorkspace." + issue.Code,
                $"Source workspace '{sourceName}': {issue.Message}",
                SyntaxId: issue.Location))
            .ToArray();
    }

    private static IReadOnlyList<MetaWeaveScriptExecutionIssue> ValidateTargetWorkspace(
        InMemoryWorkspace targetWorkspace)
    {
        var diagnostics = WorkspaceValidator.Validate(
            targetWorkspace.Model,
            targetWorkspace.Instance);
        return diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Select(issue => new MetaWeaveScriptExecutionIssue(
                "TargetWorkspace." + issue.Code,
                issue.Message,
                SyntaxId: issue.Location))
            .ToArray();
    }

    private static void ValidateTransformations(
        IReadOnlyList<MetaWeaveScriptTransformation> transformations,
        GenericModel targetModel,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        var transformationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transformation in transformations)
        {
            if (transformation is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TransformationMissing",
                    "The direction contains a null transformation."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(transformation.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TransformationNameMissing",
                    "Every WeaveScript transformation requires a name."));
            }
            else if (!transformationNames.Add(transformation.Name))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TransformationNameDuplicate",
                    $"WeaveScript transformation name '{transformation.Name}' is duplicated.",
                    transformation.Name));
            }

            if (transformation.SelectStatement is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TransformationSelectStatementMissing",
                    $"WeaveScript transformation '{transformation.Name}' has no SELECT root.",
                    transformation.Name));
            }

            if (string.IsNullOrWhiteSpace(transformation.TargetEntityName))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TargetEntityNameMissing",
                    $"WeaveScript transformation '{transformation.Name}' has no target entity name.",
                    transformation.Name));
                continue;
            }

            if (!targetNames.Add(transformation.TargetEntityName))
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TargetEntityTransformationDuplicate",
                    $"More than one WeaveScript transformation targets entity '{transformation.TargetEntityName}'.",
                    transformation.Name));
            }

            if (targetModel.FindEntity(transformation.TargetEntityName) is null)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    "TargetEntityNotFound",
                    $"Target entity '{transformation.TargetEntityName}' for transformation '{transformation.Name}' does not exist.",
                    transformation.Name));
            }
        }
    }

    private static IReadOnlyList<MetaWeaveScriptTransformation> OrderTransformations(
        IReadOnlyList<MetaWeaveScriptTransformation> transformations,
        GenericModel targetModel)
    {
        var entities = targetModel.Entities.ToDictionary(
            entity => entity.Name,
            MetaName.Comparer);
        var transformationsByEntity = transformations.ToDictionary(
            transformation => transformation.TargetEntityName,
            MetaName.Comparer);
        var orderedEntities = new List<GenericEntity>(entities.Count);
        var visiting = new HashSet<string>(MetaName.Comparer);
        var visited = new HashSet<string>(MetaName.Comparer);

        foreach (var entityName in entities.Keys.OrderBy(
                     name => name,
                     StringComparer.OrdinalIgnoreCase))
        {
            Visit(entityName);
        }

        return orderedEntities
            .Where(entity => transformationsByEntity.ContainsKey(entity.Name))
            .Select(entity => transformationsByEntity[entity.Name])
            .ToArray();

        void Visit(string entityName)
        {
            if (visited.Contains(entityName))
            {
                return;
            }

            if (!visiting.Add(entityName))
            {
                throw new InvalidOperationException(
                    "The target model violated its DAG invariant.");
            }

            var entity = entities[entityName];
            foreach (var relationship in entity.Relationships.OrderBy(
                         item => item.Entity,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!MetaName.Comparer.Equals(
                        relationship.Entity,
                        entity.Name) &&
                    entities.ContainsKey(relationship.Entity))
                {
                    Visit(relationship.Entity);
                }
            }

            visiting.Remove(entityName);
            visited.Add(entityName);
            orderedEntities.Add(entity);
        }
    }

    private static TargetInstantiationPlan CreateInstantiationPlan(
        MetaWeaveScriptTransformation transformation,
        RuntimeRowset rowset,
        GenericModel targetModel)
    {
        var targetEntity = targetModel.FindEntity(transformation.TargetEntityName)
            ?? throw Fault(
                "TargetEntityNotFound",
                $"Target entity '{transformation.TargetEntityName}' does not exist.");
        var targetMembers = targetEntity.Properties
            .Select(property =>
                (Name: property.Name, IsRelationship: false, IsSelfRelationship: false))
            .Concat(targetEntity.Relationships.Select(relationship =>
                (Name: relationship.GetColumnName(),
                 IsRelationship: true,
                 IsSelfRelationship: MetaName.Comparer.Equals(
                     relationship.Entity,
                     targetEntity.Name))))
            .ToDictionary(member => member.Name, StringComparer.OrdinalIgnoreCase);

        var columnOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var ordinal = 0; ordinal < rowset.Columns.Count; ordinal++)
        {
            var name = rowset.Columns[ordinal].Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw Fault(
                    "TargetProjectionNameMissing",
                    $"Transformation '{transformation.Name}' contains an unnamed projected column.");
            }

            if (!columnOrdinals.TryAdd(name, ordinal))
            {
                throw Fault(
                    "TargetProjectionNameDuplicate",
                    $"Transformation '{transformation.Name}' projects column '{name}' more than once.");
            }

            if (!string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase) &&
                !targetMembers.ContainsKey(name))
            {
                throw Fault(
                    "TargetProjectionMemberUnknown",
                    $"Transformation '{transformation.Name}' projects '{name}', which is not a member of target entity '{targetEntity.Name}'.");
            }
        }

        if (!columnOrdinals.TryGetValue("Id", out var idOrdinal))
        {
            throw Fault(
                "TargetIdentityProjectionMissing",
                $"Transformation '{transformation.Name}' must project target identity as 'Id'.");
        }

        var operations = new List<Operation>(rowset.Rows.Count);
        var selfRelationshipOperations = new List<Operation>();
        foreach (var row in rowset.Rows)
        {
            var identityValue = row.Values[idOrdinal];
            if (identityValue.IsNull)
            {
                throw Fault(
                    "TargetIdentityNull",
                    $"Transformation '{transformation.Name}' produced NULL target identity for entity '{targetEntity.Name}'.");
            }

            var identity = identityValue.ToInvariantString();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relationshipIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in targetMembers.Values)
            {
                if (!columnOrdinals.TryGetValue(member.Name, out var memberOrdinal))
                {
                    continue;
                }

                var value = row.Values[memberOrdinal];
                if (value.IsNull)
                {
                    continue;
                }

                if (member.IsRelationship)
                {
                    var targetId = value.ToInvariantString();
                    if (member.IsSelfRelationship)
                    {
                        selfRelationshipOperations.Add(new Operation.SetRelationship(
                            targetEntity.Name,
                            identity,
                            member.Name,
                            targetId));
                    }
                    else
                    {
                        relationshipIds.Add(member.Name, targetId);
                    }
                }
                else
                {
                    values.Add(member.Name, value.ToInvariantString());
                }
            }

            operations.Add(new Operation.InsertRecord(
                targetEntity.Name,
                identity,
                values,
                relationshipIds));
        }

        return new TargetInstantiationPlan(
            operations,
            selfRelationshipOperations);
    }

    private static void AddOperationIssues(
        ICollection<MetaWeaveScriptExecutionIssue> issues,
        MetaWeaveScriptTransformation transformation,
        MetaOperationException exception)
    {
        var operationIssues = exception.Diagnostics?.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Select(issue => new MetaWeaveScriptExecutionIssue(
                issue.Code,
                issue.Message,
                transformation.Name,
                issue.Location))
            .ToArray() ?? [];
        if (operationIssues.Length > 0)
        {
            foreach (var issue in operationIssues)
            {
                issues.Add(issue);
            }

            return;
        }

        issues.Add(new MetaWeaveScriptExecutionIssue(
            "TargetInstantiationFailed",
            exception.Message,
            transformation.Name));
    }

    private static MetaWeaveScriptExecutionFault Fault(string code, string message) =>
        new(code, message);

    private sealed record TargetInstantiationPlan(
        IReadOnlyList<Operation> InsertOperations,
        IReadOnlyList<Operation> SelfRelationshipOperations);
}
