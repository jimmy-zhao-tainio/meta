using System.Text;
using Meta.Operations.Domain;

namespace Meta.Surfaces.CSharp;

public static partial class MetaCSharpBindingWriter
{
    private static string BuildAccessSource(GenericModel model, IReadOnlyList<GenericEntity> entities)
    {
        var builder = new StringBuilder();
        void Line(string value = "") => builder.AppendLine(value);
        var modelType = TypeName(model.Name, model.Name + "Model");
        var collectionVisitor = Id("I" + model.Name + "CollectionVisitor");
        var relationshipVisitor = Id("I" + model.Name + "RelationshipVisitor");
        const string func = "global::System.Func";
        const string action = "global::System.Action";
        const string list = "global::System.Collections.Generic.IList";

        Line("// Derived typed member access. Keep outside authored workspace source sets.");
        Line("#nullable enable");
        Line("namespace " + Id(model.Name) + ";");
        Line("public interface " + collectionVisitor);
        Line("{");
        Line("    void Visit<TEntity>(string entityName, " + func + "<" + modelType + ", " + list + "<TEntity>> rows,");
        Line("        " + func + "<TEntity, string> readId, " + action + "<TEntity, string> writeId) where TEntity : class;");
        Line("}");
        Line("public interface " + relationshipVisitor);
        Line("{");
        Line("    void Visit<TRow, TTarget>(string entityName, string navigationName, string targetEntityName, bool isNullable,");
        Line("        " + func + "<" + modelType + ", " + list + "<TRow>> rows, " + func + "<TRow, TTarget?> read,");
        Line("        " + action + "<TRow, TTarget?> write, " + func + "<TTarget, string> readTargetId)");
        Line("        where TRow : class where TTarget : class;");
        Line("}");
        Line("public static class " + Id(model.Name + "ModelAccess"));
        Line("{");
        Line("    public static void VisitCollections(" + collectionVisitor + " visitor)");
        Line("    {");
        Line("        global::System.ArgumentNullException.ThrowIfNull(visitor);");
        foreach (var entity in entities)
        {
            Line("        visitor.Visit<" + TypeName(model.Name, entity.Name) + ">(" + Quote(entity.Name) + ",");
            Line("            static model => model." + Id(entity.GetListName()) + ", static row => row.Id, static (row, id) => row.Id = id);");
        }
        Line("    }");
        Line("    public static void VisitRelationships(" + relationshipVisitor + " visitor)");
        Line("    {");
        Line("        global::System.ArgumentNullException.ThrowIfNull(visitor);");
        foreach (var entity in entities)
        {
            foreach (var relationship in Relationships(entity))
            {
                var member = Id(relationship.GetNavigationName());
                Line("        visitor.Visit<" + TypeName(model.Name, entity.Name) + ", " + TypeName(model.Name, relationship.Entity) + ">(");
                Line("            " + Quote(entity.Name) + ", " + Quote(relationship.GetNavigationName()) + ", " + Quote(relationship.Entity) + ", " +
                     (relationship.IsNullable ? "true" : "false") + ", static model => model." + Id(entity.GetListName()) + ",");
                Line("            static row => row." + member + ", static (row, target) => row." + member + " = target" +
                     (relationship.IsNullable ? "" : "!") + ", static target => target.Id);");
            }
        }
        Line("    }");
        Line("}");
        return builder.ToString();
    }
}
