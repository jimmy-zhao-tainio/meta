using System.Security.Cryptography;
using System.Text;
using Meta.Operations.Domain;

namespace Meta.Surfaces.Sql;

public static class SqlWorkspaceNames
{
    public static string PrimaryKey(string entityName)
    {
        return Compose(
            "PK",
            MetaName.Require(entityName, "Entity name."));
    }

    public static string ForeignKey(
        string sourceEntityName,
        string targetEntityName,
        string relationshipName)
    {
        return Compose(
            "FK",
            MetaName.Require(sourceEntityName, "Source entity name."),
            MetaName.Require(targetEntityName, "Target entity name."),
            MetaName.Require(relationshipName, "Relationship name."));
    }

    private static string Compose(string prefix, params string[] parts)
    {
        var candidate = string.Join('_', new[] { prefix }.Concat(parts));
        if (candidate.Length <= MetaName.MaximumLength)
        {
            return candidate;
        }

        var normalized = candidate.ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return prefix + "_" + hash;
    }
}
