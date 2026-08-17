using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class MetaCliActivityIndicatorTests
{
    [Fact]
    public void ActivityUsesTheSharedSpinnerFrame()
    {
        Assert.Equal("Starting.../", MetaCliActivityFormatter.BuildLine("Starting", '/'));
    }

    [Fact]
    public void HelpRequestsAreRecognizedBeforeHelpWritesOutput()
    {
        Assert.True(MetaCliHelpService.IsHelpRequest([]));
        Assert.True(MetaCliHelpService.IsHelpRequest(["help"]));
        Assert.True(MetaCliHelpService.IsHelpRequest(["help", "execute"]));
        Assert.True(MetaCliHelpService.IsHelpRequest(["execute", "--help"]));
    }

    [Fact]
    public void OrdinaryCommandsAreNotHelpRequests()
    {
        Assert.False(MetaCliHelpService.IsHelpRequest(["execute", "--workspace", "Weave"]));
    }
}
