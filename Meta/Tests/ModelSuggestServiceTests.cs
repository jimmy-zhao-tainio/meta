using System;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;
using DomainWorkspace = Meta.Core.Domain.Workspace;

namespace Meta.Core.Tests;

public sealed class ModelSuggestServiceTests
{
    [Fact]
    public void Analyze_DemoProducesEligibleIdBasedRelationshipSuggestions()
    {
        var workspace = BuildDemoWorkspace();

        var report = ModelSuggestService.Analyze(workspace);

        Assert.Contains(report.BusinessKeys, item => IsTarget(item, "Warehouse", "Id"));
        Assert.Contains(report.BusinessKeys, item => IsTarget(item, "Product", "Id"));
        Assert.Contains(report.BusinessKeys, item => IsTarget(item, "Supplier", "Id"));
        Assert.Contains(report.BusinessKeys, item => IsTarget(item, "Category", "Id"));

        AssertEligible(report, "Order", "WarehouseId", "Warehouse", "Id");
        AssertEligible(report, "Order", "ProductId", "Product", "Id");
        AssertEligible(report, "Order", "SupplierId", "Supplier", "Id");
    }

    [Fact]
    public void Analyze_UnmatchedSourceValue_IsBlockedAndNotEligible()
    {
        var workspace = BuildDemoWorkspace();
        AddRow(workspace.Instance, "Order", "6",
            ("OrderNumber", "ORD-1006"),
            ("ProductId", "404"),
            ("SupplierId", "1"),
            ("WarehouseId", "1"),
            ("StatusText", "Held"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Order", "ProductId", "Product", "Id");
        var blocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Order", "ProductId", "Product", "Id");
        Assert.Equal(LookupCandidateStatus.Blocked, blocked.Status);
        Assert.Contains("Source values not fully resolvable against target key.", blocked.Blockers);
        Assert.Contains("404", blocked.UnmatchedDistinctValuesSample);
    }

    [Fact]
    public void Analyze_TargetDuplicateLookupKey_IsBlocked()
    {
        var workspace = BuildDemoWorkspace();
        AddRow(workspace.Instance, "Warehouse", "WH-001", ("WarehouseName", "Seattle Duplicate"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Order", "WarehouseId", "Warehouse", "Id");
        var blocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Order", "WarehouseId", "Warehouse", "Id");
        Assert.Equal(LookupCandidateStatus.Blocked, blocked.Status);
        Assert.Contains("Target lookup key is not unique.", blocked.Blockers);
    }

    [Fact]
    public void Analyze_SourceNullOrBlank_IsBlocked()
    {
        var workspace = BuildDemoWorkspace();
        AddRow(workspace.Instance, "Order", "6",
            ("OrderNumber", "ORD-1006"),
            ("ProductId", string.Empty),
            ("SupplierId", "1"),
            ("WarehouseId", "1"),
            ("StatusText", "Held"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Order", "ProductId", "Product", "Id");
        var blocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Order", "ProductId", "Product", "Id");
        Assert.Equal(LookupCandidateStatus.Blocked, blocked.Status);
        Assert.Contains("Source contains null/blank; required relationship cannot be created.", blocked.Blockers);
    }

    [Fact]
    public void Analyze_TargetNullOrBlank_IsBlocked()
    {
        var workspace = BuildDemoWorkspace();
        AddRow(workspace.Instance, "Product", string.Empty, ("ProductName", "Unknown"), ("ProductGroup", "Cycles"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Order", "ProductId", "Product", "Id");
        var blocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Order", "ProductId", "Product", "Id");
        Assert.Equal(LookupCandidateStatus.Blocked, blocked.Status);
        Assert.Contains("Target lookup key has null/blank values.", blocked.Blockers);
    }

    [Fact]
    public void Analyze_ExistingRelationship_IsBlockedAndNotEligible()
    {
        var workspace = BuildDemoWorkspace();
        workspace.Model.FindEntity("Order")!.Relationships.Add(new GenericRelationship
        {
            Entity = "Warehouse",
        });

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Order", "WarehouseId", "Warehouse", "Id");
        var blocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Order", "WarehouseId", "Warehouse", "Id");
        Assert.Equal(LookupCandidateStatus.Blocked, blocked.Status);
        Assert.Contains("Relationship 'Order.WarehouseId' already exists.", blocked.Blockers);
    }

    [Fact]
    public void Analyze_RoleStyleIdSuffix_IsReportedAsWeakSuggestion()
    {
        var workspace = CreateWorkspaceSkeleton();
        AddEntity(workspace.Model, "Product", ("ProductName", "string"));
        AddEntity(workspace.Model, "Order",
            ("OrderNumber", "string"),
            ("SourceProductId", "string"));

        AddRow(workspace.Instance, "Product", "PRD-001", ("ProductName", "Road Bike"));
        AddRow(workspace.Instance, "Product", "PRD-002", ("ProductName", "Bottle Cage"));

        AddRow(workspace.Instance, "Order", "ORD-001", ("OrderNumber", "ORD-1001"), ("SourceProductId", "PRD-001"));
        AddRow(workspace.Instance, "Order", "ORD-002", ("OrderNumber", "ORD-1002"), ("SourceProductId", "PRD-002"));
        AddRow(workspace.Instance, "Order", "ORD-003", ("OrderNumber", "ORD-1003"), ("SourceProductId", "PRD-001"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertDoesNotContainTarget(report.EligibleRelationshipSuggestions, "Order", "SourceProductId", "Product", "Id");
        var weak = Assert.Single(report.WeakRelationshipSuggestions);
        Assert.Equal("Order", weak.Source.EntityName);
        Assert.Equal("SourceProductId", weak.Source.PropertyName);

        var candidate = Assert.Single(weak.Candidates);
        Assert.Equal("Product", candidate.TargetLookup.EntityName);
        Assert.Equal("Id", candidate.TargetLookup.PropertyName);
        Assert.Equal("SourceProduct", candidate.Role);
        Assert.Equal(LookupCandidateStatus.Eligible, candidate.Status);
    }

    [Fact]
    public void Analyze_PropertyMatchingMoreThanOneTarget_IsReportedAsWeakAmbiguousSuggestion()
    {
        var workspace = CreateWorkspaceSkeleton();
        AddEntity(workspace.Model, "Type", ("TypeName", "string"));
        AddEntity(workspace.Model, "ReferenceType", ("ReferenceTypeName", "string"));
        AddEntity(workspace.Model, "Mapping",
            ("ReferenceTypeId", "string"),
            ("MappingName", "string"));

        AddRow(workspace.Instance, "Type", "TYPE-001", ("TypeName", "Alpha"));
        AddRow(workspace.Instance, "Type", "TYPE-002", ("TypeName", "Beta"));

        AddRow(workspace.Instance, "ReferenceType", "TYPE-001", ("ReferenceTypeName", "Alpha"));
        AddRow(workspace.Instance, "ReferenceType", "TYPE-002", ("ReferenceTypeName", "Beta"));

        AddRow(workspace.Instance, "Mapping", "MAP-001", ("ReferenceTypeId", "TYPE-001"), ("MappingName", "One"));
        AddRow(workspace.Instance, "Mapping", "MAP-002", ("ReferenceTypeId", "TYPE-002"), ("MappingName", "Two"));
        AddRow(workspace.Instance, "Mapping", "MAP-003", ("ReferenceTypeId", "TYPE-001"), ("MappingName", "Three"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertDoesNotContainTarget(report.EligibleRelationshipSuggestions, "Mapping", "ReferenceTypeId", "ReferenceType", "Id");
        AssertDoesNotContainTarget(report.EligibleRelationshipSuggestions, "Mapping", "ReferenceTypeId", "Type", "Id");

        var weak = Assert.Single(report.WeakRelationshipSuggestions);
        Assert.Equal("Mapping", weak.Source.EntityName);
        Assert.Equal("ReferenceTypeId", weak.Source.PropertyName);
        Assert.Equal(2, weak.Candidates.Count);

        Assert.Contains(weak.Candidates, item =>
            string.Equals(item.TargetLookup.EntityName, "ReferenceType", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetLookup.PropertyName, "Id", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Role, string.Empty, StringComparison.Ordinal));
        Assert.Contains(weak.Candidates, item =>
            string.Equals(item.TargetLookup.EntityName, "Type", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetLookup.PropertyName, "Id", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Role, "ReferenceType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_SymmetricPeerKeys_AreBlockedAsAmbiguous()
    {
        var workspace = CreateWorkspaceSkeleton();
        AddEntity(workspace.Model, "Left", ("PeerId", "int"));
        AddEntity(workspace.Model, "Right", ("PeerId", "int"));

        AddRow(workspace.Instance, "Left", "1", ("PeerId", "1"));
        AddRow(workspace.Instance, "Left", "2", ("PeerId", "2"));
        AddRow(workspace.Instance, "Right", "1", ("PeerId", "1"));
        AddRow(workspace.Instance, "Right", "2", ("PeerId", "2"));

        var report = ModelSuggestService.Analyze(workspace);

        AssertNotEligible(report, "Left", "PeerId", "Right", "PeerId");
        AssertNotEligible(report, "Right", "PeerId", "Left", "PeerId");

        var leftBlocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Left", "PeerId", "Right", "PeerId");
        Assert.Equal(LookupCandidateStatus.Blocked, leftBlocked.Status);
        Assert.Contains("Source does not show reuse; lookup direction is ambiguous.", leftBlocked.Blockers);

        var rightBlocked = ModelSuggestService.AnalyzeLookupRelationship(workspace, "Right", "PeerId", "Left", "PeerId");
        Assert.Equal(LookupCandidateStatus.Blocked, rightBlocked.Status);
        Assert.Contains("Source does not show reuse; lookup direction is ambiguous.", rightBlocked.Blockers);
    }

    [Fact]
    public void Analyze_IsDeterministic_ForStableWorkspaceState()
    {
        var workspace = BuildDemoWorkspace();

        var first = ModelSuggestService.Analyze(workspace);
        var second = ModelSuggestService.Analyze(workspace);

        var firstEligible = first.EligibleRelationshipSuggestions
            .Select(ToProjection)
            .ToArray();
        var secondEligible = second.EligibleRelationshipSuggestions
            .Select(ToProjection)
            .ToArray();
        Assert.Equal(firstEligible, secondEligible);

        var firstWeak = first.WeakRelationshipSuggestions
            .Select(item => item.Source.EntityName + "|" + item.Source.PropertyName + "|" + string.Join(";", item.Candidates.Select(ToProjection)))
            .ToArray();
        var secondWeak = second.WeakRelationshipSuggestions
            .Select(item => item.Source.EntityName + "|" + item.Source.PropertyName + "|" + string.Join(";", item.Candidates.Select(ToProjection)))
            .ToArray();
        Assert.Equal(firstWeak, secondWeak);
    }

    private static string ToProjection(LookupRelationshipSuggestion suggestion)
    {
        return string.Join(
            "|",
            suggestion.Source.EntityName,
            suggestion.Source.PropertyName,
            suggestion.TargetLookup.EntityName,
            suggestion.TargetLookup.PropertyName,
            suggestion.Role,
            suggestion.Status.ToString(),
            suggestion.Score.ToString("0.000"),
            string.Join(";", suggestion.Blockers),
            string.Join(",", suggestion.UnmatchedDistinctValuesSample));
    }

    private static bool IsTarget(BusinessKeyCandidate candidate, string entityName, string propertyName)
    {
        return string.Equals(candidate.Target.EntityName, entityName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(candidate.Target.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertEligible(
        ModelSuggestReport report,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty)
    {
        Assert.Contains(
            report.EligibleRelationshipSuggestions,
            item => Matches(item, sourceEntity, sourceProperty, targetEntity, targetProperty));
    }

    private static void AssertNotEligible(
        ModelSuggestReport report,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty)
    {
        AssertDoesNotContainTarget(report.EligibleRelationshipSuggestions, sourceEntity, sourceProperty, targetEntity, targetProperty);
    }

    private static void AssertDoesNotContainTarget(
        System.Collections.Generic.IEnumerable<LookupRelationshipSuggestion> suggestions,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty)
    {
        Assert.DoesNotContain(
            suggestions,
            item => Matches(item, sourceEntity, sourceProperty, targetEntity, targetProperty));
    }

    private static bool Matches(
        LookupRelationshipSuggestion suggestion,
        string sourceEntity,
        string sourceProperty,
        string targetEntity,
        string targetProperty)
    {
        return string.Equals(suggestion.Source.EntityName, sourceEntity, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(suggestion.Source.PropertyName, sourceProperty, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(suggestion.TargetLookup.EntityName, targetEntity, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(suggestion.TargetLookup.PropertyName, targetProperty, StringComparison.OrdinalIgnoreCase);
    }

    private static DomainWorkspace BuildDemoWorkspace()
    {
        var workspace = CreateWorkspaceSkeleton();
        AddEntity(workspace.Model, "Product", ("ProductName", "string"), ("ProductGroup", "string"));
        AddEntity(workspace.Model, "Supplier", ("SupplierName", "string"));
        AddEntity(workspace.Model, "Category", ("CategoryName", "string"));
        AddEntity(workspace.Model, "Warehouse", ("WarehouseName", "string"));
        AddEntity(workspace.Model, "Order",
            ("OrderNumber", "string"),
            ("ProductId", "string"),
            ("SupplierId", "string"),
            ("WarehouseId", "string"),
            ("StatusText", "string"));

        AddRow(workspace.Instance, "Product", "PRD-001", ("ProductName", "Road Bike"), ("ProductGroup", "Cycles"));
        AddRow(workspace.Instance, "Product", "PRD-002", ("ProductName", "Touring Bike"), ("ProductGroup", "Cycles"));
        AddRow(workspace.Instance, "Product", "PRD-003", ("ProductName", "Bottle Cage"), ("ProductGroup", "Accessories"));
        AddRow(workspace.Instance, "Product", "PRD-004", ("ProductName", "Water Bottle"), ("ProductGroup", "Accessories"));

        AddRow(workspace.Instance, "Supplier", "SUP-001", ("SupplierName", "Northwind Parts"));
        AddRow(workspace.Instance, "Supplier", "SUP-002", ("SupplierName", "Contoso Gear"));
        AddRow(workspace.Instance, "Supplier", "SUP-003", ("SupplierName", string.Empty));
        AddRow(workspace.Instance, "Supplier", "SUP-004", ("SupplierName", "Adventure Works"));

        AddRow(workspace.Instance, "Category", "CAT-001", ("CategoryName", "Cycles"));
        AddRow(workspace.Instance, "Category", "CAT-002", ("CategoryName", "Accessories"));
        AddRow(workspace.Instance, "Category", "CAT-003", ("CategoryName", "Maintenance"));

        AddRow(workspace.Instance, "Warehouse", "WH-001", ("WarehouseName", "Seattle Main"));
        AddRow(workspace.Instance, "Warehouse", "WH-002", ("WarehouseName", "Denver Hub"));
        AddRow(workspace.Instance, "Warehouse", "WH-003", ("WarehouseName", "Dallas Overflow"));

        AddRow(workspace.Instance, "Order", "ORD-001", ("OrderNumber", "ORD-1001"), ("ProductId", "PRD-001"), ("SupplierId", "SUP-001"), ("WarehouseId", "WH-001"), ("StatusText", "Released"));
        AddRow(workspace.Instance, "Order", "ORD-002", ("OrderNumber", "ORD-1002"), ("ProductId", "PRD-002"), ("SupplierId", "SUP-002"), ("WarehouseId", "WH-001"), ("StatusText", "Released"));
        AddRow(workspace.Instance, "Order", "ORD-003", ("OrderNumber", "ORD-1003"), ("ProductId", "PRD-003"), ("SupplierId", "SUP-003"), ("WarehouseId", "WH-002"), ("StatusText", "Held"));
        AddRow(workspace.Instance, "Order", "ORD-004", ("OrderNumber", "ORD-1004"), ("ProductId", "PRD-004"), ("SupplierId", "SUP-004"), ("WarehouseId", "WH-003"), ("StatusText", "Closed"));
        AddRow(workspace.Instance, "Order", "ORD-005", ("OrderNumber", "ORD-1005"), ("ProductId", "PRD-001"), ("SupplierId", "SUP-002"), ("WarehouseId", "WH-001"), ("StatusText", "Released"));

        return workspace;
    }

    private static DomainWorkspace CreateWorkspaceSkeleton()
    {
        return new DomainWorkspace
        {
            WorkspaceRootPath = "C:\\test\\workspace",
            MetadataRootPath = "C:\\test\\workspace\\metadata",
            Model = new GenericModel
            {
                Name = "SuggestModel",
            },
            Instance = new GenericInstance
            {
                ModelName = "SuggestModel",
            },
        };
    }

    private static void AddEntity(GenericModel model, string entityName, params (string Name, string DataType)[] properties)
    {
        var entity = new GenericEntity
        {
            Name = entityName,
        };

        foreach (var property in properties)
        {
            entity.Properties.Add(new GenericProperty
            {
                Name = property.Name,
                DataType = property.DataType,
                IsNullable = false,
            });
        }

        model.Entities.Add(entity);
    }

    private static void AddRow(GenericInstance instance, string entityName, string id, params (string Key, string Value)[] values)
    {
        var row = new GenericRecord
        {
            Id = id,
        };

        foreach (var (key, value) in values)
        {
            row.Values[key] = value;
        }

        instance.GetOrCreateEntityRecords(entityName).Add(row);
    }
}
