using System.Threading;
using System.Threading.Tasks;

namespace Meta.Core.Serialization;

public interface IMetaWorkspaceModel<TModel>
    where TModel : IMetaWorkspaceModel<TModel>
{
    static abstract TModel CreateEmpty();

    static abstract TModel LoadFromXmlWorkspace(
        string workspacePath,
        bool searchUpward = true);

    static abstract Task<TModel> LoadFromXmlWorkspaceAsync(
        string workspacePath,
        bool searchUpward = true,
        CancellationToken cancellationToken = default);

    void SaveToXmlWorkspace(string workspacePath);

    Task SaveToXmlWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken = default);
}
