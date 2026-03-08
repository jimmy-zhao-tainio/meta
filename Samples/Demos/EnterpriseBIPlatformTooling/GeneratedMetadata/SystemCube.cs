namespace EnterpriseBIPlatform
{
    public sealed class SystemCube
    {
        public string Id { get; internal set; } = string.Empty;
        public string ProcessingMode { get; internal set; } = string.Empty;
        public string CubeId { get; internal set; } = string.Empty;
        public Cube Cube { get; internal set; } = new Cube();
        public string SystemId { get; internal set; } = string.Empty;
        public System System { get; internal set; } = new System();
    }
}
