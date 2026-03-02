using System;
using System.Collections.Generic;
using System.Linq;

internal static class HelpTopics
{
    private static readonly string[] DomainOrder =
    {
        "Workspace",
        "Model",
        "Instance",
        "Pipeline",
    };

    private static readonly Dictionary<string, RegisteredCommand> RegisteredCommands = new(StringComparer.OrdinalIgnoreCase);
    private static int registrationOrder;

    public static void RegisterCommand(string commandName, string domain, string description)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return;
        }

        var normalizedName = commandName.Trim();
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : description.Trim();
        if (RegisteredCommands.TryGetValue(normalizedName, out var existing))
        {
            RegisteredCommands[normalizedName] = existing with
            {
                Domain = normalizedDomain,
                Description = normalizedDescription,
            };
            return;
        }

        RegisteredCommands[normalizedName] = new RegisteredCommand(normalizedDomain, normalizedDescription, registrationOrder++);
    }

    public static IReadOnlyList<(string Domain, IReadOnlyList<(string Command, string Description)> Commands)> GetCommandCatalogByDomain()
    {
        var domainBuckets = RegisteredCommands
            .GroupBy(item => item.Value.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<(string Command, string Description)>)group
                    .OrderBy(item => item.Value.Order)
                    .Select(item => (item.Key, item.Value.Description))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var orderedDomains = DomainOrder
            .Concat(domainBuckets.Keys
                .Where(domain => !DomainOrder.Contains(domain, StringComparer.OrdinalIgnoreCase))
                .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(domain => domainBuckets.ContainsKey(domain))
            .ToArray();

        return orderedDomains
            .Select(domain => (domain, domainBuckets[domain]))
            .ToArray();
    }

    public static IReadOnlyList<string> GetCommandSuggestions()
    {
        var suggestions = new List<string> { "help" };
        suggestions.AddRange(RegisteredCommands.Keys
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        return suggestions;
    }

    public static bool TryResolveHelpTopicKey(IEnumerable<string> commandArgs, out string key)
    {
        key = string.Empty;
        if (commandArgs == null)
        {
            return false;
        }

        var normalizedTokens = commandArgs
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim().ToLowerInvariant())
            .ToArray();
        if (normalizedTokens.Length == 0)
        {
            return false;
        }

        for (var length = normalizedTokens.Length; length > 0; length--)
        {
            var candidate = string.Join(" ", normalizedTokens.Take(length));
            if (TryBuildHelpTopic(candidate, out _))
            {
                key = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryResolveUsageForArgs(IEnumerable<string> commandArgs, out string usage)
    {
        usage = string.Empty;
        if (commandArgs == null)
        {
            return false;
        }

        var normalizedTokens = commandArgs
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim())
            .ToArray();
        if (normalizedTokens.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedTokens[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            usage = "meta help [<command> ...]";
            return true;
        }

        if (!TryResolveHelpTopicKey(normalizedTokens, out var key) ||
            !TryBuildHelpTopic(key, out var document) ||
            string.IsNullOrWhiteSpace(document.Usage))
        {
            return false;
        }

        usage = document.Usage;
        return true;
    }

    private static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return "Other";
        }

        return domain.Trim();
    }

    private readonly record struct RegisteredCommand(
        string Domain,
        string Description,
        int Order);

    public static bool TryBuildHelpTopic(string key, out HelpDocument document)
    {
        document = default;
        switch (key)
        {
            case "help":
                return false;

            case "init":
                document = BuildTopicDocument(
                    title: "Command: init",
                    summary: "Initialize a metadata workspace.",
                    usage: "meta init [<path>]",
                    options: Array.Empty<(string, string)>(),
                    examples: new[]
                    {
                        "meta init .",
                        "meta init .\\Workspace",
                    },
                    next: "meta status");
                return true;

            case "status":
                document = BuildTopicDocument(
                    title: "Command: status",
                    summary: "Show workspace summary and model/instance sizes.",
                    usage: "meta status [--workspace <path>]",
                    options: new[]
                    {
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta status",
                        "meta status --workspace .\\Workspace",
                    },
                    next: "meta check");
                return true;

            case "instance diff":
                document = BuildTopicDocument(
                    title: "Command: instance diff",
                    summary: "Diff instance data for two workspaces with byte-identical model.xml.",
                    usage: "meta instance diff <leftWorkspace> <rightWorkspace>",
                    options: Array.Empty<(string, string)>(),
                    examples: new[]
                    {
                        "meta instance diff .\\LeftWorkspace .\\RightWorkspace",
                    },
                    next: "meta instance merge help");
                return true;

            case "instance merge":
                document = BuildTopicDocument(
                    title: "Command: instance merge",
                    summary: "Apply an equal-model instance diff artifact to a target workspace.",
                    usage: "meta instance merge <targetWorkspace> <diffWorkspace>",
                    options: Array.Empty<(string, string)>(),
                    examples: new[]
                    {
                        "meta instance merge .\\TargetWorkspace .\\RightWorkspace.instance-diff",
                    },
                    next: "meta instance diff help");
                return true;

            case "instance diff-aligned":
                document = BuildTopicDocument(
                    title: "Command: instance diff-aligned",
                    summary: "Diff mapped instance data using an explicit alignment workspace.",
                    usage: "meta instance diff-aligned <leftWorkspace> <rightWorkspace> <alignmentWorkspace>",
                    options: Array.Empty<(string, string)>(),
                    examples: new[]
                    {
                        "meta instance diff-aligned .\\LeftWorkspace .\\RightWorkspace .\\AlignmentWorkspace",
                    },
                    next: "meta instance merge-aligned help");
                return true;

            case "instance merge-aligned":
                document = BuildTopicDocument(
                    title: "Command: instance merge-aligned",
                    summary: "Apply an aligned instance diff artifact to a target workspace.",
                    usage: "meta instance merge-aligned <targetWorkspace> <diffWorkspace>",
                    options: Array.Empty<(string, string)>(),
                    examples: new[]
                    {
                        "meta instance merge-aligned .\\TargetWorkspace .\\RightWorkspace.instance-diff-aligned",
                    },
                    next: "meta instance diff-aligned help");
                return true;

            case "list":
                document = BuildTopicDocument(
                    title: "Command: list",
                    summary: "List entities, properties, or relationships.",
                    usage: "meta list <entities|properties|relationships> ...",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[]
                    {
                        "meta list entities",
                        "meta list properties Cube",
                        "meta list relationships Measure",
                    },
                    next: "meta list entities --help");
                return true;

            case "list entities":
                document = BuildTopicDocument(
                    title: "Command: list entities",
                    summary: "List entities with instance/property/relationship counts.",
                    usage: "meta list entities [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta list entities" },
                    next: "meta view entity <Entity>");
                return true;

            case "list properties":
                document = BuildTopicDocument(
                    title: "Command: list properties",
                    summary: "List properties for one entity.",
                    usage: "meta list properties <Entity> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta list properties Cube" },
                    next: "meta view entity <Entity>");
                return true;

            case "list relationships":
                document = BuildTopicDocument(
                    title: "Command: list relationships",
                    summary: "List declared outgoing relationships for one entity.",
                    usage: "meta list relationships <Entity> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta list relationships Measure" },
                    next: "meta instance relationship list <Entity> <Id>");
                return true;

            case "view":
                document = BuildTopicDocument(
                    title: "Command: view",
                    summary: "View entity schema or one instance.",
                    usage: "meta view <entity|instance> ...",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[]
                    {
                        "meta view entity Cube",
                        "meta view instance Cube 1",
                    },
                    next: "meta view entity --help");
                return true;

            case "view entity":
                document = BuildTopicDocument(
                    title: "Command: view entity",
                    summary: "Show one entity schema card.",
                    usage: "meta view entity <Entity> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta view entity Cube" },
                    next: "meta list properties <Entity>");
                return true;

            case "view instance":
                document = BuildTopicDocument(
                    title: "Command: view instance",
                    summary: "Show one instance as field/value table.",
                    usage: "meta view instance <Entity> <Id> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta view instance Cube 1" },
                    next: "meta query <Entity> --contains <Field> <Value>");
                return true;

            case "query":
                document = BuildTopicDocument(
                    title: "Command: query",
                    summary: "Search instances using equals/contains filters.",
                    usage: "meta query <Entity> [--equals <Field> <Value>]... [--contains <Field> <Value>]... [--top <n>] [--workspace <path>]",
                    options: new[]
                    {
                        ("--equals <Field> <Value>", "Exact field value match (repeatable)."),
                        ("--contains <Field> <Value>", "Contains match (repeatable)."),
                        ("--top <n>", "Limit preview instances (default 200)."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta query Cube --contains CubeName Sales",
                        "meta query Cube --equals Id 1 --top 20",
                    },
                    next: "meta view instance <Entity> <Id>");
                return true;

            case "graph":
                document = BuildTopicDocument(
                    title: "Command: graph",
                    summary: "Inspect relationship graph structure.",
                    usage: "meta graph <stats|inbound> ...",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[]
                    {
                        "meta graph stats --top 10 --cycles 10",
                        "meta graph inbound Cube --top 20",
                    },
                    next: "meta graph stats --help");
                return true;

            case "graph stats":
                document = BuildTopicDocument(
                    title: "Command: graph stats",
                    summary: "Show graph metrics, top degrees, and cycle samples.",
                    usage: "meta graph stats [--workspace <path>] [--top <n>] [--cycles <n>]",
                    options: new[]
                    {
                        ("--top <n>", "Top N entities for degree tables."),
                        ("--cycles <n>", "Max cycle samples."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta graph stats --top 10 --cycles 5" },
                    next: "meta graph inbound <Entity>");
                return true;

            case "graph inbound":
                document = BuildTopicDocument(
                    title: "Command: graph inbound",
                    summary: "Show entities that point to the target entity.",
                    usage: "meta graph inbound <Entity> [--workspace <path>] [--top <n>]",
                    options: new[]
                    {
                        ("--top <n>", "Limit output rows."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta graph inbound Cube --top 20" },
                    next: "meta model drop-entity --help");
                return true;

            case "check":
                document = BuildTopicDocument(
                    title: "Command: check",
                    summary: "Run model + instance integrity checks.",
                    usage: "meta check [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta check" },
                    next: "meta generate sql --help");
                return true;

            case "model":
                document = BuildTopicDocument(
                    title: string.Empty,
                    summary: "Inspect and edit model entities, properties, and relationships.",
                    usage: "meta model <subcommand> [arguments] [options]",
                    options: new[] { ("--workspace <path>", "Workspace root override.") },
                    examples: new[]
                    {
                        "meta model suggest",
                        "meta model add-entity SalesCube",
                        "meta model rename-entity OldName NewName",
                        "meta model add-property Cube Purpose --required true --default-value Unknown",
                    },
                    next: "meta model <subcommand> help",
                    subcommands: new[]
                    {
                        ("add-entity", "Create an entity."),
                        ("rename-entity", "Atomically rename an entity and follow implied relationship field names."),
                        ("drop-entity", "Remove an entity (must be empty)."),
                        ("add-property", "Add a property to an entity."),
                        ("rename-property", "Rename a property."),
                        ("drop-property", "Remove a property."),
                        ("add-relationship", "Add a relationship."),
                        ("refactor", "Atomic model+instance refactors."),
                        ("drop-relationship", "Remove a relationship."),
                        ("suggest", "Read-only key/reference inference from model + instance data."),
                    });
                return true;

            case "model add-entity":
                document = BuildTopicDocument(
                    title: "Command: model add-entity",
                    summary: "Add a new entity definition.",
                    usage: "meta model add-entity <Name> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta model add-entity SourceSystem" },
                    next: "meta model rename-entity --help");
                return true;

            case "model rename-entity":
                document = BuildTopicDocument(
                    title: "Command: model rename-entity",
                    summary: "Atomically rename an entity, update relationship targets, and rename implied non-role relationship fields.",
                    usage: "meta model rename-entity <Old> <New> [--workspace <path>]",
                    options: new[]
                    {
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta model rename-entity Warehouse StorageLocation",
                    },
                    next: "meta model add-property --help");
                return true;

            case "model add-property":
                document = BuildTopicDocument(
                    title: "Command: model add-property",
                    summary: "Add an entity property.",
                    usage: "meta model add-property <Entity> <Property> [--required true|false] [--default-value <Value>] [--workspace <path>]",
                    options: new[]
                    {
                        ("--required true|false", "Set required/nullable state."),
                        ("--default-value <Value>", "Backfill existing rows (required for required properties when entity already has rows)."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta model add-property Cube Purpose --required true --default-value Unknown" },
                    next: "meta model rename-property --help");
                return true;

            case "model rename-property":
                document = BuildTopicDocument(
                    title: "Command: model rename-property",
                    summary: "Rename a property in one entity.",
                    usage: "meta model rename-property <Entity> <Old> <New> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta model rename-property Cube Purpose Description" },
                    next: "meta model drop-property --help");
                return true;

            case "model add-relationship":
                document = BuildTopicDocument(
                    title: "Command: model add-relationship",
                    summary: "Add a required relationship; use --default-id to backfill existing source instances.",
                    usage: "meta model add-relationship <FromEntity> <ToEntity> [--role <RoleName>] [--default-id <ToId>] [--workspace <path>]",
                    options: new[]
                    {
                        ("--role <RoleName>", "Optional relationship role (column becomes <RoleName>Id)."),
                        ("--default-id <ToId>", "Required when source entity already has rows; must exist in target entity."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta model add-relationship Measure Cube --default-id 1" },
                    next: "meta model drop-relationship --help");
                return true;

            case "model refactor":
                document = BuildTopicDocument(
                    title: "Command: model refactor",
                    summary: "Run atomic model+instance refactors.",
                    usage: "meta model refactor <subcommand> [arguments] [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root."), },
                    examples: new[]
                    {
                        "meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id",
                    },
                    next: "meta model refactor property-to-relationship --help",
                    subcommands: new[]
                    {
                        ("property-to-relationship", "Promote scalar property to required relationship using lookup key."),
                        ("relationship-to-property", "Demote required relationship to scalar Id property."),
                    });
                return true;

            case "model refactor property-to-relationship":
                document = BuildTopicDocument(
                    title: "Command: model refactor property-to-relationship",
                    summary: "Atomically convert a scalar source property to a required relationship using a target lookup key. Source property is dropped by default; use --preserve-property only when the source property name will not collide with the implied relationship usage name.",
                    usage: "meta model refactor property-to-relationship --source <Entity.Property> --target <Entity> --lookup <Property> [--role <Role>] [--preserve-property] [--workspace <path>]",
                    options: new[]
                    {
                        ("--source <Entity.Property>", "Required source scalar property to rewrite."),
                        ("--target <Entity>", "Required target entity."),
                        ("--lookup <Property>", "Required lookup key property on target entity."),
                        ("--role <Role>", "Optional relationship role (usage column becomes <Role>Id)."),
                        ("--preserve-property", "Keep the source scalar property. This is only valid when the source property name does not collide with the implied relationship usage name."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id",
                        "meta model refactor property-to-relationship --source Order.ProductId --target Product --lookup Id --role ProductRef --preserve-property",
                    },
                    next: "meta model suggest --print-commands");
                return true;

            case "model refactor relationship-to-property":
                document = BuildTopicDocument(
                    title: "Command: model refactor relationship-to-property",
                    summary: "Atomically convert a required relationship back to a required scalar Id property.",
                    usage: "meta model refactor relationship-to-property --source <Entity> --target <Entity> [--role <Role>] [--property <PropertyName>] [--workspace <path>]",
                    options: new[]
                    {
                        ("--source <Entity>", "Required source entity that owns the relationship."),
                        ("--target <Entity>", "Required target entity referenced by the relationship."),
                        ("--role <Role>", "Optional relationship role when multiple edges target the same entity."),
                        ("--property <PropertyName>", "Optional replacement scalar property name (default: <RoleOrTarget>Id)."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta model refactor relationship-to-property --source Order --target Warehouse",
                        "meta model refactor relationship-to-property --source Order --target Product --role ProductRef --property ProductId",
                    },
                    next: "meta model refactor relationship-to-property help");
                return true;

            case "model drop-property":
                document = BuildTopicDocument(
                    title: "Command: model drop-property",
                    summary: "Drop a property from an entity.",
                    usage: "meta model drop-property <Entity> <Property> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta model drop-property Cube Description" },
                    next: "meta model drop-entity --help");
                return true;

            case "model drop-relationship":
                document = BuildTopicDocument(
                    title: "Command: model drop-relationship",
                    summary: "Drop a declared relationship (blocked if in use).",
                    usage: "meta model drop-relationship <FromEntity> <ToEntity> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta model drop-relationship Measure Cube" },
                    next: "meta instance relationship set --help");
                return true;

            case "model drop-entity":
                document = BuildTopicDocument(
                    title: "Command: model drop-entity",
                    summary: "Drop an entity (blocked if instances or inbound relationships exist).",
                    usage: "meta model drop-entity <Entity> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta model drop-entity SourceSystem" },
                    next: "meta graph inbound --help");
                return true;

            case "model suggest":
                document = BuildTopicDocument(
                    title: "Command: model suggest",
                    summary: "Read-only relationship inference from model + instance data. Only fully resolvable many-to-one promotions are printed, using the sanctioned Id-based `<TargetEntity>Id -> <TargetEntity>.Id` inference path.",
                    usage: "meta model suggest [--show-keys] [--explain] [--print-commands] [--workspace <path>]",
                    options: new[]
                    {
                        ("--show-keys", "Also print candidate business keys."),
                        ("--explain", "Include Evidence/Stats/Why detail blocks."),
                        ("--print-commands", "Print copy/paste refactor commands for eligible relationship suggestions."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta model suggest --workspace .\\Workspace",
                        "meta model suggest --show-keys --explain --workspace .\\Workspace",
                        "meta model suggest --print-commands --workspace .\\Workspace",
                    },
                    next: "meta model suggest --help");
                return true;

            case "insert":
                document = BuildTopicDocument(
                    title: "Command: insert",
                    summary: "Insert one instance by explicit Id or auto-generated numeric Id. Use --auto-id only when creating a brand-new row with no external identity.",
                    usage: "meta insert <Entity> [<Id>|--auto-id] --set Field=Value [--set Field=Value ...] [--workspace <path>]",
                    options: new[]
                    {
                        ("--auto-id", "Generate next numeric Id from existing instances for a new row when no external Id exists."),
                        ("--set Field=Value", "Set property/relationship values."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta insert Cube 10 --set \"CubeName=Ops Cube\"",
                        "meta insert Cube --auto-id --set \"CubeName=Ops Cube\"",
                    },
                    next: "meta instance update --help");
                return true;

            case "bulk-insert":
                document = BuildTopicDocument(
                    title: "Command: bulk-insert",
                    summary: "Bulk insert instances from tsv/csv input. Use --auto-id only for new rows whose source data does not carry an external Id.",
                    usage: "meta bulk-insert <Entity> [--from tsv|csv] [--file <path>|--stdin] [--key Field[,Field2...]] [--auto-id] [--workspace <path>]",
                    options: new[]
                    {
                        ("--from tsv|csv", "Input format."),
                        ("--file <path>", "Input file."),
                        ("--stdin", "Read input from stdin."),
                        ("--key Field[,Field2...]", "Match key fields."),
                        ("--auto-id", "Generate numeric Id only for input rows that omit Id and are being created."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta bulk-insert Cube --from tsv --file .\\cube.tsv --key Id",
                        "meta bulk-insert Cube --from tsv --file .\\cube-no-id.tsv --auto-id",
                    },
                    next: "meta query --help");
                return true;

            case "instance":
                document = BuildTopicDocument(
                    title: "Command: instance",
                    summary: "Diff/merge instances and apply instance updates.",
                    usage: "meta instance <diff|merge|diff-aligned|merge-aligned|update|rename-id|relationship> ...",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[]
                    {
                        "meta instance update Cube 1 --set RefreshMode=Manual",
                        "meta instance rename-id Cube 1 Cube-001",
                        "meta instance relationship set Measure 1 --to Cube 2",
                        "meta instance diff .\\LeftWorkspace .\\RightWorkspace",
                    },
                    next: "meta instance update --help");
                return true;

            case "instance update":
                document = BuildTopicDocument(
                    title: "Command: instance update",
                    summary: "Update fields on one instance by Id. Use instance rename-id to change the row Id itself.",
                    usage: "meta instance update <Entity> <Id> --set Field=Value [--set Field=Value ...] [--workspace <path>]",
                    options: new[]
                    {
                        ("--set Field=Value", "Field assignment (repeatable)."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta instance update Cube 1 --set RefreshMode=Manual" },
                    next: "meta instance rename-id --help");
                return true;

            case "instance rename-id":
                document = BuildTopicDocument(
                    title: "Command: instance rename-id",
                    summary: "Atomically rename one instance Id and follow inbound relationships that point to it.",
                    usage: "meta instance rename-id <Entity> <OldId> <NewId> [--workspace <path>]",
                    options: new[]
                    {
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta instance rename-id Cube 1 Cube-001",
                    },
                    next: "meta instance relationship set --help");
                return true;

            case "instance relationship":
                document = BuildTopicDocument(
                    title: "Command: instance relationship",
                    summary: "Set or list relationship usage for one instance.",
                    usage: "meta instance relationship <set|list> ...",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[]
                    {
                        "meta instance relationship set Measure 1 --to Cube 2",
                        "meta instance relationship list Measure 1",
                    },
                    next: "meta instance relationship set --help");
                return true;

            case "instance relationship set":
                document = BuildTopicDocument(
                    title: "Command: instance relationship set",
                    summary: "Set one relationship usage on a row. The selector after --to may be the target entity, relationship role, or implied relationship field name.",
                    usage: "meta instance relationship set <FromEntity> <FromId> --to <RelationshipSelector> <ToId> [--workspace <path>]",
                    options: new[]
                    {
                        ("--to <RelationshipSelector> <ToId>", "Relationship selector plus target row Id. Selector may be target entity, role, or implied field name."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[]
                    {
                        "meta instance relationship set Measure 1 --to Cube 2",
                        "meta instance relationship set Order ORD-001 --to ProductRef PRD-001",
                        "meta instance relationship set Order ORD-001 --to ProductRefId PRD-001",
                    },
                    next: "meta instance relationship list --help");
                return true;

            case "instance relationship list":
                document = BuildTopicDocument(
                    title: "Command: instance relationship list",
                    summary: "List relationship usage for one instance.",
                    usage: "meta instance relationship list <FromEntity> <FromId> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta instance relationship list Measure 1" },
                    next: "meta instance relationship set --help");
                return true;

            case "delete":
                document = BuildTopicDocument(
                    title: "Command: delete",
                    summary: "Delete one instance by Id.",
                    usage: "meta delete <Entity> <Id> [--workspace <path>]",
                    options: new[] { ("--workspace <path>", "Override workspace root.") },
                    examples: new[] { "meta delete Cube 10" },
                    next: "meta view instance <Entity> <Id>");
                return true;

            case "generate":
                document = BuildTopicDocument(
                    title: "Command: generate",
                    summary: "Generate artifacts from the workspace.",
                    usage: "meta generate <sql|csharp|ssdt> --out <dir> [--workspace <path>]",
                    options: new[]
                    {
                        ("--out <dir>", "Output directory."),
                        ("--workspace <path>", "Override workspace root."),
                        ("--tooling", "C# only. Emit optional tooling helpers in <Model>.Tooling.cs."),
                    },
                    examples: new[]
                    {
                        "meta generate sql --out .\\out\\sql",
                        "meta generate csharp --out .\\out\\csharp",
                        "meta generate csharp --out .\\out\\csharp --tooling",
                        "meta generate ssdt --out .\\out\\ssdt",
                    },
                    next: "meta generate sql --help");
                return true;

            case "generate sql":
                document = BuildTopicDocument(
                    title: "Command: generate sql",
                    summary: "Generate SQL schema + data scripts.",
                    usage: "meta generate sql --out <dir> [--workspace <path>]",
                    options: new[]
                    {
                        ("--out <dir>", "Output directory."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta generate sql --out .\\out\\sql" },
                    next: "meta generate csharp --help");
                return true;

            case "generate csharp":
                document = BuildTopicDocument(
                    title: "Command: generate csharp",
                    summary: "Generate C# model and entity classes.",
                    usage: "meta generate csharp --out <dir> [--workspace <path>] [--tooling]",
                    options: new[]
                    {
                        ("--out <dir>", "Output directory."),
                        ("--workspace <path>", "Override workspace root."),
                        ("--tooling", "Emit optional tooling helpers in <Model>.Tooling.cs."),
                    },
                    examples: new[]
                    {
                        "meta generate csharp --out .\\out\\csharp",
                        "meta generate csharp --out .\\out\\csharp --tooling",
                    },
                    next: "meta generate ssdt --help");
                return true;

            case "generate ssdt":
                document = BuildTopicDocument(
                    title: "Command: generate ssdt",
                    summary: "Generate Schema.sql, Data.sql, PostDeploy.sql, and Metadata.sqlproj.",
                    usage: "meta generate ssdt --out <dir> [--workspace <path>]",
                    options: new[]
                    {
                        ("--out <dir>", "Output directory."),
                        ("--workspace <path>", "Override workspace root."),
                    },
                    examples: new[] { "meta generate ssdt --out .\\out\\ssdt" },
                    next: "meta check");
                return true;

            case "import":
                document = BuildTopicDocument(
                    title: "Command: import",
                    summary: "Import from SQL into a new workspace, or import CSV into new/existing workspace.",
                    usage: "meta import <sql|csv> ...",
                    options: new[]
                    {
                        ("--new-workspace <path>", "Required for sql; optional for csv (mutually exclusive with --workspace)."),
                        ("--workspace <path>", "Use existing workspace for csv import."),
                    },
                    examples: new[]
                    {
                        "meta import sql \"Server=...;Database=...;...\" dbo --new-workspace .\\ImportedWorkspace",
                        "meta import csv .\\landing.csv --entity Landing --new-workspace .\\ImportedWorkspace",
                    },
                    next: "meta import sql --help");
                return true;

            case "import sql":
                document = BuildTopicDocument(
                    title: "Command: import sql",
                    summary: "Import metadata from SQL into a new workspace.",
                    usage: "meta import sql <connectionString> <schema> --new-workspace <path>",
                    options: new[] { ("--new-workspace <path>", "Required. Target directory must be empty.") },
                    examples: new[] { "meta import sql \"Server=...;Database=...;...\" dbo --new-workspace .\\ImportedWorkspace" },
                    next: "meta status --workspace <path>");
                return true;

            case "import csv":
                document = BuildTopicDocument(
                    title: "Command: import csv",
                    summary: "Import one CSV file as one entity + rows. The CSV must include a column named Id (case-insensitive match); existing-entity import is deterministic upsert by Id.",
                    usage: "meta import csv <csvFile> --entity <EntityName> [--workspace <path> | --new-workspace <path>]",
                    options: new[]
                    {
                        ("--entity <EntityName>", "Required. Entity name to create (sanitized deterministically)."),
                        ("--workspace <path>", "Optional. Target existing workspace (defaults to current workspace)."),
                        ("--new-workspace <path>", "Target new workspace directory (must be empty)."),
                    },
                    examples: new[]
                    {
                        "meta import csv .\\landing.csv --entity Landing --new-workspace .\\ImportedWorkspace",
                    },
                    next: "meta check --workspace <path>");
                return true;

            default:
                return false;
        }
    }
    static HelpDocument BuildTopicDocument(
        string title,
        string summary,
        string usage,
        IReadOnlyList<(string Option, string Description)> options,
        IReadOnlyList<string> examples,
        string next,
        IReadOnlyList<(string Command, string Description)>? subcommands = null)
    {
        _ = title;
        var sections = subcommands is { Count: > 0 }
            ? new[] { new HelpSection("Subcommands:", subcommands) }
            : Array.Empty<HelpSection>();
        return new HelpDocument(
            Header: new HelpHeader("Meta CLI", null, summary),
            Usage: usage,
            OptionsTitle: "Options:",
            Options: options,
            Sections: sections,
            Examples: examples,
            Next: next);
    }
}



