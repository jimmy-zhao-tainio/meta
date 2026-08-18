using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Surfaces.Xml;
using MetaWeave.Core;
using MetaWeaveScript.Execution;
using MetaWeaveScript.Sql;

namespace MetaWeaveScript.Tests;

public sealed class MetaWeaveScriptExecutionTests
{
    [Fact]
    public void ExecutesProjectionPredicatesCaseAndClosedScalarFunctions()
    {
        var result = Execute(
            """
            SELECT
                s.Id AS Id,
                CASE WHEN s.Name LIKE 'a%' THEN UPPER(s.Name) ELSE LOWER(s.Name) END AS Normalized,
                CONCAT(TRIM(' x '), REPLACE('abc', 'B', 'X')) AS Combined,
                SUBSTRING('abcdef', 2, 3) AS Middle,
                LEFT('abcdef', 2) AS Prefix,
                RIGHT('abcdef', 2) AS Suffix,
                IIF(s.RoleId IS NULL, COALESCE(NULLIF(s.Name, ''), 'missing'), s.RoleId) AS Choice
            FROM Source AS s
            WHERE NOT (s.Kind <> 'k1') AND s.Id IN ('s1', 's2', NULL);
            """);

        Assert.Equal(
            [
                "s1|beta|xaXc|bcd|ab|ef|r2",
                "s2|ALPHA|xaXc|bcd|ab|ef|r1"
            ],
            RenderRows(result));
    }

    [Fact]
    public void ExecutesCtesDerivedTablesValuesUnionAllAndDistinct()
    {
        var result = Execute(
            """
            WITH first_cte AS
            (
                SELECT s.Id AS Id, s.Kind AS Kind
                FROM Source AS s
                WHERE s.Kind = 'K1'
            ),
            second_cte AS
            (
                SELECT f.Id AS Id, f.Kind AS Kind FROM first_cte AS f
                UNION ALL
                SELECT f.Id AS Id, f.Kind AS Kind FROM first_cte AS f
            )
            SELECT DISTINCT d.Id AS Id, d.Kind AS Kind
            FROM (SELECT c.Id AS Id, c.Kind AS Kind FROM second_cte AS c) AS d;
            """);

        Assert.Equal(["s1|K1", "s2|K1"], RenderRows(result));

        var values = Execute(
            "SELECT v.Id AS Id, v.Name AS Name FROM (VALUES (2, 'two'), (1, 'one')) AS v(Id, Name);");
        Assert.Equal(["2|two", "1|one"], RenderRows(values));

        var renamed = Execute(
            "SELECT d.Renamed AS Id FROM (SELECT s.Id AS Original FROM Source AS s) AS d(Renamed);");
        Assert.Equal(["s1", "s2", "s3"], RenderRows(renamed));
    }

    [Fact]
    public void ExecutesRecursiveCteAsAnchorAndIterativeUnionAllMember()
    {
        var result = Execute(
            """
            WITH hierarchy AS
            (
                SELECT n.Id AS Id, n.ParentId AS ParentId, n.Id AS Path
                FROM
                (
                    VALUES
                        ('root', NULL),
                        ('child-a', 'root'),
                        ('child-b', 'root'),
                        ('grandchild', 'child-a')
                ) AS n(Id, ParentId)
                WHERE n.ParentId IS NULL

                UNION ALL

                SELECT n.Id AS Id, n.ParentId AS ParentId, CONCAT(h.Path, '/', n.Id) AS Path
                FROM
                (
                    VALUES
                        ('root', NULL),
                        ('child-a', 'root'),
                        ('child-b', 'root'),
                        ('grandchild', 'child-a')
                ) AS n(Id, ParentId)
                INNER JOIN hierarchy AS h ON n.ParentId = h.Id
            )
            SELECT h.Id AS Id, h.ParentId AS ParentId, h.Path AS Path
            FROM hierarchy AS h;
            """);

        Assert.Equal(
            [
                "root|NULL|root",
                "child-a|root|root/child-a",
                "child-b|root|root/child-b",
                "grandchild|child-a|root/child-a/grandchild"
            ],
            RenderRows(result));
    }

    [Fact]
    public void RecursiveCteCanRenderOrderedTextFromAModeledNodeGraph()
    {
        var result = Execute(
            """
            WITH nodes AS
            (
                SELECT v.Id AS Id, v.ParentId AS ParentId, v.Ordinal AS Ordinal, v.Token AS Token
                FROM
                (
                    VALUES
                        ('query', NULL, '1', ''),
                        ('select', 'query', '1', 'SELECT '),
                        ('literal', 'query', '2', '1'),
                        ('alias', 'query', '3', ' AS Id'),
                        ('terminator', 'query', '4', ';')
                ) AS v(Id, ParentId, Ordinal, Token)
            ),
            walk AS
            (
                SELECT n.Id AS Id, n.ParentId AS ParentId, RIGHT(CONCAT('0000000000', n.Ordinal), 10) AS SortPath, n.Token AS Token
                FROM nodes AS n
                WHERE n.ParentId IS NULL

                UNION ALL

                SELECT n.Id AS Id, n.ParentId AS ParentId, CONCAT(w.SortPath, '/', RIGHT(CONCAT('0000000000', n.Ordinal), 10)) AS SortPath, n.Token AS Token
                FROM nodes AS n
                INNER JOIN walk AS w ON n.ParentId = w.Id
            )
            SELECT STRING_AGG(w.Token, '') WITHIN GROUP (ORDER BY w.SortPath ASC) AS DefinitionSql
            FROM walk AS w;
            """);

        Assert.Equal(["SELECT 1 AS Id;"], RenderRows(result));
    }

    [Fact]
    public void ExecutesRecursiveCteWhoseMemberUsesUnionCte()
    {
        var result = Execute(
            """
            WITH edges AS
            (
                SELECT v.ParentId AS ParentId, v.ChildId AS ChildId
                FROM (VALUES ('root', 'child')) AS v(ParentId, ChildId)

                UNION ALL

                SELECT v.ParentId AS ParentId, v.ChildId AS ChildId
                FROM (VALUES ('child', 'leaf')) AS v(ParentId, ChildId)
            ),
            walk AS
            (
                SELECT 'root' AS Id, 'root' AS Path

                UNION ALL

                SELECT e.ChildId AS Id, CONCAT(w.Path, '/', e.ChildId) AS Path
                FROM edges AS e
                INNER JOIN walk AS w ON e.ParentId = w.Id
            )
            SELECT w.Id AS Id, w.Path AS Path
            FROM walk AS w;
            """);

        Assert.Equal(
            ["root|root", "child|root/child", "leaf|root/child/leaf"],
            RenderRows(result));
    }

    [Fact]
    public void ExecutesJoinsAndLateralStringSplit()
    {
        var inner = Execute(
            "SELECT s.Id AS Id, r.Name AS RelatedName FROM Source AS s INNER JOIN Related AS r ON s.RoleId = r.Id;");
        Assert.Equal(["s1|Beta", "s2|Alpha"], RenderRows(inner));

        var left = Execute(
            "SELECT s.Id AS Id, r.Name AS RelatedName FROM Source AS s LEFT JOIN Related AS r ON s.RoleId = r.Id;");
        Assert.Equal(["s1|Beta", "s2|Alpha", "s3|NULL"], RenderRows(left));

        var cross = Execute(
            "SELECT COUNT(*) AS PairCount FROM Source AS s CROSS JOIN Related AS r;");
        Assert.Equal(["6"], RenderRows(cross));

        var crossApply = Execute(
            "SELECT s.Id AS Id, p.value AS Part, p.ordinal AS Ordinal FROM Source AS s CROSS APPLY STRING_SPLIT(s.Tags, ',', 1) AS p;");
        Assert.Equal(["s1|b|1", "s1|a|2", "s3|x|1"], RenderRows(crossApply));

        var outerApply = Execute(
            "SELECT s.Id AS Id, p.value AS Part FROM Source AS s OUTER APPLY STRING_SPLIT(s.Tags, ',') AS p;");
        Assert.Equal(["s1|b", "s1|a", "s2|NULL", "s3|x"], RenderRows(outerApply));
    }

    [Fact]
    public void ExecutesGroupingAndRetainedAggregates()
    {
        var grouped = Execute(
            """
            SELECT
                s.Kind AS Id,
                COUNT(*) AS ItemCount,
                COUNT(s.Tags) AS TaggedCount,
                MIN(s.Name) AS FirstName,
                MAX(s.Name) AS LastName,
                STRING_AGG(s.Name, ',') WITHIN GROUP (ORDER BY s.Name ASC) AS Names
            FROM Source AS s
            GROUP BY s.Kind;
            """);

        Assert.Equal(
            ["K1|2|1|alpha|Beta|alpha,Beta", "K2|1|1|||"],
            RenderRows(grouped));

        var empty = Execute(
            "SELECT COUNT(*) AS ItemCount, MIN(s.Name) AS FirstName FROM Source AS s WHERE s.Kind = 'missing';");
        Assert.Equal(["0|NULL"], RenderRows(empty));

        var expressionGrouped = Execute(
            "SELECT LOWER(s.Kind) AS Id, COUNT(*) AS ItemCount FROM Source AS s GROUP BY LOWER(s.Kind);");
        Assert.Equal(["k1|2", "k2|1"], RenderRows(expressionGrouped));
    }

    [Fact]
    public void ExecutesCorrelatedScalarAndExistsSubqueries()
    {
        var result = Execute(
            """
            SELECT
                s.Id AS Id,
                (SELECT r.Name AS Name FROM Related AS r WHERE r.Id = s.RoleId) AS RelatedName
            FROM Source AS s
            WHERE EXISTS (SELECT r.Id AS Id FROM Related AS r WHERE r.Id = s.RoleId);
            """);

        Assert.Equal(["s1|Beta", "s2|Alpha"], RenderRows(result));

        var aggregateExists = Execute(
            "SELECT s.Id AS Id FROM Source AS s WHERE EXISTS (SELECT COUNT(*) AS ItemCount FROM Related AS r WHERE r.Id = s.Id);");
        Assert.Equal(["s1", "s2", "s3"], RenderRows(aggregateExists));
    }

    [Fact]
    public void ExecutesAllRetainedComparisonAndNullTruthForms()
    {
        var result = Execute(
            """
            SELECT
                IIF(
                    ('b' > 'a' AND 'b' >= 'b' AND 'a' < 'b' AND 'a' <= 'a' AND 'a' = 'A' AND 'a' <> 'b')
                    OR 1 <> 1,
                    'yes',
                    'no') AS Comparisons,
                IIF(NULL = 'x', 'bad', 'unknown-takes-else') AS UnknownBranch,
                COALESCE(NULL, NULLIF('x', 'x'), 'fallback') AS Coalesced,
                LTRIM('  left') AS LeftTrimmed,
                RTRIM('right  ') AS RightTrimmed;
            """);

        Assert.Equal(
            ["yes|unknown-takes-else|fallback|left|right"],
            RenderRows(result));
    }

    [Fact]
    public void ExecutesLanguageOwnedBlankStringSemantics()
    {
        var result = Execute(
            "SELECT IS_BLANK(NULL) AS NullValue, IS_BLANK('   ') AS Spaces, IS_BLANK(' \t ') AS Tab, IS_BLANK('x') AS Text;");

        Assert.Equal(["1|1|1|0"], RenderRows(result));
    }

    [Fact]
    public void ExecutesLenWithSqlTrailingSpaceAndNullSemantics()
    {
        var result = Execute(
            "SELECT LEN('Ångström') AS UnicodeText, LEN('text   ') AS TrailingSpaces, LEN('') AS EmptyText, LEN(NULL) AS NullValue;");

        Assert.Equal(["8|4|0|NULL"], RenderRows(result));
    }

    [Fact]
    public void ExecutesTryConvertAndPartitionedRowNumber()
    {
        var result = Execute(
            """
            SELECT
                v.Id AS Id,
                ROW_NUMBER() OVER
                (
                    PARTITION BY v.TableId
                    ORDER BY v.Phase, COALESCE(TRY_CONVERT(int, v.SourceOrdinal), 2147483647), v.Id
                ) AS Ordinal
            FROM
            (
                VALUES
                    ('a', 't1', 10, '20'),
                    ('b', 't1', 10, '100'),
                    ('c', 't1', 10, '10'),
                    ('d', 't1', 10, 'bad'),
                    ('e', 't2', 10, '20')
            ) AS v(Id, TableId, Phase, SourceOrdinal);
            """);

        Assert.Equal(["a|2", "b|3", "c|1", "d|4", "e|1"], RenderRows(result));
    }

    [Fact]
    public void ExecutesQueryParenthesesAndBracketQuotedIdentifiers()
    {
        var result = Execute(
            """
            SELECT [d].[Id] AS [Id]
            FROM
            (
                SELECT [s].[Id] AS [Id] FROM [Source] AS [s]
                UNION ALL
                (SELECT [s].[Id] AS [Id] FROM [Source] AS [s])
            ) AS [d];
            """);

        Assert.Equal(["s1", "s2", "s3", "s1", "s2", "s3"], RenderRows(result));
    }

    [Fact]
    public void RuntimePreparationResolvesMembersEvenWhenTheSourcePopulationIsEmpty()
    {
        var source = CreateSourceWorkspace();
        source.Instance.RecordsByEntity["Source"].Clear();

        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(
            Parse("SELECT s.Missing AS Id FROM Source AS s;"),
            source);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == "ColumnReferenceNotFound");
    }

    [Fact]
    public void ExecutesAcrossNamedSourceWorkspacesWithStringParameters()
    {
        var warehouse = CreateSourceWorkspace();
        var implementation = CreateImplementationWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var targetEntity = new GenericEntity { Name = "Target" };
        targetEntity.Properties.Add(new GenericProperty { Name = "Name" });
        targetModel.Entities.Add(targetEntity);
        var target = EmptyWorkspace(targetModel);
        var direction = Direction(
            [
                new MetaWeaveScriptSourceWorkspace("warehouse", warehouse.Model.Name),
                new MetaWeaveScriptSourceWorkspace("implementation", implementation.Model.Name)
            ],
            target,
            [new TransformationSource(
                "targets",
                "Target",
                "SELECT w.Id AS Id, CONCAT(w.Name, '.', i.PhysicalName, '.', @databaseName) AS Name FROM warehouse.Source AS w INNER JOIN implementation.Mapping AS i ON w.Id = i.SourceId;")],
            stringParameters: [new MetaWeaveScriptStringParameter("databaseName")]);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            direction,
            new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase)
            {
                ["warehouse"] = warehouse,
                ["implementation"] = implementation
            },
            target,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["databaseName"] = "AdventureWorks"
            });

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        var rows = Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace)
            .Instance.RecordsByEntity["Target"];
        Assert.Equal(
            ["Beta.SourceOne.AdventureWorks", "alpha.SourceTwo.AdventureWorks"],
            rows.Select(row => row.Values["Name"]));
        Assert.Empty(result.RelationOutputs);
    }

    [Fact]
    public void UnqualifiedSourceEntityMustResolveToExactlyOneInputWorkspace()
    {
        var first = CreateSourceWorkspace();
        var second = CreateSourceWorkspace();
        second.Model.Name = "SecondSourceModel";
        second.Instance.ModelName = second.Model.Name;

        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(
            Parse("SELECT s.Id AS Id FROM Source AS s;"),
            new Dictionary<string, InMemoryWorkspace>
            {
                ["first"] = first,
                ["second"] = second
            });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == "SourceEntityAmbiguous");
    }

    [Fact]
    public void DirectionRelationsComposeRequirementsAndTransformations()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var targetEntity = new GenericEntity { Name = "Target" };
        targetEntity.Properties.Add(new GenericProperty { Name = "Name" });
        targetModel.Entities.Add(targetEntity);
        var target = EmptyWorkspace(targetModel);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(
                source,
                target,
                [new TransformationSource(
                    "targets",
                    "Target",
                    "SELECT s.Id AS Id, s.Name AS Name FROM SelectedSources AS s;")],
                [new RequirementSource(
                    "selected-names-present",
                    "SelectedNameMissing",
                    "Selected source rows require a name.",
                    "SELECT s.Id AS SourceId FROM SelectedSources AS s WHERE IS_BLANK(s.Name) = 1;")],
                [
                    new RelationSource(
                        "SourceNames",
                        "SELECT s.Id AS Id, s.Name AS Name, s.Kind AS Kind FROM Source AS s;"),
                    new RelationSource(
                        "SelectedSources",
                        "SELECT s.Id AS Id, UPPER(s.Name) AS Name FROM SourceNames AS s WHERE s.Kind = 'K1';")
                ]),
            source,
            target,
            includeRelationOutputs: true);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        var records = result.OutputWorkspace!.Instance.RecordsByEntity["Target"];
        Assert.Equal(["s1", "s2"], records.Select(record => record.Id));
        Assert.Equal(["BETA", "ALPHA"], records.Select(record => record.Values["Name"]));
        Assert.Equal(["SelectedSources", "SourceNames"], result.RelationOutputs.Keys);
        var selectedSources = result.RelationOutputs["selectedsources"];
        Assert.Equal(["Id", "Name"], selectedSources.Columns.Select(column => column.Name));
        Assert.Equal(
            ["s1|BETA", "s2|ALPHA"],
            selectedSources.Rows.Select(row => string.Join('|', row.Values)));
    }

    [Fact]
    public void DirectionRelationCyclesFailWithTheirRelationIdentity()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        targetModel.Entities.Add(new GenericEntity { Name = "Target" });
        var target = EmptyWorkspace(targetModel);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(
                source,
                target,
                [new TransformationSource("targets", "Target", "SELECT a.Id AS Id FROM RelationA AS a;")],
                relations:
                [
                    new RelationSource("RelationA", "SELECT b.Id AS Id FROM RelationB AS b;"),
                    new RelationSource("RelationB", "SELECT a.Id AS Id FROM RelationA AS a;")
                ]),
            source,
            target);

        Assert.False(result.IsSuccess);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("NamedRelationRecursive", issue.Code);
        Assert.NotNull(issue.RelationName);
    }

    [Fact]
    public void DirectionRequirementsRejectBeforeUnusedRelationsAreEvaluated()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        targetModel.Entities.Add(new GenericEntity { Name = "Target" });
        var target = EmptyWorkspace(targetModel);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(
                source,
                target,
                [new TransformationSource("targets", "Target", "SELECT s.Id AS Id FROM Source AS s;")],
                [new RequirementSource(
                    "source-rejected",
                    "SourceRejected",
                    "The source is rejected.",
                    "SELECT s.Id AS SourceId FROM Source AS s;")],
                [new RelationSource(
                    "BrokenUnusedRelation",
                    "SELECT missing.Id AS Id FROM MissingEntity AS missing;")]),
            source,
            target);

        Assert.False(result.IsSuccess);
        Assert.All(result.Issues, issue => Assert.Equal("SourceRejected", issue.Code));
    }

    [Fact]
    public void InstantiatesTargetThroughCoreOperationsInTargetDagOrder()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var target = new GenericEntity { Name = "Target" };
        target.Properties.Add(new GenericProperty { Name = "Name" });
        target.Relationships.Add(new GenericRelationship
        {
            Entity = "Related",
            Role = "Role",
            IsNullable = true
        });
        var related = new GenericEntity { Name = "Related" };
        related.Properties.Add(new GenericProperty { Name = "Name" });
        targetModel.Entities.Add(target);
        targetModel.Entities.Add(related);

        var service = new MetaWeaveScriptExecutionService();
        var targetWorkspace = EmptyWorkspace(targetModel);
        var progress = new List<MetaWeaveScriptExecutionProgress>();
        var result = service.ExecuteDirection(
            Direction(source, targetWorkspace,
            [
                new TransformationSource(
                    "targets",
                    "Target",
                    "SELECT s.Id AS Id, s.Name AS Name, s.RoleId AS RoleId FROM SourceRows AS s;"),
                new TransformationSource(
                    "related",
                    "Related",
                    "SELECT r.Id AS Id, r.Name AS Name FROM Related AS r;")
            ],
            [new RequirementSource(
                "source-identities-present",
                "SourceIdentityMissing",
                "Source identities are required.",
                "SELECT s.Id AS SourceId FROM SourceRows AS s WHERE s.Id IS NULL;")],
            [new RelationSource(
                "SourceRows",
                "SELECT s.Id AS Id, s.Name AS Name, s.RoleId AS RoleId FROM Source AS s;")]),
            source,
            targetWorkspace,
            progress.Add);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        Assert.Equal(
            [
                new MetaWeaveScriptExecutionProgress(0, 4, null, null),
                new MetaWeaveScriptExecutionProgress(1, 4, MetaWeaveScriptExecutionTaskKind.Relation, "SourceRows"),
                new MetaWeaveScriptExecutionProgress(2, 4, MetaWeaveScriptExecutionTaskKind.Requirement, "source-identities-present"),
                new MetaWeaveScriptExecutionProgress(3, 4, MetaWeaveScriptExecutionTaskKind.TargetEntity, "Related"),
                new MetaWeaveScriptExecutionProgress(4, 4, MetaWeaveScriptExecutionTaskKind.TargetEntity, "Target")
            ],
            progress);
        var output = Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace);
        Assert.Equal(["s1", "s2", "s3"], output.Instance.RecordsByEntity["Target"].Select(row => row.Id));
        Assert.Equal("r2", output.Instance.RecordsByEntity["Target"][0].RelationshipIds["RoleId"]);
        Assert.False(output.Instance.RecordsByEntity["Target"][2].RelationshipIds.ContainsKey("RoleId"));
        Assert.Equal(3, source.Instance.RecordsByEntity["Source"].Count);
        Assert.Empty(targetWorkspace.Instance.RecordsByEntity);
        Assert.NotSame(source.Model, output.Model);
    }

    [Fact]
    public void TargetWorkspaceInstancesAreNotCopiedIntoTheNewTarget()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var targetEntity = new GenericEntity { Name = "Target" };
        targetEntity.Properties.Add(new GenericProperty { Name = "Name" });
        targetModel.Entities.Add(targetEntity);
        var targetInstance = new GenericInstance { ModelName = targetModel.Name };
        targetInstance.GetOrCreateEntityRecords("Target").Add(
            Record("existing", ("Name", "Existing")));
        var targetContract = new InMemoryWorkspace(targetModel, targetInstance);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(source, targetContract,
            [new TransformationSource(
                "targets",
                "Target",
                "SELECT s.Id AS Id, s.Name AS Name FROM Source AS s;")]),
            source,
            targetContract);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        var output = Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace);
        Assert.Equal(["s1", "s2", "s3"], output.Instance.RecordsByEntity["Target"].Select(row => row.Id));
        Assert.Equal("existing", Assert.Single(targetContract.Instance.RecordsByEntity["Target"]).Id);
    }

    [Fact]
    public void DirectionRequirementsRejectViolatingRowsBeforeTargetInstantiation()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var targetEntity = new GenericEntity { Name = "Target" };
        targetEntity.Properties.Add(new GenericProperty { Name = "Name" });
        targetModel.Entities.Add(targetEntity);
        var target = EmptyWorkspace(targetModel);
        var service = new MetaWeaveScriptExecutionService();

        var rejected = service.ExecuteDirection(
            Direction(
                source,
                target,
                [new TransformationSource(
                    "targets",
                    "Target",
                    "SELECT s.Id AS Id, s.Name AS Name FROM Source AS s;")],
                [new RequirementSource(
                    "kind-must-be-k1",
                    "SourceKindUnsupported",
                    "Source rows must use K1.",
                    "SELECT s.Id AS SourceId, s.Kind AS ActualKind FROM Source AS s WHERE s.Kind <> 'K1';")]),
            source,
            target);

        Assert.False(rejected.IsSuccess);
        Assert.Null(rejected.OutputWorkspace);
        var issue = Assert.Single(rejected.Issues);
        Assert.Equal("SourceKindUnsupported", issue.Code);
        Assert.Equal("kind-must-be-k1", issue.RequirementName);
        Assert.Contains("SourceId=s3", issue.Message);
        Assert.Contains("ActualKind=K2", issue.Message);
        Assert.Empty(target.Instance.RecordsByEntity);

        var accepted = service.ExecuteDirection(
            Direction(
                source,
                target,
                [new TransformationSource(
                    "targets",
                    "Target",
                    "SELECT s.Id AS Id, s.Name AS Name FROM Source AS s;")],
                [new RequirementSource(
                    "no-missing-kind",
                    "SourceKindMissing",
                    "Source rows require a kind.",
                    "SELECT s.Id AS SourceId FROM Source AS s WHERE s.Kind IS NULL;")]),
            source,
            target);

        Assert.True(accepted.IsSuccess, RenderIssues(accepted.Issues));
        Assert.Equal(
            ["s1", "s2", "s3"],
            accepted.OutputWorkspace!.Instance.RecordsByEntity["Target"].Select(row => row.Id));
    }

    [Theory]
    [InlineData(
        "SELECT (SELECT r.Name AS Name FROM Related AS r) AS Name;",
        "ScalarSubqueryCardinalityInvalid")]
    [InlineData(
        "WITH later AS (SELECT e.Id AS Id FROM earlier AS e), earlier AS (SELECT s.Id AS Id FROM Source AS s) SELECT l.Id AS Id FROM later AS l;",
        "CommonTableExpressionForwardReference")]
    [InlineData(
        "WITH recursive_cte AS (SELECT r.Id AS Id FROM recursive_cte AS r) SELECT r.Id AS Id FROM recursive_cte AS r;",
        "CommonTableExpressionRecursiveShapeUnsupported")]
    [InlineData(
        "WITH recursive_cte AS (SELECT 1 AS Id UNION ALL SELECT a.Id AS Id FROM recursive_cte AS a INNER JOIN recursive_cte AS b ON a.Id = b.Id) SELECT r.Id AS Id FROM recursive_cte AS r;",
        "CommonTableExpressionRecursiveReferenceCountInvalid")]
    [InlineData(
        "WITH recursive_cte AS (SELECT 1 AS Id UNION ALL SELECT r.Id AS Id FROM recursive_cte AS r) SELECT r.Id AS Id FROM recursive_cte AS r;",
        "CommonTableExpressionRecursionDidNotAdvance")]
    [InlineData(
        "SELECT s.Missing AS Id FROM Source AS s;",
        "ColumnReferenceNotFound")]
    [InlineData(
        "SELECT v.Id AS Id FROM (VALUES (1)) AS v;",
        "InlineValuesColumnAliasesMissing")]
    [InlineData(
        "SELECT p.value AS Value FROM STRING_SPLIT('a,b', ',', 2) AS p;",
        "StringSplitOrdinalFlagInvalid")]
    [InlineData(
        "SELECT s.Name AS Name, COUNT(*) AS ItemCount FROM Source AS s GROUP BY s.Kind;",
        "UngroupedColumnReference")]
    public void ExecutionDefectsFailWithSpecificDiagnostics(string sql, string expectedCode)
    {
        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(Parse(sql), CreateSourceWorkspace());

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void CoreOperationRejectsAReferenceThatNoTransformationInstantiates()
    {
        var targetModel = new GenericModel { Name = "TargetModel" };
        var target = new GenericEntity { Name = "Target" };
        target.Relationships.Add(new GenericRelationship { Entity = "Related", Role = "Role" });
        targetModel.Entities.Add(target);
        targetModel.Entities.Add(new GenericEntity { Name = "Related" });

        var source = CreateSourceWorkspace();
        var targetWorkspace = EmptyWorkspace(targetModel);
        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(source, targetWorkspace,
            [new TransformationSource(
                "targets",
                "Target",
                "SELECT s.Id AS Id, s.RoleId AS RoleId FROM Source AS s WHERE s.RoleId IS NOT NULL;")]),
            source,
            targetWorkspace);

        Assert.False(result.IsSuccess);
        Assert.Null(result.OutputWorkspace);
        Assert.Contains(result.Issues, issue => issue.Code == "TargetInstantiationFailed");
    }

    [Fact]
    public void CoreOperationRejectsMissingRequiredMembersAtTheTransformationThatProducedThem()
    {
        var targetModel = new GenericModel { Name = "TargetModel" };
        var target = new GenericEntity { Name = "Target" };
        target.Properties.Add(new GenericProperty { Name = "Name", IsNullable = false });
        targetModel.Entities.Add(target);

        var source = CreateSourceWorkspace();
        var targetWorkspace = EmptyWorkspace(targetModel);
        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(source, targetWorkspace,
            [new TransformationSource(
                "targets",
                "Target",
                "SELECT s.Id AS Id FROM Source AS s;")]),
            source,
            targetWorkspace);

        Assert.False(result.IsSuccess);
        Assert.Null(result.OutputWorkspace);
        Assert.Contains(result.Issues, issue => issue.Code == "instance.required.missing");
    }

    [Fact]
    public void ConvertsTheScopedCatalogThroughACompleteRelationshipBearingDirection()
    {
        var source = LoadSampleWorkspace("SampleScopedSourceCatalog", includeInstances: true);
        var target = LoadSampleWorkspace("SampleScopedReferenceCatalog", includeInstances: false);
        var expected = LoadSampleWorkspace("SampleScopedReferenceCatalog", includeInstances: true);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            LoadSampleDirection("ScopedCatalog"),
            source,
            target);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            expected,
            Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace)));
    }

    [Fact]
    public void ConstructsAReferencePopulationFromSeveralSourceEntities()
    {
        var source = LoadSampleWorkspace("SampleSourceCatalog", includeInstances: true);
        var target = LoadSampleWorkspace("SampleReferenceCatalog", includeInstances: false);
        var expected = LoadSampleWorkspace("SampleReferenceCatalog", includeInstances: true);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            LoadSampleDirection("ReferenceTypes"),
            source,
            target);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        Assert.Null(InMemoryWorkspaceComparer.FindDifference(
            expected,
            Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace)));
    }

    [Fact]
    public void TreatsOneEntityPopulationAsAtomicForOptionalSelfReferences()
    {
        var source = CreateSourceWorkspace();
        var targetModel = new GenericModel { Name = "TargetModel" };
        var node = new GenericEntity { Name = "Node" };
        node.Relationships.Add(new GenericRelationship
        {
            Entity = "Node",
            Role = "Parent",
            IsNullable = true
        });
        targetModel.Entities.Add(node);
        var target = EmptyWorkspace(targetModel);

        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            Direction(source, target,
            [new TransformationSource(
                "nodes",
                "Node",
                "SELECT v.Id AS Id, v.ParentId AS ParentId FROM (VALUES ('child', 'root'), ('root', NULL)) AS v(Id, ParentId);")]),
            source,
            target);

        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        var nodes = Assert.IsType<InMemoryWorkspace>(result.OutputWorkspace)
            .Instance.RecordsByEntity["Node"];
        Assert.Equal("root", nodes.Single(item => item.Id == "child").RelationshipIds["ParentId"]);
        Assert.False(nodes.Single(item => item.Id == "root").RelationshipIds.ContainsKey("ParentId"));
    }

    private static MetaWeaveScriptQueryOutput Execute(string sql)
    {
        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(Parse(sql), CreateSourceWorkspace());
        Assert.True(result.IsSuccess, RenderIssues(result.Issues));
        return Assert.IsType<MetaWeaveScriptQueryOutput>(result.Output);
    }

    private static MetaWeaveModel Parse(string sql) =>
        new MetaWeaveScriptSqlService().ImportFromSqlCode(sql);

    private static string[] RenderRows(MetaWeaveScriptQueryOutput output) =>
        output.Rows
            .Select(row => string.Join("|", row.Values.Select(value => value.IsNull ? "NULL" : value.ToInvariantString())))
            .ToArray();

    private static string RenderIssues(IReadOnlyList<MetaWeaveScriptExecutionIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static InMemoryWorkspace CreateSourceWorkspace()
    {
        var model = new GenericModel { Name = "SourceModel" };
        var source = new GenericEntity { Name = "Source" };
        source.Properties.Add(new GenericProperty { Name = "Name" });
        source.Properties.Add(new GenericProperty { Name = "Kind" });
        source.Properties.Add(new GenericProperty { Name = "Tags", IsNullable = true });
        source.Relationships.Add(new GenericRelationship
        {
            Entity = "Related",
            Role = "Role",
            IsNullable = true
        });
        var related = new GenericEntity { Name = "Related" };
        related.Properties.Add(new GenericProperty { Name = "Name" });
        model.Entities.Add(source);
        model.Entities.Add(related);

        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Related").AddRange(
        [
            Record("r1", ("Name", "Alpha")),
            Record("r2", ("Name", "Beta"))
        ]);
        instance.GetOrCreateEntityRecords("Source").AddRange(
        [
            Record("s1", [("Name", "Beta"), ("Kind", "K1"), ("Tags", "b,a")], [("RoleId", "r2")]),
            Record("s2", [("Name", "alpha"), ("Kind", "K1")], [("RoleId", "r1")]),
            Record("s3", [("Name", ""), ("Kind", "K2"), ("Tags", "x")], [])
        ]);
        return new InMemoryWorkspace(model, instance);
    }

    private static InMemoryWorkspace CreateImplementationWorkspace()
    {
        var model = new GenericModel { Name = "ImplementationModel" };
        var mapping = new GenericEntity { Name = "Mapping" };
        mapping.Properties.Add(new GenericProperty { Name = "SourceId" });
        mapping.Properties.Add(new GenericProperty { Name = "PhysicalName" });
        model.Entities.Add(mapping);

        var instance = new GenericInstance { ModelName = model.Name };
        instance.GetOrCreateEntityRecords("Mapping").AddRange(
        [
            Record("m1", ("SourceId", "s1"), ("PhysicalName", "SourceOne")),
            Record("m2", ("SourceId", "s2"), ("PhysicalName", "SourceTwo"))
        ]);
        return new InMemoryWorkspace(model, instance);
    }

    private static InMemoryWorkspace EmptyWorkspace(GenericModel model) =>
        new(model, new GenericInstance { ModelName = model.Name });

    private static MetaWeaveScriptDirection Direction(
        InMemoryWorkspace source,
        InMemoryWorkspace target,
        IReadOnlyList<TransformationSource> transformations,
        IReadOnlyList<RequirementSource>? requirements = null,
        IReadOnlyList<RelationSource>? relations = null,
        string name = "test-direction")
    {
        var model = MetaWeaveModel.CreateEmpty();
        var sqlService = new MetaWeaveScriptSqlService();
        var executableTransformations = transformations.Select(transformation =>
            new MetaWeaveScriptTransformation(
                transformation.Name,
                transformation.TargetEntityName,
                sqlService.ImportIntoModel(model, transformation.Sql))).ToArray();
        var executableRequirements = (requirements ?? []).Select(requirement =>
            new MetaWeaveScriptRequirement(
                requirement.Name,
                requirement.Code,
                requirement.Message,
                sqlService.ImportIntoModel(model, requirement.Sql))).ToArray();
        var executableRelations = (relations ?? []).Select(relation =>
            new MetaWeaveScriptRelation(
                relation.Name,
                sqlService.ImportIntoModel(model, relation.Sql))).ToArray();
        return new MetaWeaveScriptDirection(
            name,
            [new MetaWeaveScriptSourceWorkspace("source", source.Model.Name)],
            target.Model.Name,
            [],
            model,
            executableTransformations,
            executableRequirements,
            executableRelations);
    }

    private static MetaWeaveScriptDirection Direction(
        IReadOnlyList<MetaWeaveScriptSourceWorkspace> sourceWorkspaces,
        InMemoryWorkspace target,
        IReadOnlyList<TransformationSource> transformations,
        IReadOnlyList<RequirementSource>? requirements = null,
        IReadOnlyList<MetaWeaveScriptStringParameter>? stringParameters = null,
        IReadOnlyList<RelationSource>? relations = null,
        string name = "test-direction")
    {
        var model = MetaWeaveModel.CreateEmpty();
        var sqlService = new MetaWeaveScriptSqlService();
        var executableTransformations = transformations.Select(transformation =>
            new MetaWeaveScriptTransformation(
                transformation.Name,
                transformation.TargetEntityName,
                sqlService.ImportIntoModel(model, transformation.Sql))).ToArray();
        var executableRequirements = (requirements ?? []).Select(requirement =>
            new MetaWeaveScriptRequirement(
                requirement.Name,
                requirement.Code,
                requirement.Message,
                sqlService.ImportIntoModel(model, requirement.Sql))).ToArray();
        var executableRelations = (relations ?? []).Select(relation =>
            new MetaWeaveScriptRelation(
                relation.Name,
                sqlService.ImportIntoModel(model, relation.Sql))).ToArray();
        return new MetaWeaveScriptDirection(
            name,
            sourceWorkspaces,
            target.Model.Name,
            stringParameters ?? [],
            model,
            executableTransformations,
            executableRequirements,
            executableRelations);
    }

    private sealed record TransformationSource(
        string Name,
        string TargetEntityName,
        string Sql);

    private sealed record RequirementSource(
        string Name,
        string Code,
        string Message,
        string Sql);

    private sealed record RelationSource(
        string Name,
        string Sql);

    private static MetaWeaveScriptDirection LoadSampleDirection(string directionName) =>
        new MetaWeaveScriptDirectionLoader().Load(Path.Combine(
            FindRepositoryRoot(),
            "MetaWeave",
            "Script",
            "Samples",
            directionName));

    private static InMemoryWorkspace LoadSampleWorkspace(
        string workspaceName,
        bool includeInstances)
    {
        var workspacePath = Path.Combine(
            FindRepositoryRoot(),
            "MetaWeave",
            "Script",
            "Samples",
            "Contracts",
            workspaceName);
        var model = XDocument.Load(Path.Combine(workspacePath, "model.xml"));
        var instances = includeInstances
            ? Directory.GetFiles(Path.Combine(workspacePath, "instances"), "*.xml")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(XDocument.Load)
                .ToArray()
            : [];
        return MetaXmlCodec.Read(model, instances);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static GenericRecord Record(string id, params (string Name, string Value)[] values) =>
        Record(id, values, []);

    private static GenericRecord Record(
        string id,
        IReadOnlyList<(string Name, string Value)> values,
        IReadOnlyList<(string Name, string Value)> relationships)
    {
        var record = new GenericRecord { Id = id };
        foreach (var value in values)
        {
            record.Values.Add(value.Name, value.Value);
        }

        foreach (var relationship in relationships)
        {
            record.RelationshipIds.Add(relationship.Name, relationship.Value);
        }

        return record;
    }
}
