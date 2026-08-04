using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;

namespace Meta.Core.Services;

public sealed class GenerationManifest
{
    public string RootPath { get; set; } = string.Empty;
    public Dictionary<string, string> FileHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CombinedHash { get; set; } = string.Empty;
}

public static partial class GenerationService
{
    public static GenerationManifest GenerateSql(
        InMemoryWorkspace state,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sql = MetaSqlWriter.Write(state);
        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, "schema.sql"),
            sql.Schema);
        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, "data.sql"),
            sql.Data);

        return GenerationOutputWriter.BuildManifest(outputRoot);
    }

    public static GenerationManifest GenerateCSharp(
        InMemoryWorkspace state,
        string outputDirectory,
        bool includeTooling = false,
        string? sourceWorkspacePath = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var outputRoot = GenerationOutputWriter.PrepareDirectory(outputDirectory);
        if (!includeTooling)
        {
            foreach (var source in BuildCSharpSources(state))
            {
                GenerationOutputWriter.WriteText(
                    Path.Combine(outputRoot, source.Key),
                    source.Value);
            }

            return GenerationOutputWriter.BuildManifest(outputRoot);
        }

        var namespaceName = ResolveModelNamespaceName(state.Model.Name);
        var modelTypeName = ResolveToolingModelTypeName(state.Model);
        var modelFileName = modelTypeName + ".cs";
        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, modelFileName),
            BuildCSharpToolingModelTypedSerializer(
                state,
                modelTypeName,
                namespaceName,
                sourceWorkspacePath));
        var emittedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modelFileName,
        };

        var toolingFileName = namespaceName + ".Tooling.cs";
        if (!emittedFiles.Add(toolingFileName))
        {
            throw new InvalidOperationException(
                $"Cannot generate C# tooling output because file name collides on '{toolingFileName}'.");
        }

        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, toolingFileName),
            BuildCSharpTooling(
                modelTypeName,
                namespaceName,
                sourceWorkspacePath));

        const string modelXmlFileName = "model.xml";
        if (!emittedFiles.Add(modelXmlFileName))
        {
            throw new InvalidOperationException(
                $"Cannot generate C# tooling output because file name collides on '{modelXmlFileName}'.");
        }

        GenerationOutputWriter.WriteText(
            Path.Combine(outputRoot, modelXmlFileName),
            BuildModelXml(state.Model));

        foreach (var entity in state.Model.Entities
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
                    sourceWorkspacePath,
                    requiresTooling: includeTooling));
        }

        return GenerationOutputWriter.BuildManifest(outputRoot);
    }

    internal static IReadOnlyDictionary<string, string> BuildCSharpSources(
        InMemoryWorkspace state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var diagnostics = WorkspaceValidator.Validate(
            state.Model,
            state.Instance);
        if (diagnostics.HasErrors)
        {
            var errors = diagnostics.Issues
                .Where(issue => issue.Severity == IssueSeverity.Error)
                .Take(5)
                .Select(issue =>
                    $"{issue.Code} {issue.Location} - {issue.Message}");
            throw new InvalidOperationException(
                "Cannot write C# for invalid metadata. " +
                string.Join(" | ", errors));
        }

        var namespaceName = ResolveModelNamespaceName(state.Model.Name);
        var modelTypeName = ResolveConsumerModelTypeName(state.Model);
        var sources = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            [modelTypeName + ".cs"] = BuildCSharpConsumerModel(
                state,
                modelTypeName,
                namespaceName),
        };

        foreach (var entity in state.Model.Entities
                     .OrderBy(item => item.Name, MetaName.Comparer)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            var fileName = entity.Name + ".cs";
            if (!sources.TryAdd(
                    fileName,
                    BuildCSharpEntity(
                        entity,
                        namespaceName,
                        workspacePath: null,
                        requiresTooling: false)))
            {
                throw new InvalidOperationException(
                    $"Cannot write C# because file name '{fileName}' is duplicated.");
            }
        }

        return sources;
    }

}
