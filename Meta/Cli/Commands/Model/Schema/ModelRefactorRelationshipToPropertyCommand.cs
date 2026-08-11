using Meta.Operations.Domain;

internal sealed partial class CliRuntime
{
    async Task<int> ModelRefactorRelationshipToPropertyAsync(string[] commandArgs)
    {
        var options = ReadModelRefactorRelationshipToPropertyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;
        var refactorOptions = commandOptions.Refactor;

        try
        {
            var operation = new Operation.RelationshipToProperty(
                refactorOptions.SourceEntityName,
                refactorOptions.TargetEntityName,
                refactorOptions.Role,
                refactorOptions.PropertyName);

            return await ExecuteOperationsAsync(
                    [operation],
                    "model refactor relationship-to-property",
                    "refactor relationship-to-property",
                    buildSuccessDetails: results =>
                    {
                        var result = (RelationshipToPropertyResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("Source", refactorOptions.SourceEntityName),
                            ("Target", refactorOptions.TargetEntityName),
                            ("Role", string.IsNullOrWhiteSpace(refactorOptions.Role) ? "(none)" : refactorOptions.Role),
                            ("Property", result.PropertyName),
                        };
                    },
                    writeSuccessOutput: results =>
                    {
                        var result = (RelationshipToPropertyResult)results.Single();
                        presenter.WriteInfo($"Rows rewritten: {result.SourceRecordCount}");
                        presenter.WriteInfo("Relationship removed: yes");
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }

    (bool Ok, RelationshipToPropertyCommandOptions Options, string ErrorMessage)
        ReadModelRefactorRelationshipToPropertyOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        var source = RequiredValue("source").Trim();
        var target = RequiredValue("target").Trim();
        var role = OptionalValue("role").Trim();
        var propertyName = OptionalValue("property").Trim();

        if (!string.IsNullOrWhiteSpace(role) && !ModelNamePattern.IsMatch(role))
        {
            return (false, default, "Error: --role must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        if (!string.IsNullOrWhiteSpace(propertyName) && !ModelNamePattern.IsMatch(propertyName))
        {
            return (false, default, "Error: --property must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        var options = new RelationshipToPropertyCommandOptions(
            WorkspacePath: workspacePath,
            Refactor: new RelationshipToPropertyOptions(
                SourceEntityName: source,
                TargetEntityName: target,
                Role: role,
                PropertyName: propertyName));

        return (true, options, string.Empty);
    }

    readonly record struct RelationshipToPropertyCommandOptions(
        string WorkspacePath,
        RelationshipToPropertyOptions Refactor);

    readonly record struct RelationshipToPropertyOptions(
        string SourceEntityName,
        string TargetEntityName,
        string Role,
        string PropertyName);
}
