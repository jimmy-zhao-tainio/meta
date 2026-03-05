using MetaSchema.Core;
using MetaSchema.Extractors.SqlServer;
using Meta.Core.Services;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        if (string.Equals(args[0], "extract", StringComparison.OrdinalIgnoreCase))
        {
            return await RunExtractAsync(args).ConfigureAwait(false);
        }

        Console.WriteLine($"Error: unknown command '{args[0]}'.");
        Console.WriteLine("Next: meta-schema help");
        return 1;
    }

    private static async Task<int> RunExtractAsync(string[] args)
    {
        if (args.Length == 1 || IsHelpToken(args[1]))
        {
            PrintExtractHelp();
            return 0;
        }

        if (!string.Equals(args[1], "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Error: unknown extractor '{args[1]}'.");
            Console.WriteLine("Next: meta-schema extract --help");
            return 1;
        }

        if (args.Length >= 3 && IsHelpToken(args[2]))
        {
            PrintExtractSqlServerHelp();
            return 0;
        }

        var parseResult = ParseSqlServerExtractOptions(args, startIndex: 2);
        if (!parseResult.Ok)
        {
            Console.WriteLine($"Error: {parseResult.ErrorMessage}");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(parseResult.Request.NewWorkspacePath))
        {
            Console.WriteLine("Error: missing required option --new-workspace <path>.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        var workspacePath = Path.GetFullPath(parseResult.Request.NewWorkspacePath);
        if (string.IsNullOrWhiteSpace(parseResult.Request.ConnectionString))
        {
            Console.WriteLine("Error: missing required option --connection <connectionString>.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(parseResult.Request.SystemName))
        {
            Console.WriteLine("Error: missing required option --system <name>.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(parseResult.Request.SchemaName) && parseResult.Request.AllSchemas)
        {
            Console.WriteLine("Error: --schema and --all-schemas cannot be used together.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(parseResult.Request.SchemaName) && !parseResult.Request.AllSchemas)
        {
            Console.WriteLine("Error: missing required scope option --schema <name> or --all-schemas.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(parseResult.Request.TableName) && parseResult.Request.AllTables)
        {
            Console.WriteLine("Error: --table and --all-tables cannot be used together.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(parseResult.Request.TableName) && !parseResult.Request.AllTables)
        {
            Console.WriteLine("Error: missing required scope option --table <name> or --all-tables.");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 1;
        }

        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
        {
            Console.WriteLine($"Error: target directory '{workspacePath}' must be empty.");
            Console.WriteLine("Next: choose a new folder or empty the target directory and retry.");
            return 4;
        }

        Directory.CreateDirectory(workspacePath);

        var extractor = new SqlServerSchemaExtractor();
        var workspace = default(Meta.Core.Domain.Workspace);
        try
        {
            workspace = extractor.ExtractMetaSchemaWorkspace(parseResult.Request);
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine($"Error: {exception.Message}");
            Console.WriteLine("Next: meta-schema extract sqlserver --help");
            return 4;
        }

        var validation = new ValidationService().Validate(workspace);
        if (validation.HasErrors)
        {
            Console.WriteLine("Error: extracted schema workspace is invalid.");
            foreach (var issue in validation.Issues.Where(item => item.Severity == Meta.Core.Domain.IssueSeverity.Error))
            {
                Console.WriteLine($"  - {issue.Code}: {issue.Message}");
            }
            Console.WriteLine("Next: fix extractor mapping and retry extract.");
            return 4;
        }

        await new WorkspaceService().SaveAsync(workspace).ConfigureAwait(false);

        Console.WriteLine("OK: metaschema workspace created");
        Console.WriteLine($"Path: {workspacePath}");
        Console.WriteLine($"Model: {workspace.Model.Name}");
        Console.WriteLine($"Systems: {workspace.Instance.GetOrCreateEntityRecords("System").Count}");
        Console.WriteLine($"Schemas: {workspace.Instance.GetOrCreateEntityRecords("Schema").Count}");
        Console.WriteLine($"Tables: {workspace.Instance.GetOrCreateEntityRecords("Table").Count}");
        Console.WriteLine($"Fields: {workspace.Instance.GetOrCreateEntityRecords("Field").Count}");
        Console.WriteLine($"TableRelationships: {workspace.Instance.GetOrCreateEntityRecords("TableRelationship").Count}");
        Console.WriteLine($"TableRelationshipFields: {workspace.Instance.GetOrCreateEntityRecords("TableRelationshipField").Count}");
        Console.WriteLine($"TypeIds: {workspace.Instance.GetOrCreateEntityRecords("Field").Select(record => record.Values.TryGetValue("TypeId", out var typeId) ? typeId : string.Empty).Where(typeId => !string.IsNullOrWhiteSpace(typeId)).Distinct(StringComparer.Ordinal).Count()}");
        return 0;
    }

    private static (bool Ok, SqlServerExtractRequest Request, string ErrorMessage) ParseSqlServerExtractOptions(
        string[] args,
        int startIndex)
    {
        var request = new SqlServerExtractRequest();
        for (var i = startIndex; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--new-workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, request, "missing value for --new-workspace.");
                }

                request.NewWorkspacePath = args[++i];
                continue;
            }

            if (string.Equals(arg, "--connection", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, request, "missing value for --connection.");
                }

                request.ConnectionString = args[++i];
                continue;
            }

            if (string.Equals(arg, "--schema", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, request, "missing value for --schema.");
                }

                request.SchemaName = args[++i];
                continue;
            }

            if (string.Equals(arg, "--all-schemas", StringComparison.OrdinalIgnoreCase))
            {
                request.AllSchemas = true;
                continue;
            }

            if (string.Equals(arg, "--system", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, request, "missing value for --system.");
                }

                request.SystemName = args[++i];
                continue;
            }

            if (string.Equals(arg, "--table", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    return (false, request, "missing value for --table.");
                }

                request.TableName = args[++i];
                continue;
            }

            if (string.Equals(arg, "--all-tables", StringComparison.OrdinalIgnoreCase))
            {
                request.AllTables = true;
                continue;
            }

            return (false, request, $"unknown option '{arg}'.");
        }

        return (true, request, string.Empty);
    }

    private static (bool Ok, string NewWorkspacePath, string ErrorMessage) ParseNewWorkspaceOnly(
        string[] args,
        int startIndex)
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
        Console.WriteLine("MetaSchema CLI");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-schema <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  help        Show this help.");
        Console.WriteLine("  extract     Materialize sanctioned MetaSchema workspaces from external sources.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-schema extract --help");
    }

    private static void PrintExtractHelp()
    {
        Console.WriteLine("Command: extract");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-schema extract <extractor> [options]");
        Console.WriteLine();
        Console.WriteLine("Extractors:");
        Console.WriteLine("  sqlserver   Extract SQL Server schema into MetaSchema workspace.");
        Console.WriteLine();
        Console.WriteLine("Next: meta-schema extract sqlserver --help");
    }

    private static void PrintExtractSqlServerHelp()
    {
        Console.WriteLine("Command: extract sqlserver");
        Console.WriteLine("Usage:");
        Console.WriteLine("  meta-schema extract sqlserver --new-workspace <path> --connection <connectionString> --system <name> (--schema <name> | --all-schemas) (--table <name> | --all-tables)");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Creates a new workspace with the MetaSchema model and validates it.");
        Console.WriteLine("  Scope is controlled by schema/table filters or all-schemas/all-tables discovery switches.");
        Console.WriteLine("  TableRelationship rows are emitted only for enforced and trusted SQL Server foreign keys.");
        Console.WriteLine("  Field rows carry a scalar TypeId such as sqlserver:type:nvarchar.");
    }

}
