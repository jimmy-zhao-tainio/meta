using Meta.Core.Domain;
using Meta.Core.Services;
using MetaTypeConversion.Core;

internal static class Program
{
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

        if (string.Equals(args[0], "resolve", StringComparison.OrdinalIgnoreCase))
        {
            return await RunResolveAsync(args).ConfigureAwait(false);
        }

        Console.WriteLine($"Error: unknown command '{args[0]}'.");
        Console.WriteLine("Next: meta-type-conversion help");
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
            Console.WriteLine("Next: meta-type-conversion init --help");
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

        var workspace = MetaTypeConversionWorkspaces.CreateMetaTypeConversionWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: metatypeconversion workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            Console.WriteLine("Next: fix the sanctioned model and retry init.");
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);

        Console.WriteLine("OK: metatypeconversion workspace created");
        Console.WriteLine($"Path: {workspacePath}");
        Console.WriteLine($"Model: {workspace.Model.Name}");
        Console.WriteLine($"ConversionImplementations: {workspace.Instance.GetOrCreateEntityRecords("ConversionImplementation").Count}");
        Console.WriteLine($"TypeMappings: {workspace.Instance.GetOrCreateEntityRecords("TypeMapping").Count}");
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
            Console.WriteLine("Next: meta-type-conversion check --help");
            return 1;
        }

        Workspace workspace;
        try
        {
            workspace = await new WorkspaceService().LoadAsync(Path.GetFullPath(parseResult.WorkspacePath), searchUpward: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }

        var result = new MetaTypeConversionService().Check(workspace);
        if (result.HasErrors)
        {
            Console.WriteLine("Error: metatypeconversion check failed.");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
            return 2;
        }

        Console.WriteLine("OK: metatypeconversion check");
        Console.WriteLine($"Workspace: {Path.GetFullPath(parseResult.WorkspacePath)}");
        Console.WriteLine($"ConversionImplementations: {result.ImplementationCount}");
        Console.WriteLine($"TypeMappings: {result.MappingCount}");
        Console.WriteLine("Errors: 0");
        return 0;
    }

    private static async Task<int> RunResolveAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintResolveHelp();
            return 0;
        }

        var parseResult = ParseResolveArgs(args, startIndex: 1);
        if (!parseResult.Ok)
        {
            Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            Console.WriteLine("Next: meta-type-conversion resolve --help");
            return 1;
        }

        Workspace workspace;
        try
        {
            workspace = await new WorkspaceService().LoadAsync(Path.GetFullPath(parseResult.WorkspacePath), searchUpward: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }

        try
        {
            var resolution = new MetaTypeConversionService().Resolve(workspace, parseResult.SourceTypeId);
            Console.WriteLine("OK: metatypeconversion resolve");
            Console.WriteLine($"Workspace: {Path.GetFullPath(parseResult.WorkspacePath)}");
            Console.WriteLine($"SourceTypeId: {resolution.SourceTypeId}");
            Console.WriteLine($"TargetTypeId: {resolution.TargetTypeId}");
            Console.WriteLine($"ConversionImplementation: {resolution.ConversionImplementationName}");
            if (!string.IsNullOrWhiteSpace(resolution.Notes))
            {
                Console.WriteLine($"Notes: {resolution.Notes}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 4;
        }
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

    private static (bool Ok, string WorkspacePath, string SourceTypeId, string ErrorMessage) ParseResolveArgs(string[] args, int startIndex)
    {
        var workspacePath = string.Empty;
        var sourceTypeId = string.Empty;
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 >= args.Length)
            {
                return (false, workspacePath, sourceTypeId, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    if (!string.IsNullOrWhiteSpace(workspacePath))
                    {
                        return (false, workspacePath, sourceTypeId, "--workspace can only be provided once.");
                    }
                    workspacePath = args[++i];
                    break;
                case "--source-type":
                    if (!string.IsNullOrWhiteSpace(sourceTypeId))
                    {
                        return (false, workspacePath, sourceTypeId, "--source-type can only be provided once.");
                    }
                    sourceTypeId = args[++i];
                    break;
                default:
                    return (false, workspacePath, sourceTypeId, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return (false, string.Empty, sourceTypeId, "missing required option --workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(sourceTypeId))
        {
            return (false, workspacePath, string.Empty, "missing required option --source-type <id>.");
        }

        return (true, workspacePath, sourceTypeId, string.Empty);
    }

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MetaTypeConversion CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type-conversion <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help        Show this help.");
        Console.WriteLine("  init        Create a new MetaTypeConversion workspace.");
        Console.WriteLine("  check       Validate sanctioned type mappings.");
        Console.WriteLine("  resolve     Resolve one source type id through the sanctioned mappings.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-type-conversion init --help");
    }

    private static void PrintInitHelp()
    {
        Console.WriteLine("Command: init");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type-conversion init --new-workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Creates a new workspace with the MetaTypeConversion model and validates it.");
    }

    private static void PrintCheckHelp()
    {
        Console.WriteLine("Command: check");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type-conversion check --workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Validates that each source type maps deterministically and that every mapping references a real ConversionImplementation.");
    }

    private static void PrintResolveHelp()
    {
        Console.WriteLine("Command: resolve");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type-conversion resolve --workspace <path> --source-type <id>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Resolves one source type id to its target type id and conversion implementation.");
    }
}
