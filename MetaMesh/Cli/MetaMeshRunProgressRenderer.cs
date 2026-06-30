using System.Globalization;

internal sealed class MetaMeshRunProgressRenderer : IDisposable
{
    private const int RailWidth = 20;
    private readonly object sync = new();
    private readonly MetaMeshLiveLineRenderer liveLine;
    private string currentStep = "starting";
    private int totalSteps;
    private int completedSteps;
    private bool disposed;

    private MetaMeshRunProgressRenderer()
    {
        liveLine = MetaMeshLiveLineRenderer.TryStart(
                BuildReadout,
                delay: TimeSpan.FromMilliseconds(180))
            ?? throw new InvalidOperationException("Console live-line renderer is not available.");
    }

    public static MetaMeshRunProgressRenderer? TryCreate()
    {
        if (Console.IsErrorRedirected || Console.IsOutputRedirected)
        {
            return null;
        }

        return new MetaMeshRunProgressRenderer();
    }

    public void StepStarted(int index, int total, string name)
    {
        lock (sync)
        {
            totalSteps = Math.Max(total, 1);
            completedSteps = Math.Clamp(index - 1, 0, totalSteps);
            currentStep = NormalizeStepName(name);
        }
    }

    public void StepCompleted(string name, bool succeeded)
    {
        lock (sync)
        {
            currentStep = NormalizeStepName(name);
            if (succeeded)
            {
                completedSteps = Math.Clamp(completedSteps + 1, 0, Math.Max(totalSteps, 1));
            }
        }
    }

    public void Complete(bool failed)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        liveLine.Complete(failed ? BuildFailureReadout() : BuildCompletionReadout());
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
        }

        liveLine.Dispose();
    }

    private string BuildReadout(char spinnerFrame)
    {
        lock (sync)
        {
            if (totalSteps <= 0)
            {
                return currentStep;
            }

            var countWidth = CountWidth(totalSteps);
            return string.Join("  ", new[]
            {
                $"Progress {BuildProgressRail(completedSteps, totalSteps, RailWidth, spinnerFrame)} {FormatCount(completedSteps, countWidth)}/{FormatCount(totalSteps, countWidth)}",
                $"Step {currentStep}"
            });
        }
    }

    private string BuildCompletionReadout()
    {
        lock (sync)
        {
            var safeTotal = Math.Max(totalSteps, completedSteps);
            var countWidth = CountWidth(safeTotal);
            return string.Join("  ", new[]
            {
                $"Progress {BuildProgressRail(completedSteps, safeTotal, RailWidth)} {FormatCount(completedSteps, countWidth)}/{FormatCount(safeTotal, countWidth)}",
                "OK"
            });
        }
    }

    private string BuildFailureReadout()
    {
        lock (sync)
        {
            var safeTotal = Math.Max(totalSteps, 1);
            var countWidth = CountWidth(safeTotal);
            return string.Join("  ", new[]
            {
                $"Progress {BuildProgressRail(completedSteps, safeTotal, RailWidth)} {FormatCount(completedSteps, countWidth)}/{FormatCount(safeTotal, countWidth)}",
                $"FAIL {currentStep}"
            });
        }
    }

    private static string BuildProgressRail(int completed, int total, int width)
    {
        var safeTotal = Math.Max(1, total);
        var safeWidth = Math.Max(1, width);
        var safeCompleted = Math.Clamp(completed, 0, safeTotal);
        var filled = safeCompleted >= safeTotal
            ? safeWidth
            : (int)Math.Floor(safeCompleted * safeWidth / (double)safeTotal);
        return $"[{new string('#', filled)}{new string('.', safeWidth - filled)}]";
    }

    private static string BuildProgressRail(int completed, int total, int width, char spinnerFrame)
    {
        var safeTotal = Math.Max(1, total);
        var safeWidth = Math.Max(1, width);
        var safeCompleted = Math.Clamp(completed, 0, safeTotal);
        if (safeCompleted >= safeTotal)
        {
            return BuildProgressRail(safeCompleted, safeTotal, safeWidth);
        }

        var spinnerIndex = (int)Math.Floor(safeCompleted * safeWidth / (double)safeTotal);
        spinnerIndex = Math.Clamp(spinnerIndex, 0, safeWidth - 1);
        return $"[{new string('#', spinnerIndex)}{spinnerFrame}{new string('.', safeWidth - spinnerIndex - 1)}]";
    }

    private static string NormalizeStepName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "working" : value.Trim();

    private static int CountWidth(int maxValue) =>
        Math.Max(2, Math.Max(1, maxValue).ToString(CultureInfo.InvariantCulture).Length);

    private static string FormatCount(int value, int width) =>
        Math.Max(0, value).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, width));
}
