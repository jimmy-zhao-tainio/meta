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

        return suite;
    }

    private static void MergeOne(MetaDocsModel target, MetaDocsModel source)
    {
        var maps = new MergeMaps();

        foreach (var row in source.DocumentationWorkspaceTypeList)
        {
            AddById(target.DocumentationWorkspaceTypeList, row, CloneWorkspaceType(row), maps.WorkspaceTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationSourceTypeList)
        {
            AddById(target.DocumentationSourceTypeList, row, CloneSourceType(row), maps.SourceTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationSubjectTypeList)
        {
            AddById(target.DocumentationSubjectTypeList, row, CloneSubjectType(row), maps.SubjectTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationFactTypeList)
        {
            AddById(target.DocumentationFactTypeList, row, CloneFactType(row), maps.FactTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationValueTypeList)
        {
            AddById(target.DocumentationValueTypeList, row, CloneValueType(row), maps.ValueTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationRelationshipTypeList)
        {
            AddById(target.DocumentationRelationshipTypeList, row, CloneRelationshipType(row), maps.RelationshipTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationViewTypeList)
        {
            AddById(target.DocumentationViewTypeList, row, CloneViewType(row), maps.ViewTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateTypeList)
        {
            AddById(target.DocumentationTemplateTypeList, row, CloneTemplateType(row), maps.TemplateTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateRegionTypeList)
        {
            AddById(target.DocumentationTemplateRegionTypeList, row, CloneTemplateRegionType(row), maps.TemplateRegionTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationThemeAssetTypeList)
        {
            AddById(target.DocumentationThemeAssetTypeList, row, CloneThemeAssetType(row), maps.ThemeAssetTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationLayoutTypeList)
        {
            AddById(target.DocumentationLayoutTypeList, row, CloneLayoutType(row), maps.LayoutTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationComponentTemplateTypeList)
        {
            AddById(target.DocumentationComponentTemplateTypeList, row, CloneComponentTemplateType(row), maps.ComponentTemplateTypes, item => item.Id);
        }

        foreach (var row in source.DocumentationWorkspaceList)
        {
            var clone = CloneWorkspace(row);
            clone.DocumentationWorkspaceType = maps.WorkspaceTypes[row.DocumentationWorkspaceType];
            AddById(target.DocumentationWorkspaceList, row, clone, maps.Workspaces, item => item.Id);
        }

        foreach (var row in source.DocumentationThemeList)
        {
            AddById(target.DocumentationThemeList, row, CloneTheme(row), maps.Themes, item => item.Id);
        }

        foreach (var row in source.DocumentationViewList)
        {
            var clone = CloneView(row);
            clone.DocumentationViewType = maps.ViewTypes[row.DocumentationViewType];
            AddById(target.DocumentationViewList, row, clone, maps.Views, item => item.Id);
        }

        foreach (var row in source.DocumentationSourceList)
        {
            var clone = CloneSource(row);
            clone.DocumentationSourceType = maps.SourceTypes[row.DocumentationSourceType];
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
            clone.DocumentationSubjectType = maps.SubjectTypes[row.DocumentationSubjectType];
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
            clone.DocumentationFactType = maps.FactTypes[row.DocumentationFactType];
            clone.DocumentationValueType = maps.ValueTypes[row.DocumentationValueType];
            AddById(target.DocumentationFactList, row, clone, maps.Facts, item => item.Id);
        }

        foreach (var row in source.DocumentationNarrativeList)
        {
            var clone = CloneNarrative(row);
            clone.DocumentationSubject = maps.Subjects[row.DocumentationSubject];
            AddById(target.DocumentationNarrativeList, row, clone, maps.Narratives, item => item.Id);
        }

        foreach (var row in source.DocumentationExampleList)
        {
            var clone = CloneExample(row);
            clone.DocumentationSubject = maps.Subjects[row.DocumentationSubject];
            AddById(target.DocumentationExampleList, row, clone, maps.Examples, item => item.Id);
        }

        foreach (var row in source.DocumentationExampleSectionList)
        {
            var clone = CloneExampleSection(row);
            clone.DocumentationExample = maps.Examples[row.DocumentationExample];
            AddById(target.DocumentationExampleSectionList, row, clone, maps.ExampleSections, item => item.Id);
        }

        foreach (var row in source.DocumentationExampleCodeList)
        {
            var clone = CloneExampleCode(row);
            clone.DocumentationExampleSection = maps.ExampleSections[row.DocumentationExampleSection];
            AddById(target.DocumentationExampleCodeList, row, clone, maps.ExampleCodes, item => item.Id);
        }

        foreach (var row in source.DocumentationRelationshipList)
        {
            var clone = CloneRelationship(row);
            clone.DocumentationSource = maps.Sources[row.DocumentationSource];
            clone.DocumentationImportBatch = maps.Batches[row.DocumentationImportBatch];
            clone.DocumentationRelationshipType = maps.RelationshipTypes[row.DocumentationRelationshipType];
            clone.FromSubject = maps.Subjects[row.FromSubject];
            clone.ToSubject = maps.Subjects[row.ToSubject];
            AddById(target.DocumentationRelationshipList, row, clone, maps.Relationships, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateList)
        {
            var clone = CloneTemplate(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            clone.DocumentationTemplateType = maps.TemplateTypes[row.DocumentationTemplateType];
            AddById(target.DocumentationTemplateList, row, clone, maps.Templates, item => item.Id);
        }

        foreach (var row in source.DocumentationTemplateRegionList)
        {
            var clone = CloneTemplateRegion(row);
            clone.DocumentationTemplate = maps.Templates[row.DocumentationTemplate];
            clone.DocumentationTemplateRegionType = maps.TemplateRegionTypes[row.DocumentationTemplateRegionType];
            AddById(target.DocumentationTemplateRegionList, row, clone, maps.TemplateRegions, item => item.Id);
        }

        foreach (var row in source.DocumentationThemeAssetList)
        {
            var clone = CloneThemeAsset(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            clone.DocumentationThemeAssetType = maps.ThemeAssetTypes[row.DocumentationThemeAssetType];
            AddById(target.DocumentationThemeAssetList, row, clone, maps.ThemeAssets, item => item.Id);
        }

        foreach (var row in source.DocumentationLayoutList)
        {
            var clone = CloneLayout(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            clone.DocumentationLayoutType = maps.LayoutTypes[row.DocumentationLayoutType];
            AddById(target.DocumentationLayoutList, row, clone, maps.Layouts, item => item.Id);
        }

        foreach (var row in source.DocumentationComponentTemplateList)
        {
            var clone = CloneComponentTemplate(row);
            clone.DocumentationTheme = maps.Themes[row.DocumentationTheme];
            clone.DocumentationComponentTemplateType = maps.ComponentTemplateTypes[row.DocumentationComponentTemplateType];
            AddById(target.DocumentationComponentTemplateList, row, clone, maps.ComponentTemplates, item => item.Id);
        }

        foreach (var row in source.DocumentationViewNodeList)
        {
            var clone = CloneViewNode(row);
            clone.DocumentationView = maps.Views[row.DocumentationView];
            if (row.DocumentationSubject is not null &&
                maps.Subjects.TryGetValue(row.DocumentationSubject, out var viewNodeSubject))
            {
                clone.DocumentationSubject = viewNodeSubject;
            }
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
        ApplyStructuralRelationships(source, maps);
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

    private static DocumentationWorkspaceType CloneWorkspaceType(DocumentationWorkspaceType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationSourceType CloneSourceType(DocumentationSourceType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationSubjectType CloneSubjectType(DocumentationSubjectType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationFactType CloneFactType(DocumentationFactType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationValueType CloneValueType(DocumentationValueType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationRelationshipType CloneRelationshipType(DocumentationRelationshipType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationViewType CloneViewType(DocumentationViewType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationTemplateType CloneTemplateType(DocumentationTemplateType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationTemplateRegionType CloneTemplateRegionType(DocumentationTemplateRegionType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationThemeAssetType CloneThemeAssetType(DocumentationThemeAssetType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationLayoutType CloneLayoutType(DocumentationLayoutType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationComponentTemplateType CloneComponentTemplateType(DocumentationComponentTemplateType row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
        };

    private static DocumentationWorkspace CloneWorkspace(DocumentationWorkspace row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Summary = row.Summary,
        };

    private static DocumentationSource CloneSource(DocumentationSource row) =>
        new()
        {
            Id = row.Id,
            DisplayName = row.DisplayName,
            ImportedAt = row.ImportedAt,
            ImporterId = row.ImporterId,
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
            SourceTypeName = row.SourceTypeName,
            NativeId = row.NativeId,
            DisplayName = row.DisplayName,
            DisplayPath = row.DisplayPath,
            Summary = row.Summary,
            Status = row.Status,
        };

    private static DocumentationSubjectAlias CloneSubjectAlias(DocumentationSubjectAlias row) =>
        new()
        {
            Id = row.Id,
            Alias = row.Alias,
            Reason = row.Reason,
        };

    private static DocumentationFact CloneFact(DocumentationFact row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Value = row.Value,
            SourceFingerprint = row.SourceFingerprint,
            Status = row.Status,
        };

    private static DocumentationNarrative CloneNarrative(DocumentationNarrative row) =>
        new()
        {
            Id = row.Id,
            Slot = row.Slot,
            Title = row.Title,
            Body = row.Body,
            BodyFormat = row.BodyFormat,
            Origin = row.Origin,
            LastReviewedImportBatchId = row.LastReviewedImportBatchId,
            ReviewStatus = row.ReviewStatus,
        };

    private static DocumentationExample CloneExample(DocumentationExample row) =>
        new()
        {
            Id = row.Id,
            Title = row.Title,
            Summary = row.Summary,
            Origin = row.Origin,
            ReviewStatus = row.ReviewStatus,
        };

    private static DocumentationExampleSection CloneExampleSection(DocumentationExampleSection row) =>
        new()
        {
            Id = row.Id,
            Title = row.Title,
            Body = row.Body,
            BodyFormat = row.BodyFormat,
        };

    private static DocumentationExampleCode CloneExampleCode(DocumentationExampleCode row) =>
        new()
        {
            Id = row.Id,
            Title = row.Title,
            Language = row.Language,
            Code = row.Code,
        };

    private static DocumentationRelationship CloneRelationship(DocumentationRelationship row) =>
        new()
        {
            Id = row.Id,
        };

    private static DocumentationView CloneView(DocumentationView row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Title = row.Title,
            Summary = row.Summary,
        };

    private static DocumentationViewNode CloneViewNode(DocumentationViewNode row) =>
        new()
        {
            Id = row.Id,
            ParentNodeId = row.ParentNodeId,
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
            Html = row.Html,
            SourceUrl = row.SourceUrl,
        };

    private static DocumentationTemplateRegion CloneTemplateRegion(DocumentationTemplateRegion row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
        };

    private static DocumentationThemeAsset CloneThemeAsset(DocumentationThemeAsset row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
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
        };

    private static DocumentationComponentTemplate CloneComponentTemplate(DocumentationComponentTemplate row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
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
            source.DocumentationExampleList,
            maps.Examples,
            static row => row.PreviousExample,
            static (row, previous) => row.PreviousExample = previous);
        ApplyPrevious(
            source.DocumentationExampleSectionList,
            maps.ExampleSections,
            static row => row.PreviousSection,
            static (row, previous) => row.PreviousSection = previous);
        ApplyPrevious(
            source.DocumentationExampleCodeList,
            maps.ExampleCodes,
            static row => row.PreviousCode,
            static (row, previous) => row.PreviousCode = previous);
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

    private static void ApplyStructuralRelationships(MetaDocsModel source, MergeMaps maps)
    {
        foreach (var sourceSubject in source.DocumentationSubjectList)
        {
            if (!maps.Subjects.TryGetValue(sourceSubject, out var targetSubject))
            {
                continue;
            }

            if (sourceSubject.ParentSubject is not null &&
                maps.Subjects.TryGetValue(sourceSubject.ParentSubject, out var targetParent))
            {
                targetSubject.ParentSubject = targetParent;
            }
        }

        foreach (var sourceView in source.DocumentationViewList)
        {
            if (!maps.Views.TryGetValue(sourceView, out var targetView))
            {
                continue;
            }

            if (sourceView.RootSubject is not null &&
                maps.Subjects.TryGetValue(sourceView.RootSubject, out var targetRoot))
            {
                targetView.RootSubject = targetRoot;
                targetView.DocumentationViewType = maps.ViewTypes[sourceView.DocumentationViewType];
                targetView.Name = sourceView.Name;
                targetView.Title = sourceView.Title;
                targetView.Summary = sourceView.Summary;
            }
        }
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
        public Dictionary<DocumentationWorkspaceType, DocumentationWorkspaceType> WorkspaceTypes { get; } = new();
        public Dictionary<DocumentationSourceType, DocumentationSourceType> SourceTypes { get; } = new();
        public Dictionary<DocumentationSubjectType, DocumentationSubjectType> SubjectTypes { get; } = new();
        public Dictionary<DocumentationFactType, DocumentationFactType> FactTypes { get; } = new();
        public Dictionary<DocumentationValueType, DocumentationValueType> ValueTypes { get; } = new();
        public Dictionary<DocumentationRelationshipType, DocumentationRelationshipType> RelationshipTypes { get; } = new();
        public Dictionary<DocumentationViewType, DocumentationViewType> ViewTypes { get; } = new();
        public Dictionary<DocumentationTemplateType, DocumentationTemplateType> TemplateTypes { get; } = new();
        public Dictionary<DocumentationTemplateRegionType, DocumentationTemplateRegionType> TemplateRegionTypes { get; } = new();
        public Dictionary<DocumentationThemeAssetType, DocumentationThemeAssetType> ThemeAssetTypes { get; } = new();
        public Dictionary<DocumentationLayoutType, DocumentationLayoutType> LayoutTypes { get; } = new();
        public Dictionary<DocumentationComponentTemplateType, DocumentationComponentTemplateType> ComponentTemplateTypes { get; } = new();
        public Dictionary<DocumentationWorkspace, DocumentationWorkspace> Workspaces { get; } = new();
        public Dictionary<DocumentationSource, DocumentationSource> Sources { get; } = new();
        public Dictionary<DocumentationImportBatch, DocumentationImportBatch> Batches { get; } = new();
        public Dictionary<DocumentationSubject, DocumentationSubject> Subjects { get; } = new();
        public Dictionary<DocumentationSubjectAlias, DocumentationSubjectAlias> SubjectAliases { get; } = new();
        public Dictionary<DocumentationFact, DocumentationFact> Facts { get; } = new();
        public Dictionary<DocumentationNarrative, DocumentationNarrative> Narratives { get; } = new();
        public Dictionary<DocumentationExample, DocumentationExample> Examples { get; } = new();
        public Dictionary<DocumentationExampleSection, DocumentationExampleSection> ExampleSections { get; } = new();
        public Dictionary<DocumentationExampleCode, DocumentationExampleCode> ExampleCodes { get; } = new();
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
