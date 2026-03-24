using System;

namespace Meta.Core.Services;

public sealed class WorkspaceConflictException : InvalidOperationException
{
    public WorkspaceConflictException(string message, string expectedFingerprint, string actualFingerprint)
        : base(message)
    {
        ExpectedFingerprint = expectedFingerprint ?? string.Empty;
        ActualFingerprint = actualFingerprint ?? string.Empty;
    }

    public string ExpectedFingerprint { get; }
    public string ActualFingerprint { get; }
}
