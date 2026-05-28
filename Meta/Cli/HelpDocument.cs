internal readonly record struct HelpHeader(
    string Product,
    string? Version,
    string? Note);

internal readonly record struct HelpSection(
    string Title,
    IReadOnlyList<(string Command, string Description)> Entries);

internal readonly record struct HelpDocument(
    HelpHeader Header,
    string Usage,
    string OptionsTitle,
    IReadOnlyList<(string Option, string Description)> Options,
    IReadOnlyList<HelpSection> Sections,
    IReadOnlyList<string> Examples,
    string Next);
