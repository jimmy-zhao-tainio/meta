namespace EnterpriseBIPlatform
{
    public sealed class System
    {
        public string Id { get; internal set; } = string.Empty;
        public string DeploymentDate { get; internal set; } = string.Empty;
        public string SystemName { get; internal set; } = string.Empty;
        public string Version { get; internal set; } = string.Empty;
        public string SystemTypeId { get; internal set; } = string.Empty;
        public SystemType SystemType { get; internal set; } = new SystemType();
    }
}
