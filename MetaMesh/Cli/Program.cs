using System.Reflection;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using MetaCli.Core;
using MetaMesh.Core;

internal static class Program
{
    private static readonly ConsolePresenter Presenter = new();
    private static readonly MetaMeshWorkspaceService Service = new();
    private static readonly CliAppDefinition Cli = new(
        "meta-mesh",
        new[]
        {
            "meta-mesh <command> [options]"
        },
        new[]
        {
            new CliCommandDefinition(
                "init",
                "Create an empty MetaMesh workspace.",
                new[] { "meta-mesh init --new-workspace <path> [--name <name>]" },
                new[]
                {
                    new CliOptionDefinition("--new-workspace <path>", "Required. Empty target directory for the MetaMesh workspace."),
                    new CliOptionDefinition("--name <name>", "Optional mesh display name.")
                }),
            new CliCommandDefinition(
                "scan",
                "Scan a root folder and create a MetaMesh workspace from discovered meta workspaces.",
                new[] { "meta-mesh scan <root> --new-workspace <path> [--name <name>]" },
                new[]
                {
                    new CliOptionDefinition("<root>", "Required. Folder to scan for meta workspaces."),
                    new CliOptionDefinition("--new-workspace <path>", "Required. Empty target directory for the generated MetaMesh workspace."),
                    new CliOptionDefinition("--name <name>", "Optional mesh display name.")
                }),
            new CliCommandDefinition(
                "suggest",
                "Suggest logical workspace handles and mesh improvements without applying changes.",
                new[] { "meta-mesh suggest <root-or-mesh>" },
                new[]
                {
                    new CliOptionDefinition("<root-or-mesh>", "Required. Root folder to scan or existing MetaMesh workspace.")
                }),
            new CliCommandDefinition(
                "show",
                "Show workspace handles, mounts, links, and suggestions in a MetaMesh workspace.",
                new[] { "meta-mesh show --mesh <mesh-workspace>" },
                new[]
                {
                    new CliOptionDefinition("--mesh <mesh-workspace>", "Required. MetaMesh workspace to inspect.")
                }),
            new CliCommandDefinition(
                "check",
                "Validate a MetaMesh workspace map.",
                new[] { "meta-mesh check --mesh <mesh-workspace>" },
                new[]
                {
                    new CliOptionDefinition("--mesh <mesh-workspace>", "Required. MetaMesh workspace to validate.")
                }),
            new CliCommandDefinition(
                "impact",
                "Walk modeled workspace links from one handle.",
                new[] { "meta-mesh impact --mesh <mesh-workspace> --workspace <handle>" },
                new[]
                {
                    new CliOptionDefinition("--mesh <mesh-workspace>", "Required. MetaMesh workspace to inspect."),
                    new CliOptionDefinition("--workspace <handle>", "Required. Logical workspace handle to start from.")
                }),
            new CliCommandDefinition(
                "mount",
                "Mount or update a workspace path under a stable handle.",
                new[] { "meta-mesh mount --mesh <mesh-workspace> --handle <handle> --path <path>" },
                new[]
                {
                    new CliOptionDefinition("--mesh <mesh-workspace>", "Required. MetaMesh workspace to update."),
                    new CliOptionDefinition("--handle <handle>", "Required. Logical workspace handle."),
                    new CliOptionDefinition("--path <path>", "Required. Physical workspace path to mount.")
                }),
            new CliCommandDefinition(
                "link",
                "Add or update a modeled link between two workspace handles.",
                new[] { "meta-mesh link --mesh <mesh-workspace> --from <handle> --to <handle> --kind <kind>" },
                new[]
                {
                    new CliOptionDefinition("--mesh <mesh-workspace>", "Required. MetaMesh workspace to update."),
                    new CliOptionDefinition("--from <handle>", "Required. Source workspace handle."),
                    new CliOptionDefinition("--to <handle>", "Required. Target workspace handle."),
                    new CliOptionDefinition("--kind <kind>", "Required. Link kind such as depends-on or derives.")
                }),
            new CliCommandDefinition(
                "describe",
                "Describe this CLI in the MetaCli contract shape.",
                new[] { "meta-mesh describe" },
                Notes: new[] { "This is a machine-readable-contract-shaped console projection; it is not the source of MetaMesh truth." }),
            new CliCommandDefinition(
                "help",
                "Show this help.",
                new[] { "meta-mesh help" })
        },
        Next: "meta-mesh scan <root> --new-workspace <mesh-workspace>");

    internal static CliAppDefinition CreateAppDefinition() => Cli;

    private static async Task<int> Main(string[] args)
    {
        if (CliVersion.TryWriteVersion(Presenter, Cli.Name, args, out var versionExitCode))
        {
            return versionExitCode;
        }

        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "init" => RunWithHelp(args, "init", RunInit),
                "scan" => RunWithHelp(args, "scan", RunScan),
                "suggest" => RunWithHelp(args, "suggest", RunSuggest),
                "show" => RunWithHelp(args, "show", RunShow),
                "check" => RunWithHelp(args, "check", RunCheck),
                "impact" => RunWithHelp(args, "impact", RunImpact),
                "mount" => RunWithHelp(args, "mount", RunMount),
                "link" => RunWithHelp(args, "link", RunLink),
                "describe" => RunWithHelp(args, "describe", RunDescribe),
                "help" => ReturnHelp(),
                _ => Fail($"unknown command '{args[0]}'.", "meta-mesh help")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return Fail("Cannot run meta-mesh.", "check the paths and handles, then retry.", 4, new[] { $"  {ex.Message}" });
        }
        finally
        {
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    private static int RunInit(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: false, "--new-workspace", "--name");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("init"));
        }

        var workspacePath = RequireOption(parse, "--new-workspace", HelpCommand("init"));
        var name = GetOption(parse, "--name", "Mesh");
        var fullPath = Path.GetFullPath(workspacePath);
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            return Fail("target directory must be empty.", "choose a new folder or empty the target directory and retry.", 4, new[] { $"  Target: {fullPath}" });
        }

        Service.CreateEmpty(name).SaveToXmlWorkspace(fullPath);
        Presenter.WriteOk();
        return 0;
    }

    private static int RunScan(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: true, "--new-workspace", "--name");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("scan"));
        }

        var root = RequireSinglePositional(parse, "<root>", HelpCommand("scan"));
        var workspacePath = RequireOption(parse, "--new-workspace", HelpCommand("scan"));
        var name = GetOption(parse, "--name", "Mesh");
        var fullTarget = Path.GetFullPath(workspacePath);
        if (Directory.Exists(fullTarget) && Directory.EnumerateFileSystemEntries(fullTarget).Any())
        {
            return Fail("target directory must be empty.", "choose a new folder or empty the target directory and retry.", 4, new[] { $"  Target: {fullTarget}" });
        }

        var result = Service.ScanToWorkspace(root, fullTarget, name);
        Presenter.WriteOk();
        WriteWorkspaceTable(result.Workspaces);
        WriteSuggestions(result.Suggestions);
        return 0;
    }

    private static int RunSuggest(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: true);
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("suggest"));
        }

        var path = RequireSinglePositional(parse, "<root-or-mesh>", HelpCommand("suggest"));
        IReadOnlyList<MetaMeshSuggestionSummary> suggestions;
        if (LooksLikeMetaMeshWorkspace(path))
        {
            suggestions = Service.Show(path).Suggestions;
        }
        else
        {
            suggestions = Service.SuggestFromRoot(path).Suggestions;
        }

        WriteSuggestions(suggestions);
        return 0;
    }

    private static int RunShow(string[] args)
    {
        var meshPath = ParseMeshOnly(args, "show");
        var result = Service.Show(meshPath);
        Presenter.WriteKeyValueBlock("MetaMesh", new[]
        {
            ("Name", result.MeshName),
            ("Root", result.RootPath),
            ("Workspaces", result.Workspaces.Count.ToString()),
            ("Links", result.Links.Count.ToString()),
        });
        WriteWorkspaceTable(result.Workspaces);
        WriteLinkTable(result.Links);
        WriteSuggestions(result.Suggestions);
        return 0;
    }

    private static int RunCheck(string[] args)
    {
        var meshPath = ParseMeshOnly(args, "check");
        var result = Service.Check(meshPath);
        if (result.Issues.Count == 0)
        {
            Presenter.WriteOk();
            return 0;
        }

        Presenter.WriteTable(
            new[] { "Severity", "Code", "Handle", "Message" },
            result.Issues.Select(issue => (IReadOnlyList<string>)new[] { issue.Severity, issue.Code, issue.WorkspaceHandle, issue.Message }).ToArray());
        return result.HasErrors ? 2 : 0;
    }

    private static int RunImpact(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: false, "--mesh", "--workspace");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("impact"));
        }

        var meshPath = RequireOption(parse, "--mesh", HelpCommand("impact"));
        var handle = RequireOption(parse, "--workspace", HelpCommand("impact"));
        var result = Service.Impact(meshPath, handle);

        Presenter.WriteKeyValueBlock("Impact", new[]
        {
            ("Workspace", result.WorkspaceHandle),
            ("AffectedWorkspaces", result.AffectedHandles.Count.ToString()),
        });
        if (result.AffectedHandles.Count > 0)
        {
            Presenter.WriteInfo("Affected handles:");
            foreach (var affected in result.AffectedHandles)
            {
                Presenter.WriteInfo("  " + affected);
            }
        }

        WriteLinkTable(result.AffectedLinks);
        return 0;
    }

    private static int RunMount(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: false, "--mesh", "--handle", "--path");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("mount"));
        }

        var summary = Service.Mount(
            RequireOption(parse, "--mesh", HelpCommand("mount")),
            RequireOption(parse, "--handle", HelpCommand("mount")),
            RequireOption(parse, "--path", HelpCommand("mount")));
        Presenter.WriteOk();
        WriteWorkspaceTable(new[] { summary });
        return 0;
    }

    private static int RunLink(string[] args)
    {
        var parse = ParseOptions(args, 1, allowPositionals: false, "--mesh", "--from", "--to", "--kind");
        if (!parse.Ok)
        {
            return Fail(parse.ErrorMessage, HelpCommand("link"));
        }

        var summary = Service.Link(
            RequireOption(parse, "--mesh", HelpCommand("link")),
            RequireOption(parse, "--from", HelpCommand("link")),
            RequireOption(parse, "--to", HelpCommand("link")),
            RequireOption(parse, "--kind", HelpCommand("link")));
        Presenter.WriteOk();
        WriteLinkTable(new[] { summary });
        return 0;
    }

    private static int RunDescribe(string[] args)
    {
        var descriptor = MetaCliDescriptorFactory.FromCliAppDefinition(
            Cli,
            ResolveVersion(),
            new[] { "MetaMesh" },
            new[] { "describe", "show", "check", "impact" },
            ResolveEffects);

        Presenter.WriteKeyValueBlock("MetaCli", new[]
        {
            ("Name", descriptor.Name),
            ("Version", descriptor.Version),
            ("SupportedModels", string.Join(", ", descriptor.SupportedModels)),
            ("Operations", descriptor.Operations.Count.ToString()),
        });

        Presenter.WriteTable(
            new[] { "Operation", "Host", "Effects", "Inputs" },
            descriptor.Operations.Select(operation => (IReadOnlyList<string>)new[]
            {
                operation.Name,
                operation.CanHandleHostRequest ? "yes" : "no",
                string.Join(",", operation.Effects),
                string.Join(",", operation.Inputs.Select(input => input.Name))
            }).ToArray());
        return 0;
    }

    private static IReadOnlyList<string> ResolveEffects(CliCommandDefinition command) =>
        command.Name switch
        {
            "init" => new[] { "derives workspace" },
            "scan" => new[] { "derives workspace" },
            "mount" => new[] { "mutates workspace" },
            "link" => new[] { "mutates workspace" },
            "show" or "suggest" or "check" or "impact" or "describe" => new[] { "pure" },
            _ => new[] { "pure" }
        };

    private static string ParseMeshOnly(string[] args, string commandName)
    {
        var parse = ParseOptions(args, 1, allowPositionals: false, "--mesh");
        if (!parse.Ok)
        {
            throw new InvalidOperationException(parse.ErrorMessage);
        }

        return RequireOption(parse, "--mesh", HelpCommand(commandName));
    }

    private static void WriteWorkspaceTable(IReadOnlyList<MetaMeshWorkspaceSummary> workspaces)
    {
        Presenter.WriteTable(
            new[] { "Handle", "Model", "Kind", "Lifecycle", "Path" },
            workspaces.Select(workspace => (IReadOnlyList<string>)new[]
            {
                workspace.Handle,
                workspace.ModelName,
                workspace.WorkspaceKind,
                workspace.Lifecycle,
                workspace.PhysicalPath
            }).ToArray());
    }

    private static void WriteLinkTable(IReadOnlyList<MetaMeshLinkSummary> links)
    {
        if (links.Count == 0)
        {
            return;
        }

        Presenter.WriteTable(
            new[] { "From", "Kind", "To", "Description" },
            links.Select(link => (IReadOnlyList<string>)new[] { link.FromHandle, link.Kind, link.ToHandle, link.Description }).ToArray());
    }

    private static void WriteSuggestions(IReadOnlyList<MetaMeshSuggestionSummary> suggestions)
    {
        if (suggestions.Count == 0)
        {
            Presenter.WriteInfo("Suggestions: (none)");
            return;
        }

        Presenter.WriteTable(
            new[] { "Severity", "Kind", "Handle", "Message" },
            suggestions.Select(suggestion => (IReadOnlyList<string>)new[]
            {
                suggestion.Severity,
                suggestion.SuggestionKind,
                suggestion.WorkspaceHandle,
                suggestion.Message
            }).ToArray());
    }

    private static bool LooksLikeMetaMeshWorkspace(string path)
    {
        try
        {
            var modelPath = Path.Combine(Path.GetFullPath(path), "model.xml");
            if (!File.Exists(modelPath))
            {
                return false;
            }

            var text = File.ReadAllText(modelPath);
            return text.Contains("<Model name=\"MetaMesh\"", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static int RunWithHelp(string[] args, string commandName, Func<string[], int> execute)
    {
        if (args.Length >= 2 && IsHelpToken(args[1]))
        {
            PrintCommandHelp(commandName);
            return 0;
        }

        return execute(args);
    }

    private static int ReturnHelp()
    {
        PrintHelp();
        return 0;
    }

    private static ParseResult ParseOptions(string[] args, int startIndex, bool allowPositionals, params string[] allowedOptions)
    {
        var allowed = new HashSet<string>(allowedOptions, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var index = startIndex; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (!allowPositionals)
                {
                    return ParseResult.Fail($"unknown option '{arg}'.");
                }

                positionals.Add(arg);
                continue;
            }

            if (!allowed.Contains(arg))
            {
                return ParseResult.Fail($"unknown option '{arg}'.");
            }

            if (index + 1 >= args.Length)
            {
                return ParseResult.Fail($"missing value for {arg}.");
            }

            if (values.ContainsKey(arg))
            {
                return ParseResult.Fail($"{arg} can only be provided once.");
            }

            values[arg] = args[++index];
        }

        return new ParseResult(true, values, positionals, string.Empty);
    }

    private static string RequireOption(ParseResult parse, string optionName, string next)
    {
        if (!parse.Values.TryGetValue(optionName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"missing required option {optionName} <value>. Next: {next}");
        }

        return value;
    }

    private static string GetOption(ParseResult parse, string optionName, string fallback) =>
        parse.Values.TryGetValue(optionName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string RequireSinglePositional(ParseResult parse, string label, string next)
    {
        if (parse.Positionals.Count == 0)
        {
            throw new InvalidOperationException($"missing required argument {label}. Next: {next}");
        }

        if (parse.Positionals.Count > 1)
        {
            throw new InvalidOperationException($"expected one positional argument {label}, found {parse.Positionals.Count}. Next: {next}");
        }

        return parse.Positionals[0];
    }

    private static bool IsHelpToken(string value) =>
        string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

    private static void PrintHelp() => CliHelpRenderer.WriteAppHelp(Presenter, Cli);

    private static void PrintCommandHelp(string commandName) => CliHelpRenderer.WriteCommandHelp(Presenter, Cli, Cli.GetCommand(commandName));

    private static string HelpCommand(string commandName) => Cli.GetCommand(commandName).HelpCommand(Cli.Name);

    private static int Fail(string message, string next, int exitCode = 1, IEnumerable<string>? details = null)
    {
        var renderedDetails = new List<string>();
        if (details != null)
        {
            renderedDetails.AddRange(details.Where(static detail => !string.IsNullOrWhiteSpace(detail)));
        }

        renderedDetails.Add($"Next: {next}");
        Presenter.WriteFailure(message, renderedDetails);
        return exitCode;
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "unknown";
    }

    private sealed record ParseResult(
        bool Ok,
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyList<string> Positionals,
        string ErrorMessage)
    {
        public static ParseResult Fail(string message) =>
            new(false, new Dictionary<string, string>(), Array.Empty<string>(), message);
    }
}
