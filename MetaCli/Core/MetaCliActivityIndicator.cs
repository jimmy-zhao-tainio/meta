namespace MetaCli.Core;

/// <summary>
/// Renders delayed indeterminate activity until ownership passes to command
/// output or a determinate progress meter.
/// </summary>
public sealed class MetaCliActivityIndicator : IDisposable
{
    private readonly MetaCliProgressLiveLine liveLine;

    private MetaCliActivityIndicator(string label, TimeSpan delay)
    {
        liveLine = new MetaCliProgressLiveLine(
            frame => MetaCliActivityFormatter.BuildLine(label, frame),
            delay);
    }

    /// <summary>
    /// Starts interactive activity, or returns <see langword="null"/> when the
    /// process is not attached to a console suitable for live output.
    /// </summary>
    public static MetaCliActivityIndicator? TryStart(
        string label = "Starting",
        TimeSpan? delay = null)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Activity label cannot be blank.", nameof(label));
        }

        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            return null;
        }

        return new MetaCliActivityIndicator(
            label.Trim(),
            delay ?? TimeSpan.FromMilliseconds(350));
    }

    public void Dispose() => liveLine.Clear();
}

internal static class MetaCliActivityFormatter
{
    public static string BuildLine(string label, char spinnerFrame) =>
        $"{label}...{spinnerFrame}";
}
