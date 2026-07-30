using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public sealed class GenerationManifest
{
    public string RootPath { get; set; } = string.Empty;
    public Dictionary<string, string> FileHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CombinedHash { get; set; } = string.Empty;
}

public static partial class GenerationService
{
    public static GenerationManifest GenerateSql(Workspace workspace, string outputDirectory)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var schema = SqlGenerationArtifacts.BuildSchema(workspace);
        var data = SqlGenerationArtifacts.BuildData(workspace);
        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "schema.sql"), schema);
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "data.sql"), data);

        return GenerationOutputWriter.BuildManifest(outputRoot);
    }

    public static GenerationManifest GenerateCSharp(
        Workspace workspace,
        string outputDirectory,
        bool includeTooling = false)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        var namespaceName = ResolveModelNamespaceName(workspace.Model.Name);
        var modelTypeName = includeTooling
            ? ResolveToolingModelTypeName(workspace.Model)
            : ResolveConsumerModelTypeName(workspace.Model);
        var modelFileName = modelTypeName + ".cs";
        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, modelFileName),
            includeTooling
                ? BuildCSharpToolingModelTypedSerializer(workspace, modelTypeName, namespaceName)
                : BuildCSharpConsumerModel(workspace, modelTypeName, namespaceName));
        var emittedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modelFileName,
        };

        if (includeTooling)
        {
            var toolingFileName = namespaceName + ".Tooling.cs";
            if (!emittedFiles.Add(toolingFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# tooling output because file name collides on '{toolingFileName}'.");
            }

            GenerationOutputWriter.WriteText(Path.Combine(outputRoot, toolingFileName), BuildCSharpTooling(modelTypeName, namespaceName, workspace.WorkspaceRootPath));

            const string modelXmlFileName = "model.xml";
            if (!emittedFiles.Add(modelXmlFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# tooling output because file name collides on '{modelXmlFileName}'.");
            }

            GenerationOutputWriter.WriteText(Path.Combine(outputRoot, modelXmlFileName), BuildModelXml(workspace.Model));
        }

        foreach (var entity in workspace.Model.Entities
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entityFileName = entity.Name + ".cs";
            if (!emittedFiles.Add(entityFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate C# output because model and entity file names collide on '{entityFileName}'.");
            }

            GenerationOutputWriter.WriteText(
                Path.Combine(outputRoot, entityFileName),
                BuildCSharpEntity(
                    entity,
                    namespaceName,
                    workspace.WorkspaceRootPath,
                    requiresTooling: includeTooling));
        }

        return GenerationOutputWriter.BuildManifest(outputRoot);
    }

    public static GenerationManifest GenerateCSharpWorkspace(
        GenericModel model,
        GenericInstance instance,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(instance);

        var workspace = new Workspace
        {
            Model = model,
            Instance = instance,
        };
        var diagnostics = new ValidationService().Validate(workspace);
        if (diagnostics.HasErrors)
        {
            var details = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException(
                $"Cannot generate a C# workspace from invalid metadata. {string.Join(" | ", details)}");
        }

        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        var namespaceName = ResolveModelNamespaceName(model.Name);
        var modelTypeName = ResolveConsumerModelTypeName(model);
        var modelFileName = modelTypeName + ".cs";
        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, modelFileName),
            BuildCSharpWorkspaceModel(workspace, modelTypeName, namespaceName));

        var emittedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modelFileName,
        };
        foreach (var entity in model.Entities
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            var entityFileName = entity.Name + ".cs";
            if (!emittedFiles.Add(entityFileName))
            {
                throw new InvalidOperationException(
                    $"Cannot generate a C# workspace because file name collides on '{entityFileName}'.");
            }

            GenerationOutputWriter.WriteText(
                Path.Combine(outputRoot, entityFileName),
                BuildCSharpWorkspaceEntity(entity, namespaceName));
        }

        return GenerationOutputWriter.BuildManifest(outputRoot);
    }

    public static GenerationManifest GenerateSsdt(Workspace workspace, string outputDirectory)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        var schema = SqlGenerationArtifacts.BuildSchema(workspace);
        var data = SqlGenerationArtifacts.BuildData(workspace);
        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "Schema.sql"), schema);
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "Data.sql"), data);
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "PostDeploy.sql"), SqlGenerationArtifacts.BuildPostDeployScript());
        GenerationOutputWriter.WriteText(Path.Combine(outputRoot, "Metadata.sqlproj"), SqlGenerationArtifacts.BuildSqlProjectFile(workspace));
        return GenerationOutputWriter.BuildManifest(outputRoot);
    }
}
