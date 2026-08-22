using System.Globalization;

namespace MetaDocs.Core;

public sealed class MetaDocsCliMatrixService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> KnownVerbs = new(
        [
            "add", "allow", "author", "bind", "browse", "check", "contents", "create", "delete", "deploy", "diff", "drop",
            "emit", "execute", "explain", "export", "extract", "from", "graph", "help", "import", "include",
            "infer", "insert", "inspect", "list", "merge", "process", "promote", "prune", "query", "refactor",
            "refresh", "remove", "rename", "render", "resolve", "restore", "run", "search", "set", "show",
            "status", "suggest", "to", "update", "validate", "view",
        ],
        Comparer);

    public MetaDocsCliMatrix Build(MetaDocsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subjects = model.DocumentationSubjectList
            .Where(IsCurrent)
            .ToArray();
        var factsBySubject = model.DocumentationFactList
            .Where(IsCurrent)
            .GroupBy(static fact => fact.DocumentationSubject.Id, Comparer)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), Comparer);
        var childrenByParent = subjects
            .Where(static subject => subject.ParentSubject is not null)
            .GroupBy(static subject => subject.ParentSubject!.Id, Comparer)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), Comparer);

        var applications = subjects
            .Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliApplication"))
            .OrderBy(static subject => subject.DisplayName, Comparer)
            .ToArray();
        var rows = new List<MetaDocsCliMatrixRow>();

        foreach (var application in applications)
        {
            foreach (var command in Children(application, "CliCommand"))
            {
                var options = Children(command, "CliOption")
                    .Select(subject => Parameter(subject, "option"))
                    .OrderBy(static parameter => parameter.Name, Comparer)
                    .ToArray();
                var arguments = Children(command, "CliArgument")
                    .Select(subject => Parameter(subject, "argument"))
                    .OrderBy(static parameter => parameter.Name, Comparer)
                    .ToArray();
                var groups = Children(command, "CliParameterGroup")
                    .Select(Group)
                    .OrderBy(static group => group.Name, Comparer)
                    .ToArray();

                EnsureDeclaredCount(command, "OptionCount", options.Length);
                EnsureDeclaredCount(command, "PositionalCount", arguments.Length);
                EnsureDeclaredCount(command, "ParameterGroupCount", groups.Length);

                var route = FirstNonEmpty(Fact(command, "Cli", "Name"), RelativeRoute(application, command));
                var commandPath = FirstNonEmpty(Fact(command, "Cli", "CommandPath"), command.DisplayPath, $"{application.DisplayName} {route}");
                var verb = Verb(route);
                var routePattern = RoutePattern(route, verb);
                var outputMode = OutputMode(options, groups);
                var workspaceInputs = options
                    .Where(static parameter => NormalizeParameterName(parameter.Name).Contains("workspace", StringComparison.OrdinalIgnoreCase))
                    .Select(static parameter => parameter.Name)
                    .Distinct(Comparer)
                    .OrderBy(static name => name, Comparer)
                    .ToArray();
                var operationIntent = OperationIntent(application.DisplayName, route, verb, options, outputMode);
                var subject = CommandSubject(application.DisplayName, route, verb, operationIntent, options, arguments);
                var inputPattern = InputPattern(options, arguments);
                var resultPattern = ResultPattern(operationIntent, outputMode, options);
                var sideEffect = SideEffect(operationIntent, resultPattern, route);
                var classificationStatus = string.Equals(operationIntent, "unclassified", StringComparison.OrdinalIgnoreCase)
                    ? "needs-review"
                    : "classified";

                rows.Add(new MetaDocsCliMatrixRow(
                    application.Id,
                    application.DisplayName,
                    command.Id,
                    route,
                    commandPath,
                    verb,
                    routePattern,
                    ActionFamily(verb, operationIntent),
                    operationIntent,
                    subject.Name,
                    subject.Scope,
                    subject.Selection,
                    inputPattern,
                    resultPattern,
                    sideEffect,
                    classificationStatus,
                    command.Summary ?? string.Empty,
                    workspaceInputs,
                    outputMode,
                    options,
                    arguments,
                    groups));
            }
        }

        EnsureEveryCommandWasRead(subjects, rows);
        var orderedRows = rows
            .OrderBy(static row => row.Application, Comparer)
            .ThenBy(static row => row.Command, Comparer)
            .ToArray();
        var findings = Analyze(orderedRows);
        return new MetaDocsCliMatrix(orderedRows, findings, BuildDecisionCohorts(orderedRows, findings));

        IEnumerable<DocumentationSubject> Children(DocumentationSubject parent, string subjectType) =>
            childrenByParent.TryGetValue(parent.Id, out var children)
                ? MetaDocsOrdering.ByPrevious(
                    children.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, subjectType)),
                    static subject => subject.PreviousSubject,
                    static subject => FirstNonEmpty(subject.DisplayPath, subject.DisplayName, subject.Id))
                : [];

        string Fact(DocumentationSubject subject, string factType, string name) =>
            factsBySubject.TryGetValue(subject.Id, out var facts)
                ? facts.FirstOrDefault(fact =>
                        MetaDocsVocabulary.IsFactType(fact, factType) &&
                        string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty
                : string.Empty;

        MetaDocsCliMatrixParameter Parameter(DocumentationSubject subject, string kind) =>
            new(
                subject.Id,
                kind,
                FirstNonEmpty(Fact(subject, "Cli", "Name"), subject.DisplayName),
                Fact(subject, "Cli", "Syntax"),
                Fact(subject, "Cli", "ParameterId"),
                Fact(subject, "Cli", "ValueName"),
                Fact(subject, "Cli", "ValueShape"),
                Fact(subject, "Cli", "ValueArity"),
                NullableInt(Fact(subject, "Cli", "MinValueCount")),
                NullableInt(Fact(subject, "Cli", "MaxValueCount")),
                NullableBoolean(Fact(subject, "Cli", "Required")),
                NullableBoolean(Fact(subject, "Cli", "Repeatable")),
                Fact(subject, "Cli", "DefaultValue"),
                Split(Fact(subject, "Cli", "Aliases"), ' '),
                Split(Fact(subject, "Cli", "AllowedValues"), ','));

        MetaDocsCliMatrixParameterGroup Group(DocumentationSubject subject)
        {
            var members = Split(Fact(subject, "Cli", "Members"), ',')
                .Select(NormalizeParameterName)
                .Where(static member => !string.IsNullOrWhiteSpace(member))
                .Distinct(Comparer)
                .OrderBy(static member => member, Comparer)
                .ToArray();
            var declaredMemberCount = NullableInt(Fact(subject, "Cli", "MemberCount"));
            if (declaredMemberCount is not null && declaredMemberCount != members.Length)
            {
                throw new InvalidOperationException(
                    $"CLI parameter group '{subject.DisplayPath}' declares {declaredMemberCount} member(s), but {members.Length} were read.");
            }

            return new MetaDocsCliMatrixParameterGroup(
                subject.Id,
                FirstNonEmpty(Fact(subject, "Cli", "Name"), subject.DisplayName),
                NullableBoolean(Fact(subject, "Cli", "Required")),
                NullableBoolean(Fact(subject, "Cli", "AllowsMultiple")),
                members);
        }

        void EnsureDeclaredCount(DocumentationSubject command, string factName, int actual)
        {
            var declaredText = Fact(command, "Cli", factName);
            if (string.IsNullOrWhiteSpace(declaredText))
            {
                return;
            }

            if (!int.TryParse(declaredText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared) || declared != actual)
            {
                throw new InvalidOperationException(
                    $"CLI command '{command.DisplayPath}' declares {factName}={declaredText}, but {actual} row(s) were read.");
            }
        }
    }

    private static IReadOnlyList<MetaDocsCliMatrixFinding> Analyze(IReadOnlyList<MetaDocsCliMatrixRow> rows)
    {
        var findings = new List<MetaDocsCliMatrixFinding>();
        AddCreateDestinationFindings(rows, findings);
        AddOptionContractFindings(rows, findings);
        AddConnectionEnvironmentGroupFindings(rows, findings);
        AddUngroupedOutputFindings(rows, findings);
        AddProductPrefixFindings(rows, findings);
        AddVerbCohortFindings(rows, findings);
        AddImplicitVerbFindings(rows, findings);
        AddClassificationGapFindings(rows, findings);
        return findings
            .OrderBy(static finding => finding.Code, Comparer)
            .ThenBy(static finding => finding.Application, Comparer)
            .ThenBy(static finding => finding.Command, Comparer)
            .ThenBy(static finding => finding.Evidence, Comparer)
            .ToArray();
    }

    private static void AddCreateDestinationFindings(
        IReadOnlyList<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        var commonCreateCount = rows.Count(row =>
            string.Equals(row.Command, "create", StringComparison.OrdinalIgnoreCase) &&
            HasAllOptions(row, "--xml", "--csharp", "--sql") &&
            !HasOption(row, "--new-workspace"));
        foreach (var row in rows.Where(row =>
                     string.Equals(row.Command, "create", StringComparison.OrdinalIgnoreCase) &&
                     HasOption(row, "--new-workspace")))
        {
            findings.Add(Finding(
                "MDCLI001",
                "contract",
                "create-destination",
                row,
                "Create uses a separate destination option instead of the common workspace-surface contract.",
                $"{row.CommandPath} exposes --new-workspace; {commonCreateCount} other create commands use --xml/--csharp/--sql directly."));
        }
    }

    private static void AddOptionContractFindings(
        IReadOnlyList<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        var occurrences = rows
            .SelectMany(row => row.Options.Select(option => new OptionOccurrence(row, option)))
            .GroupBy(static occurrence => occurrence.Option.Name, Comparer)
            .Where(group => group.Select(static occurrence => occurrence.Row.Application).Distinct(Comparer).Count() >= 3);

        foreach (var tokenGroup in occurrences)
        {
            if (IsFleetContractOption(tokenGroup.Key))
            {
                var arityVariants = Variants(
                    tokenGroup,
                    static occurrence => FirstNonEmpty(occurrence.Option.ValueArity, "unspecified"));
                AddMinorityOptionFindings(
                    findings,
                    tokenGroup.Key,
                    arityVariants,
                    "MDCLI002",
                    "contract",
                    "value arity",
                    static occurrence => FirstNonEmpty(occurrence.Option.ValueArity, "unspecified"));

                var shapeVariants = Variants(
                    tokenGroup,
                    static occurrence => FirstNonEmpty(occurrence.Option.ValueShape, "unspecified"));
                AddMinorityOptionFindings(
                    findings,
                    tokenGroup.Key,
                    shapeVariants,
                    "MDCLI009",
                    "contract",
                    "value shape",
                    static occurrence => FirstNonEmpty(occurrence.Option.ValueShape, "unspecified"));
            }

            var valuedOccurrences = tokenGroup
                .Where(static occurrence => !string.IsNullOrWhiteSpace(occurrence.Option.ValueName))
                .ToArray();
            foreach (var contractGroup in valuedOccurrences.GroupBy(
                         static occurrence => $"{FirstNonEmpty(occurrence.Option.ValueShape, "unspecified")}\u001f{FirstNonEmpty(occurrence.Option.ValueArity, "unspecified")}",
                         Comparer))
            {
                var labelVariants = Variants(contractGroup, static occurrence => occurrence.Option.ValueName);
                AddMinorityOptionFindings(
                    findings,
                    tokenGroup.Key,
                    labelVariants,
                    "MDCLI007",
                    "label",
                    "value label",
                    static occurrence => occurrence.Option.ValueName,
                    contractGroup.Key.Replace('\u001f', '/'));
            }
        }

        static IReadOnlyList<OptionVariant> Variants(
            IEnumerable<OptionOccurrence> source,
            Func<OptionOccurrence, string> value) =>
            source
                .GroupBy(value, Comparer)
                .Select(group => new OptionVariant(
                    group.Key,
                    group.ToArray(),
                    group.Select(static occurrence => occurrence.Row.Application).Distinct(Comparer).Count()))
                .OrderByDescending(static variant => variant.ApplicationCount)
                .ThenByDescending(static variant => variant.Rows.Count)
                .ThenBy(static variant => variant.Value, Comparer)
                .ToArray();
    }

    private static void AddMinorityOptionFindings(
        ICollection<MetaDocsCliMatrixFinding> findings,
        string token,
        IReadOnlyList<OptionVariant> variants,
        string code,
        string category,
        string dimension,
        Func<OptionOccurrence, string> value,
        string decisionContext = "")
    {
        if (variants.Count < 2)
        {
            return;
        }

        var canonical = variants[0];
        if (canonical.ApplicationCount == variants[1].ApplicationCount && canonical.Rows.Count == variants[1].Rows.Count)
        {
            return;
        }

        foreach (var variant in variants.Skip(1))
        {
            foreach (var applicationGroup in variant.Rows.GroupBy(static occurrence => occurrence.Row.Application, Comparer))
            {
                var applicationOccurrences = applicationGroup
                    .OrderBy(static occurrence => occurrence.Row.Command, Comparer)
                    .ToArray();
                var representative = applicationOccurrences[0];
                findings.Add(Finding(
                    code,
                    category,
                    OptionDecisionKey(
                        code,
                        token,
                        decisionContext,
                        variant.Value,
                        canonical.Value,
                        applicationGroup.Key,
                        applicationOccurrences),
                    representative.Row,
                    $"Option {token} uses a different {dimension} from the fleet convention.",
                    $"Option {token}: {applicationGroup.Key} uses {value(representative)} on {applicationOccurrences.Length} command(s); the most widespread form is {canonical.Value} in {canonical.ApplicationCount} application(s).",
                    applicationOccurrences.Select(static occurrence => occurrence.Row.Command).ToArray()));
            }
        }
    }

    private static string OptionDecisionKey(
        string code,
        string token,
        string decisionContext,
        string variant,
        string canonical,
        string application,
        IReadOnlyList<OptionOccurrence> occurrences)
    {
        var name = NormalizeParameterName(token);
        if ((code is "MDCLI002" or "MDCLI009") &&
            IsDirectSurfaceName(name) &&
            string.Equals(application, "meta", StringComparison.OrdinalIgnoreCase) &&
            occurrences.All(static occurrence => string.Equals(occurrence.Row.Command, "create", StringComparison.OrdinalIgnoreCase)))
        {
            return "create-destination";
        }

        if (code == "MDCLI009" && IsPrefixedSurfaceName(name))
        {
            return "workspace-output-value-shape";
        }

        if (code == "MDCLI007" && IsPrefixedSurfaceName(name))
        {
            return "workspace-output-value-label";
        }

        if (code == "MDCLI009" && name.StartsWith("is-", StringComparison.OrdinalIgnoreCase))
        {
            return "boolean-value-shape";
        }

        return $"{code}:{name}:{decisionContext}:{variant}:{canonical}";
    }

    private static void AddConnectionEnvironmentGroupFindings(
        IEnumerable<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        foreach (var row in rows)
        {
            foreach (var group in row.ParameterGroups.Where(group =>
                         group.Members.Contains("connection-env", Comparer) &&
                         group.Members.Count(IsDirectSurfaceName) >= 2))
            {
                findings.Add(Finding(
                    "MDCLI003",
                    "contract",
                    "connection-environment-output-choice",
                    row,
                    "Connection configuration is part of a workspace-surface choice.",
                    $"Group {group.Name} contains {string.Join(", ", group.Members)}; other create contracts keep --connection-env outside the --xml/--csharp/--sql choice."));
            }
        }
    }

    private static void AddUngroupedOutputFindings(
        IEnumerable<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        foreach (var row in rows)
        {
            var outputNames = row.Options
                .Select(static option => NormalizeParameterName(option.Name))
                .Where(IsPrefixedSurfaceName)
                .Distinct(Comparer)
                .OrderBy(static name => name, Comparer)
                .ToArray();
            if (outputNames.Length < 2)
            {
                continue;
            }

            var explicitOutputChoice = row.ParameterGroups.FirstOrDefault(group =>
            {
                var includesCurrentOrNamedWorkspace = group.Members.Any(member =>
                    string.Equals(NormalizeParameterName(member), "workspace", StringComparison.OrdinalIgnoreCase));
                return group.AllowsMultiple != true &&
                       (group.Required == true || includesCurrentOrNamedWorkspace) &&
                       group.Members.Count(member => outputNames.Contains(member, Comparer)) >= 2;
            });
            if (explicitOutputChoice is not null)
            {
                continue;
            }

            findings.Add(Finding(
                "MDCLI004",
                "contract",
                "workspace-output-grouping",
                row,
                "Workspace output surfaces are not one explicit, exclusive choice.",
                $"{row.CommandPath} exposes {string.Join(", ", outputNames.Select(static name => "--" + name))} without one exclusive parameter group; output-only commands must also require that group."));
        }
    }

    private static void AddProductPrefixFindings(
        IReadOnlyList<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        foreach (var row in rows)
        {
            var product = row.Application.StartsWith("meta-", StringComparison.OrdinalIgnoreCase)
                ? row.Application["meta-".Length..]
                : string.Empty;
            if (string.IsNullOrWhiteSpace(product) || row.Command.Contains(' '))
            {
                continue;
            }

            var prefix = $"{row.Verb}-{product}-";
            if (!row.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var shorterRoute = $"{row.Verb}-{row.Command[prefix.Length..]}";
            var counterparts = rows
                .Where(candidate =>
                    !string.Equals(candidate.Application, row.Application, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Command, shorterRoute, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (counterparts.Length == 0)
            {
                continue;
            }

            var shorterOptions = counterparts
                .SelectMany(static candidate => candidate.Options)
                .Select(static option => option.Name)
                .ToHashSet(Comparer);
            var redundantOptions = row.Options
                .Select(static option => option.Name)
                .Where(option => option.StartsWith($"--{product}-", StringComparison.OrdinalIgnoreCase))
                .Where(option => shorterOptions.Contains("--" + option[$"--{product}-".Length..]))
                .OrderBy(static option => option, Comparer)
                .ToArray();
            var optionEvidence = redundantOptions.Length == 0
                ? string.Empty
                : $" Matching shorter options: {string.Join(", ", redundantOptions)}.";
            findings.Add(Finding(
                "MDCLI005",
                "vocabulary",
                $"product-prefix:{row.Application}",
                row,
                "The route repeats its product name while a comparable fleet command uses the shorter noun.",
                $"{row.CommandPath} corresponds structurally to {string.Join(", ", counterparts.Select(static candidate => candidate.CommandPath))}.{optionEvidence}"));
        }
    }

    private static void AddVerbCohortFindings(
        IReadOnlyList<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        var cohorts = rows
            .Where(static row => !string.Equals(row.Verb, "implicit", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                static row => string.Join(
                    "\u001f",
                    row.ActionFamily,
                    row.SubjectScope,
                    row.Subject,
                    row.ResultPattern),
                Comparer)
            .Select(static group => group.ToArray())
            .Where(group => group.Select(static row => row.Verb).Distinct(Comparer).Count() >= 2)
            .OrderBy(static group => group[0].ActionFamily, Comparer)
            .ThenBy(static group => group[0].Subject, Comparer)
            .ToArray();

        foreach (var cohort in cohorts)
        {
            var representative = cohort[0];
            var variants = cohort
                .Select(static row => row.Verb)
                .Distinct(Comparer)
                .OrderBy(static verb => verb, Comparer)
                .ToArray();
            var intents = cohort.Select(static row => row.OperationIntent).Distinct(Comparer).OrderBy(static value => value, Comparer);
            var inputs = cohort.Select(static row => row.InputPattern).Distinct(Comparer).OrderBy(static value => value, Comparer);
            var decisionKey = $"verb-family:{representative.ActionFamily}:{representative.SubjectScope}:{representative.Subject}:{representative.ResultPattern}";
            foreach (var row in cohort)
            {
                findings.Add(Finding(
                    "MDCLI006",
                    "vocabulary",
                    decisionKey,
                    row,
                    $"The {representative.ActionFamily} outcome uses more than one surface verb.",
                    $"Scope/subject/result {representative.SubjectScope}/{representative.Subject}/{representative.ResultPattern} uses {string.Join(", ", variants)} across intent(s) {string.Join(", ", intents)} and input pattern(s) {string.Join(", ", inputs)}. This row uses {row.Verb}: {row.CommandPath}."));
            }
        }
    }

    private static void AddClassificationGapFindings(
        IEnumerable<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        foreach (var row in rows.Where(static row =>
                     string.Equals(row.ClassificationStatus, "needs-review", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Finding(
                "MDCLI008",
                "classifier",
                $"classifier:{row.Verb}",
                row,
                "The mechanical classifier could not assign this command an operation intent.",
                $"{row.CommandPath}: verb={row.Verb}; subject={row.Subject}; summary={row.Summary}"));
        }
    }

    private static void AddImplicitVerbFindings(
        IEnumerable<MetaDocsCliMatrixRow> rows,
        ICollection<MetaDocsCliMatrixFinding> findings)
    {
        foreach (var row in rows.Where(static row => string.Equals(row.Verb, "implicit", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Finding(
                "MDCLI010",
                "vocabulary",
                "implicit-action-verb",
                row,
                "The command route has no explicit action verb.",
                $"{row.CommandPath} uses route pattern {row.RoutePattern}; classified intent/action is {row.OperationIntent}/{row.ActionFamily}."));
        }
    }

    private static string OutputMode(
        IReadOnlyList<MetaDocsCliMatrixParameter> options,
        IReadOnlyList<MetaDocsCliMatrixParameterGroup> groups)
    {
        var optionNames = options
            .Select(static option => NormalizeParameterName(option.Name))
            .ToHashSet(Comparer);
        var surfaceNames = optionNames
            .Where(name => IsDirectSurfaceName(name) || IsPrefixedSurfaceName(name))
            .ToArray();
        if (surfaceNames.Length != 0)
        {
            var surfaceGroup = groups.FirstOrDefault(group => group.Members.Count(member => surfaceNames.Contains(member)) >= 2);
            if (surfaceGroup is not null)
            {
                var includesExisting = surfaceGroup.Members.Any(static member => string.Equals(member, "workspace", StringComparison.OrdinalIgnoreCase));
                if (includesExisting)
                {
                    return "existing-or-output";
                }

                return surfaceGroup.Required == true ? "required-surface-choice" : "optional-surface-choice";
            }

            return surfaceNames.Length >= 2 ? "independent-surface-options" : "single-surface-option";
        }

        return optionNames.Contains("out") ? "file" : "none";
    }

    private static string OperationIntent(
        string application,
        string route,
        string verb,
        IReadOnlyList<MetaDocsCliMatrixParameter> options,
        string outputMode)
    {
        if (string.Equals(verb, "help", StringComparison.OrdinalIgnoreCase))
        {
            return "help";
        }

        if (string.Equals(verb, "implicit", StringComparison.OrdinalIgnoreCase) &&
            route is "operations" or "steps" or "workspaces")
        {
            return "inspect";
        }

        if (string.Equals(verb, "create", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(route, "create-pipeline-db", StringComparison.OrdinalIgnoreCase))
        {
            return "create";
        }

        if (verb is "check" or "validate")
        {
            return "validate";
        }

        if (verb is "browse" or "contents" or "explain" or "graph" or "inspect" or "list" or "query" or
            "resolve" or "search" or "show" or "status" or "suggest" or "view")
        {
            return "inspect";
        }

        if (verb is "add" or "allow" or "author" or "include" or "insert" or "promote")
        {
            return "author";
        }

        if (verb is "delete" or "refactor" or "remove" or "rename" or "set" or "update" ||
            (verb is "drop" or "prune" && !IsExternalLifecycle(application, route)))
        {
            return "mutate";
        }

        if (verb is "bind" or "diff" or "extract" or "from" or "infer" or "merge" or "to" ||
            string.Equals(route, "deploy-plan", StringComparison.OrdinalIgnoreCase))
        {
            return "derive";
        }

        if (verb is "emit" or "export" or "render" || string.Equals(outputMode, "file", StringComparison.OrdinalIgnoreCase))
        {
            return "emit";
        }

        if (verb is "import" or "refresh")
        {
            return "import";
        }

        if (verb is "execute" or "process" or "run")
        {
            return "execute";
        }

        if (verb is "deploy" or "restore" || IsExternalLifecycle(application, route))
        {
            return "external-lifecycle";
        }

        if (!string.Equals(outputMode, "none", StringComparison.OrdinalIgnoreCase) ||
            options.Any(option => IsPrefixedSurfaceName(NormalizeParameterName(option.Name))))
        {
            return "derive";
        }

        return "unclassified";
    }

    private static bool IsExternalLifecycle(string application, string route) =>
        string.Equals(route, "create-pipeline-db", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(route, "prune-pipeline-db", StringComparison.OrdinalIgnoreCase) ||
        (route is "drop" && application is "meta-tabular" or "meta-multi-dimensional");

    private static string ActionFamily(string verb, string operationIntent) =>
        verb switch
        {
            "add" or "insert" => "add",
            "allow" => "allow",
            "author" => "upsert",
            "include" => "include",
            "promote" => "promote",
            "set" or "update" => "update",
            "delete" or "drop" or "prune" or "remove" => "remove",
            "rename" => "rename",
            "refactor" => "refactor",
            "inspect" or "show" or "view" => "inspect",
            "contents" or "list" => "list",
            "query" or "search" => "search",
            "browse" => "browse",
            "explain" => "explain",
            "graph" => "graph",
            "resolve" => "resolve",
            "status" => "status",
            "suggest" => "suggest",
            "check" or "validate" => "validate",
            "execute" or "process" or "run" => "execute",
            "emit" => "emit",
            "export" => "export",
            "render" => "render",
            "import" => "import",
            "refresh" => "refresh",
            "create" => "create",
            "deploy" => "deploy",
            "restore" => "restore",
            "bind" => "bind",
            "diff" => "diff",
            "extract" => "extract",
            "from" or "to" => "convert",
            "infer" => "create",
            "merge" => "merge",
            "help" => "help",
            _ => operationIntent,
        };

    private static SubjectAxis CommandSubject(
        string application,
        string route,
        string verb,
        string operationIntent,
        IReadOnlyList<MetaDocsCliMatrixParameter> options,
        IReadOnlyList<MetaDocsCliMatrixParameter> arguments)
    {
        var segments = route
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static component => component.ToLowerInvariant())
                .ToArray())
            .ToArray();
        var flat = segments.SelectMany(static segment => segment).ToArray();
        var verbIndex = Array.FindIndex(flat, component => string.Equals(component, verb, StringComparison.OrdinalIgnoreCase));
        var routeSubject = RouteSubject(application, verb, flat, verbIndex);
        var selector = arguments
            .Select(static argument => NormalizeParameterName(argument.Name))
            .FirstOrDefault(static name => name is "entity" or "subject" or "type");
        var scope = CommandSubjectScope(application, verb, operationIntent, flat, verbIndex, selector);

        if (!string.IsNullOrWhiteSpace(selector))
        {
            if (string.IsNullOrWhiteSpace(routeSubject) || routeSubject is "bulk")
            {
                return new SubjectAxis(selector, scope, "argument");
            }

            return new SubjectAxis(routeSubject, scope, "route+argument");
        }

        if (!string.IsNullOrWhiteSpace(routeSubject))
        {
            return new SubjectAxis(routeSubject, scope, "route");
        }

        var optionSelector = options
            .Select(static option => NormalizeParameterName(option.Name))
            .FirstOrDefault(static name => name is "operation" or "plan" or "manifest");
        if (!string.IsNullOrWhiteSpace(optionSelector))
        {
            return new SubjectAxis(optionSelector, scope, "option");
        }

        return new SubjectAxis(
            operationIntent switch
            {
                "help" => "cli",
                "external-lifecycle" => "external-target",
                "execute" => "operation",
                "create" or "derive" or "import" => "workspace",
                _ => "workspace",
            },
            scope,
            "implicit");
    }

    private static string CommandSubjectScope(
        string application,
        string verb,
        string operationIntent,
        IReadOnlyList<string> components,
        int verbIndex,
        string? selector)
    {
        var routePrefix = verbIndex > 0
            ? string.Join('-', components.Take(verbIndex))
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(routePrefix) && routePrefix != "bulk" && verb is not "from" and not "to")
        {
            return routePrefix;
        }

        if (verb is "from" or "to")
        {
            return "conversion";
        }

        if (string.Equals(application, "meta", StringComparison.OrdinalIgnoreCase) &&
            (!string.IsNullOrWhiteSpace(selector) || routePrefix == "bulk"))
        {
            return "instance";
        }

        return operationIntent switch
        {
            "author" or "mutate" => "model",
            "create" or "derive" or "import" => "workspace",
            "execute" => "operation",
            "external-lifecycle" => "external-target",
            "help" => "cli",
            "inspect" or "validate" => "workspace",
            "emit" => "artifact",
            _ => "workspace",
        };
    }

    private static string RouteSubject(string application, string verb, IReadOnlyList<string> components, int verbIndex)
    {
        if (verbIndex < 0)
        {
            return string.Empty;
        }

        IEnumerable<string> subjectComponents;
        if (verb is "from" or "to")
        {
            subjectComponents = components.Where((_, index) => index != verbIndex);
        }
        else
        {
            var afterVerb = components.Skip(verbIndex + 1).ToArray();
            subjectComponents = afterVerb.Length == 0 ? components.Take(verbIndex) : afterVerb;
        }

        var subject = subjectComponents.ToList();
        var product = application.StartsWith("meta-", StringComparison.OrdinalIgnoreCase)
            ? application["meta-".Length..]
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static component => component.ToLowerInvariant())
                .ToArray()
            : [];
        if (product.Length != 0 && subject.Count > product.Length && subject.Take(product.Length).SequenceEqual(product, Comparer))
        {
            subject.RemoveRange(0, product.Length);
        }

        return string.Join('-', subject);
    }

    private static string InputPattern(
        IReadOnlyList<MetaDocsCliMatrixParameter> options,
        IReadOnlyList<MetaDocsCliMatrixParameter> arguments)
    {
        var names = options
            .Concat(arguments)
            .Select(static parameter => NormalizeParameterName(parameter.Name))
            .Where(static name => name.Contains("workspace", StringComparison.OrdinalIgnoreCase))
            .Distinct(Comparer)
            .ToArray();
        var hasCurrent = names.Contains("workspace", Comparer);
        var namedCount = names.Count(static name =>
            name != "workspace" &&
            name != "new-workspace" &&
            !name.StartsWith("output-", StringComparison.OrdinalIgnoreCase));
        return (hasCurrent, namedCount) switch
        {
            (false, 0) => "none",
            (true, 0) => "current-workspace",
            (false, 1) => "one-named-workspace",
            (false, > 1) => "multiple-named-workspaces",
            (true, 1) => "current-plus-one-named-workspace",
            _ => "current-plus-multiple-named-workspaces",
        };
    }

    private static string ResultPattern(
        string operationIntent,
        string outputMode,
        IReadOnlyList<MetaDocsCliMatrixParameter> options)
    {
        var output = outputMode switch
        {
            "existing-or-output" => "existing-or-workspace",
            "required-surface-choice" => "workspace-required",
            "optional-surface-choice" => "workspace-optional",
            "independent-surface-options" => "workspace-ambiguous",
            "single-surface-option" => "workspace-single",
            "file" => "file",
            _ => string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        if (options.Any(option => string.Equals(NormalizeParameterName(option.Name), "out", StringComparison.OrdinalIgnoreCase)))
        {
            return "file";
        }

        return operationIntent switch
        {
            "help" or "inspect" or "validate" => "console",
            "execute" or "external-lifecycle" => "external-or-runtime",
            "author" or "mutate" or "import" => "in-place-workspace",
            "create" or "derive" => "workspace",
            _ => "none",
        };
    }

    private static string SideEffect(string operationIntent, string resultPattern, string route)
    {
        if (operationIntent is "help" or "inspect" or "validate")
        {
            return "read-only";
        }

        if (string.Equals(resultPattern, "file", StringComparison.OrdinalIgnoreCase))
        {
            return "file-write";
        }

        if (resultPattern.Contains("workspace", StringComparison.OrdinalIgnoreCase))
        {
            return "workspace-write";
        }

        if (operationIntent == "execute")
        {
            return "runtime-execution";
        }

        if (operationIntent == "external-lifecycle" || route.Contains("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            return "external-write";
        }

        return operationIntent is "author" or "mutate" or "import" or "create" or "derive"
            ? "workspace-write"
            : "unknown";
    }

    private static IReadOnlyList<MetaDocsCliDecisionCohort> BuildDecisionCohorts(
        IReadOnlyList<MetaDocsCliMatrixRow> rows,
        IReadOnlyList<MetaDocsCliMatrixFinding> findings)
    {
        var rowsByCommand = rows.ToDictionary(
            static row => CommandKey(row.Application, row.Command),
            static row => row,
            Comparer);
        return findings
            .GroupBy(static finding => finding.DecisionKey, Comparer)
            .Select(group =>
            {
                var occurrences = group.ToArray();
                var affectedRows = occurrences
                    .SelectMany(static finding => finding.AffectedCommands.Select(command => CommandKey(finding.Application, command)))
                    .Distinct(Comparer)
                    .Where(rowsByCommand.ContainsKey)
                    .Select(key => rowsByCommand[key])
                    .OrderBy(static row => row.Application, Comparer)
                    .ThenBy(static row => row.Command, Comparer)
                    .ToArray();
                var first = occurrences[0];
                return new MetaDocsCliDecisionCohort(
                    group.Key,
                    string.Join(" | ", Values(occurrences.Select(static finding => finding.Code))),
                    first.Category,
                    DecisionTitle(group.Key, first.Message),
                    Values(affectedRows.Select(static row => row.OperationIntent)),
                    Values(affectedRows.Select(static row => row.ActionFamily)),
                    Values(affectedRows.Select(static row => row.SubjectScope)),
                    Values(affectedRows.Select(static row => row.Subject)),
                    Values(affectedRows.Select(static row => row.SubjectSelection)),
                    Values(affectedRows.Select(static row => row.InputPattern)),
                    Values(affectedRows.Select(static row => row.ResultPattern)),
                    Values(affectedRows.Select(static row => row.SideEffect)),
                    Values(affectedRows.Select(static row => row.Verb)),
                    Values(affectedRows.Select(static row => row.RoutePattern)),
                    affectedRows.Select(static row => row.Application).Distinct(Comparer).Count(),
                    Values(affectedRows.Select(static row => row.Application)),
                    affectedRows.Length,
                    affectedRows.Select(static row => $"{row.Application} {row.Command}").ToArray(),
                    Values(occurrences.Select(static finding => finding.Evidence)));
            })
            .OrderBy(cohort => CategoryPriority(cohort.Category))
            .ThenBy(static cohort => cohort.Code, Comparer)
            .ThenBy(static cohort => cohort.DecisionKey, Comparer)
            .ToArray();

        static IReadOnlyList<string> Values(IEnumerable<string> values) =>
            values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(Comparer)
                .OrderBy(static value => value, Comparer)
                .ToArray();
    }

    private static int CategoryPriority(string category) =>
        category switch
        {
            "contract" => 0,
            "vocabulary" => 1,
            "label" => 2,
            "classifier" => 3,
            _ => 4,
        };

    private static string DecisionTitle(string decisionKey, string fallback) =>
        decisionKey switch
        {
            "workspace-output-value-shape" => "Workspace output options use different value shapes.",
            "workspace-output-value-label" => "Workspace output options use different value labels.",
            "boolean-value-shape" => "Boolean-style options use non-Boolean value shapes.",
            _ => fallback,
        };

    private static void EnsureEveryCommandWasRead(
        IReadOnlyList<DocumentationSubject> subjects,
        IReadOnlyList<MetaDocsCliMatrixRow> rows)
    {
        var expected = subjects
            .Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliCommand"))
            .Select(static subject => subject.Id)
            .OrderBy(static id => id, Comparer)
            .ToArray();
        var actual = rows
            .Select(static row => row.CommandId)
            .OrderBy(static id => id, Comparer)
            .ToArray();
        if (!expected.SequenceEqual(actual, Comparer))
        {
            var missing = expected.Except(actual, Comparer).ToArray();
            var repeated = actual.GroupBy(static id => id, Comparer).Where(static group => group.Count() > 1).Select(static group => group.Key).ToArray();
            throw new InvalidOperationException(
                $"CLI matrix coverage failed. Expected {expected.Length} command(s), read {actual.Length}. Missing: {string.Join(", ", missing)}. Repeated: {string.Join(", ", repeated)}.");
        }
    }

    private static MetaDocsCliMatrixFinding Finding(
        string code,
        string category,
        string decisionKey,
        MetaDocsCliMatrixRow row,
        string message,
        string evidence,
        IReadOnlyList<string>? affectedCommands = null) =>
        new(code, category, decisionKey, row.Application, row.Command, message, evidence, affectedCommands ?? [row.Command]);

    private static bool HasOption(MetaDocsCliMatrixRow row, string token) =>
        row.Options.Any(option => string.Equals(option.Name, token, StringComparison.OrdinalIgnoreCase));

    private static bool HasAllOptions(MetaDocsCliMatrixRow row, params string[] tokens) =>
        tokens.All(token => HasOption(row, token));

    private static bool IsDirectSurfaceName(string name) =>
        name is "xml" or "csharp" or "sql";

    private static bool IsPrefixedSurfaceName(string name) =>
        name is "output-xml" or "output-csharp" or "output-sql";

    private static bool IsFleetContractOption(string token)
    {
        var name = NormalizeParameterName(token);
        return name is "workspace" or "source-workspace" or "connection-env" or "out" ||
               name.StartsWith("is-", StringComparison.OrdinalIgnoreCase) ||
               IsDirectSurfaceName(name) ||
               IsPrefixedSurfaceName(name);
    }

    private static string NormalizeParameterName(string name) =>
        name.Trim().Trim('<', '>').TrimStart('-').ToLowerInvariant();

    private static string CommandKey(string application, string command) =>
        $"{application}\u001f{command}";

    private static string RelativeRoute(DocumentationSubject application, DocumentationSubject command)
    {
        var display = FirstNonEmpty(command.DisplayPath, command.DisplayName);
        return display.StartsWith(application.DisplayName + " ", StringComparison.OrdinalIgnoreCase)
            ? display[(application.DisplayName.Length + 1)..]
            : display;
    }

    private static string Verb(string route)
    {
        foreach (var component in route
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .SelectMany(static segment => segment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (KnownVerbs.Contains(component))
            {
                return component.ToLowerInvariant();
            }
        }

        return "implicit";
    }

    private static string RoutePattern(string route, string verb)
    {
        if (string.Equals(verb, "implicit", StringComparison.OrdinalIgnoreCase))
        {
            return "noun-only";
        }

        var segments = route.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstComponents = segments[0].Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (string.Equals(firstComponents[0], verb, StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length > 1)
            {
                return "verb-first-space";
            }

            return firstComponents.Length == 1 ? "verb-only" : "verb-first-hyphen";
        }

        if (firstComponents.Contains(verb, Comparer))
        {
            return "infix-hyphen";
        }

        return "scoped-space";
    }

    private static IReadOnlyList<string> Split(string value, char separator) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int? NullableInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static bool? NullableBoolean(string value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;

    private static bool IsCurrent(DocumentationSubject subject) =>
        IsCurrentStatus(subject.Status);

    private static bool IsCurrent(DocumentationFact fact) =>
        IsCurrentStatus(fact.Status);

    private static bool IsCurrentStatus(string? status) =>
        !string.Equals(status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record OptionOccurrence(
        MetaDocsCliMatrixRow Row,
        MetaDocsCliMatrixParameter Option);

    private sealed record OptionVariant(
        string Value,
        IReadOnlyList<OptionOccurrence> Rows,
        int ApplicationCount);

    private sealed record SubjectAxis(string Name, string Scope, string Selection);
}

public sealed class MetaDocsCliMatrix
{
    public MetaDocsCliMatrix(
        IReadOnlyList<MetaDocsCliMatrixRow> commands,
        IReadOnlyList<MetaDocsCliMatrixFinding> findings,
        IReadOnlyList<MetaDocsCliDecisionCohort> decisionCohorts)
    {
        Commands = commands;
        Findings = findings;
        DecisionCohorts = decisionCohorts;
    }

    public IReadOnlyList<MetaDocsCliMatrixRow> Commands { get; }
    public IReadOnlyList<MetaDocsCliMatrixFinding> Findings { get; }
    public IReadOnlyList<MetaDocsCliDecisionCohort> DecisionCohorts { get; }
    public int ApplicationCount => Commands.Select(static row => row.Application).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int CommandCount => Commands.Count;
    public int ParameterCount => Commands.Sum(static row => row.Options.Count + row.Arguments.Count);
    public int ParameterGroupCount => Commands.Sum(static row => row.ParameterGroups.Count);
    public int UnclassifiedCount => Commands.Count(static row => string.Equals(row.ClassificationStatus, "needs-review", StringComparison.OrdinalIgnoreCase));
}

public sealed record MetaDocsCliMatrixRow(
    string ApplicationId,
    string Application,
    string CommandId,
    string Command,
    string CommandPath,
    string Verb,
    string RoutePattern,
    string ActionFamily,
    string OperationIntent,
    string Subject,
    string SubjectScope,
    string SubjectSelection,
    string InputPattern,
    string ResultPattern,
    string SideEffect,
    string ClassificationStatus,
    string Summary,
    IReadOnlyList<string> WorkspaceInputs,
    string OutputMode,
    IReadOnlyList<MetaDocsCliMatrixParameter> Options,
    IReadOnlyList<MetaDocsCliMatrixParameter> Arguments,
    IReadOnlyList<MetaDocsCliMatrixParameterGroup> ParameterGroups);

public sealed record MetaDocsCliMatrixParameter(
    string SubjectId,
    string Kind,
    string Name,
    string Syntax,
    string ParameterId,
    string ValueName,
    string ValueShape,
    string ValueArity,
    int? MinValueCount,
    int? MaxValueCount,
    bool? Required,
    bool? Repeatable,
    string DefaultValue,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> AllowedValues);

public sealed record MetaDocsCliMatrixParameterGroup(
    string SubjectId,
    string Name,
    bool? Required,
    bool? AllowsMultiple,
    IReadOnlyList<string> Members);

public sealed record MetaDocsCliMatrixFinding(
    string Code,
    string Category,
    string DecisionKey,
    string Application,
    string Command,
    string Message,
    string Evidence,
    IReadOnlyList<string> AffectedCommands);

public sealed record MetaDocsCliDecisionCohort(
    string DecisionKey,
    string Code,
    string Category,
    string Title,
    IReadOnlyList<string> OperationIntents,
    IReadOnlyList<string> ActionFamilies,
    IReadOnlyList<string> SubjectScopes,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<string> SubjectSelections,
    IReadOnlyList<string> InputPatterns,
    IReadOnlyList<string> ResultPatterns,
    IReadOnlyList<string> SideEffects,
    IReadOnlyList<string> SurfaceVerbs,
    IReadOnlyList<string> RoutePatterns,
    int ApplicationCount,
    IReadOnlyList<string> Applications,
    int CommandCount,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> Evidence);
