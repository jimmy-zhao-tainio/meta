using System.Collections.Generic;

internal readonly record struct ErrorDocument(
    string Message,
    string? Usage,
    IReadOnlyList<string> Details,
    string Next);
