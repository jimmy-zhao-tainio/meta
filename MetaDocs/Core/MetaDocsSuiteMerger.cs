using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsSuiteMerger
{
    public MetaDocsModel MergeIntoNew(IEnumerable<MetaDocsModel> sourceModels)
    {
        ArgumentNullException.ThrowIfNull(sourceModels);

        var suite = MetaDocsModel.CreateEmpty();
        MetaDocsDefaults.EnsureDocumentationWorkspace(suite, "workspace:suite", "Documentation suite", "SuiteDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(suite);
        MetaDocsDefaults.EnsureDefaultView(suite);

        foreach (var sourceModel in sourceModels)
        {
            MergeOne(suite, sourceModel);
        }

        MetaDocsPublicReferenceViewBuilder.EnsurePublicReferenceView(suite);
        return suite;
    }

    private static void MergeOne(MetaDocsModel target, MetaDocsModel source)
    {
        var maps = new MergeMaps();

        foreach (var row in source.DocumentationWorkspaceList)
        {
            AddById(target.DocumentationWorkspaceList, row, CloneWorkspace(row), maps.Workspaces, item => item.Id);
        }

        foreach (var row in source.DocumentationThemeList)
        {
            AddById(target.DocumentationThemeList, row, CloneTheme(row), maps.Themes, item => item.Id);
        }

        foreach (var row in source.DocumentationViewList)
        {
            AddById(target.DocumentationViewList, row, CloneView(row), maps.Views, item => item.Id);
        }

        foreach (var row in source.DocumentationSourceList)
        {
            var clone = CloneSource(row);
            if (row.DocumentationWorkspace is not null &&
                maps.Workspaces.TryGetValue(row.DocumentationWorkspace, out var workspace))
            {
                clone.DocumentationWorkspace = workspace;
            }

            AddById(target.DocumentationSourceList, row, clone, maps.Sources, item => item.Id);
        }

        foreach (var row in source.DocumentationImportBatchList)
        {
            var clone = CloneBatch(row);
            clone.DocumentationSource = maps.Sources[row.DocumentationSource];
            AddById(target.DocumentationImportBatchList, row, clone, maps.Batches, item => item.Id);
        }

        foreach (var row in source.DocumentationSubjectList)
        {
            var clone = CloneSubject(row);
            clone.DocumentationSource = maps.Sources[row.DocumentationSource];
            AddById(target.DocumentationSubjectList, row, clone, maps.Subjects, item => item.Id);
        }

        foreach (var row in source.DocumentationSubjectAliasList)
        {
            var clone = CloneSubjectAlias(row);
            clone.DocumentationSubject = maps.Subjects[row.DocumentationSubject];
            AddById(target.DocumentationSubjectAliasList, row, clone, maps.SubjectAliases, item => item.Id);
        }

        foreach (var row in source.DocumentationFactList)
        {
            var clone = CloneFact(row);
            clone.DocumentationSubject = maps.Subjects[row.DocumentationSubject];
            clone.DocumentationSource = maps.Sources[row.DocumentationSource];
            clone.DocumentationImportBatch = maps.Batches[row.DocumentationImportBatch];
            AddById(target.DocumentationFactList, row, clone, maps.Facts, item => item.Id);
        }

        foreach (var row in source.DocumentationNarrativeList)
        {
            var clone = CloneNarrative(row);
            clone.DocumentationSubject = maps.Subjects[row.DocumentationSubject];
            AddById(target.DocumentationNarrativeList, row, clone, maps.Narratives, item => item.Id);
        }

        foreach (var row in source.DocumentationRelationshipList)
        {
            var clone = CloneRelationship(row);
            clone.DocumentationSource = maps.Sources[row.DocumentationSource];
            clone.DocumentationImportBatch = maps.Batches[row.DocumentationImportBatch];
            AddById(target.DocumentationRelationshipList, row, clone, maps.Relationships, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateList)
        {
            var clone = CloneTemplate(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            AddById(target.DocumentationTemplateList, row, clone, maps.Templates, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateRegionList)
        {
            var clone = CloneTemplateRegion(row);
            clone.DocumentationTemplate = maps.Templates[row.DocumentationTemplate];
            AddById(target.DocumentationTemplateRegionList, row, clone, maps.TemplateRegions, item => item.Id);
        }

        foreach (var row in source.DocumentationThemeAssetList)
        {
            var clone = CloneThemeAsset(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            AddById(target.DocumentationThemeAssetList, row, clone, maps.ThemeAssets, item => item.Id);
        }

        foreach (var row in source.DocumentationLayoutList)
        {
            var clone = CloneLayout(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            AddById(target.DocumentationLayoutList, row, clone, maps.Layouts, item => item.Id);
        }

        foreach (var row in source.DocumentationComponentTemplateList)
        {
            var clone = CloneComponentTemplate(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            AddById(target.DocumentationComponentTemplateList, row, clone, maps.ComponentTemplates, item => item.Id);
        }

        foreach (var row in source.DocumentationViewNodeList)
        {
            var clone = CloneViewNode(row);
            clone.DocumentationView = maps.Views[row.DocumentationView];
            AddById(target.DocumentationViewNodeList, row, clone, maps.ViewNodes, item => item.Id);
        }

        foreach (var row in source.DocumentationInstanceImportSpecList)
        {
            var clone = CloneInstanceImportSpec(row);
            if (row.DocumentationSource is not null &&
                maps.Sources.TryGetValue(row.DocumentationSource, out var sourceClone))
            {
                clone.DocumentationSource = sourceClone;
            }

            AddById(target.DocumentationInstanceImportSpecList, row, clone, maps.InstanceImportSpecs, item => item.Id);
        }

        foreach (var row in source.DocumentationEntityImportSpecList)
        {
            var clone = CloneEntityImportSpec(row);
            clone.DocumentationInstanceImportSpec = maps.InstanceImportSpecs[row.DocumentationInstanceImportSpec];
            AddById(target.DocumentationEntityImportSpecList, row, clone, maps.EntityImportSpecs, item => item.Id);
        }

        foreach (var row in source.DocumentationPropertyImportSpecList)
        {
            var clone = ClonePropertyImportSpec(row);
            clone.DocumentationEntityImportSpec = maps.EntityImportSpecs[row.DocumentationEntityImportSpec];
            AddById(target.DocumentationPropertyImportSpecList, row, clone, maps.PropertyImportSpecs, item => item.Id);
        }

        foreach (var row in source.DocumentationRelationshipImportSpecList)
        {
            var clone = CloneRelationshipImportSpec(row);
            clone.DocumentationEntityImportSpec = maps.EntityImportSpecs[row.DocumentationEntityImportSpec];
            AddById(target.DocumentationRelationshipImportSpecList, row, clone, maps.RelationshipImportSpecs, item => item.Id);
        }

        ApplyPreviousRelationships(source, maps);
    }

    private static T AddById<T>(
        List<T> target,
        T source,
        T clone,
        Dictionary<T, T> map,
        Func<T, string> getId)
        where T : class
    {
        var existing = target.FirstOrDefault(candidate =>
            string.Equals(getId(candidate), getId(clone), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            map[source] = existing;
            return existing;
        }

        target.Add(clone);
        map[source] = clone;
        return clone;
    }

    private static DocumentationWorkspace CloneWorkspace(DocumentationWorkspace row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Kind = row.Kind,
            Summary = row.Summary,
        };

    private static DocumentationSource CloneSource(DocumentationSource row) =>
        new()
        {
            Id = row.Id,
            DisplayName = row.DisplayName,
            ImportedAt = row.ImportedAt,
            ImporterId = row.ImporterId,
            Kind = row.Kind,
            Locator = row.Locator,
            SourceFingerprint = row.SourceFingerprint,
            Status = row.Status,
        };

    private static DocumentationImportBatch CloneBatch(DocumentationImportBatch row) =>
        new()
        {
            Id = row.Id,
            ImportedAt = row.ImportedAt,
            ImporterId = row.ImporterId,
            ImporterVersion = row.ImporterVersion,
            SourceFingerprint = row.SourceFingerprint,
            Status = row.Status,
        };

    private static DocumentationSubject CloneSubject(DocumentationSubject row) =>
        new()
        {
            Id = row.Id,
            Key = row.Key,
            Kind = row.Kind,
            NativeKind = row.NativeKind,
            NativeId = row.NativeId,
            DisplayName = row.DisplayName,
            DisplayPath = row.DisplayPath,
            Summary = row.Summary,
            ParentKey = row.ParentKey,
            Status = row.Status,
        };

    private static DocumentationSubjectAlias CloneSubjectAlias(DocumentationSubjectAlias row) =>
        new()
        {
            Id = row.Id,
            AliasKey = row.AliasKey,
            SubjectKey = row.SubjectKey,
            Reason = row.Reason,
        };

    private static DocumentationFact CloneFact(DocumentationFact row) =>
        new()
        {
            Id = row.Id,
            SubjectKey = row.SubjectKey,
            Kind = row.Kind,
            Name = row.Name,
            Value = row.Value,
            ValueKind = row.ValueKind,
            SourceFingerprint = row.SourceFingerprint,
            Status = row.Status,
        };

    private static DocumentationNarrative CloneNarrative(DocumentationNarrative row) =>
        new()
        {
            Id = row.Id,
            SubjectKey = row.SubjectKey,
            Slot = row.Slot,
            Title = row.Title,
            Body = row.Body,
            BodyFormat = row.BodyFormat,
            Origin = row.Origin,
            LastReviewedImportBatchId = row.LastReviewedImportBatchId,
            ReviewStatus = row.ReviewStatus,
        };

    private static DocumentationRelationship CloneRelationship(DocumentationRelationship row) =>
        new()
        {
            Id = row.Id,
            FromSubjectKey = row.FromSubjectKey,
            Kind = row.Kind,
            ToSubjectKey = row.ToSubjectKey,
        };

    private static DocumentationView CloneView(DocumentationView row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Kind = row.Kind,
            Title = row.Title,
            Summary = row.Summary,
        };

    private static DocumentationViewNode CloneViewNode(DocumentationViewNode row) =>
        new()
        {
            Id = row.Id,
            ParentNodeId = row.ParentNodeId,
            SubjectKey = row.SubjectKey,
            Selection = row.Selection,
            Title = row.Title,
        };

    private static DocumentationTheme CloneTheme(DocumentationTheme row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Version = row.Version,
            RenderOptions = row.RenderOptions,
        };

    private static DocumentationTemplate CloneTemplate(DocumentationTemplate row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Kind = row.Kind,
            Html = row.Html,
            SourceUrl = row.SourceUrl,
        };

    private static DocumentationTemplateRegion CloneTemplateRegion(DocumentationTemplateRegion row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            RegionKind = row.RegionKind,
        };

    private static DocumentationThemeAsset CloneThemeAsset(DocumentationThemeAsset row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            AssetKind = row.AssetKind,
            MediaType = row.MediaType,
            Href = row.Href,
            Content = row.Content,
            Hash = row.Hash,
        };

    private static DocumentationLayout CloneLayout(DocumentationLayout row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            LayoutKind = row.LayoutKind,
        };

    private static DocumentationComponentTemplate CloneComponentTemplate(DocumentationComponentTemplate row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            ComponentKind = row.ComponentKind,
            TemplateText = row.TemplateText,
        };

    private static DocumentationInstanceImportSpec CloneInstanceImportSpec(DocumentationInstanceImportSpec row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            IncludeInstances = row.IncludeInstances,
            SafetyStatus = row.SafetyStatus,
        };

    private static DocumentationEntityImportSpec CloneEntityImportSpec(DocumentationEntityImportSpec row) =>
        new()
        {
            Id = row.Id,
            EntityName = row.EntityName,
            IncludeInstances = row.IncludeInstances,
            DisplayNameProperty = row.DisplayNameProperty,
            SummaryProperty = row.SummaryProperty,
            ReviewStatus = row.ReviewStatus,
        };

    private static DocumentationPropertyImportSpec ClonePropertyImportSpec(DocumentationPropertyImportSpec row) =>
        new()
        {
            Id = row.Id,
            PropertyName = row.PropertyName,
            Include = row.Include,
            ReviewStatus = row.ReviewStatus,
        };

    private static DocumentationRelationshipImportSpec CloneRelationshipImportSpec(DocumentationRelationshipImportSpec row) =>
        new()
        {
            Id = row.Id,
            RelationshipSelector = row.RelationshipSelector,
            Include = row.Include,
            ReviewStatus = row.ReviewStatus,
        };

    private static void ApplyPreviousRelationships(MetaDocsModel source, MergeMaps maps)
    {
        ApplyPrevious(
            source.DocumentationSubjectList,
            maps.Subjects,
            static row => row.PreviousSubject,
            static (row, previous) => row.PreviousSubject = previous);
        ApplyPrevious(
            source.DocumentationNarrativeList,
            maps.Narratives,
            static row => row.PreviousNarrative,
            static (row, previous) => row.PreviousNarrative = previous);
        ApplyPrevious(
            source.DocumentationRelationshipList,
            maps.Relationships,
            static row => row.PreviousRelationship,
            static (row, previous) => row.PreviousRelationship = previous);
        ApplyPrevious(
            source.DocumentationTemplateList,
            maps.Templates,
            static row => row.PreviousTemplate,
            static (row, previous) => row.PreviousTemplate = previous);
        ApplyPrevious(
            source.DocumentationTemplateRegionList,
            maps.TemplateRegions,
            static row => row.PreviousRegion,
            static (row, previous) => row.PreviousRegion = previous);
        ApplyPrevious(
            source.DocumentationThemeAssetList,
            maps.ThemeAssets,
            static row => row.PreviousAsset,
            static (row, previous) => row.PreviousAsset = previous);
        ApplyPrevious(
            source.DocumentationLayoutList,
            maps.Layouts,
            static row => row.PreviousLayout,
            static (row, previous) => row.PreviousLayout = previous);
        ApplyPrevious(
            source.DocumentationComponentTemplateList,
            maps.ComponentTemplates,
            static row => row.PreviousComponent,
            static (row, previous) => row.PreviousComponent = previous);
        ApplyPrevious(
            source.DocumentationViewNodeList,
            maps.ViewNodes,
            static row => row.PreviousNode,
            static (row, previous) => row.PreviousNode = previous);
    }

    private static void ApplyPrevious<T>(
        IEnumerable<T> sourceRows,
        IReadOnlyDictionary<T, T> map,
        Func<T, T?> previous,
        Action<T, T?> assign)
        where T : class
    {
        foreach (var sourceRow in sourceRows)
        {
            if (!map.TryGetValue(sourceRow, out var targetRow))
            {
                continue;
            }

            var sourcePrevious = previous(sourceRow);
            assign(
                targetRow,
                sourcePrevious is not null && map.TryGetValue(sourcePrevious, out var targetPrevious)
                    ? targetPrevious
                    : null);
        }
    }

    private sealed class MergeMaps
    {
        public Dictionary<DocumentationWorkspace, DocumentationWorkspace> Workspaces { get; } = new();
        public Dictionary<DocumentationSource, DocumentationSource> Sources { get; } = new();
        public Dictionary<DocumentationImportBatch, DocumentationImportBatch> Batches { get; } = new();
        public Dictionary<DocumentationSubject, DocumentationSubject> Subjects { get; } = new();
        public Dictionary<DocumentationSubjectAlias, DocumentationSubjectAlias> SubjectAliases { get; } = new();
        public Dictionary<DocumentationFact, DocumentationFact> Facts { get; } = new();
        public Dictionary<DocumentationNarrative, DocumentationNarrative> Narratives { get; } = new();
        public Dictionary<DocumentationRelationship, DocumentationRelationship> Relationships { get; } = new();
        public Dictionary<DocumentationTheme, DocumentationTheme> Themes { get; } = new();
        public Dictionary<DocumentationTemplate, DocumentationTemplate> Templates { get; } = new();
        public Dictionary<DocumentationTemplateRegion, DocumentationTemplateRegion> TemplateRegions { get; } = new();
        public Dictionary<DocumentationThemeAsset, DocumentationThemeAsset> ThemeAssets { get; } = new();
        public Dictionary<DocumentationLayout, DocumentationLayout> Layouts { get; } = new();
        public Dictionary<DocumentationComponentTemplate, DocumentationComponentTemplate> ComponentTemplates { get; } = new();
        public Dictionary<DocumentationView, DocumentationView> Views { get; } = new();
        public Dictionary<DocumentationViewNode, DocumentationViewNode> ViewNodes { get; } = new();
        public Dictionary<DocumentationInstanceImportSpec, DocumentationInstanceImportSpec> InstanceImportSpecs { get; } = new();
        public Dictionary<DocumentationEntityImportSpec, DocumentationEntityImportSpec> EntityImportSpecs { get; } = new();
        public Dictionary<DocumentationPropertyImportSpec, DocumentationPropertyImportSpec> PropertyImportSpecs { get; } = new();
        public Dictionary<DocumentationRelationshipImportSpec, DocumentationRelationshipImportSpec> RelationshipImportSpecs { get; } = new();
    }
}
