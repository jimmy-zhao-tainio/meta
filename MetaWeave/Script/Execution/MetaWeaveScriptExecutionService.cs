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
        SelectStatement selectStatement,
        InMemoryWorkspace sourceWorkspace)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(selectStatement);
        ArgumentNullException.ThrowIfNull(sourceWorkspace);

        var sourceIssues = ValidateSourceWorkspace(sourceWorkspace);
        if (sourceIssues.Count > 0)
        {
            return new MetaWeaveScriptQueryResult(null, sourceIssues);
        }

        try
        {
            var snapshot = sourceWorkspace.Clone();
            var rowset = new MetaWeaveScriptExecutionSession(
                model,
                selectStatement,
                snapshot).Execute();
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
        InMemoryWorkspace targetWorkspace)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(targetWorkspace);

        var issues = new List<MetaWeaveScriptExecutionIssue>();
        ValidateDirection(direction, sourceWorkspace, targetWorkspace, issues);
        var requirements = direction.Requirements ?? [];
        var transformations = direction.Transformations ?? [];
        issues.AddRange(ValidateSourceWorkspace(sourceWorkspace));
        issues.AddRange(ValidateTargetWorkspace(targetWorkspace));
        ValidateRequirements(requirements, issues);
        ValidateTransformations(transformations, targetWorkspace.Model, issues);
        var orderedTransformations = issues.Count == 0
            ? OrderTransformations(transformations, targetWorkspace.Model)
            : [];
        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        var sourceSnapshot = sourceWorkspace.Clone();
        ExecuteRequirements(direction.Model, requirements, sourceSnapshot, issues);
        if (issues.Count > 0)
        {
            return new MetaWeaveScriptApplicationResult(null, issues);
        }

        var currentTarget = targetWorkspace.Clone();

        foreach (var transformation in orderedTransformations)
        {
            try
            {
                var rowset = new MetaWeaveScriptExecutionSession(
                    direction.Model,
                    transformation.SelectStatement,
                    sourceSnapshot).Execute();
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
            }
            catch (MetaWeaveScriptExecutionFault fault)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    fault.Code,
                    fault.Message,
                    transformation.Name,
                    fault.SyntaxId));
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

        return new MetaWeaveScriptApplicationResult(currentTarget, []);
    }

    private static void ValidateDirection(
        MetaWeaveScriptDirection direction,
        InMemoryWorkspace sourceWorkspace,
        InMemoryWorkspace targetWorkspace,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(direction.Name))
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "DirectionNameMissing",
                "A WeaveScript direction requires a name."));
        }

        if (!MetaName.Comparer.Equals(
                direction.SourceModelName,
                sourceWorkspace.Model.Name))
        {
            issues.Add(new MetaWeaveScriptExecutionIssue(
                "SourceModelMismatch",
                $"Direction '{direction.Name}' requires source model '{direction.SourceModelName}' but received '{sourceWorkspace.Model.Name}'."));
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

    private static void ExecuteRequirements(
        MetaWeaveModel model,
        IReadOnlyList<MetaWeaveScriptRequirement> requirements,
        InMemoryWorkspace sourceWorkspace,
        ICollection<MetaWeaveScriptExecutionIssue> issues)
    {
        foreach (var requirement in requirements)
        {
            try
            {
                var rowset = new MetaWeaveScriptExecutionSession(
                    model,
                    requirement.SelectStatement,
                    sourceWorkspace).Execute();
                foreach (var row in rowset.Rows)
                {
                    issues.Add(new MetaWeaveScriptExecutionIssue(
                        requirement.Code,
                        FormatRequirementViolation(requirement.Message, rowset.Columns, row),
                        RequirementName: requirement.Name));
                }
            }
            catch (MetaWeaveScriptExecutionFault fault)
            {
                issues.Add(new MetaWeaveScriptExecutionIssue(
                    fault.Code,
                    fault.Message,
                    SyntaxId: fault.SyntaxId,
                    RequirementName: requirement.Name));
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

    private static IReadOnlyList<MetaWeaveScriptExecutionIssue> ValidateSourceWorkspace(
        InMemoryWorkspace sourceWorkspace)
    {
        var diagnostics = WorkspaceValidator.Validate(sourceWorkspace.Model, sourceWorkspace.Instance);
        return diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Select(issue => new MetaWeaveScriptExecutionIssue(
                "SourceWorkspace." + issue.Code,
                issue.Message,
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
