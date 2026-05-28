namespace EnterpriseBIPlatform
{
    public sealed class Fact
    {
        public string Id { get; internal set; } = string.Empty;
        public string BusinessArea { get; internal set; } = string.Empty;
        public string FactName { get; internal set; } = string.Empty;
        public string Grain { get; internal set; } = string.Empty;
        public string MeasureCount { get; internal set; } = string.Empty;
    }
}
