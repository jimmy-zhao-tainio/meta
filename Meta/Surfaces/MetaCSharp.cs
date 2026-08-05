using System.Collections.ObjectModel;
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
