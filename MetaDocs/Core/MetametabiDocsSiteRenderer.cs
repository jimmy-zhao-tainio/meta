using System.Net;
using System.Text;
using System.Xml.Linq;
using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetametabiDocsSiteRenderer
{
    public string RenderCliReferenceSite(MetaDocsModel model) => RenderSite(model);

    public string RenderSite(MetaDocsModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        MetaDocsDefaults.EnsureDocumentationWorkspace(model, "workspace:default", "Documentation", "SourceDocumentation");
        MetaDocsDefaults.EnsureDefaultTheme(model);
        var view = MetaDocsPublicReferenceViewBuilder.EnsurePublicReferenceView(model);
        var siteTitle = ResolveSiteTitle(view);

        var subjectsByKey = model.DocumentationSubjectList
            .GroupBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var tree = ResolveReferenceTree(model, view, subjectsByKey);

        return ApplyShellTemplate(
            ResolveShellTemplate(model),
            $"{siteTitle} · Reference",
            ResolveCss(model),
            BuildNavigation(model),
            BuildContent(model, view, tree),
            BuildFaviconLink(model),
            BuildRoutingScript(siteTitle));
    }

    private static ReferenceTree ResolveReferenceTree(
        MetaDocsModel model,
        DocumentationView view,
        IReadOnlyDictionary<string, DocumentationSubject> subjectsByKey)
    {
        var subjectNodes = model.DocumentationViewNodeList
            .Where(node =>
                ReferenceEquals(node.DocumentationView, view) &&
                !string.IsNullOrWhiteSpace(node.SubjectKey))
            .OrderBy(node => ParseOrdinal(node.Ordinal))
            .Select(node => subjectsByKey.TryGetValue(node.SubjectKey!, out var subject) &&
                            MetaDocsPublicReferenceClassifier.TryClassify(model, subject, out var classification)
                ? new ClassifiedReferenceSubject(subject, classification)
                : null)
            .Where(static item => item is not null)
            .Cast<ClassifiedReferenceSubject>()
            .GroupBy(item => item.Subject.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var families = subjectNodes
            .GroupBy(item => item.Classification.ProductFamily)
            .OrderBy(group => group.Key)
            .Select(familyGroup =>
            {
                var surfaces = familyGroup
                    .GroupBy(item => item.Classification.Surface)
                    .OrderBy(group => group.Key)
                    .Select(surfaceGroup => new SurfaceSection(
                        surfaceGroup.Key,
                        MetaDocsPublicReferenceClassifier.FormatReferenceSurface(surfaceGroup.Key),
                        surfaceGroup
                            .OrderBy(item => item.Classification.SortKey, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.Subject.DisplayName, StringComparer.OrdinalIgnoreCase)
                            .Select(item => item.Subject)
                            .ToArray()))
                    .ToArray();
                return new FamilySection(
                    familyGroup.Key,
                    MetaDocsPublicReferenceClassifier.FormatProductFamily(familyGroup.Key),
                    surfaces);
            })
            .ToArray();
        return new ReferenceTree(families);
    }

    private static string BuildNavigation(MetaDocsModel model)
    {
        var brandMarkSvg = ResolveInlineBrandMarkSvg(model);
        var builder = new StringBuilder();
        builder.AppendLine("    <header class=\"topbar\">");
        builder.AppendLine("      <div class=\"topbar-inner\">");
        builder.Append("        <a class=\"brand\" href=\"https://metametabi.com\" aria-label=\"metametabi.com home\">")
            .Append(brandMarkSvg)
            .Append("<span>")
            .Append("meta+meta-bi")
            .AppendLine("</span></a>");
        builder.AppendLine("        <a class=\"home-button\" href=\"https://metametabi.com\">Home</a>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </header>");
        return builder.ToString();
    }

    private static string BuildContent(
        MetaDocsModel model,
        DocumentationView view,
        ReferenceTree tree)
    {
        var builder = new StringBuilder();
        builder.AppendLine("    <div class=\"app\" id=\"docs-app\">");
        AppendSidebar(builder, tree);
        builder.AppendLine("      <main class=\"viewer\" id=\"viewer\" tabindex=\"0\">");
        AppendHomePanel(builder, view, tree);
        foreach (var family in tree.Families)
        {
            AppendGroupPanel(builder, model, family, MetaDocsReferenceSurface.Cli);
            AppendGroupPanel(builder, model, family, MetaDocsReferenceSurface.Models);

            foreach (var subject in SubjectsFor(family, MetaDocsReferenceSurface.Cli))
            {
                AppendCliApplicationPanel(builder, model, family, subject);
            }

            foreach (var subject in SubjectsFor(family, MetaDocsReferenceSurface.Models))
            {
                AppendModelPanel(builder, model, family, subject);
            }
        }

        builder.AppendLine("      </main>");
        builder.AppendLine("    </div>");
        return builder.ToString();
    }

    private static void AppendSidebar(StringBuilder builder, ReferenceTree tree)
    {
        builder.AppendLine("      <aside class=\"sidebar\" aria-label=\"Reference navigation\">");
        builder.AppendLine("        <div class=\"side-kicker\">Reference</div>");
        foreach (var family in tree.Families)
        {
            builder.Append("        <div class=\"nav-product\">")
                .Append(Html(family.Title))
                .AppendLine("</div>");
            AppendSidebarSurface(builder, family, MetaDocsReferenceSurface.Cli);
            AppendSidebarSurface(builder, family, MetaDocsReferenceSurface.Models);
        }

        builder.AppendLine("      </aside>");
    }

    private static void AppendSidebarSurface(
        StringBuilder builder,
        FamilySection family,
        MetaDocsReferenceSurface surface)
    {
        var surfaceSection = Surface(family, surface);
        if (surfaceSection is null)
        {
            return;
        }

        var groupPanelId = GroupPanelId(family, surface);
        builder.Append("        <a class=\"nav-surface\" href=\"#")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(groupPanelId))
            .Append("\">")
            .Append(Html(surfaceSection.Title))
            .AppendLine("</a>");
        builder.AppendLine("        <ul class=\"nav-list\">");
        foreach (var subject in surfaceSection.Subjects)
        {
            var panelId = PanelId(subject, surface);
            builder.Append("          <li><a class=\"nav-link\" href=\"#")
                .Append(Attr(panelId))
                .Append("\" data-panel-link=\"")
                .Append(Attr(panelId))
                .Append("\">")
                .Append(Html(subject.DisplayName))
                .AppendLine("</a></li>");
        }

        builder.AppendLine("        </ul>");
    }

    private static void AppendHomePanel(StringBuilder builder, DocumentationView view, ReferenceTree tree)
    {
        builder.AppendLine("        <section class=\"panel is-active\" id=\"home\" data-panel=\"home\">");
        builder.AppendLine("          <div class=\"hero\">");
        builder.AppendLine("            <div class=\"eyebrow\">Reference</div>");
        builder.Append("            <h1>")
            .Append(Html(FirstNonEmpty(view.Title, view.Name, "Documentation")))
            .AppendLine("</h1>");
        builder.Append("            <p>")
            .Append(Html(FirstNonEmpty(view.Summary, "Modeled documentation for metadata-shaped things.")))
            .AppendLine("</p>");
        builder.AppendLine("          </div>");

        builder.AppendLine("          <section class=\"entry-grid\" aria-label=\"Reference entry points\">");
        foreach (var family in tree.Families)
        {
            AppendEntryCard(builder, family, MetaDocsReferenceSurface.Cli);
            AppendEntryCard(builder, family, MetaDocsReferenceSurface.Models);
        }

        builder.AppendLine("          </section>");
        builder.AppendLine("        </section>");
    }

    private static void AppendEntryCard(
        StringBuilder builder,
        FamilySection family,
        MetaDocsReferenceSurface surface)
    {
        var surfaceSection = Surface(family, surface);
        if (surfaceSection is null)
        {
            return;
        }

        var groupPanelId = GroupPanelId(family, surface);
        builder.Append("            <a class=\"entry-card\" href=\"#")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(groupPanelId))
            .AppendLine("\">");
        builder.Append("              <div class=\"label\">")
            .Append(Html(family.Title))
            .AppendLine("</div>");
        builder.Append("              <h2>")
            .Append(Html(surfaceSection.Title))
            .AppendLine("</h2>");
        builder.Append("              <p>")
            .Append(surfaceSection.Subjects.Count)
            .Append(surfaceSection.Subjects.Count == 1 ? " reference" : " references")
            .AppendLine("</p>");
        builder.AppendLine("            </a>");
    }

    private static void AppendGroupPanel(
        StringBuilder builder,
        MetaDocsModel model,
        FamilySection family,
        MetaDocsReferenceSurface surface)
    {
        var surfaceSection = Surface(family, surface);
        if (surfaceSection is null)
        {
            return;
        }

        var groupPanelId = GroupPanelId(family, surface);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel=\"")
            .Append(Attr(groupPanelId))
            .AppendLine("\">");
        builder.AppendLine("          <header class=\"panel-header\">");
        builder.Append("            <div class=\"breadcrumb\">")
            .Append(Html(family.Title))
            .Append(" / ")
            .Append(Html(surfaceSection.Title))
            .AppendLine("</div>");
        builder.Append("            <h2>")
            .Append(Html(family.Title))
            .Append(' ')
            .Append(Html(surfaceSection.Title))
            .AppendLine("</h2>");
        builder.Append("            <p class=\"panel-lead\">")
            .Append(Html(GroupSummary(family, surface)))
            .AppendLine("</p>");
        builder.AppendLine("          </header>");
        builder.AppendLine("          <div class=\"card\">");
        builder.AppendLine("            <div class=\"card-header\">");
        builder.Append("              <h3>")
            .Append(surface == MetaDocsReferenceSurface.Cli ? "Command-line tools" : "Models")
            .AppendLine("</h3>");
        builder.AppendLine("              <p>Select an item from the navigation to open its reference page.</p>");
        builder.AppendLine("            </div>");
        builder.AppendLine("            <div class=\"card-body\">");

        if (surface == MetaDocsReferenceSurface.Cli)
        {
            AppendCliGroupTable(builder, model, surfaceSection);
        }
        else
        {
            AppendModelGroupTable(builder, model, surfaceSection);
        }

        builder.AppendLine("            </div>");
        builder.AppendLine("          </div>");
        builder.AppendLine("        </section>");
    }

    private static void AppendCliGroupTable(StringBuilder builder, MetaDocsModel model, SurfaceSection surface)
    {
        builder.AppendLine("              <table>");
        builder.AppendLine("                <thead><tr><th>CLI</th><th>Summary</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var subject in surface.Subjects)
        {
            var panelId = PanelId(subject, MetaDocsReferenceSurface.Cli);
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td class=\"cmd\"><a href=\"#")
                .Append(Attr(panelId))
                .Append("\" data-panel-link=\"")
                .Append(Attr(panelId))
                .Append("\">")
                .Append(Html(subject.DisplayName))
                .AppendLine("</a></td>");
            builder.Append("                    <td>")
                .Append(Html(SubjectSummary(model, subject)))
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendModelGroupTable(StringBuilder builder, MetaDocsModel model, SurfaceSection surface)
    {
        builder.AppendLine("              <table>");
        builder.AppendLine("                <thead><tr><th>Model</th><th>Entities</th><th>Properties</th><th>Relationships</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var subject in surface.Subjects)
        {
            var panelId = PanelId(subject, MetaDocsReferenceSurface.Models);
            var entities = ChildSubjects(model, subject, "Entity");
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td><a href=\"#")
                .Append(Attr(panelId))
                .Append("\" data-panel-link=\"")
                .Append(Attr(panelId))
                .Append("\">")
                .Append(Html(subject.DisplayName))
                .AppendLine("</a></td>");
            builder.Append("                    <td>")
                .Append(Html(FindFact(model, subject, "Model", "EntityCount")))
                .AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(entities.Sum(entity => ChildSubjects(model, entity, "Property").Length))
                .AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(entities.Sum(entity => ChildSubjects(model, entity, "Relationship").Length))
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendCliApplicationPanel(
        StringBuilder builder,
        MetaDocsModel model,
        FamilySection family,
        DocumentationSubject application)
    {
        var commands = CliCommandSubjects(model, application);
        var panelId = PanelId(application, MetaDocsReferenceSurface.Cli);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(panelId))
            .Append("\" data-panel=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        AppendPanelHeader(builder, family.Title, "CLI", application.DisplayName, SubjectSummary(model, application));

        builder.AppendLine("          <div class=\"card\">");
        builder.AppendLine("            <div class=\"card-header\">");
        builder.AppendLine("              <h3>Commands</h3>");
        builder.Append("              <p>")
            .Append(commands.Length)
            .Append(commands.Length == 1 ? " command exposed by " : " commands exposed by ")
            .Append(Html(application.DisplayName))
            .AppendLine(".</p>");
        builder.AppendLine("            </div>");
        builder.AppendLine("            <div class=\"card-body\">");
        AppendCommandOverviewTable(builder, model, commands);
        builder.AppendLine("            </div>");
        builder.AppendLine("          </div>");

        foreach (var command in commands)
        {
            AppendCommandCard(builder, model, command);
        }

        builder.AppendLine("        </section>");
    }

    private static void AppendCommandOverviewTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> commands)
    {
        builder.AppendLine("              <table>");
        builder.AppendLine("                <thead><tr><th>Command</th><th>Summary</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var command in commands)
        {
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td class=\"cmd\">")
                .Append(Html(CommandUsageName(model, command)))
                .AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(Html(SubjectSummary(model, command)))
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendCommandCard(StringBuilder builder, MetaDocsModel model, DocumentationSubject command)
    {
        var options = ChildSubjects(model, command, "CliOption");
        var examples = CommandExamples(model, command);
        var guidance = CommandGuidance(model, command);
        var summary = SubjectSummary(model, command);
        builder.AppendLine("          <details class=\"card cli-command-card\">");
        builder.AppendLine("            <summary class=\"command-summary\">");
        builder.AppendLine("              <span class=\"command-toggle\" aria-hidden=\"true\"></span>");
        builder.AppendLine("              <span class=\"command-main\">");
        builder.Append("                <span class=\"command-name\">")
            .Append(Html(CommandUsageName(model, command)))
            .AppendLine("</span>");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.Append("                <span class=\"command-summary-text\">")
                .Append(Html(summary))
                .AppendLine("</span>");
        }

        builder.AppendLine("              </span>");
        builder.Append("              <span class=\"command-counts\">")
            .Append(options.Length)
            .Append(options.Length == 1 ? " option" : " options")
            .Append(" / ")
            .Append(examples.Length)
            .Append(examples.Length == 1 ? " example" : " examples")
            .AppendLine("</span>");
        builder.AppendLine("            </summary>");
        builder.AppendLine("            <div class=\"command-body\">");
        builder.AppendLine("              <div class=\"command-body-inner\">");
        builder.AppendLine("              <section class=\"subsection\">");
        builder.AppendLine("                <h4 class=\"subsection-title\">Usage</h4>");
        builder.Append("                <pre><code>")
            .Append(Html(CommandUsage(model, command)))
            .AppendLine("</code></pre>");
        builder.AppendLine("              </section>");

        if (!string.IsNullOrWhiteSpace(guidance))
        {
            builder.AppendLine("              <section class=\"subsection\">");
            builder.AppendLine("                <h4 class=\"subsection-title\">Guidance</h4>");
            builder.Append("                <p class=\"note\">")
                .Append(Html(guidance))
                .AppendLine("</p>");
            builder.AppendLine("              </section>");
        }

        if (options.Length > 0)
        {
            AppendOptionsTable(builder, model, options);
        }

        if (examples.Length > 0)
        {
            builder.AppendLine("              <section class=\"subsection\">");
            builder.AppendLine("                <h4 class=\"subsection-title\">Examples</h4>");
            foreach (var example in examples)
            {
                builder.Append("                <pre><code>")
                    .Append(Html(example))
                    .AppendLine("</code></pre>");
            }

            builder.AppendLine("              </section>");
        }

        builder.AppendLine("              </div>");
        builder.AppendLine("            </div>");
        builder.AppendLine("          </details>");
    }

    private static void AppendOptionsTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> options)
    {
        builder.AppendLine("              <section class=\"subsection\">");
        builder.AppendLine("                <h4 class=\"subsection-title\">Options</h4>");
        builder.AppendLine("                <table>");
        builder.AppendLine("                  <thead><tr><th>Option</th><th>Description</th><th>Value</th><th>Required</th></tr></thead>");
        builder.AppendLine("                  <tbody>");
        foreach (var option in options)
        {
            var description = SubjectSummary(model, option);
            builder.AppendLine("                    <tr>");
            builder.Append("                      <td class=\"opt\">")
                .Append(Html(FirstNonEmpty(FindFact(model, option, "Cli", "Syntax"), option.DisplayName)))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(description))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, option, "Cli", "ValueName")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(IsRequiredOption(description)))
                .AppendLine("</td>");
            builder.AppendLine("                    </tr>");
        }

        builder.AppendLine("                  </tbody>");
        builder.AppendLine("                </table>");
        builder.AppendLine("              </section>");
    }

    private static void AppendModelPanel(
        StringBuilder builder,
        MetaDocsModel model,
        FamilySection family,
        DocumentationSubject modelSubject)
    {
        var entities = ChildSubjects(model, modelSubject, "Entity");
        var propertyCount = entities.Sum(entity => ChildSubjects(model, entity, "Property").Length);
        var relationshipCount = entities.Sum(entity => ChildSubjects(model, entity, "Relationship").Length);
        var panelId = PanelId(modelSubject, MetaDocsReferenceSurface.Models);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(panelId))
            .Append("\" data-panel=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        AppendPanelHeader(builder, family.Title, "Models", modelSubject.DisplayName, SubjectSummary(model, modelSubject));
        builder.AppendLine("          <div class=\"summary-row\">");
        builder.Append("            <span class=\"pill\">")
            .Append(entities.Length)
            .Append(entities.Length == 1 ? " entity" : " entities")
            .AppendLine("</span>");
        builder.Append("            <span class=\"pill\">")
            .Append(propertyCount)
            .Append(propertyCount == 1 ? " property" : " properties")
            .AppendLine("</span>");
        builder.Append("            <span class=\"pill\">")
            .Append(relationshipCount)
            .Append(relationshipCount == 1 ? " relationship" : " relationships")
            .AppendLine("</span>");
        builder.AppendLine("          </div>");

        builder.AppendLine("          <div class=\"card\">");
        builder.AppendLine("            <div class=\"card-header\">");
        builder.AppendLine("              <h3>Entity index</h3>");
        builder.Append("              <p>Entities defined by the ")
            .Append(Html(modelSubject.DisplayName))
            .AppendLine(" model.</p>");
        builder.AppendLine("            </div>");
        builder.AppendLine("            <div class=\"card-body\">");
        AppendEntityIndexTable(builder, model, entities);
        builder.AppendLine("            </div>");
        builder.AppendLine("          </div>");

        foreach (var entity in entities)
        {
            AppendEntityCard(builder, model, entity);
        }

        builder.AppendLine("          <details class=\"inline-details\">");
        builder.AppendLine("            <summary>Technical metadata</summary>");
        builder.Append("            <p class=\"note\">")
            .Append(Html(TechnicalMetadataSummary(modelSubject)))
            .AppendLine("</p>");
        builder.AppendLine("          </details>");
        builder.AppendLine("        </section>");
    }

    private static void AppendPanelHeader(
        StringBuilder builder,
        string productName,
        string surfaceName,
        string name,
        string summary)
    {
        builder.AppendLine("          <header class=\"panel-header\">");
        builder.Append("            <div class=\"breadcrumb\">")
            .Append(Html(productName))
            .Append(" / ")
            .Append(Html(surfaceName))
            .AppendLine("</div>");
        builder.Append("            <h2>")
            .Append(Html(name))
            .AppendLine("</h2>");
        builder.Append("            <p class=\"panel-lead\">")
            .Append(Html(summary))
            .AppendLine("</p>");
        builder.AppendLine("          </header>");
    }

    private static void AppendEntityIndexTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> entities)
    {
        builder.AppendLine("              <table>");
        builder.AppendLine("                <thead><tr><th>Entity</th><th>Properties</th><th>Relationships</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var entity in entities)
        {
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td>");
            AppendSubjectAnchorLink(builder, entity, entity.DisplayName);
            builder.AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(ChildSubjects(model, entity, "Property").Length)
                .AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(ChildSubjects(model, entity, "Relationship").Length)
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendEntityCard(StringBuilder builder, MetaDocsModel model, DocumentationSubject entity)
    {
        var properties = ChildSubjects(model, entity, "Property");
        var relationships = ChildSubjects(model, entity, "Relationship");
        var incomingReferences = IncomingEntityReferences(model, entity);
        builder.Append("          <details class=\"card model-entity-card\" id=\"")
            .Append(Attr(SubjectAnchorId(entity)))
            .AppendLine("\">");
        builder.AppendLine("            <summary class=\"entity-summary\">");
        builder.AppendLine("              <span class=\"entity-toggle\" aria-hidden=\"true\"></span>");
        builder.AppendLine("              <span class=\"entity-main\">");
        builder.Append("                <span class=\"entity-name\">")
            .Append(Html(entity.DisplayName))
            .AppendLine("</span>");
        var summary = EntitySummary(model, entity);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.Append("                <span class=\"entity-summary-text\">")
                .Append(Html(summary))
                .AppendLine("</span>");
        }

        builder.AppendLine("              </span>");
        builder.Append("              <span class=\"entity-counts\">")
            .Append(properties.Length)
            .Append(properties.Length == 1 ? " property" : " properties")
            .Append(" / ")
            .Append(relationships.Length)
            .Append(relationships.Length == 1 ? " relationship" : " relationships")
            .Append(" / ")
            .Append(incomingReferences.Length)
            .Append(incomingReferences.Length == 1 ? " reference" : " references")
            .AppendLine("</span>");
        builder.AppendLine("            </summary>");
        builder.AppendLine("            <div class=\"entity-body\">");
        builder.AppendLine("              <div class=\"entity-body-inner\">");

        if (properties.Length > 0)
        {
            AppendPropertyTable(builder, model, properties);
        }

        if (relationships.Length > 0)
        {
            AppendRelationshipTable(builder, model, relationships);
        }

        if (incomingReferences.Length > 0)
        {
            AppendReferencedByTable(builder, model, incomingReferences);
        }

        builder.AppendLine("              </div>");
        builder.AppendLine("            </div>");
        builder.AppendLine("          </details>");
    }

    private static void AppendPropertyTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> properties)
    {
        builder.AppendLine("              <section class=\"subsection\">");
        builder.AppendLine("                <h4 class=\"subsection-title\">Properties</h4>");
        builder.AppendLine("                <table>");
        builder.AppendLine("                  <thead><tr><th>Name</th><th>Type</th><th>Required</th><th>Nullable</th><th>Description</th></tr></thead>");
        builder.AppendLine("                  <tbody>");
        foreach (var property in properties)
        {
            builder.AppendLine("                    <tr>");
            builder.Append("                      <td>")
                .Append(Html(property.DisplayName))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, property, "Model", "DataType")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, property, "Model", "Required")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, property, "Model", "Nullable")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(SubjectSummary(model, property)))
                .AppendLine("</td>");
            builder.AppendLine("                    </tr>");
        }

        builder.AppendLine("                  </tbody>");
        builder.AppendLine("                </table>");
        builder.AppendLine("              </section>");
    }

    private static void AppendRelationshipTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> relationships)
    {
        builder.AppendLine("              <section class=\"subsection\">");
        builder.AppendLine("                <h4 class=\"subsection-title\">Relationships</h4>");
        builder.AppendLine("                <table>");
        builder.AppendLine("                  <thead><tr><th>Name</th><th>Target</th><th>Role</th><th>Column</th><th>Required</th></tr></thead>");
        builder.AppendLine("                  <tbody>");
        foreach (var relationship in relationships)
        {
            builder.Append("                    <tr id=\"")
                .Append(Attr(SubjectAnchorId(relationship)))
                .AppendLine("\">");
            builder.Append("                      <td>")
                .Append(Html(relationship.DisplayName))
                .AppendLine("</td>");
            builder.Append("                      <td>");
            var targetEntity = ReferencedEntity(model, relationship);
            if (targetEntity is null)
            {
                builder.Append(Html(FindFact(model, relationship, "Model", "TargetEntity")));
            }
            else
            {
                AppendSubjectAnchorLink(builder, targetEntity, targetEntity.DisplayName);
            }

            builder.AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, relationship, "Model", "Role")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, relationship, "Model", "ColumnName")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, relationship, "Model", "Required")))
                .AppendLine("</td>");
            builder.AppendLine("                    </tr>");
        }

        builder.AppendLine("                  </tbody>");
        builder.AppendLine("                </table>");
        builder.AppendLine("              </section>");
    }

    private static void AppendReferencedByTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<EntityReference> references)
    {
        builder.AppendLine("              <section class=\"subsection\">");
        builder.AppendLine("                <h4 class=\"subsection-title\">Referenced by</h4>");
        builder.AppendLine("                <table>");
        builder.AppendLine("                  <thead><tr><th>Entity</th><th>Relationship</th><th>Role</th><th>Column</th><th>Required</th></tr></thead>");
        builder.AppendLine("                  <tbody>");
        foreach (var reference in references)
        {
            builder.AppendLine("                    <tr>");
            builder.Append("                      <td>");
            AppendSubjectAnchorLink(builder, reference.SourceEntity, reference.SourceEntity.DisplayName);
            builder.AppendLine("</td>");
            builder.Append("                      <td>");
            AppendSubjectAnchorLink(builder, reference.Relationship, reference.Relationship.DisplayName);
            builder.AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, reference.Relationship, "Model", "Role")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, reference.Relationship, "Model", "ColumnName")))
                .AppendLine("</td>");
            builder.Append("                      <td>")
                .Append(Html(FindFact(model, reference.Relationship, "Model", "Required")))
                .AppendLine("</td>");
            builder.AppendLine("                    </tr>");
        }

        builder.AppendLine("                  </tbody>");
        builder.AppendLine("                </table>");
        builder.AppendLine("              </section>");
    }

    private static string BuildRoutingScript(string siteTitle)
    {
        var title = Html(siteTitle);
        var builder = new StringBuilder();
        builder.AppendLine("    <script>");
        builder.AppendLine("      (function () {");
        builder.AppendLine("        const panels = Array.from(document.querySelectorAll('[data-panel]'));");
        builder.AppendLine("        const viewer = document.getElementById('viewer');");
        builder.AppendLine("        const app = document.getElementById('docs-app');");
        builder.AppendLine("        function focusViewer() {");
        builder.AppendLine("          if (viewer) viewer.focus({ preventScroll: true });");
        builder.AppendLine("        }");
        builder.AppendLine("        function normalizeHash() {");
        builder.AppendLine("          const raw = window.location.hash ? window.location.hash.slice(1) : 'home';");
        builder.AppendLine("          return raw || 'home';");
        builder.AppendLine("        }");
        builder.AppendLine("        function activate(id) {");
        builder.AppendLine("          const target = document.getElementById(id) || document.getElementById('home');");
        builder.AppendLine("          panels.forEach(panel => panel.classList.toggle('is-active', panel === target));");
        builder.AppendLine("          document.querySelectorAll('[data-panel-link]').forEach(link => {");
        builder.AppendLine("            link.classList.toggle('is-active', link.getAttribute('href') === '#' + target.id);");
        builder.AppendLine("          });");
        builder.AppendLine("          if (viewer) viewer.scrollTop = 0;");
        builder.AppendLine("          focusViewer();");
        builder.Append("          document.title = target.id === 'home' ? '")
            .Append(title)
            .Append(" · Reference' : (target.querySelector('h2')?.textContent || '")
            .Append(title)
            .Append("') + ' · ")
            .Append(title)
            .AppendLine("';");
        builder.AppendLine("        }");
        builder.AppendLine("        if (viewer) viewer.addEventListener('pointerdown', focusViewer);");
        builder.AppendLine("        if (app) app.addEventListener('pointerdown', event => {");
        builder.AppendLine("          if (!(event.target instanceof Element) || event.target.closest('.sidebar')) return;");
        builder.AppendLine("          focusViewer();");
        builder.AppendLine("        });");
        builder.AppendLine("        document.addEventListener('click', event => {");
        builder.AppendLine("          if (!(event.target instanceof Element)) return;");
        builder.AppendLine("          const link = event.target.closest('[data-local-anchor]');");
        builder.AppendLine("          if (!link) return;");
        builder.AppendLine("          const targetId = link.getAttribute('data-local-anchor');");
        builder.AppendLine("          const target = targetId ? document.getElementById(targetId) : null;");
        builder.AppendLine("          if (!target) return;");
        builder.AppendLine("          event.preventDefault();");
        builder.AppendLine("          const panel = target.closest('[data-panel]');");
        builder.AppendLine("          if (panel && !panel.classList.contains('is-active')) activate(panel.id);");
        builder.AppendLine("          const details = target instanceof HTMLDetailsElement ? target : target.closest('details');");
        builder.AppendLine("          if (details) details.open = true;");
        builder.AppendLine("          target.scrollIntoView({ block: 'start' });");
        builder.AppendLine("          focusViewer();");
        builder.AppendLine("        });");
        builder.AppendLine("        window.addEventListener('hashchange', () => activate(normalizeHash()));");
        builder.AppendLine("        activate(normalizeHash());");
        builder.AppendLine("      })();");
        builder.AppendLine("    </script>");
        return builder.ToString();
    }

    private static SurfaceSection? Surface(FamilySection family, MetaDocsReferenceSurface surface) =>
        family.Surfaces.FirstOrDefault(section => section.Surface == surface);

    private static IReadOnlyList<DocumentationSubject> SubjectsFor(
        FamilySection family,
        MetaDocsReferenceSurface surface) =>
        Surface(family, surface)?.Subjects ?? Array.Empty<DocumentationSubject>();

    private static string PanelId(DocumentationSubject subject, MetaDocsReferenceSurface surface)
    {
        var prefix = surface == MetaDocsReferenceSurface.Cli ? "cli" : "model";
        var stableName = surface == MetaDocsReferenceSurface.Cli
            ? FirstNonEmpty(subject.DisplayName, subject.NativeId, subject.Id)
            : FirstNonEmpty(subject.DisplayName.Replace(" model", string.Empty, StringComparison.OrdinalIgnoreCase), subject.NativeId, subject.Id);
        return $"{prefix}-{MetaDocsImportSession.NormalizeKey(stableName)}";
    }

    private static string GroupPanelId(FamilySection family, MetaDocsReferenceSurface surface) =>
        $"group-{MetaDocsPublicReferenceClassifier.ProductFamilyKey(family.Family)}-{MetaDocsPublicReferenceClassifier.ReferenceSurfaceKey(surface)}";

    private static string SubjectAnchorId(DocumentationSubject subject) =>
        $"subject-{MetaDocsImportSession.NormalizeKey(subject.Id)}";

    private static void AppendSubjectAnchorLink(
        StringBuilder builder,
        DocumentationSubject subject,
        string label)
    {
        var anchorId = SubjectAnchorId(subject);
        builder.Append("<a class=\"entity-ref-link\" href=\"#")
            .Append(Attr(anchorId))
            .Append("\" data-local-anchor=\"")
            .Append(Attr(anchorId))
            .Append("\">")
            .Append(Html(FirstNonEmpty(label, subject.DisplayName, subject.NativeId, subject.Id)))
            .Append("</a>");
    }

    private static string GroupSummary(FamilySection family, MetaDocsReferenceSurface surface) =>
        (family.Family, surface) switch
        {
            (MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Cli) =>
                "Core metadata command-line tools.",
            (MetaDocsProductFamily.Meta, MetaDocsReferenceSurface.Models) =>
                "Core metadata model references.",
            (MetaDocsProductFamily.MetaBi, MetaDocsReferenceSurface.Cli) =>
                "BI modeling and conversion command-line tools.",
            (MetaDocsProductFamily.MetaBi, MetaDocsReferenceSurface.Models) =>
                "BI-side sanctioned model references.",
            _ => "Reference material.",
        };

    private static string SubjectSummary(MetaDocsModel model, DocumentationSubject subject) =>
        ShortSummary(FirstNonEmpty(FindNarrative(model, subject, "Summary")?.Body, subject.Summary));

    private static string EntitySummary(MetaDocsModel model, DocumentationSubject entity)
    {
        var summary = SubjectSummary(model, entity);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        var fallback = $"Entity {entity.DisplayName}.";
        return string.Equals(summary.Trim(), fallback, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : summary;
    }

    private static string CommandUsageName(MetaDocsModel model, DocumentationSubject command) =>
        FirstNonEmpty(FindFact(model, command, "Cli", "CommandPath"), command.DisplayName);

    private static string CommandUsage(MetaDocsModel model, DocumentationSubject command)
    {
        var usages = Facts(model, command)
            .Where(fact =>
                string.Equals(fact.Kind, "Usage", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(fact.Value))
            .OrderBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .Select(fact => fact.Value!)
            .ToArray();
        return usages.Length == 0 ? CommandUsageName(model, command) : string.Join(Environment.NewLine, usages);
    }

    private static string CommandGuidance(MetaDocsModel model, DocumentationSubject command)
    {
        var preferred = Narratives(model, command)
            .Where(narrative =>
                !string.Equals(narrative.Slot, "Summary", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(narrative.Slot, "Example", StringComparison.OrdinalIgnoreCase))
            .Select(narrative => CleanProse(narrative.Body))
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return ShortSummary(preferred);
        }

        var note = Facts(model, command)
            .Where(fact => string.Equals(fact.Kind, "Note", StringComparison.OrdinalIgnoreCase))
            .Select(fact => CleanProse(fact.Value))
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
        return ShortSummary(note);
    }

    private static string[] CommandExamples(MetaDocsModel model, DocumentationSubject command)
    {
        var examples = new List<string>();
        foreach (var narrative in Narratives(model, command)
                     .Where(narrative => string.Equals(narrative.Slot, "Example", StringComparison.OrdinalIgnoreCase)))
        {
            examples.AddRange(ExtractCodeBlocks(narrative.Body));
        }

        examples.AddRange(ChildSubjects(model, command, "CliArgument")
            .Where(subject => string.Equals(subject.NativeKind, "CliExample", StringComparison.OrdinalIgnoreCase))
            .Select(subject => FirstNonEmpty(FindFact(model, subject, "Cli", "CommandText"), subject.NativeId))
            .Where(static example => !string.IsNullOrWhiteSpace(example)));
        return examples
            .Select(CleanProse)
            .Where(static example => !string.IsNullOrWhiteSpace(example))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExtractCodeBlocks(string? body)
    {
        var text = body ?? string.Empty;
        if (!text.Contains("```", StringComparison.Ordinal))
        {
            yield return text;
            yield break;
        }

        var code = new StringBuilder();
        var inFence = false;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence)
                {
                    yield return code.ToString().Trim();
                    code.Clear();
                    inFence = false;
                }
                else
                {
                    inFence = true;
                }

                continue;
            }

            if (inFence)
            {
                code.AppendLine(line);
            }
        }

        if (code.Length > 0)
        {
            yield return code.ToString().Trim();
        }
    }

    private static string IsRequiredOption(string description) =>
        description.TrimStart().StartsWith("Required", StringComparison.OrdinalIgnoreCase) ? "True" : "False";

    private static string TechnicalMetadataSummary(DocumentationSubject subject) =>
        $"Source: {FirstNonEmpty(subject.DocumentationSource?.DisplayName, subject.DocumentationSource?.Id, "unknown")}. Status: {FirstNonEmpty(subject.Status, "Current")}.";

    private static DocumentationNarrative? FindNarrative(
        MetaDocsModel model,
        DocumentationSubject subject,
        string slot) =>
        Narratives(model, subject).FirstOrDefault(row =>
            string.Equals(row.Slot, slot, StringComparison.OrdinalIgnoreCase));

    private static DocumentationNarrative[] Narratives(MetaDocsModel model, DocumentationSubject subject) =>
        model.DocumentationNarrativeList
            .Where(row => string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase))
            .Where(row => !string.IsNullOrWhiteSpace(row.Body))
            .OrderBy(row => ParseOrdinal(row.Ordinal))
            .ToArray();

    private static DocumentationFact[] Facts(MetaDocsModel model, DocumentationSubject subject) =>
        model.DocumentationFactList
            .Where(row =>
                string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static DocumentationSubject[] ChildSubjects(
        MetaDocsModel model,
        DocumentationSubject parent,
        string kind) =>
        model.DocumentationSubjectList
            .Where(subject => string.Equals(subject.ParentKey ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase))
            .Where(subject => string.Equals(subject.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Where(IsRenderable)
            .OrderBy(subject => ParseOrdinal(subject.Ordinal))
            .ThenBy(subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static EntityReference[] IncomingEntityReferences(MetaDocsModel model, DocumentationSubject entity)
    {
        return model.DocumentationRelationshipList
            .Where(relationship =>
                string.Equals(relationship.Kind, "ReferencesEntity", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.ToSubjectKey, entity.Id, StringComparison.OrdinalIgnoreCase))
            .Select(relationship => FindSubject(model, relationship.FromSubjectKey))
            .Where(subject => subject is not null && IsRenderable(subject))
            .Cast<DocumentationSubject>()
            .Select(relationshipSubject =>
            {
                var sourceEntity = string.IsNullOrWhiteSpace(relationshipSubject.ParentKey)
                    ? null
                    : FindSubject(model, relationshipSubject.ParentKey!);
                return sourceEntity is null || !IsRenderable(sourceEntity)
                    ? null
                    : new EntityReference(sourceEntity, relationshipSubject);
            })
            .Where(reference => reference is not null)
            .Cast<EntityReference>()
            .OrderBy(reference => reference.SourceEntity.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Relationship.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DocumentationSubject? ReferencedEntity(MetaDocsModel model, DocumentationSubject relationshipSubject)
    {
        var targetKey = model.DocumentationRelationshipList
            .Where(relationship =>
                string.Equals(relationship.Kind, "ReferencesEntity", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.FromSubjectKey, relationshipSubject.Id, StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.ToSubjectKey)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(targetKey) ? null : FindSubject(model, targetKey!);
    }

    private static DocumentationSubject? FindSubject(MetaDocsModel model, string subjectKey) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Id, subjectKey, StringComparison.OrdinalIgnoreCase));

    private static DocumentationSubject[] CliCommandSubjects(MetaDocsModel model, DocumentationSubject application)
    {
        var childrenByParent = model.DocumentationSubjectList
            .Where(IsRenderable)
            .Where(subject => !string.IsNullOrWhiteSpace(subject.ParentKey))
            .GroupBy(subject => subject.ParentKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var commands = new List<DocumentationSubject>();
        AddCliCommandDescendants(application.Id);
        return commands.ToArray();

        void AddCliCommandDescendants(string parentKey)
        {
            if (!childrenByParent.TryGetValue(parentKey, out var children))
            {
                return;
            }

            foreach (var command in children
                         .Where(subject => string.Equals(subject.Kind, "CliCommand", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(subject => ParseOrdinal(subject.Ordinal))
                         .ThenBy(subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                commands.Add(command);
                AddCliCommandDescendants(command.Id);
            }
        }
    }

    private static string FindFact(
        MetaDocsModel model,
        DocumentationSubject subject,
        string kind,
        string name) =>
        model.DocumentationFactList
            .Where(row =>
                string.Equals(row.SubjectKey, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ResolveCss(MetaDocsModel model)
    {
        var asset = model.DocumentationThemeAssetList
            .Where(row => string.Equals(row.AssetKind, "Css", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => ParseOrdinal(row.Ordinal))
            .FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.Content));
        return asset?.Content ?? string.Empty;
    }

    private static string ResolveBrandMarkSvg(MetaDocsModel model)
    {
        var asset = model.DocumentationThemeAssetList
            .Where(row => string.Equals(row.Id, "theme:metametabi-static:asset:brand-mark", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => ParseOrdinal(row.Ordinal))
            .FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.Content));
        return asset?.Content ?? MetaDocsDefaults.MetametabiBrandMarkSvg;
    }

    private static string ResolveInlineBrandMarkSvg(MetaDocsModel model)
    {
        var svg = ResolveBrandMarkSvg(model);
        try
        {
            var root = XElement.Parse(svg, LoadOptions.PreserveWhitespace);
            root.SetAttributeValue("class", "brand-mark");
            root.SetAttributeValue("aria-hidden", "true");
            root.SetAttributeValue("focusable", "false");
            return root.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return MetaDocsDefaults.MetametabiBrandMarkSvg
                .Replace("<svg ", "<svg class=\"brand-mark\" aria-hidden=\"true\" focusable=\"false\" ", StringComparison.Ordinal);
        }
    }

    private static string BuildFaviconLink(MetaDocsModel model)
    {
        var svg = ResolveBrandMarkSvg(model);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        return $"<link rel=\"icon\" type=\"image/svg+xml\" href=\"data:image/svg+xml;base64,{base64}\" />";
    }

    private static string ResolveShellTemplate(MetaDocsModel model) =>
        model.DocumentationTemplateList
            .Where(row => string.Equals(row.Kind, "SiteShell", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => ParseOrdinal(row.Ordinal))
            .Select(row => row.Html)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
        ?? MetaDocsDefaults.DefaultShellTemplate;

    private static string ApplyShellTemplate(
        string template,
        string title,
        string css,
        string navigation,
        string content,
        string favicon,
        string script) =>
        template
            .Replace("{{title}}", Html(title), StringComparison.Ordinal)
            .Replace("{{css}}", css ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{navigation}}", navigation ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{content}}", content ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{favicon}}", favicon ?? string.Empty, StringComparison.Ordinal)
            .Replace("<link rel=\"icon\" type=\"image/svg+xml\" href=\"metametabi-mark.svg\" />", favicon ?? string.Empty, StringComparison.Ordinal)
            .Replace("{{script}}", script ?? string.Empty, StringComparison.Ordinal);

    private static bool IsRenderable(DocumentationSubject subject) =>
        !string.Equals(subject.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Deprecated", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(subject.Status, "Ignored", StringComparison.OrdinalIgnoreCase) &&
        !IsSourceFingerprintSubject(subject);

    private static bool IsSourceFingerprintSubject(DocumentationSubject subject) =>
        string.Equals(subject.NativeId, "SourceFingerprint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(subject.DisplayName, "SourceFingerprint", StringComparison.OrdinalIgnoreCase);

    private static string ResolveSiteTitle(DocumentationView view)
    {
        var title = FirstNonEmpty(view.Title, view.Name, "meta + meta-bi");
        return title.EndsWith(" reference", StringComparison.OrdinalIgnoreCase)
            ? title[..^" reference".Length]
            : title;
    }

    private static int ParseOrdinal(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : int.MaxValue;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ShortSummary(string? value)
    {
        var text = CleanProse(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var firstLine = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        if (firstLine.Length <= 220)
        {
            return firstLine;
        }

        var sentenceEnd = firstLine.IndexOf(". ", StringComparison.Ordinal);
        if (sentenceEnd > 40 && sentenceEnd < 220)
        {
            return firstLine[..(sentenceEnd + 1)];
        }

        return firstLine[..217].TrimEnd() + "...";
    }

    private static string CleanProse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(CleanProseLine)
            .ToArray();
        var builder = new StringBuilder();
        var previousBlank = true;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!previousBlank)
                {
                    builder.AppendLine();
                }

                previousBlank = true;
                continue;
            }

            builder.AppendLine(line);
            previousBlank = false;
        }

        return builder.ToString().Trim();
    }

    private static string CleanProseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return trimmed[2..].TrimStart();
        }

        if (trimmed.StartsWith("• ", StringComparison.Ordinal))
        {
            return trimmed[2..].TrimStart();
        }

        var dotIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dotIndex > 0 &&
            dotIndex < 4 &&
            trimmed[..dotIndex].All(char.IsDigit))
        {
            return trimmed[(dotIndex + 2)..].TrimStart();
        }

        return trimmed;
    }

    private static string Html(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Attr(string? value) =>
        Html(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed record ClassifiedReferenceSubject(
        DocumentationSubject Subject,
        MetaDocsPublicReferenceClassification Classification);

    private sealed record ReferenceTree(IReadOnlyList<FamilySection> Families);

    private sealed record FamilySection(
        MetaDocsProductFamily Family,
        string Title,
        IReadOnlyList<SurfaceSection> Surfaces);

    private sealed record SurfaceSection(
        MetaDocsReferenceSurface Surface,
        string Title,
        IReadOnlyList<DocumentationSubject> Subjects);

    private sealed record EntityReference(
        DocumentationSubject SourceEntity,
        DocumentationSubject Relationship);
}
