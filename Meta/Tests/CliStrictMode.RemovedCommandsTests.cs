using System;
using System.Threading.Tasks;

namespace Meta.Core.Tests;

public sealed partial class CliStrictModeTests
{
    [Fact]
    public async Task RandomCreate_IsNotExposed_OnCliSurface()
    {
        var result = await RunCliAsync("random", "create");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'random'.", result.CombinedOutput, StringComparison.Ordinal);
    }
}
