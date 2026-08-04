using System.Collections.ObjectModel;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Serialization;

public sealed class MetaCSharp
{
    public MetaCSharp(IReadOnlyDictionary<string, string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Sources = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                sources,
                StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string> Sources { get; }
}

public static class MetaCSharpWriter
{
    public static MetaCSharp Write(InMemoryWorkspace state)
    {
        return new MetaCSharp(
            GenerationService.BuildCSharpSources(state));
    }
}
