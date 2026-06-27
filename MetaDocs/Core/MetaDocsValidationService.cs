using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsValidationService
{
    public MetaDocsValidationResult Validate(MetaDocsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var diagnostics = new List<MetaDocsDiagnostic>();
        var subjectsById = model.DocumentationSubjectList
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var subjectIds = subjectsById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceIds = model.DocumentationSourceList
            .Select(source => source.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var batchIds = model.DocumentationImportBatchList
            .Select(batch => batch.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CheckDuplicateSubjectKeys(model, diagnostics);
        CheckDuplicateDisplayPaths(model, diagnostics);
        CheckFactReferences(model, diagnostics, subjectIds, sourceIds, batchIds);
        CheckNarrativeReferences(model, diagnostics, subjectIds);
        CheckRelationships(model, diagnostics, subjectIds, sourceIds, batchIds);
        CheckViews(model, diagnostics, subjectsById);
        CheckNarrativeReviewState(model, diagnostics);
        CheckMissingExpectedNarratives(model, diagnostics);
        CheckAliases(model, diagnostics, subjectIds);
        CheckTheme(model, diagnostics);
        CheckImportSpecs(model, diagnostics, sourceIds);
        CheckInstancePolicies(model, diagnostics);
        CheckInstanceDocs(model, diagnostics, subjectIds);
        CheckCliReference(model, diagnostics);
        CheckPublicReferenceView(model, diagnostics);

        return new MetaDocsValidationResult(diagnostics);
    }

    private static void CheckDuplicateSubjectKeys(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        foreach (var group in model.DocumentationSubjectList
                     .Where(subject => !string.IsNullOrWhiteSpace(subject.Key))
                     .GroupBy(subject => subject.Key, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Error(
                "MDOC001",
                "DuplicateSubjectKey",
                $"DocumentationSubject key '{group.Key}' appears {group.Count()} times.",
                string.Join(", ", group.Select(subject => subject.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))));
        }
    }

    private static void CheckDuplicateDisplayPaths(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        foreach (var group in model.DocumentationSubjectList
                     .Where(IsActive)
                     .Where(subject => !string.IsNullOrWhiteSpace(subject.DisplayPath))
                     .GroupBy(subject => subject.DisplayPath!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Warning(
                "MDOC002",
                "DuplicateDisplayPath",
                $"Active display path '{group.Key}' resolves to {group.Count()} subjects.",
                string.Join(", ", group.Select(subject => subject.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))));
        }
    }

    private static void CheckFactReferences(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds,
        ISet<string> sourceIds,
        ISet<string> batchIds)
    {
        foreach (var fact in model.DocumentationFactList)
        {
            if (!subjectIds.Contains(fact.SubjectKey))
            {
                diagnostics.Add(Error(
                    "MDOC003",
                    "MissingFactSubject",
                    $"DocumentationFact '{fact.Id}' references missing subject '{fact.SubjectKey}'.",
                    fact.Id));
            }

            if (fact.DocumentationSubject is null ||
                !string.Equals(fact.DocumentationSubject.Id, fact.SubjectKey, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "MDOC012",
                    "InconsistentFactReference",
                    $"DocumentationFact '{fact.Id}' has an inconsistent subject reference.",
                    fact.Id));
            }

            if (fact.DocumentationSource is null || !sourceIds.Contains(fact.DocumentationSource.Id))
            {
                diagnostics.Add(Error(
                    "MDOC012",
                    "InconsistentFactReference",
                    $"DocumentationFact '{fact.Id}' references a missing documentation source.",
                    fact.Id));
            }

            if (fact.DocumentationImportBatch is null || !batchIds.Contains(fact.DocumentationImportBatch.Id))
            {
                diagnostics.Add(Error(
                    "MDOC012",
                    "InconsistentFactReference",
                    $"DocumentationFact '{fact.Id}' references a missing import batch.",
                    fact.Id));
            }
        }
    }

    private static void CheckNarrativeReferences(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds)
    {
        foreach (var narrative in model.DocumentationNarrativeList)
        {
            if (!subjectIds.Contains(narrative.SubjectKey))
            {
                diagnostics.Add(Error(
                    "MDOC004",
                    "MissingNarrativeSubject",
                    $"DocumentationNarrative '{narrative.Id}' references missing subject '{narrative.SubjectKey}'.",
                    narrative.Id));
            }

            if (narrative.DocumentationSubject is null ||
                !string.Equals(narrative.DocumentationSubject.Id, narrative.SubjectKey, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "MDOC004",
                    "MissingNarrativeSubject",
                    $"DocumentationNarrative '{narrative.Id}' has an inconsistent subject reference.",
                    narrative.Id));
            }
        }
    }

    private static void CheckRelationships(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds,
        ISet<string> sourceIds,
        ISet<string> batchIds)
    {
        foreach (var relationship in model.DocumentationRelationshipList)
        {
            if (!subjectIds.Contains(relationship.FromSubjectKey) ||
                !subjectIds.Contains(relationship.ToSubjectKey))
            {
                diagnostics.Add(Error(
                    "MDOC005",
                    "BrokenRelationship",
                    $"DocumentationRelationship '{relationship.Id}' references missing subject(s).",
                    relationship.Id));
            }

            if (relationship.DocumentationSource is null || !sourceIds.Contains(relationship.DocumentationSource.Id) ||
                relationship.DocumentationImportBatch is null || !batchIds.Contains(relationship.DocumentationImportBatch.Id))
            {
                diagnostics.Add(Error(
                    "MDOC005",
                    "BrokenRelationship",
                    $"DocumentationRelationship '{relationship.Id}' has inconsistent source or import batch references.",
                    relationship.Id));
            }
        }
    }

    private static void CheckViews(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        IReadOnlyDictionary<string, DocumentationSubject[]> subjectsById)
    {
        foreach (var node in model.DocumentationViewNodeList.Where(node => !string.IsNullOrWhiteSpace(node.SubjectKey)))
        {
            if (!subjectsById.TryGetValue(node.SubjectKey!, out var subjects))
            {
                diagnostics.Add(Error(
                    "MDOC006",
                    "BrokenViewNode",
                    $"DocumentationViewNode '{node.Id}' references missing subject '{node.SubjectKey}'.",
                    node.Id));
                continue;
            }

            if (subjects.Any(subject => string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Warning(
                    "MDOC007",
                    "MissingSourceSubjectInView",
                    $"DocumentationViewNode '{node.Id}' includes a subject that is missing from source.",
                    node.Id));
            }
        }
    }

    private static void CheckNarrativeReviewState(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        var subjectsById = model.DocumentationSubjectList
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var narrative in model.DocumentationNarrativeList)
        {
            if (string.Equals(narrative.ReviewStatus, "NeedsReview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(narrative.ReviewStatus, "NeedsAuthoring", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    "MDOC008",
                    "NarrativeNeedsReview",
                    $"DocumentationNarrative '{narrative.Id}' review status is '{narrative.ReviewStatus}'.",
                    narrative.Id));
            }

            if (subjectsById.TryGetValue(narrative.SubjectKey, out var subject) &&
                (string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(narrative.Origin, "Authored", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(narrative.ReviewStatus, "Current", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    "MDOC008",
                    "NarrativeNeedsReview",
                    $"Authored narrative '{narrative.Id}' is attached to a {subject.Status} subject.",
                    narrative.Id));
            }
        }
    }

    private static void CheckMissingExpectedNarratives(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        var authoredBySubject = model.DocumentationNarrativeList
            .Where(narrative =>
                string.Equals(narrative.Origin, "Authored", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(narrative.Origin, "ImportedMarkdown", StringComparison.OrdinalIgnoreCase))
            .Select(narrative => narrative.SubjectKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedFactSubjects = model.DocumentationFactList
            .Where(fact => !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.SubjectKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var subject in model.DocumentationSubjectList
                     .Where(IsActive)
                     .Where(subject => generatedFactSubjects.Contains(subject.Id))
                     .Where(subject => ShouldExpectNarrative(subject) && !authoredBySubject.Contains(subject.Id)))
        {
            diagnostics.Add(Info(
                "MDOC009",
                "MissingExpectedNarrative",
                $"Subject '{subject.Id}' has generated facts but no authored narrative.",
                subject.Id));
        }
    }

    private static void CheckAliases(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds)
    {
        foreach (var alias in model.DocumentationSubjectAliasList)
        {
            if (!subjectIds.Contains(alias.SubjectKey))
            {
                diagnostics.Add(Error(
                    "MDOC013",
                    "BrokenAlias",
                    $"DocumentationSubjectAlias '{alias.Id}' references missing subject '{alias.SubjectKey}'.",
                    alias.Id));
            }
        }

        foreach (var group in model.DocumentationSubjectAliasList
                     .Where(alias => !string.IsNullOrWhiteSpace(alias.AliasKey))
                     .GroupBy(alias => alias.AliasKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(alias => alias.SubjectKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            diagnostics.Add(Warning(
                "MDOC014",
                "AmbiguousAlias",
                $"Alias '{group.Key}' resolves to more than one subject.",
                string.Join(", ", group.Select(alias => alias.SubjectKey).Distinct(StringComparer.OrdinalIgnoreCase))));
        }
    }

    private static void CheckTheme(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        if (!model.DocumentationThemeAssetList.Any(asset =>
                string.Equals(asset.AssetKind, "Css", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(asset.Content)))
        {
            diagnostics.Add(Error(
                "MDOC010",
                "MissingThemeAsset",
                "No modeled CSS DocumentationThemeAsset with content is available for render-site.",
                "DocumentationThemeAsset"));
        }

        if (!model.DocumentationTemplateList.Any(template =>
                string.Equals(template.Kind, "SiteShell", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(template.Html)))
        {
            diagnostics.Add(Error(
                "MDOC011",
                "MissingSiteShellTemplate",
                "No modeled SiteShell DocumentationTemplate with HTML is available for render-site.",
                "DocumentationTemplate"));
        }
    }

    private static void CheckImportSpecs(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> sourceIds)
    {
        foreach (var spec in model.DocumentationInstanceImportSpecList)
        {
            if (spec.DocumentationSource is not null && !sourceIds.Contains(spec.DocumentationSource.Id))
            {
                diagnostics.Add(Error(
                    "MDOC015",
                    "BrokenInstanceImportSpec",
                    $"DocumentationInstanceImportSpec '{spec.Id}' references a missing source.",
                    spec.Id));
            }
        }
    }

    private static void CheckInstancePolicies(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        foreach (var spec in model.DocumentationInstanceImportSpecList.Where(spec => IsIncluded(spec.IncludeInstances)))
        {
            var entitySpecs = model.DocumentationEntityImportSpecList
                .Where(entity => ReferenceEquals(entity.DocumentationInstanceImportSpec, spec))
                .Where(entity => IsIncluded(entity.IncludeInstances))
                .ToArray();
            if (entitySpecs.Length == 0)
            {
                diagnostics.Add(Info(
                    "MDOC023",
                    "EmptyInstanceImportPolicy",
                    $"DocumentationInstanceImportSpec '{spec.Id}' includes instances but has no included entities.",
                    spec.Id));
            }

            foreach (var entitySpec in entitySpecs)
            {
                var entitySubject = FindModelEntitySubject(model, spec.DocumentationSource?.Id ?? string.Empty, entitySpec.EntityName);
                if (entitySubject is null)
                {
                    diagnostics.Add(Warning(
                        "MDOC016",
                        "InstancePolicyEntityMissing",
                        $"DocumentationEntityImportSpec '{entitySpec.Id}' references entity '{entitySpec.EntityName}' that is not present in imported model docs.",
                        entitySpec.Id));
                    continue;
                }

                foreach (var propertySpec in model.DocumentationPropertyImportSpecList
                             .Where(property => ReferenceEquals(property.DocumentationEntityImportSpec, entitySpec))
                             .Where(property => IsIncluded(property.Include)))
                {
                    if (FindModelPropertySubject(model, entitySubject, propertySpec.PropertyName) is null)
                    {
                        diagnostics.Add(Warning(
                            "MDOC017",
                            "InstancePolicyPropertyMissing",
                            $"DocumentationPropertyImportSpec '{propertySpec.Id}' references property '{propertySpec.PropertyName}' that is not present on entity '{entitySpec.EntityName}'.",
                            propertySpec.Id));
                    }
                }

                foreach (var relationshipSpec in model.DocumentationRelationshipImportSpecList
                             .Where(relationship => ReferenceEquals(relationship.DocumentationEntityImportSpec, entitySpec))
                             .Where(relationship => IsIncluded(relationship.Include)))
                {
                    if (FindModelRelationshipSubject(model, entitySubject, relationshipSpec.RelationshipSelector) is null)
                    {
                        diagnostics.Add(Warning(
                            "MDOC018",
                            "InstancePolicyRelationshipMissing",
                            $"DocumentationRelationshipImportSpec '{relationshipSpec.Id}' references relationship '{relationshipSpec.RelationshipSelector}' that is not present on entity '{entitySpec.EntityName}'.",
                            relationshipSpec.Id));
                    }
                }
            }
        }
    }

    private static void CheckInstanceDocs(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds)
    {
        var includedEntities = model.DocumentationEntityImportSpecList
            .Where(spec => IsIncluded(spec.IncludeInstances))
            .Select(spec => spec.EntityName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includedPropertiesByEntity = model.DocumentationPropertyImportSpecList
            .Where(spec => IsIncluded(spec.Include))
            .Where(spec => spec.DocumentationEntityImportSpec is not null)
            .GroupBy(spec => spec.DocumentationEntityImportSpec.EntityName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(spec => spec.PropertyName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var subject in model.DocumentationSubjectList
                     .Where(subject => string.Equals(subject.Kind, "Instance", StringComparison.OrdinalIgnoreCase))
                     .Where(IsActive))
        {
            var entityName = subject.NativeKind ?? string.Empty;
            if (string.IsNullOrWhiteSpace(subject.DisplayName))
            {
                diagnostics.Add(Warning(
                    "MDOC022",
                    "InstanceSubjectMissingDisplayName",
                    $"Instance subject '{subject.Id}' has no display-name fallback.",
                    subject.Id));
            }

            if (!includedEntities.Contains(entityName))
            {
                diagnostics.Add(Warning(
                    "MDOC019",
                    "InstanceSubjectNotAllowedByPolicy",
                    $"Instance subject '{subject.Id}' is active but entity '{entityName}' is not allowed by current instance policy.",
                    subject.Id));
            }
        }

        foreach (var fact in model.DocumentationFactList
                     .Where(fact => string.Equals(fact.Kind, "InstancePropertyValue", StringComparison.OrdinalIgnoreCase))
                     .Where(fact => !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase)))
        {
            var subject = model.DocumentationSubjectList.FirstOrDefault(subject =>
                string.Equals(subject.Id, fact.SubjectKey, StringComparison.OrdinalIgnoreCase));
            var entityName = subject?.NativeKind ?? string.Empty;
            if (!includedPropertiesByEntity.TryGetValue(entityName, out var properties) ||
                !properties.Contains(fact.Name))
            {
                diagnostics.Add(Warning(
                    "MDOC021",
                    "InstancePropertyFactNotAllowedByPolicy",
                    $"Instance property fact '{fact.Id}' is active but property '{fact.Name}' is not allowed by current instance policy.",
                    fact.Id));
            }
        }

        foreach (var relationship in model.DocumentationRelationshipList
                     .Where(relationship => relationship.Kind.StartsWith("InstanceRelationship:", StringComparison.OrdinalIgnoreCase)))
        {
            if (!subjectIds.Contains(relationship.FromSubjectKey) || !subjectIds.Contains(relationship.ToSubjectKey))
            {
                diagnostics.Add(Error(
                    "MDOC020",
                    "BrokenInstanceRelationship",
                    $"Instance relationship '{relationship.Id}' references missing subject(s).",
                    relationship.Id));
            }
        }
    }

    private static void CheckCliReference(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        var activeSubjects = model.DocumentationSubjectList
            .Where(IsActive)
            .ToArray();
        var activeById = activeSubjects.ToDictionary(subject => subject.Id, StringComparer.OrdinalIgnoreCase);
        var activeChildrenByParent = activeSubjects
            .Where(subject => !string.IsNullOrWhiteSpace(subject.ParentKey))
            .GroupBy(subject => subject.ParentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var application in activeSubjects.Where(subject => string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase)))
        {
            var commands = CliCommandDescendants(activeChildrenByParent, application.Id).ToArray();
            if (commands.Length == 0)
            {
                diagnostics.Add(Warning(
                    "MDOC024",
                    "CliApplicationHasNoCommands",
                    $"CLI application '{application.DisplayName}' has no active command subjects.",
                    application.Id));
            }

            foreach (var group in commands
                         .Where(command => !string.IsNullOrWhiteSpace(command.DisplayPath))
                         .GroupBy(command => command.DisplayPath!, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                diagnostics.Add(Warning(
                    "MDOC027",
                    "DuplicateCliCommandDisplayPath",
                    $"CLI application '{application.DisplayName}' has duplicate command display path '{group.Key}'.",
                    string.Join(", ", group.Select(command => command.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))));
            }
        }

        foreach (var command in activeSubjects.Where(subject => string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(command.ParentKey) ||
                !activeById.TryGetValue(command.ParentKey, out var parent) ||
                (!string.Equals(parent.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parent.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Error(
                    "MDOC025",
                    "CliCommandMissingApplication",
                    $"CLI command '{command.Id}' is not parented by an active CLI application or CLI command.",
                    command.Id));
            }

            if (GetNumericFact(model, command.Id, "Cli", "UsageCount") > 0 &&
                !HasFact(model, command, "Usage"))
            {
                diagnostics.Add(Warning(
                    "MDOC028",
                    "CliCommandUsageFactsMissing",
                    $"CLI command '{command.Id}' declares usage rows but no usage facts are present.",
                    command.Id));
            }

            var optionCount = GetNumericFact(model, command.Id, "Cli", "OptionCount");
            var optionChildren = activeChildrenByParent.TryGetValue(command.Id, out var children)
                ? children.Count(subject => string.Equals(subject.Kind, "CliOption", StringComparison.OrdinalIgnoreCase))
                : 0;
            if (optionCount > 0 && optionChildren == 0)
            {
                diagnostics.Add(Warning(
                    "MDOC029",
                    "CliCommandOptionSubjectsMissing",
                    $"CLI command '{command.Id}' declares options but no active option subjects are present.",
                    command.Id));
            }

            if (!HasAuthoredOrImportedNarrative(model, command.Id))
            {
                diagnostics.Add(Info(
                    "MDOC030",
                    "CliCommandProseMissing",
                    $"CLI command '{command.Id}' has no authored or imported command prose.",
                    command.Id));
            }
        }

        foreach (var option in activeSubjects.Where(subject => string.Equals(subject.Kind, "CliOption", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(option.ParentKey) ||
                !activeById.TryGetValue(option.ParentKey, out var parent) ||
                (!string.Equals(parent.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(parent.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Error(
                    "MDOC026",
                    "CliOptionMissingOwner",
                    $"CLI option '{option.Id}' is not parented by an active CLI application or CLI command.",
                    option.Id));
            }
        }
    }

    private static IEnumerable<DocumentationSubject> CliCommandDescendants(
        IReadOnlyDictionary<string, DocumentationSubject[]> activeChildrenByParent,
        string parentKey)
    {
        if (!activeChildrenByParent.TryGetValue(parentKey, out var children))
        {
            yield break;
        }

        foreach (var command in children
                     .Where(subject => string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(subject => ParseOrdinal(subject.Ordinal))
                     .ThenBy(subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            yield return command;
            foreach (var descendant in CliCommandDescendants(activeChildrenByParent, command.Id))
            {
                yield return descendant;
            }
        }
    }

    private static void CheckPublicReferenceView(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        var view = model.DocumentationViewList.FirstOrDefault(row =>
            string.Equals(row.Id, "view:default", StringComparison.OrdinalIgnoreCase));
        if (view is null)
        {
            return;
        }

        var nodes = model.DocumentationViewNodeList
            .Where(node => node.DocumentationView is not null &&
                           string.Equals(node.DocumentationView.Id, view.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var nodesById = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Id))
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var subjectsById = model.DocumentationSubjectList
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var publicSubjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in nodes
                     .GroupBy(node => $"{node.ParentNodeId ?? string.Empty}|{node.Title}", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Warning(
                "MDOC034",
                "DuplicatePublicViewNodeTitle",
                $"Public view contains duplicate node title '{group.First().Title}' under the same parent.",
                string.Join(", ", group.Select(node => node.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))));
        }

        foreach (var node in nodes)
        {
            if (IsStalePublicTitle(node.Title) ||
                string.IsNullOrWhiteSpace(node.ParentNodeId) &&
                string.Equals(node.Title, "MetaDocs", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    "MDOC031",
                    "StalePublicViewNode",
                    $"Public view node '{node.Id}' uses stale public documentation title '{node.Title}'.",
                    node.Id));
            }

            if (string.IsNullOrWhiteSpace(node.SubjectKey))
            {
                continue;
            }

            if (!subjectsById.TryGetValue(node.SubjectKey!, out var subject))
            {
                continue;
            }

            if (!MetaDocsPublicReferenceClassifier.TryClassify(model, subject, out var classification))
            {
                diagnostics.Add(Warning(
                    "MDOC032",
                    "NonReferenceSubjectInPublicView",
                    $"Public view node '{node.Id}' references non-public subject '{subject.DisplayName}' of kind '{subject.Kind}'.",
                    node.Id));
                continue;
            }

            publicSubjectIds.Add(subject.Id);
            if (!PublicViewParentageMatches(nodesById, node, classification))
            {
                diagnostics.Add(Warning(
                    "MDOC033",
                    "PublicReferenceMisclassified",
                    $"Public view node '{node.Id}' is not under {MetaDocsPublicReferenceClassifier.FormatProductFamily(classification.ProductFamily)} / {MetaDocsPublicReferenceClassifier.FormatReferenceSurface(classification.Surface)}.",
                    node.Id));
            }
        }

        foreach (var subject in model.DocumentationSubjectList.Where(subject =>
                     MetaDocsPublicReferenceClassifier.TryClassify(model, subject, out _)))
        {
            if (!publicSubjectIds.Contains(subject.Id))
            {
                diagnostics.Add(Warning(
                    "MDOC035",
                    "PublicReferenceSubjectMissingFromView",
                    $"Public reference subject '{subject.DisplayName}' is not present in the default public view.",
                    subject.Id));
            }
        }
    }

    private static DocumentationSubject? FindModelEntitySubject(MetaDocsModel model, string sourceId, string entityName)
    {
        var expectedId = string.IsNullOrWhiteSpace(sourceId)
            ? string.Empty
            : $"{sourceId}:entity:{MetaDocsImportSession.NormalizeKey(entityName)}";
        if (!string.IsNullOrWhiteSpace(expectedId))
        {
            var exact = model.DocumentationSubjectList.FirstOrDefault(subject =>
                string.Equals(subject.Id, expectedId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        return model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Kind, "Entity", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(subject.NativeId ?? string.Empty, entityName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, entityName, StringComparison.OrdinalIgnoreCase)));
    }

    private static DocumentationSubject? FindModelPropertySubject(
        MetaDocsModel model,
        DocumentationSubject entitySubject,
        string propertyName) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Kind, "Property", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(subject.ParentKey ?? string.Empty, entitySubject.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(subject.NativeId ?? string.Empty, propertyName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase)));

    private static DocumentationSubject? FindModelRelationshipSubject(
        MetaDocsModel model,
        DocumentationSubject entitySubject,
        string relationshipSelector) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Kind, "Relationship", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(subject.ParentKey ?? string.Empty, entitySubject.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(subject.NativeId ?? string.Empty, relationshipSelector, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, relationshipSelector, StringComparison.OrdinalIgnoreCase) ||
             HasFact(model, subject, "Model", "ColumnName", relationshipSelector)));

    private static bool IsActive(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

    private static int ParseOrdinal(string? value) =>
        int.TryParse(value, out var ordinal) ? ordinal : int.MaxValue;

    private static bool IsIncluded(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("include", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("included", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("enabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind,
        string name,
        string value) =>
        model.DocumentationFactList.Any(fact =>
            string.Equals(fact.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fact.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fact.Value ?? string.Empty, value, StringComparison.OrdinalIgnoreCase));

    private static bool HasFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind) =>
        model.DocumentationFactList.Any(fact =>
            string.Equals(fact.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fact.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase));

    private static int GetNumericFact(
        MetaDocsModel model,
        string subjectKey,
        string kind,
        string name)
    {
        var value = model.DocumentationFactList
            .Where(fact =>
                string.Equals(fact.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fact.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value)
            .FirstOrDefault();
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static bool HasAuthoredOrImportedNarrative(MetaDocsModel model, string subjectKey) =>
        model.DocumentationNarrativeList.Any(narrative =>
            string.Equals(narrative.SubjectKey, subjectKey, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(narrative.Body) &&
            (string.Equals(narrative.Origin, "Authored", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(narrative.Origin, "ImportedMarkdown", StringComparison.OrdinalIgnoreCase)));

    private static bool PublicViewParentageMatches(
        IReadOnlyDictionary<string, DocumentationViewNode> nodesById,
        DocumentationViewNode node,
        MetaDocsPublicReferenceClassification classification)
    {
        var expectedSurface = MetaDocsPublicReferenceClassifier.FormatReferenceSurface(classification.Surface);
        var expectedFamily = MetaDocsPublicReferenceClassifier.FormatProductFamily(classification.ProductFamily);
        var hasSurface = false;
        var hasFamily = false;
        var current = node;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current.ParentNodeId) &&
               nodesById.TryGetValue(current.ParentNodeId!, out var parent) &&
               guard++ < 16)
        {
            if (string.Equals(parent.Title, expectedSurface, StringComparison.OrdinalIgnoreCase))
            {
                hasSurface = true;
            }

            if (string.Equals(parent.Title, expectedFamily, StringComparison.OrdinalIgnoreCase))
            {
                hasFamily = true;
            }

            current = parent;
        }

        return hasSurface && hasFamily;
    }

    private static bool IsStalePublicTitle(string? title) =>
        string.Equals(title, "What is Meta?", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "What is Meta-BI?", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "Getting started", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "Documentation lifecycle", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "Selected safe instance docs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "Generic workspace and model docs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(title, "Authored docs page", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldExpectNarrative(DocumentationSubject subject) =>
        string.Equals(subject.Kind, "CliApplication", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.Kind, "Model", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.Kind, "Entity", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.Kind, "Concept", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.Kind, "Guide", StringComparison.OrdinalIgnoreCase);

    private static MetaDocsDiagnostic Error(string id, string code, string message, string location) =>
        new(MetaDocsDiagnosticSeverity.Error, id, code, message, location);

    private static MetaDocsDiagnostic Warning(string id, string code, string message, string location) =>
        new(MetaDocsDiagnosticSeverity.Warning, id, code, message, location);

    private static MetaDocsDiagnostic Info(string id, string code, string message, string location) =>
        new(MetaDocsDiagnosticSeverity.Info, id, code, message, location);
}

public sealed record MetaDocsValidationResult(IReadOnlyList<MetaDocsDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == MetaDocsDiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == MetaDocsDiagnosticSeverity.Warning);

    public int InfoCount => Diagnostics.Count(diagnostic => diagnostic.Severity == MetaDocsDiagnosticSeverity.Info);

    public bool HasErrors(bool warningsAsErrors = false) =>
        ErrorCount > 0 || warningsAsErrors && WarningCount > 0;
}

public sealed record MetaDocsDiagnostic(
    MetaDocsDiagnosticSeverity Severity,
    string Id,
    string Code,
    string Message,
    string Location);

public enum MetaDocsDiagnosticSeverity
{
    Error,
    Warning,
    Info,
}
