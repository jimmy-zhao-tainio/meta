using System.Diagnostics;

namespace Meta.Core.Presentation.Cli;

public sealed class CliActivityLine : IDisposable
{
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMilliseconds(16);
    private static readonly object ConsoleSync = new();

    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly string caption;
    private readonly TimeSpan delay;
    private readonly TimeSpan refreshInterval;
    private readonly bool interactive;
    private readonly Thread? renderThread;
    private int renderedLength;
    private bool rendered;
    private bool completed;
    private bool disposed;

    private CliActivityLine(
        string caption,
        TimeSpan delay,
        TimeSpan refreshInterval,
        bool interactive)
    {
        this.caption = NormalizeCaption(caption);
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        this.refreshInterval = refreshInterval <= TimeSpan.Zero ? DefaultRefreshInterval : refreshInterval;
        this.interactive = interactive;

        if (!interactive)
        {
            return;
        }

        renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "meta-cli-activity-line",
            Priority = ThreadPriority.Highest,
        };
        renderThread.Start();
    }

    public static CliActivityLine Start(
        string caption,
        TimeSpan? delay = null,
        TimeSpan? refreshInterval = null)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            throw new ArgumentException("Activity caption cannot be blank.", nameof(caption));
        }

        return new CliActivityLine(
            caption,
            delay ?? DefaultDelay,
            refreshInterval ?? DefaultRefreshInterval,
            interactive: !Console.IsOutputRedirected);
    }

    public static void WriteCompleted(string caption, string result = "Ok")
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            throw new ArgumentException("Activity caption cannot be blank.", nameof(caption));
        }

        Console.Out.WriteLine(BuildLine(NormalizeCaption(caption), NormalizeResult(result)));
    }

    public void Succeed(string result = "Ok")
    {
        Complete(NormalizeResult(result));
    }

    public void Complete(string result)
    {
        var renderedResult = NormalizeResult(result);
        lock (sync)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            disposed = true;
            cancellation.Cancel();
        }

        JoinRenderThread();
        cancellation.Dispose();

        var line = BuildLine(caption, renderedResult);
        lock (ConsoleSync)
        {
            if (interactive && rendered)
            {
                Console.Out.Write('\r');
                Console.Out.Write(line);
                ClearRemainder(line.Length);
                Console.Out.WriteLine();
                return;
            }

            Console.Out.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Cancel();
        }

        JoinRenderThread();
        cancellation.Dispose();

        lock (ConsoleSync)
        {
            if (interactive && rendered)
            {
                Console.Out.Write('\r');
                ClearRemainder(0);
                Console.Out.Write('\r');
            }
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
        lock (sync)
        {
            if (disposed || completed)
            {
                return;
            }
        }

        var frameIndex = (int)(stopwatch.ElapsedTicks / Math.Max(1, Stopwatch.Frequency / 60L)) % SpinnerFrames.Length;
        var line = BuildLine(caption, SpinnerFrames[frameIndex].ToString());
        var width = GetConsoleWidth();
        if (line.Length > width)
        {
            line = line[..Math.Max(0, width - 1)];
        }

        lock (ConsoleSync)
        {
            Console.Out.Write('\r');
            Console.Out.Write(line);
            ClearRemainder(line.Length);
            renderedLength = line.Length;
            rendered = true;
        }
    }

    private void JoinRenderThread()
    {
        if (renderThread is null)
        {
            return;
        }

        if (!renderThread.Join(TimeSpan.FromMilliseconds(250)))
        {
            renderThread.Join(TimeSpan.FromMilliseconds(50));
        }
    }

    private void ClearRemainder(int newLength)
    {
        if (renderedLength > newLength)
        {
            Console.Out.Write(new string(' ', renderedLength - newLength));
        }
    }

    private static string BuildLine(string caption, string suffix) => $"{caption}...{suffix}";

    private static string NormalizeCaption(string value)
    {
        var normalized = value.Trim();
        while (normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized;
    }

    private static string NormalizeResult(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Ok"
            : value.Trim();

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
}
