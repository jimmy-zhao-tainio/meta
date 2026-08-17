using System.Diagnostics;
using System.Globalization;

namespace MetaCli.Core;

/// <summary>
/// Renders a single interactive progress meter. Callers own the meaning of the
/// counts and detail; MetaCLI owns the console behavior and visual form.
/// </summary>
public sealed class MetaCliProgressMeter : IDisposable
{
    private const int RailWidth = 20;
    private readonly object sync = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly MetaCliProgressLiveLine liveLine;
    private readonly string label;
    private int completed;
    private int total;
    private string? detail;
    private bool disposed;

    private MetaCliProgressMeter(
        string label,
        string? initialDetail,
        TimeSpan? delay)
    {
        this.label = NormalizeRequired(label, nameof(label));
        detail = NormalizeOptional(initialDetail);
        liveLine = new MetaCliProgressLiveLine(
            BuildRunningLine,
            delay ?? TimeSpan.FromMilliseconds(180));
    }

    /// <summary>
    /// Starts an interactive meter, or returns <see langword="null"/> when the
    /// process is not attached to a console suitable for live output.
    /// </summary>
    public static MetaCliProgressMeter? TryStart(
        string label = "Progress",
        string? initialDetail = null,
        TimeSpan? delay = null)
    {
        _ = NormalizeRequired(label, nameof(label));
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return null;
        }

        return new MetaCliProgressMeter(label, initialDetail, delay);
    }

    /// <summary>Reports the latest completed and total task counts.</summary>
    public void Report(int completed, int total, string? detail = null)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            this.total = Math.Max(0, total);
            this.completed = this.total == 0
                ? 0
                : Math.Clamp(completed, 0, this.total);
            this.detail = NormalizeOptional(detail);
        }
    }

    /// <summary>Finishes the meter with a successful outcome.</summary>
    public void Succeed(string? detail = null) => Complete(succeeded: true, detail);

    /// <summary>Finishes the meter with a failed outcome.</summary>
    public void Fail(string? detail = null) => Complete(succeeded: false, detail);

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        liveLine.Clear();
    }

    private void Complete(bool succeeded, string? finalDetail)
    {
        int completedSnapshot;
        int totalSnapshot;
        string? detailSnapshot;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (succeeded && total > 0)
            {
                completed = total;
            }

            completedSnapshot = completed;
            totalSnapshot = total;
            detailSnapshot = NormalizeOptional(finalDetail);
            if (!succeeded && detailSnapshot is null)
            {
                detailSnapshot = detail;
            }
        }

        liveLine.Complete(MetaCliProgressFormatter.BuildOutcome(
            label,
            completedSnapshot,
            totalSnapshot,
            succeeded,
            detailSnapshot,
            stopwatch.Elapsed,
            RailWidth));
    }

    private string BuildRunningLine(char spinnerFrame)
    {
        lock (sync)
        {
            return MetaCliProgressFormatter.BuildRunning(
                label,
                completed,
                total,
                detail,
                spinnerFrame,
                RailWidth);
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Progress label cannot be blank.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class MetaCliProgressFormatter
{
    public static string BuildRunning(
        string label,
        int completed,
        int total,
        string? detail,
        char spinnerFrame,
        int railWidth)
    {
        var segments = new List<string>
        {
            total > 0
                ? $"{label} {BuildRunningRail(completed, total, spinnerFrame, railWidth)} {FormatCounts(completed, total)}"
                : $"{label} [{spinnerFrame}{new string('-', Math.Max(1, railWidth) - 1)}] --/--"
        };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            segments.Add(detail.Trim());
        }

        return string.Join("  ", segments);
    }

    public static string BuildOutcome(
        string label,
        int completed,
        int total,
        bool succeeded,
        string? detail,
        TimeSpan elapsed,
        int railWidth)
    {
        var segments = new List<string>();
        if (total > 0)
        {
            var outcomeCompleted = succeeded ? total : Math.Clamp(completed, 0, total);
            segments.Add($"{label} {BuildStaticRail(outcomeCompleted, total, railWidth)} {FormatCounts(outcomeCompleted, total)}");
        }
        else
        {
            segments.Add(label);
        }

        segments.Add(succeeded ? "OK" : "FAIL");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            segments.Add(detail.Trim());
        }

        segments.Add(FormatElapsed(elapsed));
        return string.Join("  ", segments);
    }

    private static string BuildRunningRail(
        int completed,
        int total,
        char spinnerFrame,
        int width)
    {
        var safeTotal = Math.Max(1, total);
        var safeWidth = Math.Min(Math.Max(1, width), safeTotal);
        var safeCompleted = Math.Clamp(completed, 0, safeTotal);
        if (safeCompleted >= safeTotal)
        {
            return $"[{new string('=', safeWidth)}]";
        }

        var frontier = Math.Clamp(
            (int)Math.Floor(safeCompleted * safeWidth / (double)safeTotal),
            0,
            safeWidth - 1);
        return $"[{new string('=', frontier)}{spinnerFrame}{new string('-', safeWidth - frontier - 1)}]";
    }

    private static string BuildStaticRail(int completed, int total, int width)
    {
        var safeTotal = Math.Max(1, total);
        var safeWidth = Math.Min(Math.Max(1, width), safeTotal);
        var safeCompleted = Math.Clamp(completed, 0, safeTotal);
        var filled = safeCompleted >= safeTotal
            ? safeWidth
            : (int)Math.Floor(safeCompleted * safeWidth / (double)safeTotal);
        return $"[{new string('=', filled)}{new string('-', safeWidth - filled)}]";
    }

    private static string FormatCounts(int completed, int total)
    {
        return $"{Math.Clamp(completed, 0, total).ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}

internal sealed class MetaCliProgressLiveLine : IDisposable
{
    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];
    private static readonly object ConsoleSync = new();
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly Func<char, string> readoutFactory;
    private readonly TimeSpan delay;
    private readonly Thread renderThread;
    private readonly bool previousCursorVisible;
    private readonly bool cursorVisibilityChanged;
    private int renderedLength;
    private bool rendered;
    private bool disposed;

    public MetaCliProgressLiveLine(Func<char, string> readoutFactory, TimeSpan delay)
    {
        this.readoutFactory = readoutFactory ?? throw new ArgumentNullException(nameof(readoutFactory));
        this.delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        (previousCursorVisible, cursorVisibilityChanged) = HideCursor();
        renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "meta-cli-progress-meter",
            Priority = ThreadPriority.AboveNormal,
        };
        renderThread.Start();
    }

    public void Complete(string line)
    {
        var state = Stop();
        if (!state.Stopped)
        {
            return;
        }

        WriteFinalLine(line, state);
    }

    public void Clear()
    {
        var state = Stop();
        if (!state.Stopped)
        {
            return;
        }

        lock (ConsoleSync)
        {
            if (state.Rendered)
            {
                Console.Error.Write('\r');
                Console.Error.Write(new string(' ', state.RenderedLength));
                Console.Error.Write('\r');
            }

            RestoreCursor();
        }
    }

    public void Dispose() => Clear();

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
                cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(16));
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

            var frameIndex = (int)(stopwatch.ElapsedTicks / Math.Max(1, Stopwatch.Frequency / 30L)) % SpinnerFrames.Length;
            line = readoutFactory(SpinnerFrames[frameIndex]).Trim();
        }

        var width = GetConsoleWidth();
        if (line.Length > width)
        {
            line = line[..Math.Max(0, width - 1)];
        }

        lock (ConsoleSync)
        {
            Console.Error.Write('\r');
            Console.Error.Write(line);
            if (renderedLength > line.Length)
            {
                Console.Error.Write(new string(' ', renderedLength - line.Length));
            }

            renderedLength = line.Length;
            rendered = true;
        }
    }

    private void WriteFinalLine(string line, StopState state)
    {
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
            else if (normalizedLine.Length > 0)
            {
                Console.Error.WriteLine(normalizedLine);
            }

            RestoreCursor();
        }
    }

    private StopState Stop()
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

    private static (bool Previous, bool Changed) HideCursor()
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

    private readonly record struct StopState(bool Stopped, bool Rendered, int RenderedLength);
}
