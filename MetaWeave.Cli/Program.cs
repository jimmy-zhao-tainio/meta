using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;

internal static class Program
{
    private const int PersistRetryCount = 3;
    private static readonly TimeSpan PersistRetryDelay = TimeSpan.FromMilliseconds(50);

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        {
            return await RunInitAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
        {
            return await RunCheckAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-model", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddModelAsync(args).ConfigureAwait(false);
        }

        if (string.Equals(args[0], "add-binding", StringComparison.OrdinalIgnoreCase))
        {
            return await RunAddBindingAsync(args).ConfigureAwait(false);
        }

        Console.WriteLine($"Error: unknown command '{args[0]}'.");
        Console.WriteLine("Next: meta-weave help");
        return 1;
    }

    private static async Task<int> RunInitAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintInitHelp();
            return 0;
        }

        var parseResult = ParseNewWorkspaceOnly(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            Console.WriteLine("Next: meta-weave init --help");
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.NewWorkspacePath);
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            Console.WriteLine($"Error: target directory '{workspacePath}' must be empty.");
            Console.WriteLine("Next: choose a new folder or empty the target directory and retry.");
            return 4;
        }

        Directory.CreateDirectory(workspacePath);

        var workspace = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: metaweave workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            Console.WriteLine("Next: fix the sanctioned model and retry init.");
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);
        await WaitForWorkspaceReadyAsync(workspacePath).ConfigureAwait(false);

        Console.WriteLine("OK: metaweave workspace created");
        Console.WriteLine($"Path: {workspacePath}");
        Console.WriteLine($"Model: {workspace.Model.Name}");
        return 0;
    }

    private static async Task<int> RunCheckAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintCheckHelp();
            return 0;
        }

        var parseResult = ParseWorkspaceOnly(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            Console.WriteLine("Next: meta-weave check --help");
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.WorkspacePath);
        Workspace workspace;
        WeaveCheckResult result;
        try
        {
            workspace = await new WorkspaceService().LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
            result = await new MetaWeaveService().CheckAsync(workspace).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }

        if (result.HasErrors)
        {
            Console.WriteLine("Error: weave check failed.");
            foreach (var binding in result.Bindings)
            {
                foreach (var error in binding.Errors)
                {
                    Console.WriteLine($"  - {binding.BindingName}: {error}");
                }
            }
            return 2;
        }

        Console.WriteLine("OK: weave check");
        Console.WriteLine($"Workspace: {workspacePath}");
        Console.WriteLine($"Bindings: {result.BindingCount}");
        Console.WriteLine($"ResolvedRows: {result.ResolvedRowCount}");
        Console.WriteLine($"Errors: {result.ErrorCount}");
        return 0;
    }

    private static async Task<int> RunAddModelAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddModelHelp();
            return 0;
        }

        (bool Ok, string WorkspacePath, string Alias, string ModelName, string ModelWorkspacePath, string ErrorMessage) parse;
        try
        {
            parse = ParseAddModelArgs(args, 1);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Next: meta-weave add-model --help");
            return 1;
        }
        if (!parse.Ok)
        {
            Console.WriteLine($"Error: {parse.ErrorMessage}");
            Console.WriteLine("Next: meta-weave add-model --help");
            return 1;
        }

        var workspacePath = Path.GetFullPath(parse.WorkspacePath);
        var workspaceService = new WorkspaceService();
        var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
        try
        {
            var resolvedWorkspacePath = Path.GetFullPath(parse.ModelWorkspacePath);
            new MetaWeaveAuthoringService().AddModelReference(workspace, parse.Alias, parse.ModelName, resolvedWorkspacePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }

        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: weave workspace is invalid after add-model.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            return 4;
        }

        await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
        await WaitForPersistedModelReferenceAsync(workspacePath, parse.Alias).ConfigureAwait(false);
        Console.WriteLine("OK: weave model reference added");
        Console.WriteLine($"Workspace: {workspacePath}");
        Console.WriteLine($"Alias: {parse.Alias}");
        Console.WriteLine($"Model: {parse.ModelName}");
        Console.WriteLine($"WorkspacePath: {parse.ModelWorkspacePath}");
        return 0;
    }

    private static async Task<int> RunAddBindingAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintAddBindingHelp();
            return 0;
        }

        (bool Ok, string WorkspacePath, string Name, string SourceModelAlias, string SourceEntity, string SourceProperty, string TargetModelAlias, string TargetEntity, string TargetProperty, string ErrorMessage) parse;
        try
        {
            parse = ParseAddBindingArgs(args, 1);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("Next: meta-weave add-binding --help");
            return 1;
        }
        if (!parse.Ok)
        {
            Console.WriteLine($"Error: {parse.ErrorMessage}");
            Console.WriteLine("Next: meta-weave add-binding --help");
            return 1;
        }

        var workspacePath = Path.GetFullPath(parse.WorkspacePath);
        var workspaceService = new WorkspaceService();
        var workspace = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
        try
        {
            new MetaWeaveAuthoringService().AddPropertyBinding(
                workspace,
                parse.Name,
                parse.SourceModelAlias,
                parse.SourceEntity,
                parse.SourceProperty,
                parse.TargetModelAlias,
                parse.TargetEntity,
                parse.TargetProperty);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }

        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: weave workspace is invalid after add-binding.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            return 4;
        }

        await workspaceService.SaveAsync(workspace).ConfigureAwait(false);
        await WaitForPersistedBindingAsync(workspacePath, parse.Name).ConfigureAwait(false);
        Console.WriteLine("OK: weave property binding added");
        Console.WriteLine($"Workspace: {workspacePath}");
        Console.WriteLine($"Binding: {parse.Name}");
        return 0;
    }

    private static (bool Ok, string WorkspacePath, string ErrorMessage) ParseWorkspaceOnly(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (!string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                return (false, workspacePath, $"unknown option '{arg}'.");
            }

            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, "missing value for --workspace.");
            }

            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                return (false, workspacePath, "--workspace can only be provided once.");
            }

            workspacePath = args[++i];
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, string.Empty, "missing required option --workspace <path>.");
        }

        return (true, workspacePath, string.Empty);
    }

    private static (bool Ok, string NewWorkspacePath, string ErrorMessage) ParseNewWorkspaceOnly(string[] args, int startIndex)
    {
        var newWorkspacePath = string.Empty;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (!string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                return (false, newWorkspacePath, $"unknown option '{arg}'.");
            }

            if (i + 1 >= args.Length)
            {
                return (false, newWorkspacePath, "missing value for --new-workspace.");
            }

            if (!string.IsNullOrWhiteSpace(newWorkspacePath))
            {
                return (false, newWorkspacePath, "--new-workspace can only be provided once.");
            }

            newWorkspacePath = args[++i];
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, string.Empty, "missing required option --new-workspace <path>.");
        }

        return (true, newWorkspacePath, string.Empty);
    }

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MetaWeave CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help        Show this help.");
        Console.WriteLine("  init        Create a new MetaWeave workspace.");
        Console.WriteLine("  add-model   Add a referenced model workspace.");
        Console.WriteLine("  add-binding Add a property binding between two model references.");
        Console.WriteLine("  check       Validate property bindings across referenced workspaces.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-weave check --help");
    }

    private static void PrintInitHelp()
    {
        Console.WriteLine("Command: init");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave init --new-workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Creates a new workspace with the MetaWeave model and validates it.");
    }

    private static void PrintCheckHelp()
    {
        Console.WriteLine("Command: check");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave check --workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Loads referenced workspaces and validates that every bound source property resolves exactly once in the target model.");
    }

    private static void PrintAddModelHelp()
    {
        Console.WriteLine("Command: add-model");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave add-model --workspace <path> --alias <alias> --model <modelName> --workspace-path <path>");
    }

    private static void PrintAddBindingHelp()
    {
        Console.WriteLine("Command: add-binding");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-weave add-binding --workspace <path> --name <bindingName> --source-model <alias> --source-entity <entity> --source-property <property> --target-model <alias> --target-entity <entity> --target-property <property>");
    }

    private static (bool Ok, string WorkspacePath, string Alias, string ModelName, string ModelWorkspacePath, string ErrorMessage) ParseAddModelArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var alias = string.Empty;
        var modelName = string.Empty;
        var modelWorkspacePath = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, alias, modelName, modelWorkspacePath, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--alias":
                    alias = EnsureUnsetThenAssign(alias, args[++i], "--alias");
                    break;
                case "--model":
                    modelName = EnsureUnsetThenAssign(modelName, args[++i], "--model");
                    break;
                case "--workspace-path":
                    modelWorkspacePath = EnsureUnsetThenAssign(modelWorkspacePath, args[++i], "--workspace-path");
                    break;
                default:
                    return (false, workspacePath, alias, modelName, modelWorkspacePath, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(alias))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --alias <alias>.");
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --model <modelName>.");
        }

        if (string.IsNullOrWhiteSpace(modelWorkspacePath))
        {
            return (false, workspacePath, alias, modelName, modelWorkspacePath, "missing required option --workspace-path <path>.");
        }

        return (true, workspacePath, alias, modelName, modelWorkspacePath, string.Empty);
    }

    private static (bool Ok, string WorkspacePath, string Name, string SourceModelAlias, string SourceEntity, string SourceProperty, string TargetModelAlias, string TargetEntity, string TargetProperty, string ErrorMessage) ParseAddBindingArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var name = string.Empty;
        var sourceModelAlias = string.Empty;
        var sourceEntity = string.Empty;
        var sourceProperty = string.Empty;
        var targetModelAlias = string.Empty;
        var targetEntity = string.Empty;
        var targetProperty = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    workspacePath = EnsureUnsetThenAssign(workspacePath, args[++i], "--workspace");
                    break;
                case "--name":
                    name = EnsureUnsetThenAssign(name, args[++i], "--name");
                    break;
                case "--source-model":
                    sourceModelAlias = EnsureUnsetThenAssign(sourceModelAlias, args[++i], "--source-model");
                    break;
                case "--source-entity":
                    sourceEntity = EnsureUnsetThenAssign(sourceEntity, args[++i], "--source-entity");
                    break;
                case "--source-property":
                    sourceProperty = EnsureUnsetThenAssign(sourceProperty, args[++i], "--source-property");
                    break;
                case "--target-model":
                    targetModelAlias = EnsureUnsetThenAssign(targetModelAlias, args[++i], "--target-model");
                    break;
                case "--target-entity":
                    targetEntity = EnsureUnsetThenAssign(targetEntity, args[++i], "--target-entity");
                    break;
                case "--target-property":
                    targetProperty = EnsureUnsetThenAssign(targetProperty, args[++i], "--target-property");
                    break;
                default:
                    return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --name <bindingName>.");
        }

        if (string.IsNullOrWhiteSpace(sourceModelAlias))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-model <alias>.");
        }

        if (string.IsNullOrWhiteSpace(sourceEntity))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(sourceProperty))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --source-property <property>.");
        }

        if (string.IsNullOrWhiteSpace(targetModelAlias))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-model <alias>.");
        }

        if (string.IsNullOrWhiteSpace(targetEntity))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-entity <entity>.");
        }

        if (string.IsNullOrWhiteSpace(targetProperty))
        {
            return (false, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, "missing required option --target-property <property>.");
        }

        return (true, workspacePath, name, sourceModelAlias, sourceEntity, sourceProperty, targetModelAlias, targetEntity, targetProperty, string.Empty);
    }

    private static string EnsureUnsetThenAssign(string currentValue, string nextValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            throw new InvalidOperationException($"{optionName} can only be provided once.");
        }

        return nextValue;
    }

    private static async Task WaitForWorkspaceReadyAsync(string workspacePath)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => !string.IsNullOrWhiteSpace(workspace.Model?.Name),
            "workspace initialization").ConfigureAwait(false);
    }

    private static async Task WaitForPersistedModelReferenceAsync(string workspacePath, string alias)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => workspace.Instance.GetOrCreateEntityRecords("ModelReference")
                .Any(record =>
                    record.Values.TryGetValue("Alias", out var value) &&
                    string.Equals(value, alias, StringComparison.Ordinal)),
            $"model reference '{alias}'").ConfigureAwait(false);
    }

    private static async Task WaitForPersistedBindingAsync(string workspacePath, string bindingName)
    {
        await WaitForWorkspaceStateAsync(
            workspacePath,
            workspace => workspace.Instance.GetOrCreateEntityRecords("PropertyBinding")
                .Any(record =>
                    record.Values.TryGetValue("Name", out var value) &&
                    string.Equals(value, bindingName, StringComparison.Ordinal)),
            $"property binding '{bindingName}'").ConfigureAwait(false);
    }

    private static async Task WaitForWorkspaceStateAsync(string workspacePath, Func<Workspace, bool> predicate, string description)
    {
        var workspaceService = new WorkspaceService();
        var lockPath = Path.Combine(workspacePath, ".meta.lock");
        for (var attempt = 0; attempt < PersistRetryCount; attempt++)
        {
            try
            {
                if (File.Exists(lockPath))
                {
                    throw new IOException($"Workspace lock '{lockPath}' is still present.");
                }

                var loaded = await workspaceService.LoadAsync(workspacePath, searchUpward: false).ConfigureAwait(false);
                if (predicate(loaded))
                {
                    return;
                }
            }
            catch (Exception) when (attempt < PersistRetryCount - 1)
            {
                // Retry below.
            }

            if (attempt < PersistRetryCount - 1)
            {
                await Task.Delay(PersistRetryDelay).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Workspace save did not persist expected {description}.");
    }
}
