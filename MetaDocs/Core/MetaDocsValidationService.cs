using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsValidationService
{
    public MetaDocsValidationResult Validate(MetaDocsModel model, MetaDocsValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= MetaDocsValidationOptions.Default;

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

        CheckDuplicateSubjectIds(model, diagnostics);
        CheckDuplicateDisplayPaths(model, diagnostics);
        CheckFactReferences(model, diagnostics, subjectIds, sourceIds, batchIds);
        CheckNarrativeReferences(model, diagnostics, subjectIds);
        CheckExamples(model, diagnostics, subjectIds);
        CheckRelationships(model, diagnostics, subjectIds, sourceIds, batchIds);
        CheckViews(model, diagnostics, subjectsById);
        CheckNarrativeReviewState(model, diagnostics, options.IncludeDescriptionDiagnostics);
        if (options.IncludeDescriptionDiagnostics)
        {
            CheckMissingExpectedNarratives(model, diagnostics);
        }
        CheckAliases(model, diagnostics, subjectIds);
        CheckTheme(model, diagnostics);
        CheckImportSpecs(model, diagnostics, sourceIds);
        CheckInstancePolicies(model, diagnostics);
        CheckInstanceDocs(model, diagnostics, subjectIds);
        CheckCliReference(model, diagnostics, options.IncludeDescriptionDiagnostics);

        return new MetaDocsValidationResult(diagnostics);
    }

    private static void CheckDuplicateSubjectIds(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        foreach (var group in model.DocumentationSubjectList
                     .Where(subject => !string.IsNullOrWhiteSpace(subject.Id))
                     .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            diagnostics.Add(Error(
                "MDOC001",
                "DuplicateSubjectId",
                $"DocumentationSubject id '{group.Key}' appears {group.Count()} times.",
                group.Key));
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
            if (fact.DocumentationSubject is null ||
                !subjectIds.Contains(fact.DocumentationSubject.Id))
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
            if (narrative.DocumentationSubject is null ||
                !subjectIds.Contains(narrative.DocumentationSubject.Id))
            {
                diagnostics.Add(Error(
                    "MDOC004",
                    "MissingNarrativeSubject",
                    $"DocumentationNarrative '{narrative.Id}' has an inconsistent subject reference.",
                    narrative.Id));
            }
        }
    }

    private static void CheckExamples(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics,
        ISet<string> subjectIds)
    {
        var exampleIds = model.DocumentationExampleList
            .Select(example => example.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sectionIds = model.DocumentationExampleSectionList
            .Select(section => section.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sectionsByExample = model.DocumentationExampleSectionList
            .Where(section => section.DocumentationExample is not null)
            .GroupBy(section => section.DocumentationExample.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var codesBySection = model.DocumentationExampleCodeList
            .Where(code => code.DocumentationExampleSection is not null)
            .GroupBy(code => code.DocumentationExampleSection.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var example in model.DocumentationExampleList)
        {
            if (example.DocumentationSubject is null || !subjectIds.Contains(example.DocumentationSubject.Id))
            {
                diagnostics.Add(Error(
                    "MDOC036",
                    "BrokenExampleSubject",
                    $"DocumentationExample '{example.Id}' references a missing subject.",
                    example.Id));
            }

            if (example.PreviousExample is not null &&
                (!exampleIds.Contains(example.PreviousExample.Id) ||
                 !ReferenceEquals(example.PreviousExample.DocumentationSubject, example.DocumentationSubject)))
            {
                diagnostics.Add(Error(
                    "MDOC037",
                    "BrokenExampleOrder",
                    $"DocumentationExample '{example.Id}' has a previous example outside the same subject.",
                    example.Id));
            }

            if (!sectionsByExample.ContainsKey(example.Id))
            {
                diagnostics.Add(Error(
                    "MDOC038",
                    "EmptyDocumentationExample",
                    $"DocumentationExample '{example.Id}' has no sections.",
                    example.Id));
            }
        }

        foreach (var section in model.DocumentationExampleSectionList)
        {
            if (section.DocumentationExample is null || !exampleIds.Contains(section.DocumentationExample.Id))
            {
                diagnostics.Add(Error(
                    "MDOC039",
                    "BrokenExampleSection",
                    $"DocumentationExampleSection '{section.Id}' references a missing example.",
                    section.Id));
            }

            if (section.PreviousSection is not null &&
                (!sectionIds.Contains(section.PreviousSection.Id) ||
                 !ReferenceEquals(section.PreviousSection.DocumentationExample, section.DocumentationExample)))
            {
                diagnostics.Add(Error(
                    "MDOC040",
                    "BrokenExampleSectionOrder",
                    $"DocumentationExampleSection '{section.Id}' has a previous section outside the same example.",
                    section.Id));
            }

            if (string.IsNullOrWhiteSpace(section.Body) &&
                (!codesBySection.TryGetValue(section.Id, out var codeRows) || codeRows.Length == 0))
            {
                diagnostics.Add(Error(
                    "MDOC041",
                    "EmptyExampleSection",
                    $"DocumentationExampleSection '{section.Id}' has neither text nor code.",
                    section.Id));
            }
        }

        foreach (var code in model.DocumentationExampleCodeList)
        {
            if (code.DocumentationExampleSection is null || !sectionIds.Contains(code.DocumentationExampleSection.Id))
            {
                diagnostics.Add(Error(
                    "MDOC042",
                    "BrokenExampleCode",
                    $"DocumentationExampleCode '{code.Id}' references a missing example section.",
                    code.Id));
            }

            if (code.PreviousCode is not null &&
                (!model.DocumentationExampleCodeList.Contains(code.PreviousCode) ||
                 !ReferenceEquals(code.PreviousCode.DocumentationExampleSection, code.DocumentationExampleSection)))
            {
                diagnostics.Add(Error(
                    "MDOC043",
                    "BrokenExampleCodeOrder",
                    $"DocumentationExampleCode '{code.Id}' has a previous code block outside the same section.",
                    code.Id));
            }

            if (string.IsNullOrWhiteSpace(code.Code))
            {
                diagnostics.Add(Error(
                    "MDOC044",
                    "EmptyExampleCode",
                    $"DocumentationExampleCode '{code.Id}' has no code text.",
                    code.Id));
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
            if (relationship.FromSubject is null ||
                relationship.ToSubject is null ||
                !subjectIds.Contains(relationship.FromSubject.Id) ||
                !subjectIds.Contains(relationship.ToSubject.Id))
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
        foreach (var node in model.DocumentationViewNodeList.Where(node => node.DocumentationSubject is not null))
        {
            if (!subjectsById.TryGetValue(node.DocumentationSubject!.Id, out var subjects))
            {
                diagnostics.Add(Error(
                    "MDOC006",
                    "BrokenViewNode",
                    $"DocumentationViewNode '{node.Id}' references a missing subject.",
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
        ICollection<MetaDocsDiagnostic> diagnostics,
        bool includeDescriptionDiagnostics)
    {
        var subjectsById = model.DocumentationSubjectList
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var narrative in model.DocumentationNarrativeList)
        {
            if (includeDescriptionDiagnostics &&
                (string.Equals(narrative.ReviewStatus, "NeedsReview", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(narrative.ReviewStatus, "NeedsAuthoring", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(Warning(
                    "MDOC008",
                    "NarrativeNeedsReview",
                    $"DocumentationNarrative '{narrative.Id}' review status is '{narrative.ReviewStatus}'.",
                    narrative.Id));
            }

            if (narrative.DocumentationSubject is not null &&
                subjectsById.TryGetValue(narrative.DocumentationSubject.Id, out var subject) &&
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
            .Where(narrative => narrative.DocumentationSubject is not null)
            .Select(narrative => narrative.DocumentationSubject.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedFactSubjects = model.DocumentationFactList
            .Where(fact => !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Where(fact => fact.DocumentationSubject is not null)
            .Select(fact => fact.DocumentationSubject.Id)
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
            if (alias.DocumentationSubject is null || !subjectIds.Contains(alias.DocumentationSubject.Id))
            {
                diagnostics.Add(Error(
                    "MDOC013",
                    "BrokenAlias",
                    $"DocumentationSubjectAlias '{alias.Id}' references a missing subject.",
                    alias.Id));
            }
        }

        foreach (var group in model.DocumentationSubjectAliasList
                     .Where(alias => !string.IsNullOrWhiteSpace(alias.Alias))
                     .Where(alias => alias.DocumentationSubject is not null)
                     .GroupBy(alias => alias.Alias, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(alias => alias.DocumentationSubject.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            diagnostics.Add(Warning(
                "MDOC014",
                "AmbiguousAlias",
                $"Alias '{group.Key}' resolves to more than one subject.",
                string.Join(", ", group.Select(alias => alias.DocumentationSubject.Id).Distinct(StringComparer.OrdinalIgnoreCase))));
        }
    }

    private static void CheckTheme(
        MetaDocsModel model,
        ICollection<MetaDocsDiagnostic> diagnostics)
    {
        if (!model.DocumentationThemeAssetList.Any(asset =>
                MetaDocsVocabulary.IsThemeAssetType(asset, "Css") &&
                !string.IsNullOrWhiteSpace(asset.Content)))
        {
            diagnostics.Add(Error(
                "MDOC010",
                "MissingThemeAsset",
                "No modeled CSS DocumentationThemeAsset with content is available for render-site.",
                "DocumentationThemeAsset"));
        }

        if (!model.DocumentationTemplateList.Any(template =>
                MetaDocsVocabulary.IsTemplateType(template, "SiteShell") &&
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
                     .Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "Instance"))
                     .Where(IsActive))
        {
            var entityName = subject.SourceTypeName ?? string.Empty;
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
                     .Where(fact => MetaDocsVocabulary.IsFactType(fact, "InstancePropertyValue"))
                     .Where(fact => !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase)))
        {
            var entityName = fact.DocumentationSubject?.SourceTypeName ?? string.Empty;
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
                     .Where(relationship => MetaDocsVocabulary.RelationshipTypeName(relationship).StartsWith("InstanceRelationship:", StringComparison.OrdinalIgnoreCase)))
        {
            if (relationship.FromSubject is null ||
                relationship.ToSubject is null ||
                !subjectIds.Contains(relationship.FromSubject.Id) ||
                !subjectIds.Contains(relationship.ToSubject.Id))
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
        ICollection<MetaDocsDiagnostic> diagnostics,
        bool includeDescriptionDiagnostics)
    {
        var activeSubjects = model.DocumentationSubjectList
            .Where(IsActive)
            .ToArray();
        var activeById = activeSubjects
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var activeChildrenByParent = activeSubjects
            .Where(subject => subject.ParentSubject is not null)
            .GroupBy(subject => subject.ParentSubject!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var application in activeSubjects.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliApplication")))
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

        foreach (var command in activeSubjects.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliCommand")))
        {
            if (command.ParentSubject is null ||
                !activeById.TryGetValue(command.ParentSubject.Id, out var parent) ||
                (!MetaDocsVocabulary.IsSubjectType(parent, "CliApplication") &&
                 !MetaDocsVocabulary.IsSubjectType(parent, "CliCommand")))
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
                ? children.Count(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliOption"))
                : 0;
            if (optionCount > 0 && optionChildren == 0)
            {
                diagnostics.Add(Warning(
                    "MDOC029",
                    "CliCommandOptionSubjectsMissing",
                    $"CLI command '{command.Id}' declares options but no active option subjects are present.",
                    command.Id));
            }

            if (includeDescriptionDiagnostics && !HasAuthoredOrImportedNarrative(model, command.Id))
            {
                diagnostics.Add(Info(
                    "MDOC030",
                    "CliCommandDescriptionMissing",
                    $"CLI command '{command.Id}' has no authored or imported command description.",
                    command.Id));
            }
        }

        foreach (var option in activeSubjects.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliOption")))
        {
            if (option.ParentSubject is null ||
                !activeById.TryGetValue(option.ParentSubject.Id, out var parent) ||
                (!MetaDocsVocabulary.IsSubjectType(parent, "CliApplication") &&
                 !MetaDocsVocabulary.IsSubjectType(parent, "CliCommand")))
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

            foreach (var command in MetaDocsOrdering.ByPrevious(
                     children.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliCommand")),
                     static subject => subject.PreviousSubject,
                     static subject => subject.DisplayName))
        {
            yield return command;
            foreach (var descendant in CliCommandDescendants(activeChildrenByParent, command.Id))
            {
                yield return descendant;
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
            MetaDocsVocabulary.IsSubjectType(subject, "Entity") &&
            (string.Equals(subject.NativeId ?? string.Empty, entityName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, entityName, StringComparison.OrdinalIgnoreCase)));
    }

    private static DocumentationSubject? FindModelPropertySubject(
        MetaDocsModel model,
        DocumentationSubject entitySubject,
        string propertyName) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            MetaDocsVocabulary.IsSubjectType(subject, "Property") &&
            string.Equals(subject.ParentSubject?.Id ?? string.Empty, entitySubject.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(subject.NativeId ?? string.Empty, propertyName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase)));

    private static DocumentationSubject? FindModelRelationshipSubject(
        MetaDocsModel model,
        DocumentationSubject entitySubject,
        string relationshipSelector) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            MetaDocsVocabulary.IsSubjectType(subject, "Relationship") &&
            string.Equals(subject.ParentSubject?.Id ?? string.Empty, entitySubject.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(subject.NativeId ?? string.Empty, relationshipSelector, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(subject.DisplayName, relationshipSelector, StringComparison.OrdinalIgnoreCase) ||
             HasFact(model, subject, "Model", "ColumnName", relationshipSelector)));

    private static bool IsActive(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase);

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
            ReferenceEquals(fact.DocumentationSubject, subject) &&
            MetaDocsVocabulary.IsFactType(fact, kind) &&
            string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fact.Value ?? string.Empty, value, StringComparison.OrdinalIgnoreCase));

    private static bool HasFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind) =>
        model.DocumentationFactList.Any(fact =>
            ReferenceEquals(fact.DocumentationSubject, subject) &&
            MetaDocsVocabulary.IsFactType(fact, kind) &&
            !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase));

    private static int GetNumericFact(
        MetaDocsModel model,
        string subjectKey,
        string kind,
        string name)
    {
        var value = model.DocumentationFactList
            .Where(fact =>
                string.Equals(fact.DocumentationSubject?.Id ?? string.Empty, subjectKey, StringComparison.OrdinalIgnoreCase) &&
                MetaDocsVocabulary.IsFactType(fact, kind) &&
                string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value)
            .FirstOrDefault();
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string GetTextFact(
        MetaDocsModel model,
        string subjectKey,
        string kind,
        string name) =>
        model.DocumentationFactList
            .Where(fact =>
                string.Equals(fact.DocumentationSubject?.Id ?? string.Empty, subjectKey, StringComparison.OrdinalIgnoreCase) &&
                MetaDocsVocabulary.IsFactType(fact, kind) &&
                string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fact.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(fact => fact.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool HasAuthoredOrImportedNarrative(MetaDocsModel model, string subjectKey) =>
        model.DocumentationNarrativeList.Any(narrative =>
            string.Equals(narrative.DocumentationSubject?.Id ?? string.Empty, subjectKey, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(narrative.Body) &&
            (string.Equals(narrative.Origin, "Authored", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(narrative.Origin, "ImportedMarkdown", StringComparison.OrdinalIgnoreCase)));

    private static bool ShouldExpectNarrative(DocumentationSubject subject) =>
        MetaDocsVocabulary.IsSubjectType(subject, "CliApplication") ||
        MetaDocsVocabulary.IsSubjectType(subject, "CliCommand") ||
        MetaDocsVocabulary.IsSubjectType(subject, "Model") ||
        MetaDocsVocabulary.IsSubjectType(subject, "Entity") ||
        MetaDocsVocabulary.IsSubjectType(subject, "Concept") ||
        MetaDocsVocabulary.IsSubjectType(subject, "Guide");

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

public sealed class MetaDocsValidationOptions
{
    public static MetaDocsValidationOptions Default { get; } = new();

    public bool IncludeDescriptionDiagnostics { get; init; }
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
