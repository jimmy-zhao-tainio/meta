using System;
using System.IO;
using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;

namespace Meta.Core.Tests;

public sealed class ConsolePresenterTests
{
    private static readonly object ConsoleSync = new();

    [Fact]
    public void WriteOk_DoesNotAppendBlankLine_AfterSummaryBlock()
    {
        var output = Capture(presenter => presenter.WriteOk("created workspace"));

        Assert.Equal("Ok" + Environment.NewLine, output);
    }

    [Fact]
    public void WriteFailure_DoesNotAppendBlankLine_AfterNext()
    {
        var output = Capture(presenter => presenter.WriteFailure("could not load workspace", new[] { "Next: meta help" }));

        Assert.Equal(
            "Cannot continue." + Environment.NewLine +
            Environment.NewLine +
            "could not load workspace" + Environment.NewLine +
            Environment.NewLine +
            "Next: meta help" + Environment.NewLine,
            output);
    }

    [Fact]
    public void WriteCannotContinue_RendersIntentDetailsAndNext()
    {
        var output = Capture(presenter => presenter.WriteCannotContinue(
            "Cannot build run plan",
            new[] { "dbo.Customer has ambiguous writes." },
            "add an explicit order"));

        Assert.Equal(
            "Cannot build run plan." + Environment.NewLine +
            Environment.NewLine +
            "dbo.Customer has ambiguous writes." + Environment.NewLine +
            Environment.NewLine +
            "Next: add an explicit order" + Environment.NewLine,
            output);
    }

    [Fact]
    public void WriteWarning_DoesNotAppendBlankLine()
    {
        var output = Capture(presenter => presenter.WriteWarning("using default workspace"));

        Assert.Equal("Warning: using default workspace" + Environment.NewLine, output);
    }

    [Fact]
    public void ActivityLine_WriteCompleted_RendersOneVerbCompletion()
    {
        var output = Capture(_ => CliActivityLine.WriteCompleted("Binding"));

        Assert.Equal("Binding...Ok" + Environment.NewLine, output);
    }

    [Fact]
    public void ActivityLine_Succeed_RendersOneVerbCompletion()
    {
        var output = Capture(_ =>
        {
            using var activity = CliActivityLine.Start("Importing", delay: TimeSpan.FromDays(1));
            activity.Succeed();
        });

        Assert.Equal("Importing...Ok" + Environment.NewLine, output);
    }

    private static string Capture(Action<ConsolePresenter> render)
    {
        lock (ConsoleSync)
        {
            var presenter = new ConsolePresenter();
            var writer = new StringWriter();
            var originalOut = Console.Out;

            try
            {
                Console.SetOut(writer);
                render(presenter);
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
