using System.Diagnostics;

internal sealed class MetaMeshLiveLineRenderer : IDisposable
{
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];
    private static readonly object ConsoleSync = new();
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Func<char, string> readoutFactory;
    private readonly TimeSpan delay;
    private readonly TimeSpan refreshInterval;
    private readonly Thread renderThread;
    private readonly bool previousCursorVisible;
    private readonly bool cursorVisibilityChanged;
    private int renderedLength;
    private bool rendered;
    private bool disposed;

    private MetaMeshLiveLineRenderer(
        Func<char, string> readoutFactory,
        TimeSpan delay,
        TimeSpan refreshInterval)
    {
        this.readoutFactory = readoutFactory ?? throw new ArgumentNullException(nameof(readoutFactory));
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        this.refreshInterval = refreshInterval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(16)
            : refreshInterval;
        (previousCursorVisible, cursorVisibilityChanged) = HideCursor();
        renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "meta-mesh-live-line",
            Priority = ThreadPriority.AboveNormal,
        };
        renderThread.Start();
    }

    public static MetaMeshLiveLineRenderer? TryStart(
        Func<char, string> readoutFactory,
        TimeSpan? delay = null,
        TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(readoutFactory);

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return null;
        }

        return new MetaMeshLiveLineRenderer(
            readoutFactory,
            delay ?? TimeSpan.FromMilliseconds(350),
            refreshInterval ?? TimeSpan.FromMilliseconds(16));
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

            RestoreCursor();
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

            RestoreCursor();
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
        string line;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            line = readoutFactory(ResolveSpinnerFrame()).Trim();
        }

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

    private char ResolveSpinnerFrame()
    {
        var frameIndex = (int)(stopwatch.ElapsedTicks / Math.Max(1, Stopwatch.Frequency / 60L)) % SpinnerFrames.Length;
        return SpinnerFrames[frameIndex];
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

    private static (bool PreviousCursorVisible, bool Changed) HideCursor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, false);
        }

        try
        {
            var previous = Console.CursorVisible;
            Console.CursorVisible = false;
            return (previous, true);
        }
        catch (IOException)
        {
            return (false, false);
        }
        catch (InvalidOperationException)
        {
            return (false, false);
        }
        catch (PlatformNotSupportedException)
        {
            return (false, false);
        }
    }

    private void RestoreCursor()
    {
        if (!cursorVisibilityChanged || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Console.CursorVisible = previousCursorVisible;
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (PlatformNotSupportedException)
        {
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
