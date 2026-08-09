#nullable enable

using System.Collections.Generic;

namespace MetaMesh
{
    public sealed partial class MetaMeshModel
    {
        public static MetaMeshModel CreateEmpty() => new();

        public List<CSharpWorkspace> CSharpWorkspaceList { get; set; } = new();
        public List<Mesh> MeshList { get; set; } = new();
        public List<Operation> OperationList { get; set; } = new();
        public List<OperationStep> OperationStepList { get; set; } = new();
        public List<SqlWorkspace> SqlWorkspaceList { get; set; } = new();
        public List<Workspace> WorkspaceList { get; set; } = new();
        public List<XmlWorkspace> XmlWorkspaceList { get; set; } = new();
    }
}
