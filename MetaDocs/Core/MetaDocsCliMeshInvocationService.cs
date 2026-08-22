using System.Text;
using Meta.Integration;
using MetaMeshModel = global::MetaMesh.MetaMeshModel;

namespace MetaDocs.Core;

public sealed class MetaDocsCliMeshInvocationService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<MetaDocsCliMeshSource> LoadSources(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var workspacePaths = new HashSet<string>(Comparer);
        foreach (var rootValue in roots)
        {
            if (string.IsNullOrWhiteSpace(rootValue))
            {
                continue;
            }

            var root = Path.GetFullPath(rootValue);
            if (File.Exists(root))
            {
                if (string.Equals(Path.GetFileName(root), "workspace.meta", StringComparison.OrdinalIgnoreCase) &&
                    IsMeshWorkspaceDirectory(Path.GetDirectoryName(root)))
                {
                    workspacePaths.Add(Path.GetDirectoryName(root)!);
                }

                continue;
            }

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Mesh root '{root}' does not exist.");
            }

            if (File.Exists(Path.Combine(root, "workspace.meta")) && IsMeshWorkspaceDirectory(root))
            {
                workspacePaths.Add(root);
                continue;
            }

            foreach (var directory in EnumerateMeshWorkspaceDirectories(root))
            {
                workspacePaths.Add(directory);
            }
        }

        return workspacePaths
            .OrderBy(static path => path, Comparer)
            .Select(path => new MetaDocsCliMeshSource(
                path,
                TypedWorkspaceModelMapper.Load<MetaMeshModel>(path, searchUpward: false)))
            .ToArray();
    }

    public MetaDocsCliMeshInvocationMatrix Build(
        MetaDocsCliMatrix cliMatrix,
        IEnumerable<MetaDocsCliMeshSource> sources)
    {
        ArgumentNullException.ThrowIfNull(cliMatrix);
        ArgumentNullException.ThrowIfNull(sources);

        var commandsByApplication = cliMatrix.Commands
            .GroupBy(static row => row.Application, Comparer)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(row => new CommandCandidate(row, Tokenize(row.Command)))
                    .OrderByDescending(static candidate => candidate.Tokens.Count)
                    .ThenBy(static candidate => candidate.Row.Command, Comparer)
                    .ToArray(),
                Comparer);
        var rows = new List<MetaDocsCliMeshInvocationRow>();

        foreach (var source in sources.OrderBy(static item => item.WorkspacePath, Comparer))
        {
            var meshName = source.Model.MeshList.Count == 1
                ? source.Model.MeshList[0].Name
                : Path.GetFileName(source.WorkspacePath);
            foreach (var operation in source.Model.OperationList.OrderBy(static item => item.Name, Comparer))
            {
                var steps = OrderSteps(source.Model, operation);
                for (var index = 0; index < steps.Count; index++)
                {
                    var step = steps[index];
                    var row = AnalyzeStep(
                        source.WorkspacePath,
                        meshName,
                        operation.Name,
                        index + 1,
                        step,
                        commandsByApplication);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
            }
        }

        return new MetaDocsCliMeshInvocationMatrix(rows);
    }

    private static MetaDocsCliMeshInvocationRow? AnalyzeStep(
        string meshWorkspace,
        string meshName,
        string operation,
        int stepIndex,
        global::MetaMesh.OperationStep step,
        IReadOnlyDictionary<string, CommandCandidate[]> commandsByApplication)
    {
        var application = NormalizeApplication(step.Executable);
        if (!commandsByApplication.TryGetValue(application, out var candidates))
        {
            if (!application.StartsWith("meta", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Row(
                command: string.Empty,
                [new InvocationIssue("MDCLIINV001", $"Application '{application}' is not present in the CLI matrix.")]);
        }

        var tokens = Tokenize(step.Arguments ?? string.Empty);
        var command = candidates.FirstOrDefault(candidate => StartsWith(tokens, candidate.Tokens));
        if (command is null)
        {
            var supplied = tokens.Count == 0 ? "(none)" : tokens[0];
            return Row(
                command: supplied,
                [new InvocationIssue("MDCLIINV002", $"Command beginning '{supplied}' is not modeled for '{application}'.")]);
        }

        var issues = ValidateArguments(command.Row, tokens.Skip(command.Tokens.Count).ToArray());
        return Row(command.Row.Command, issues);

        MetaDocsCliMeshInvocationRow Row(string command, IReadOnlyList<InvocationIssue> issues) =>
            new(
                meshWorkspace,
                meshName,
                operation,
                stepIndex,
                step.Name,
                step.Executable,
                step.Arguments ?? string.Empty,
                application,
                command,
                issues.Count == 0 ? "conformant" : "violation",
                issues.Select(static issue => issue.Code).Distinct(Comparer).OrderBy(static code => code, Comparer).ToArray(),
                issues.Select(static issue => issue.Message).Distinct(Comparer).ToArray());
    }

    private static IReadOnlyList<InvocationIssue> ValidateArguments(
        MetaDocsCliMatrixRow command,
        IReadOnlyList<string> tokens)
    {
        var issues = new List<InvocationIssue>();
        var optionLookup = new Dictionary<string, MetaDocsCliMatrixParameter>(Comparer);
        foreach (var option in command.Options)
        {
            foreach (var token in OptionTokens(option))
            {
                optionLookup[token] = option;
            }
        }

        var occurrences = new Dictionary<string, int>(Comparer);
        var positionalValues = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                positionalValues.Add(token);
                continue;
            }

            if (!optionLookup.TryGetValue(token, out var option))
            {
                issues.Add(new InvocationIssue("MDCLIINV003", $"Unknown option '{token}' for '{command.CommandPath}'."));
                continue;
            }

            var parameterName = NormalizeParameterName(option.Name);
            occurrences.TryGetValue(parameterName, out var occurrenceCount);
            occurrences[parameterName] = occurrenceCount + 1;
            if (occurrenceCount > 0 && option.Repeatable is not true)
            {
                issues.Add(new InvocationIssue("MDCLIINV006", $"Option '{token}' is not repeatable."));
            }

            var (minimum, maximum) = ValueCount(option);
            var values = new List<string>();
            while (values.Count < maximum && index + 1 < tokens.Count)
            {
                var next = tokens[index + 1];
                if (next.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }

                values.Add(next);
                index++;
            }

            if (values.Count < minimum)
            {
                issues.Add(new InvocationIssue("MDCLIINV004", $"Option '{token}' requires {minimum} value(s)."));
                continue;
            }

            if (option.AllowedValues.Count != 0)
            {
                foreach (var value in values.Where(value => !option.AllowedValues.Contains(value, Comparer)))
                {
                    issues.Add(new InvocationIssue(
                        "MDCLIINV005",
                        $"Value '{value}' is not allowed for '{token}'; expected {string.Join(", ", option.AllowedValues)}."));
                }
            }
        }

        foreach (var option in command.Options.Where(static option => option.Required is true))
        {
            var name = NormalizeParameterName(option.Name);
            if (!occurrences.ContainsKey(name) && string.IsNullOrWhiteSpace(option.DefaultValue))
            {
                issues.Add(new InvocationIssue("MDCLIINV007", $"Required option '{PrimaryOptionToken(option)}' is missing."));
            }
        }

        var requiredPositionals = command.Arguments.Sum(static argument =>
            argument.Required is true ? Math.Max(1, argument.MinValueCount ?? 1) : argument.MinValueCount ?? 0);
        var maximumPositionals = command.Arguments.Count == 0
            ? 0
            : command.Arguments.Any(static argument => argument.MaxValueCount is null &&
                                                    !string.Equals(argument.ValueArity, "One", StringComparison.OrdinalIgnoreCase))
                ? int.MaxValue
                : command.Arguments.Sum(static argument => argument.MaxValueCount ?? 1);
        if (positionalValues.Count < requiredPositionals)
        {
            issues.Add(new InvocationIssue(
                "MDCLIINV007",
                $"Command requires {requiredPositionals} positional value(s), but {positionalValues.Count} were supplied."));
        }
        else if (positionalValues.Count > maximumPositionals)
        {
            issues.Add(new InvocationIssue(
                "MDCLIINV009",
                $"Command accepts at most {maximumPositionals} positional value(s), but {positionalValues.Count} were supplied."));
        }

        foreach (var group in command.ParameterGroups)
        {
            var selected = group.Members.Count(member => occurrences.ContainsKey(NormalizeParameterName(member)));
            if (group.Required is true && selected == 0)
            {
                issues.Add(new InvocationIssue(
                    "MDCLIINV008",
                    $"Parameter group '{group.Name}' requires one of: {string.Join(", ", group.Members)}."));
            }
            else if (group.AllowsMultiple is not true && selected > 1)
            {
                issues.Add(new InvocationIssue(
                    "MDCLIINV008",
                    $"Parameter group '{group.Name}' accepts only one member."));
            }
        }

        return issues;
    }

    private static (int Minimum, int Maximum) ValueCount(MetaDocsCliMatrixParameter parameter)
    {
        if (string.Equals(parameter.ValueArity, "None", StringComparison.OrdinalIgnoreCase))
        {
            return (0, 0);
        }

        var minimum = parameter.MinValueCount ?? 1;
        var maximum = parameter.MaxValueCount ??
                      (string.Equals(parameter.ValueArity, "One", StringComparison.OrdinalIgnoreCase) ? 1 : minimum);
        return (minimum, Math.Max(minimum, maximum));
    }

    private static IEnumerable<string> OptionTokens(MetaDocsCliMatrixParameter parameter)
    {
        var tokens = new HashSet<string>(Comparer);
        if (parameter.Name.StartsWith("-", StringComparison.Ordinal))
        {
            tokens.Add(parameter.Name);
        }

        var syntaxToken = parameter.Syntax.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (syntaxToken?.StartsWith("-", StringComparison.Ordinal) is true)
        {
            tokens.Add(syntaxToken);
        }

        foreach (var alias in parameter.Aliases.Where(static alias => alias.StartsWith("-", StringComparison.Ordinal)))
        {
            tokens.Add(alias);
        }

        return tokens;
    }

    private static string PrimaryOptionToken(MetaDocsCliMatrixParameter parameter) =>
        OptionTokens(parameter).FirstOrDefault() ?? parameter.Name;

    private static string NormalizeParameterName(string value) => value.Trim().TrimStart('-');

    private static string NormalizeApplication(string executable)
    {
        var fileName = Path.GetFileName(executable.Trim());
        return string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private static bool StartsWith(IReadOnlyList<string> tokens, IReadOnlyList<string> prefix)
    {
        if (tokens.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(tokens[index], prefix[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length != 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
        }

        if (quoted)
        {
            throw new InvalidOperationException($"Invocation arguments contain an unmatched quote: {value}");
        }

        if (token.Length != 0)
        {
            tokens.Add(token.ToString());
        }

        return tokens;
    }

    private static IReadOnlyList<global::MetaMesh.OperationStep> OrderSteps(
        MetaMeshModel model,
        global::MetaMesh.Operation operation)
    {
        var steps = model.OperationStepList.Where(step => ReferenceEquals(step.Operation, operation)).ToArray();
        var ordered = new List<global::MetaMesh.OperationStep>();
        var remaining = steps.ToList();
        foreach (var head in steps.Where(static step => step.PreviousStep is null).OrderBy(static step => step.Name, Comparer))
        {
            Append(head);
        }

        foreach (var step in remaining.OrderBy(static step => step.Name, Comparer).ToArray())
        {
            Append(step);
        }

        return ordered;

        void Append(global::MetaMesh.OperationStep step)
        {
            if (!remaining.Remove(step))
            {
                return;
            }

            ordered.Add(step);
            foreach (var next in remaining.Where(candidate => ReferenceEquals(candidate.PreviousStep, step)).ToArray())
            {
                Append(next);
            }
        }
    }

    private static bool IsMeshWorkspaceDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".MetaMesh", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateMeshWorkspaceDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (name is ".git" or "bin" or "obj")
                {
                    continue;
                }

                if (IsMeshWorkspaceDirectory(child) && File.Exists(Path.Combine(child, "workspace.meta")))
                {
                    yield return child;
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private sealed record CommandCandidate(MetaDocsCliMatrixRow Row, IReadOnlyList<string> Tokens);
    private sealed record InvocationIssue(string Code, string Message);
}

public sealed record MetaDocsCliMeshSource(string WorkspacePath, MetaMeshModel Model);

public sealed class MetaDocsCliMeshInvocationMatrix
{
    public MetaDocsCliMeshInvocationMatrix(IReadOnlyList<MetaDocsCliMeshInvocationRow> invocations)
    {
        Invocations = invocations;
    }

    public IReadOnlyList<MetaDocsCliMeshInvocationRow> Invocations { get; }
    public int ViolationCount => Invocations.Count(static row => row.Status == "violation");
    public int ConformantCount => Invocations.Count - ViolationCount;
    public int MeshCount => Invocations.Select(static row => row.MeshWorkspace).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

public sealed record MetaDocsCliMeshInvocationRow(
    string MeshWorkspace,
    string Mesh,
    string Operation,
    int StepIndex,
    string Step,
    string Executable,
    string Arguments,
    string Application,
    string Command,
    string Status,
    IReadOnlyList<string> Codes,
    IReadOnlyList<string> Evidence);
