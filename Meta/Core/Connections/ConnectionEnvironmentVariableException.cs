namespace Meta.Core.Connections;

public enum ConnectionEnvironmentVariableFailureKind
{
    Missing,
    Empty,
}

public sealed class ConnectionEnvironmentVariableException : InvalidOperationException
{
    public ConnectionEnvironmentVariableException(
        string environmentVariableName,
        ConnectionEnvironmentVariableFailureKind failureKind)
        : base(BuildMessage(environmentVariableName, failureKind))
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            throw new ArgumentException("Connection environment variable name is required.", nameof(environmentVariableName));
        }

        EnvironmentVariableName = environmentVariableName.Trim();
        FailureKind = failureKind;
    }

    public string EnvironmentVariableName { get; }

    public ConnectionEnvironmentVariableFailureKind FailureKind { get; }

    private static string BuildMessage(
        string environmentVariableName,
        ConnectionEnvironmentVariableFailureKind failureKind)
    {
        var reference = ConnectionEnvironmentVariableResolver.FormatReference(environmentVariableName);
        return failureKind switch
        {
            ConnectionEnvironmentVariableFailureKind.Missing => $"{reference} was not found.",
            ConnectionEnvironmentVariableFailureKind.Empty => $"{reference} is defined but empty.",
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
        };
    }
}
