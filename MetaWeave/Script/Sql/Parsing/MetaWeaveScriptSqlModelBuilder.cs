namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    private readonly MetaWeave.MetaWeaveModel model;
    private readonly Dictionary<string, int> nextIdByEntityName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, object?>> findRowByEntityName = new(StringComparer.Ordinal);

    public MetaWeaveScriptSqlModelBuilder()
    {
        model = MetaWeave.MetaWeaveModel.CreateEmpty();
        InitializeModelAccess();
        BuiltNode.SetResolver(ResolveBuiltNodeReference);
    }

    public MetaWeaveScriptSqlModelBuilder(MetaWeave.MetaWeaveModel seedModel)
    {
        ArgumentNullException.ThrowIfNull(seedModel);
        model = seedModel;
        InitializeModelAccess();
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

        if (!findRowByEntityName.TryGetValue(entityName, out var findRow))
        {
            throw new InvalidOperationException($"Model does not expose list for '{entityName}'.");
        }

        return findRow(id)
            ?? throw new InvalidOperationException($"Could not resolve '{entityName}' row '{id}'.");
    }

    private void InitializeModelAccess() =>
        MetaWeave.MetaWeaveModelAccess.VisitCollections(new ModelCollectionAccess(this));

    private sealed class ModelCollectionAccess(MetaWeaveScriptSqlModelBuilder builder) : MetaWeave.IMetaWeaveCollectionVisitor
    {
        public void Visit<TEntity>(string entityName,
            Func<MetaWeave.MetaWeaveModel, IList<TEntity>> rows,
            Func<TEntity, string> readId, Action<TEntity, string> writeId) where TEntity : class
        {
            var collection = rows(builder.model);
            builder.findRowByEntityName.Add(entityName, id => collection.FirstOrDefault(row =>
                row is not null && string.Equals(readId(row), id, StringComparison.Ordinal)));
            foreach (var row in collection)
            {
                if (row is null || !TryReadEntityId(readId(row), out var idEntityName, out var numericId))
                {
                    continue;
                }

                if (!builder.nextIdByEntityName.TryGetValue(idEntityName, out var current) || numericId > current)
                {
                    builder.nextIdByEntityName[idEntityName] = numericId;
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
