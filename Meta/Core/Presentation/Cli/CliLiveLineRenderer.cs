using System.Diagnostics;

namespace Meta.Core.Presentation.Cli;

public sealed class CliLiveLineRenderer : IDisposable
{
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMilliseconds(16);
    private static readonly object ConsoleSync = new();

    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Func<string> readoutFactory;
    private readonly TimeSpan delay;
    private readonly TimeSpan refreshInterval;
    private readonly Thread renderThread;
    private int renderedLength;
    private bool rendered;
    private bool disposed;

    private CliLiveLineRenderer(
        Func<string> readoutFactory,
        TimeSpan delay,
        TimeSpan refreshInterval)
    {
        this.readoutFactory = readoutFactory ?? throw new ArgumentNullException(nameof(readoutFactory));
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        this.refreshInterval = refreshInterval <= TimeSpan.Zero ? DefaultRefreshInterval : refreshInterval;
        renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "meta-cli-live-line",
            Priority = ThreadPriority.AboveNormal,
        };
        renderThread.Start();
    }

    public static CliLiveLineRenderer? TryStart(
        Func<string> readoutFactory,
        TimeSpan? delay = null,
        TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(readoutFactory);

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return null;
        }

        return new CliLiveLineRenderer(
            readoutFactory,
            delay ?? DefaultDelay,
            refreshInterval ?? DefaultRefreshInterval);
    }

    public void Dispose()
    {
        var state = StopRenderLoop();
        if (!state.Stopped)
        {
            return;
        }

        lock (ConsoleSync)
        {
            if (state.Rendered)
            {
                Console.Error.WriteLine();
            }
        }
    }

    public void Complete(string line)
    {
        var state = StopRenderLoop();
        if (!state.Stopped)
        {
            return;
        }

        var normalizedLine = line?.Trim() ?? string.Empty;
        lock (ConsoleSync)
        {
            if (state.Rendered)
            {
                Console.Error.Write('\r');
                Console.Error.Write(normalizedLine);
                if (state.RenderedLength > normalizedLine.Length)
                {
                    Console.Error.Write(new string(' ', state.RenderedLength - normalizedLine.Length));
                }

                Console.Error.WriteLine();
            }
            else if (!string.IsNullOrWhiteSpace(normalizedLine))
            {
                Console.Error.WriteLine(normalizedLine);
            }
        }
    }

    public void Clear()
    {
        var state = StopRenderLoop();
        if (!state.Stopped || !state.Rendered)
        {
            return;
        }

        lock (ConsoleSync)
        {
            Console.Error.Write('\r');
            Console.Error.Write(new string(' ', state.RenderedLength));
            Console.Error.Write('\r');
        }
    }

    private void RenderLoop()
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (stopwatch.Elapsed >= delay)
            {
                Render();
            }

            try
            {
                cancellation.Token.WaitHandle.WaitOne(refreshInterval);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Render()
    {
        string readout;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            readout = readoutFactory();
        }

        var frameIndex = (int)(stopwatch.ElapsedTicks / Math.Max(1, Stopwatch.Frequency / 60L)) % SpinnerFrames.Length;
        var line = BuildLine(SpinnerFrames[frameIndex], readout);
        var width = GetConsoleWidth();
        if (line.Length > width)
        {
            line = line[..Math.Max(0, width - 1)];
        }

        lock (ConsoleSync)
        {
            var padding = renderedLength > line.Length
                ? new string(' ', renderedLength - line.Length)
                : string.Empty;
            Console.Error.Write('\r');
            Console.Error.Write(line);
            Console.Error.Write(padding);
            renderedLength = line.Length;
            rendered = true;
        }
    }

    private static string BuildLine(char frame, string? readout)
    {
        var normalizedReadout = readout?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedReadout)
            ? frame.ToString()
            : $"{frame}  {normalizedReadout}";
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Math.Max(20, Console.WindowWidth - 1);
        }
        catch (IOException)
        {
            return 120;
        }
        catch (InvalidOperationException)
        {
            return 120;
        }
    }

    private StopState StopRenderLoop()
    {
        lock (sync)
        {
            if (disposed)
            {
                return new StopState(false, rendered, renderedLength);
            }

            disposed = true;
            cancellation.Cancel();
        }

        if (!renderThread.Join(TimeSpan.FromMilliseconds(250)))
        {
            renderThread.Join(TimeSpan.FromMilliseconds(50));
        }

        cancellation.Dispose();
        return new StopState(true, rendered, renderedLength);
    }

    private readonly record struct StopState(bool Stopped, bool Rendered, int RenderedLength);
}
