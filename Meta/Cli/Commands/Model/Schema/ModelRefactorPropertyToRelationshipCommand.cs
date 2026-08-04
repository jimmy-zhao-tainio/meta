using System.Text.RegularExpressions;

internal sealed partial class CliRuntime
{
    private static readonly Regex ModelNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    async Task<int> ModelRefactorPropertyToRelationshipAsync(string[] commandArgs)
    {
        var options = ReadModelRefactorPropertyToRelationshipOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;
        var refactorOptions = commandOptions.Refactor;

        try
        {
            var operation = new Operation.PropertyToRelationship(
                refactorOptions.SourceEntityName,
                refactorOptions.SourcePropertyName,
                refactorOptions.TargetEntityName,
                refactorOptions.LookupPropertyName,
                refactorOptions.Role,
                PreserveProperty: !refactorOptions.DropSourceProperty);

            return await ExecuteOperationsAsync(
                    [operation],
                    "model refactor property-to-relationship",
                    "refactor property-to-relationship",
                    buildSuccessDetails: results =>
                    {
                        var result = (PropertyToRelationshipResult)results.Single();
                        return new (string Key, string Value)[]
                        {
                            ("Source", refactorOptions.SourceEntityName + "." + refactorOptions.SourcePropertyName),
                            ("Target", refactorOptions.TargetEntityName),
                            ("Lookup", refactorOptions.TargetEntityName + "." + refactorOptions.LookupPropertyName),
                            ("Role", string.IsNullOrWhiteSpace(refactorOptions.Role) ? "(none)" : refactorOptions.Role),
                            ("Preserve property", result.PropertyRemoved ? "no" : "yes"),
                        };
                    },
                    writeSuccessOutput: results =>
                    {
                        var result = (PropertyToRelationshipResult)results.Single();
                        presenter.WriteInfo($"Rows rewritten: {result.SourceRecordCount}");
                        presenter.WriteInfo($"Property dropped: {(result.PropertyRemoved ? "yes" : "no")}");
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }
    }

    (bool Ok, PropertyToRelationshipCommandOptions Options, string ErrorMessage)
        ReadModelRefactorPropertyToRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        var source = RequiredValue("source").Trim();
        var target = RequiredValue("target").Trim();
        var lookup = RequiredValue("lookup").Trim();
        var role = OptionalValue("role").Trim();
        var preserveProperty = Flag("preserve-property");

        var separatorIndex = source.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == source.Length - 1 || source.IndexOf('.', separatorIndex + 1) >= 0)
        {
            return (false, default, "Error: --source must be in format <Entity.Property>.");
        }

        var sourceEntityName = source[..separatorIndex].Trim();
        var sourcePropertyName = source[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(sourceEntityName) || string.IsNullOrWhiteSpace(sourcePropertyName))
        {
            return (false, default, "Error: --source must be in format <Entity.Property>.");
        }

        var options = new PropertyToRelationshipCommandOptions(
            WorkspacePath: workspacePath,
            Refactor: new PropertyToRelationshipOptions(
                SourceEntityName: sourceEntityName,
                SourcePropertyName: sourcePropertyName,
                TargetEntityName: target,
                LookupPropertyName: lookup,
                Role: role,
                DropSourceProperty: !preserveProperty));

        return (true, options, string.Empty);
    }

    readonly record struct PropertyToRelationshipCommandOptions(
        string WorkspacePath,
        PropertyToRelationshipOptions Refactor);

    readonly record struct PropertyToRelationshipOptions(
        string SourceEntityName,
        string SourcePropertyName,
        string TargetEntityName,
        string LookupPropertyName,
        string Role,
        bool DropSourceProperty);
}
