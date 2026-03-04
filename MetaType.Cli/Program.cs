using Meta.Core.Services;
using MetaType.Core;

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

        Console.WriteLine($"Error: unknown command '{args[0]}'.");
        Console.WriteLine("Next: meta-type help");
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
            Console.WriteLine("Next: meta-type init --help");
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

        var workspace = MetaTypeWorkspaces.CreateMetaTypeWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: metatype workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == Meta.Core.Domain.IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            Console.WriteLine("Next: fix the sanctioned model and retry init.");
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);

        Console.WriteLine("OK: metatype workspace created");
        Console.WriteLine($"Path: {workspacePath}");
        Console.WriteLine($"Model: {workspace.Model.Name}");
        Console.WriteLine($"TypeSystems: {workspace.Instance.GetOrCreateEntityRecords("TypeSystem").Count}");
        Console.WriteLine($"Types: {workspace.Instance.GetOrCreateEntityRecords("Type").Count}");
        Console.WriteLine($"TypeSpecs: {workspace.Instance.GetOrCreateEntityRecords("TypeSpec").Count}");
        return 0;
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
        Console.WriteLine("MetaType CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help        Show this help.");
        Console.WriteLine("  init        Create a new MetaType workspace.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-type init --help");
    }

    private static void PrintInitHelp()
    {
        Console.WriteLine("Command: init");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-type init --new-workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Creates a new workspace with the MetaType model, sanctioned type instances, and validates it.");
    }
}
