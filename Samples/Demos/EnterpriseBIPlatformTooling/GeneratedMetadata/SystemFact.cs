namespace EnterpriseBIPlatform
{
    public sealed class SystemFact
    {
        public string Id { get; internal set; } = string.Empty;
        public string LoadPattern { get; internal set; } = string.Empty;
        public string FactId { get; internal set; } = string.Empty;
        public Fact Fact { get; internal set; } = new Fact();
        public string SystemId { get; internal set; } = string.Empty;
        public System System { get; internal set; } = new System();
    }
}
