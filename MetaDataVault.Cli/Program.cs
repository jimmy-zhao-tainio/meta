using Meta.Core.Services;
using Meta.Core.Domain;
using MetaDataVault.Core;

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

        if (string.Equals(args[0], "from-metaschema", StringComparison.OrdinalIgnoreCase))
        {
            return await RunFromMetaSchemaAsync(args).ConfigureAwait(false);
        }

        Console.WriteLine($"Error: unknown command '{args[0]}'.");
        Console.WriteLine("Next: meta-datavault help");
        return 1;
    }

    private static async Task<int> RunInitAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintInitHelp();
            return 0;
        }

        var parseResult = ParseNewWorkspaceOnly(args, 1);
        if (!parseResult.Ok)
        {
            Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            Console.WriteLine("Next: meta-datavault init --help");
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
        var workspace = MetaDataVaultWorkspaces.CreateEmptyMetaRawDataVaultWorkspace(workspacePath);
        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: metarawdatavault workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == Meta.Core.Domain.IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            Console.WriteLine("Next: fix the sanctioned model and retry init.");
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);
        Console.WriteLine("OK: metarawdatavault workspace created");
        Console.WriteLine($"Path: {workspacePath}");
        Console.WriteLine($"Model: {workspace.Model.Name}");
        return 0;
    }

    private static async Task<int> RunFromMetaSchemaAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintFromMetaSchemaHelp();
            return 0;
        }

        var parse = ParseFromMetaSchemaArgs(args, 1);
        if (!parse.Ok)
        {
            Console.WriteLine($"Error: {parse.ErrorMessage}");
            Console.WriteLine("Next: meta-datavault from-metaschema --help");
            return 1;
        }

        var sourceWorkspacePath = Path.GetFullPath(parse.SourceWorkspacePath);
        var newWorkspacePath = Path.GetFullPath(parse.NewWorkspacePath);
        if (Directory.Exists(newWorkspacePath) && Directory.EnumerateFileSystemEntries(newWorkspacePath).Any())
        {
            Console.WriteLine($"Error: target directory '{newWorkspacePath}' must be empty.");
            Console.WriteLine("Next: choose a new folder or empty the target directory and retry.");
            return 4;
        }

        Workspace sourceWorkspace;
        try
        {
            sourceWorkspace = await new WorkspaceService().LoadAsync(sourceWorkspacePath, searchUpward: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: could not load source workspace '{sourceWorkspacePath}'.");
            Console.WriteLine($"  - {ex.Message}");
            return 4;
        }

        Workspace rawDataVaultWorkspace;
        try
        {
            rawDataVaultWorkspace = new MetaSchemaToRawDataVaultConverter().Convert(sourceWorkspace, newWorkspacePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: could not convert MetaSchema workspace to raw datavault.");
            Console.WriteLine($"  - {ex.Message}");
            return 4;
        }

        var validation = new ValidationService().Validate(rawDataVaultWorkspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: generated metarawdatavault workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == Meta.Core.Domain.IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            return 4;
        }

        Directory.CreateDirectory(newWorkspacePath);
        await new WorkspaceService().SaveAsync(rawDataVaultWorkspace).ConfigureAwait(false);

        Console.WriteLine("OK: raw datavault generated from metaschema");
        Console.WriteLine($"Source Workspace: {sourceWorkspacePath}");
        Console.WriteLine($"Path: {newWorkspacePath}");
        Console.WriteLine($"Model: {rawDataVaultWorkspace.Model.Name}");
        Console.WriteLine($"SourceTables: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("SourceTable").Count}");
        Console.WriteLine($"RawHubs: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("RawHub").Count}");
        Console.WriteLine($"RawLinks: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("RawLink").Count}");
        Console.WriteLine($"RawLinkEnds: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("RawLinkEnd").Count}");
        Console.WriteLine($"RawSatellites: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("RawSatellite").Count}");
        Console.WriteLine($"RawSatelliteAttributes: {rawDataVaultWorkspace.Instance.GetOrCreateEntityRecords("RawSatelliteAttribute").Count}");
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

    private static (bool Ok, string SourceWorkspacePath, string NewWorkspacePath, string ErrorMessage) ParseFromMetaSchemaArgs(string[] args, int startIndex)
    {
        var sourceWorkspacePath = string.Empty;
        var newWorkspacePath = string.Empty;

        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--source-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, sourceWorkspacePath, newWorkspacePath, "missing value for --source-workspace.");
                }

                if (!string.IsNullOrWhiteSpace(sourceWorkspacePath))
                {
                    return (false, sourceWorkspacePath, newWorkspacePath, "--source-workspace can only be provided once.");
                }

                sourceWorkspacePath = args[++i];
                continue;
            }

            if (string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, sourceWorkspacePath, newWorkspacePath, "missing value for --new-workspace.");
                }

                if (!string.IsNullOrWhiteSpace(newWorkspacePath))
                {
                    return (false, sourceWorkspacePath, newWorkspacePath, "--new-workspace can only be provided once.");
                }

                newWorkspacePath = args[++i];
                continue;
            }

            return (false, sourceWorkspacePath, newWorkspacePath, $"unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(sourceWorkspacePath))
        {
            return (false, sourceWorkspacePath, newWorkspacePath, "missing required option --source-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, sourceWorkspacePath, newWorkspacePath, "missing required option --new-workspace <path>.");
        }

        return (true, sourceWorkspacePath, newWorkspacePath, string.Empty);
    }

    private static bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MetaDataVault CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-datavault <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help            Show this help.");
        Console.WriteLine("  init            Create an empty MetaRawDataVault workspace.");
        Console.WriteLine("  from-metaschema Consume a MetaSchema workspace and generate raw datavault metadata.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-datavault from-metaschema --help");
    }

    private static void PrintInitHelp()
    {
        Console.WriteLine("Command: init");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-datavault init --new-workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Creates a new workspace with the sanctioned MetaRawDataVault model (raw vault only).");
    }

    private static void PrintFromMetaSchemaHelp()
    {
        Console.WriteLine("Command: from-metaschema");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-datavault from-metaschema --source-workspace <path> --new-workspace <path>");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Loads a MetaSchema workspace and materializes a deterministic MetaRawDataVault workspace (hubs + links + satellites).");
        Console.WriteLine("  RawLink rows are created only from explicit MetaSchema table relationships when both ends are present.");
    }
}
