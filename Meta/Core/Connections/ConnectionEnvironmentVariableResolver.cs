namespace Meta.Core.Connections;

public static class ConnectionEnvironmentVariableResolver
{
    public static string ResolveRequired(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            throw new ArgumentException("Connection environment variable name is required.", nameof(environmentVariableName));
        }

        var normalizedName = environmentVariableName.Trim();
        var value = Environment.GetEnvironmentVariable(normalizedName);
        if (value is null)
        {
            throw new ConnectionEnvironmentVariableException(
                normalizedName,
                ConnectionEnvironmentVariableFailureKind.Missing);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConnectionEnvironmentVariableException(
                normalizedName,
                ConnectionEnvironmentVariableFailureKind.Empty);
        }

        return value;
    }

    public static string FormatReference(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            return "Connection environment variable";
        }

        return $"Connection environment variable '{environmentVariableName.Trim()}'";
    }
}
