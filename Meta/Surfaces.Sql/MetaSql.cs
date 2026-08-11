using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

public sealed record MetaSql(
    string DatabaseName,
    string Schema,
    string Data);

public static class MetaSqlWriter
{
    public static MetaSql Write(InMemoryWorkspace state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var diagnostics = WorkspaceValidator.Validate(
            state.Model,
            state.Instance);
        if (diagnostics.HasErrors)
        {
            var errors = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue =>
                    $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException(
                "Cannot write SQL for invalid metadata. " +
                string.Join(" | ", errors));
        }

        return new MetaSql(
            state.Model.Name,
            SqlGenerationArtifacts.BuildSchema(state),
            SqlGenerationArtifacts.BuildData(state));
    }
}
