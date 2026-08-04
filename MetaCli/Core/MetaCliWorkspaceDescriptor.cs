using System.Text;

namespace MetaCli.Core;

internal abstract record MetaCliWorkspaceDescriptor(string Directory)
{
    public const string FileName = "workspace.meta";

    public static MetaCliWorkspaceDescriptor Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        var path = Path.Combine(fullDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Workspace descriptor '{path}' was not found.");
        }

        var lines = File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (lines.Length != 2 || !string.Equals(lines[0], "workspace", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workspace descriptor '{path}' is invalid. Expected 'workspace' followed by one representation.");
        }

        var separator = lines[1].IndexOf(' ');
        if (separator <= 0 || separator == lines[1].Length - 1)
        {
            throw new InvalidOperationException($"Workspace descriptor '{path}' has an invalid representation.");
        }

        var value = lines[1][(separator + 1)..].Trim();
        return lines[1][..separator] switch
        {
            "xml" => new XmlWorkspaceDescriptor(fullDirectory, ResolvePath(fullDirectory, value)),
            "csharp" => new CSharpWorkspaceDescriptor(fullDirectory, ResolvePath(fullDirectory, value)),
            "sql" => new SqlWorkspaceDescriptor(fullDirectory, value),
            var representation => throw new InvalidOperationException(
                $"Workspace representation '{representation}' is not supported."),
        };
    }

    public static void WriteXml(string directory) =>
        Write(directory, "xml .");

    public static void WriteCSharp(string directory) =>
        Write(directory, "csharp .");

    public static void WriteSql(string directory, string connectionEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionEnvironmentVariable);
        Write(directory, $"sql {connectionEnvironmentVariable.Trim()}");
    }

    private static string ResolvePath(string directory, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(directory, value));

    private static void Write(string directory, string representation)
    {
        var fullDirectory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(fullDirectory);
        File.WriteAllText(
            Path.Combine(fullDirectory, FileName),
            $"workspace\n{representation}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

internal sealed record XmlWorkspaceDescriptor(string WorkspaceDirectory, string Path)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);

internal sealed record CSharpWorkspaceDescriptor(string WorkspaceDirectory, string Path)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);

internal sealed record SqlWorkspaceDescriptor(string WorkspaceDirectory, string ConnectionEnvironmentVariable)
    : MetaCliWorkspaceDescriptor(WorkspaceDirectory);
