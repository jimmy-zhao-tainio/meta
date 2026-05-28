using Meta.Core.Connections;

namespace Meta.Core.Tests;

public sealed class ConnectionEnvironmentVariableResolverTests
{
    [Fact]
    public void ResolveRequired_ReturnsValue_WhenEnvironmentVariableExists()
    {
        var environmentVariableName = "META_TEST_CONNECTION_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var expectedValue = "Server=.;Database=TestDb;Integrated Security=true;";

        var restore = CaptureEnvironmentVariable(environmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(environmentVariableName, expectedValue);

            var actual = ConnectionEnvironmentVariableResolver.ResolveRequired(environmentVariableName);

            Assert.Equal(expectedValue, actual);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void ResolveRequired_ThrowsClearError_WhenEnvironmentVariableIsMissing()
    {
        var environmentVariableName = "META_TEST_CONNECTION_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        var restore = CaptureEnvironmentVariable(environmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);

            var exception = Assert.Throws<ConnectionEnvironmentVariableException>(
                () => ConnectionEnvironmentVariableResolver.ResolveRequired(environmentVariableName));

            Assert.Equal(ConnectionEnvironmentVariableFailureKind.Missing, exception.FailureKind);
            Assert.Equal(environmentVariableName, exception.EnvironmentVariableName);
            Assert.Equal($"Connection environment variable '{environmentVariableName}' was not found.", exception.Message);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void ResolveRequired_ThrowsClearError_WhenEnvironmentVariableIsWhitespace()
    {
        var environmentVariableName = "META_TEST_CONNECTION_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        var restore = CaptureEnvironmentVariable(environmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(environmentVariableName, "   ");

            var exception = Assert.Throws<ConnectionEnvironmentVariableException>(
                () => ConnectionEnvironmentVariableResolver.ResolveRequired(environmentVariableName));

            Assert.Equal(ConnectionEnvironmentVariableFailureKind.Empty, exception.FailureKind);
            Assert.Equal(environmentVariableName, exception.EnvironmentVariableName);
            Assert.Equal($"Connection environment variable '{environmentVariableName}' is defined but empty.", exception.Message);
        }
        finally
        {
            restore();
        }
    }

    private static Action CaptureEnvironmentVariable(string name)
    {
        var originalValue = Environment.GetEnvironmentVariable(name);
        return () => Environment.SetEnvironmentVariable(name, originalValue);
    }
}
