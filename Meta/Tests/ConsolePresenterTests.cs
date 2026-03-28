using System;
using System.IO;
using Meta.Core.Presentation;

namespace Meta.Core.Tests;

public sealed class ConsolePresenterTests
{
    private static readonly object ConsoleSync = new();

    [Fact]
    public void WriteOk_DoesNotAppendBlankLine_AfterSummaryBlock()
    {
        var output = Capture(presenter => presenter.WriteOk("created workspace"));

        Assert.Equal("OK: created workspace" + Environment.NewLine, output);
    }

    [Fact]
    public void WriteFailure_DoesNotAppendBlankLine_AfterNext()
    {
        var output = Capture(presenter => presenter.WriteFailure("could not load workspace", new[] { "Next: meta help" }));

        Assert.Equal(
            "Error: could not load workspace" + Environment.NewLine +
            "Next: meta help" + Environment.NewLine,
            output);
    }

    [Fact]
    public void WriteWarning_DoesNotAppendBlankLine()
    {
        var output = Capture(presenter => presenter.WriteWarning("using default workspace"));

        Assert.Equal("Warning: using default workspace" + Environment.NewLine, output);
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
