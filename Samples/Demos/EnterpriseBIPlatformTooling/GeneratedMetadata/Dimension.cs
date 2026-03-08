namespace EnterpriseBIPlatform
{
    public sealed class Dimension
    {
        public string Id { get; internal set; } = string.Empty;
        public string DimensionName { get; internal set; } = string.Empty;
        public string HierarchyCount { get; internal set; } = string.Empty;
        public string IsConformed { get; internal set; } = string.Empty;
    }
}
