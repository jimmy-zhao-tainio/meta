using Meta.Core.Presentation.Cli;

namespace MetaCli.Core;

public sealed record MetaCliDescriptor(
    string Name,
    string Version,
    IReadOnlyList<string> SupportedModels,
    IReadOnlyList<MetaCliOperationDescriptor> Operations);

public sealed record MetaCliOperationDescriptor(
    string Name,
    string Description,
    IReadOnlyList<MetaCliParameterDescriptor> Inputs,
    IReadOnlyList<MetaCliParameterDescriptor> Outputs,
    IReadOnlyList<string> Effects,
    bool CanHandleHostRequest);

public sealed record MetaCliParameterDescriptor(
    string Name,
    string ValueLabel,
    bool Required);

public static class MetaCliDescriptorFactory
{
    public static MetaCliDescriptor FromCliAppDefinition(
        CliAppDefinition app,
        string version,
        IEnumerable<string> supportedModels,
        IEnumerable<string>? hostCallableOperations = null,
        Func<CliCommandDefinition, IReadOnlyList<string>>? resolveEffects = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var hostCallable = new HashSet<string>(
            hostCallableOperations ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var operations = app.Commands
            .Where(static command => command.ShowInCommandCatalog)
            .Select(command => new MetaCliOperationDescriptor(
                command.Name,
                command.Description,
                command.Options.Select(ToParameter).ToArray(),
                Array.Empty<MetaCliParameterDescriptor>(),
                resolveEffects?.Invoke(command) ?? InferEffects(command),
                hostCallable.Contains(command.Name)))
            .ToArray();

        return new MetaCliDescriptor(
            app.Name,
            string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim(),
            supportedModels.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            operations);
    }

    private static MetaCliParameterDescriptor ToParameter(CliOptionDefinition option)
    {
        var syntax = option.Syntax.Trim();
        var parts = syntax.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length == 0 ? syntax : parts[0];
        var valueLabel = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;
        var required = option.Description.Contains("Required.", StringComparison.OrdinalIgnoreCase);
        return new MetaCliParameterDescriptor(name, valueLabel, required);
    }

    private static IReadOnlyList<string> InferEffects(CliCommandDefinition command)
    {
        var text = string.Join(
            " ",
            new[] { command.Name, command.Description }.Concat(command.Notes).Concat(command.Usages));

        if (text.Contains("--new-workspace", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("creates", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "derives workspace" };
        }

        if (text.Contains("--workspace", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("add", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("update", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("mount", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("link", StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { "mutates workspace" };
        }

        return new[] { "pure" };
    }
}
