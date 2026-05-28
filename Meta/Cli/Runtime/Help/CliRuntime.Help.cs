internal sealed partial class CliRuntime
{
    string BuildUsageHintForCurrentArgs()
    {
        return HelpTopics.TryResolveUsageForArgs(args, out var usage)
            ? usage
            : string.Empty;
    }

    string NormalizeUsageSyntax(string usage)
    {
        if (string.IsNullOrWhiteSpace(usage))
        {
            return string.Empty;
        }
    
        var trimmed = usage.Trim();
        const string Prefix = "Usage:";
        if (trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[Prefix.Length..].Trim();
        }
    
        if (!trimmed.StartsWith("meta ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "meta " + trimmed;
        }
    
        return trimmed;
    }

    string BuildNextHelpHintFromUsage(string usage)
    {
        var normalizedUsage = NormalizeUsageSyntax(usage);
        if (string.IsNullOrWhiteSpace(normalizedUsage))
        {
            return "meta help";
        }
    
        var tokens = normalizedUsage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1 || !string.Equals(tokens[0], "meta", StringComparison.OrdinalIgnoreCase))
        {
            return "meta help";
        }
    
        var topic = new List<string>();
        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("<", StringComparison.Ordinal) ||
                token.StartsWith("[", StringComparison.Ordinal) ||
                token.StartsWith("--", StringComparison.Ordinal) ||
                token.Contains('|', StringComparison.Ordinal))
            {
                break;
            }
    
            topic.Add(token);
        }
    
        if (topic.Count == 0)
        {
            return "meta help";
        }
    
        if (topic.Count == 1 && string.Equals(topic[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            return "meta help";
        }
    
        return $"meta {string.Join(" ", topic)} help";
    }

    string BuildNextHelpHintForCurrentArgs()
    {
        var usage = BuildUsageHintForCurrentArgs();
        return BuildNextHelpHintFromUsage(usage);
    }

    bool IsHelpToken(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    bool TryHandleHelpRequest(string[] commandArgs, out int exitCode)
    {
        exitCode = 0;
        if (commandArgs.Length == 0)
        {
            return false;
        }
    
        if (IsHelpToken(commandArgs[0]))
        {
            exitCode = PrintHelpForTopic(commandArgs.Skip(1).ToArray());
            return true;
        }
    
        var helpIndex = Array.FindIndex(commandArgs, IsHelpToken);
        if (helpIndex > 0)
        {
            exitCode = PrintHelpForTopic(commandArgs.Take(helpIndex).ToArray());
            return true;
        }
    
        return false;
    }

    int PrintHelpForTopic(string[] topicTokens)
    {
        if (topicTokens == null || topicTokens.Length == 0)
        {
            PrintUsage();
            return 0;
        }
    
        var normalizedTokens = topicTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim())
            .ToArray();
        if (normalizedTokens.Length == 0)
        {
            PrintUsage();
            return 0;
        }
    
        var key = string.Join(" ", normalizedTokens).ToLowerInvariant();
        if (string.Equals(key, "help", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        if (HelpTopics.TryBuildHelpTopic(key, out var topicDocument))
        {
            RenderHelpDocument(WithRuntimeHeader(topicDocument));
            return 0;
        }
    
        var suggestionCandidates = HelpTopics.GetCommandSuggestions();
        var suggestions = SuggestValues(normalizedTokens[0], suggestionCandidates);
    
        var hints = new List<string>();
        if (suggestions.Count > 0)
        {
            hints.Add("Did you mean: " + string.Join(", ", suggestions.Take(3)));
        }
    
        hints.Add("Usage: meta help [<command> ...]");
        hints.Add("Next: meta help");
    
        return PrintFormattedError(
            "E_USAGE",
            $"unknown help topic '{string.Join(" ", normalizedTokens)}'.",
            exitCode: 1,
            hints: hints);
    }

    void PrintUsage()
    {
        var sections = HelpTopics.GetCommandCatalogByDomain()
            .Select(item => (Title: NormalizeHelpDomainTitle(item.Domain), item.Commands))
            .Select(item => new HelpSection($"{item.Title}:", item.Commands))
            .ToArray();

        RenderHelpDocument(new HelpDocument(
            Header: new HelpHeader(
                "Meta CLI",
                TryGetCliVersion(),
                "Workspace is discovered from current directory; use --workspace to override."),
            Usage: "meta <command> [options]",
            OptionsTitle: "Global options:",
            Options: new[]
            {
                ("--workspace <path>", "Override workspace root."),
                ("--strict", "Treat warnings as errors for mutating commands."),
            },
            Sections: sections,
            Examples: new[]
            {
                "meta status",
                "meta model add-entity SourceSystem",
                "meta insert Cube 10 --set \"CubeName=Ops Cube\"",
            },
            Next: "meta <command> help"));
    }

    void RenderHelpDocument(HelpDocument document)
    {
        WriteHelpHeader(document.Header);

        presenter.WriteInfo(string.Empty);
        presenter.WriteUsage(document.Usage);

        presenter.WriteInfo(string.Empty);
        presenter.WriteOptionCatalog(document.Options, document.OptionsTitle);

        if (document.Sections is { Count: > 0 })
        {
            presenter.WriteInfo(string.Empty);
            for (var i = 0; i < document.Sections.Count; i++)
            {
                var section = document.Sections[i];
                presenter.WriteCommandCatalog(section.Title, section.Entries);
                if (i < document.Sections.Count - 1)
                {
                    presenter.WriteInfo(string.Empty);
                }
            }
        }

        if (document.Examples is { Count: > 0 })
        {
            presenter.WriteInfo(string.Empty);
            presenter.WriteExamples(document.Examples);
        }

        presenter.WriteInfo(string.Empty);
        presenter.WriteNext(NormalizeNextHelpHint(document.Next));
    }

    HelpDocument WithRuntimeHeader(HelpDocument document)
    {
        var header = document.Header;
        var version = string.IsNullOrWhiteSpace(header.Version)
            ? TryGetCliVersion()
            : header.Version;

        var product = string.IsNullOrWhiteSpace(header.Product)
            ? "Meta CLI"
            : header.Product;

        return document with
        {
            Header = header with
            {
                Product = product,
                Version = version,
            },
        };
    }
    static string NormalizeNextHelpHint(string next)
    {
        var trimmed = next?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "meta help";
        }

        if (trimmed.EndsWith(" --help", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^7] + " help";
        }

        return trimmed;
    }

    static string NormalizeHelpDomainTitle(string domain)
    {
        return domain.Trim().ToLowerInvariant() switch
        {
            "workspace" => "Workspace",
            "model" => "Model",
            "instance" => "Instance",
            "pipeline" => "Pipeline",
            "inspect" => "Model",
            "modify" => "Instance",
            "generate" => "Pipeline",
            "utility" => "Utility",
            _ => string.IsNullOrWhiteSpace(domain) ? "Other" : domain.Trim(),
        };
    }

    int PrintCommandUnknownError(string command)
    {
        var suggestionCandidates = HelpTopics.GetCommandSuggestions();
        var suggestions = SuggestValues(command, suggestionCandidates);
        var hints = new List<string>();
        if (suggestions.Count > 0)
        {
            hints.Add("Did you mean: " + string.Join(", ", suggestions.Take(3)));
        }
    
        hints.Add("Next: meta help");
    
        return PrintFormattedError(
            "E_COMMAND_UNKNOWN",
            $"Unknown command '{command}'.",
            exitCode: 1,
            hints: hints);
    }

    void WriteHelpHeader(HelpHeader header)
    {
        if (!string.IsNullOrWhiteSpace(header.Note))
        {
            presenter.WriteInfo(header.Note);
        }
    }

    static string TryGetCliVersion()
    {
        try
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (version == null)
            {
                return string.Empty;
            }

            return version.Revision > 0
                ? version.ToString(3)
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
        catch
        {
            return string.Empty;
        }
    }
}
