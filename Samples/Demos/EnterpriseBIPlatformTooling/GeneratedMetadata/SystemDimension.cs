namespace EnterpriseBIPlatform
{
    public sealed class SystemDimension
    {
        public string Id { get; internal set; } = string.Empty;
        public string ConformanceLevel { get; internal set; } = string.Empty;
        public string DimensionId { get; internal set; } = string.Empty;
        public Dimension Dimension { get; internal set; } = new Dimension();
        public string SystemId { get; internal set; } = string.Empty;
        public System System { get; internal set; } = new System();
    }
}
