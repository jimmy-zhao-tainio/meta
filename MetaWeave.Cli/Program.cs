using Meta.Core.Domain;
using Meta.Core.Services;
using MetaWeave.Core;

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

        var workspace = await new WorkspaceService().LoadAsync(parseResult.WorkspacePath, searchUpward: false).ConfigureAwait(false);
        var result = await new MetaWeaveService().CheckAsync(workspace).ConfigureAwait(false);
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
        Console.WriteLine($"Workspace: {Path.GetFullPath(parseResult.WorkspacePath)}");
        Console.WriteLine($"Bindings: {result.BindingCount}");
        Console.WriteLine($"ResolvedRows: {result.ResolvedRowCount}");
        Console.WriteLine($"Errors: {result.ErrorCount}");
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
}
