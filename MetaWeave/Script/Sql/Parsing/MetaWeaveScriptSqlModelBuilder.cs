namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    private readonly MetaWeave.MetaWeaveModel model;
    private readonly Dictionary<string, int> nextIdByEntityName = new(StringComparer.Ordinal);

    public MetaWeaveScriptSqlModelBuilder()
    {
        model = MetaWeave.MetaWeaveModel.CreateEmpty();
        BuiltNode.SetResolver(ResolveBuiltNodeReference);
    }

    public MetaWeaveScriptSqlModelBuilder(MetaWeave.MetaWeaveModel seedModel)
    {
        ArgumentNullException.ThrowIfNull(seedModel);
        model = seedModel;
        InitializeNextIdState(seedModel);
        BuiltNode.SetResolver(ResolveBuiltNodeReference);
    }

    public MetaWeave.MetaWeaveModel Build() => model;

    private string NextId(string entityName)
    {
        nextIdByEntityName.TryGetValue(entityName, out var current);
        current++;
        nextIdByEntityName[entityName] = current;
        return $"{entityName}:{current}";
    }

    private object ResolveBuiltNodeReference(string entityName, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var listProperty = model.GetType().GetProperty(entityName + "List")
            ?? throw new InvalidOperationException($"Model does not expose list for '{entityName}'.");
        if (listProperty.GetValue(model) is not System.Collections.IEnumerable rows)
        {
            throw new InvalidOperationException($"Model list for '{entityName}' is not readable.");
        }

        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var rowId = row.GetType().GetProperty("Id")?.GetValue(row) as string;
            if (string.Equals(rowId, id, StringComparison.Ordinal))
            {
                return row;
            }
        }

        throw new InvalidOperationException($"Could not resolve '{entityName}' row '{id}'.");
    }

    private void InitializeNextIdState(MetaWeave.MetaWeaveModel seedModel)
    {
        var modelType = seedModel.GetType();
        var properties = modelType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var property in properties)
        {
            if (!property.Name.EndsWith("List", StringComparison.Ordinal))
            {
                continue;
            }

            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            var rows = property.GetValue(seedModel) as System.Collections.IEnumerable;
            if (rows is null)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var idValue = row?.GetType().GetProperty("Id")?.GetValue(row) as string;
                if (!TryReadEntityId(idValue, out var entityName, out var numericId))
                {
                    continue;
                }

                if (!nextIdByEntityName.TryGetValue(entityName, out var current) || numericId > current)
                {
                    nextIdByEntityName[entityName] = numericId;
                }
            }
        }
    }

    private static bool TryReadEntityId(string? value, out string entityName, out int numericId)
    {
        entityName = string.Empty;
        numericId = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator >= value.Length - 1)
        {
            return false;
        }

        var suffix = value[(separator + 1)..];
        if (!int.TryParse(suffix, out numericId))
        {
            return false;
        }

        entityName = value[..separator];
        return !string.IsNullOrWhiteSpace(entityName);
    }

    internal sealed class BuiltNode
    {
        private readonly IReadOnlyDictionary<string, string> idsByEntityName;
        private static readonly System.Threading.AsyncLocal<Func<string, string, object>?> Resolve = new();

        private BuiltNode(IReadOnlyDictionary<string, string> idsByEntityName)
        {
            this.idsByEntityName = idsByEntityName;
        }

        public string GetId(string entityName) =>
            idsByEntityName.TryGetValue(entityName, out var id)
                ? id
                : throw new InvalidOperationException($"Built node did not expose entity id '{entityName}'.");

        public T GetRef<T>(string entityName)
            where T : class
        {
            var id = GetId(entityName);
            var resolver = Resolve.Value;
            if (resolver is null)
            {
                throw new InvalidOperationException("Built node resolver is not configured.");
            }

            return (T)resolver(entityName, id);
        }

        public bool TryGetId(string entityName, out string id) =>
            idsByEntityName.TryGetValue(entityName, out id!);

        public static void SetResolver(Func<string, string, object> resolver)
        {
            Resolve.Value = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public static BuiltNode Create(params (string EntityName, string Id)[] ids) =>
            new(ids.ToDictionary(static pair => pair.EntityName, static pair => pair.Id, StringComparer.Ordinal));
    }
}
