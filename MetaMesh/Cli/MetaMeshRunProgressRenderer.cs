using MetaCli.Core;

internal sealed class MetaMeshRunProgressRenderer : IDisposable
{
    private readonly MetaCliProgressMeter meter;
    private int totalSteps;
    private int completedSteps;
    private string currentStep = "starting";
    private bool disposed;

    private MetaMeshRunProgressRenderer(MetaCliProgressMeter meter)
    {
        this.meter = meter;
        meter.Report(0, 0, currentStep);
    }

    public static MetaMeshRunProgressRenderer? TryCreate()
    {
        var meter = MetaCliProgressMeter.TryStart(initialDetail: "starting");
        return meter is null ? null : new MetaMeshRunProgressRenderer(meter);
    }

    public void StepStarted(int index, int total, string name)
    {
        totalSteps = Math.Max(total, 1);
        completedSteps = Math.Clamp(index - 1, 0, totalSteps);
        currentStep = NormalizeStepName(name);
        meter.Report(completedSteps, totalSteps, $"Step {currentStep}");
    }

    public void StepCompleted(string name, bool succeeded)
    {
        currentStep = NormalizeStepName(name);
        if (succeeded)
        {
            completedSteps = Math.Clamp(completedSteps + 1, 0, Math.Max(totalSteps, 1));
        }

        meter.Report(completedSteps, totalSteps, $"Step {currentStep}");
    }

    public void Complete(bool failed)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (failed)
        {
            meter.Fail($"Step {currentStep}");
        }
        else
        {
            meter.Succeed();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        meter.Dispose();
    }

    private static string NormalizeStepName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "working" : value.Trim();
}
