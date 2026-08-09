#nullable enable

using System.Collections.Generic;

namespace MetaWeave
{
    public sealed partial class MetaWeaveModel
    {
        public static MetaWeaveModel CreateEmpty() => new();

        public List<ModelReference> ModelReferenceList { get; set; } = new();
        public List<PropertyBinding> PropertyBindingList { get; set; } = new();
    }
}
