using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class MetaCliProgressMeterTests
{
    [Fact]
    public void RunningMeterPlacesTheSpinnerAtTheCompletedFrontier()
    {
        var line = MetaCliProgressFormatter.BuildRunning(
            "Progress",
            4,
            29,
            "relation render_events",
            '/',
            20);

        Assert.Equal("Progress [==/-----------------] 4/29  relation render_events", line);
    }

    [Fact]
    public void RunningMeterRepresentsUnknownWorkWithoutInventingAPercentage()
    {
        var line = MetaCliProgressFormatter.BuildRunning(
            "Progress",
            0,
            0,
            "preparing",
            '/',
            20);

        Assert.Equal("Progress [/-------------------] --/--  preparing", line);
    }

    [Theory]
    [InlineData(0, "Progress [/---] 0/4  working")]
    [InlineData(1, "Progress [=/--] 1/4  working")]
    [InlineData(2, "Progress [==/-] 2/4  working")]
    [InlineData(3, "Progress [===/] 3/4  working")]
    [InlineData(4, "Progress [====] 4/4  working")]
    public void SmallTaskCountsUseOneRailCellPerTask(int completed, string expected)
    {
        var line = MetaCliProgressFormatter.BuildRunning(
            "Progress",
            completed,
            4,
            "working",
            '/',
            20);

        Assert.Equal(expected, line);
    }

    [Fact]
    public void SuccessfulOutcomeCompletesTheRailAndReportsElapsedTime()
    {
        var line = MetaCliProgressFormatter.BuildOutcome(
            "Progress",
            4,
            29,
            succeeded: true,
            detail: null,
            TimeSpan.FromSeconds(7),
            20);

        Assert.Equal("Progress [====================] 29/29  OK  00:07", line);
    }

    [Fact]
    public void SmallSuccessfulOutcomeKeepsTheDiscreteRailWidth()
    {
        var line = MetaCliProgressFormatter.BuildOutcome(
            "Progress",
            4,
            4,
            succeeded: true,
            detail: null,
            TimeSpan.FromSeconds(1),
            20);

        Assert.Equal("Progress [====] 4/4  OK  00:01", line);
    }

    [Fact]
    public void FailedOutcomePreservesTheLastCompletedCountAndDetail()
    {
        var line = MetaCliProgressFormatter.BuildOutcome(
            "Progress",
            4,
            29,
            succeeded: false,
            "relation render_events",
            TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(2),
            20);

        Assert.Equal("Progress [==------------------] 4/29  FAIL  relation render_events  01:02", line);
    }
}
