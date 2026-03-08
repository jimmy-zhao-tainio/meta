namespace EnterpriseBIPlatform
{
    public sealed class Measure
    {
        public string Id { get; internal set; } = string.Empty;
        public string MDX { get; internal set; } = string.Empty;
        public string MeasureName { get; internal set; } = string.Empty;
        public string CubeId { get; internal set; } = string.Empty;
        public Cube Cube { get; internal set; } = new Cube();
    }
}
