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
        var view = MetaDocsDefaults.EnsureDefaultView(model);
        var tree = ResolveReferenceTree(model, view);
        var siteTitle = ResolveSiteTitle(view, tree.Root);

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
        DocumentationView view)
    {
        var root = view.RootSubject ??
                   model.DocumentationSubjectList
                       .Where(IsRenderable)
                       .Where(static subject => subject.ParentSubject is null)
                       .OrderBy(static subject => subject.DisplayName, StringComparer.OrdinalIgnoreCase)
                       .FirstOrDefault()
                   ?? throw new InvalidOperationException("The documentation view does not declare a root subject.");

        var childrenByParent = model.DocumentationSubjectList
            .Where(IsRenderable)
            .Where(static subject => subject.ParentSubject is not null)
            .GroupBy(static subject => subject.ParentSubject!.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return new ReferenceTree(root, ChildNodes(root));

        IReadOnlyList<ReferenceNode> ChildNodes(DocumentationSubject parent) =>
            MetaDocsOrdering.ByPrevious(
                    childrenByParent.TryGetValue(parent.Id, out var children)
                        ? children
                        : Array.Empty<DocumentationSubject>(),
                    static subject => subject.PreviousSubject,
                    static subject => FirstNonEmpty(subject.DisplayPath, subject.DisplayName, subject.Id))
                .Select(subject => new ReferenceNode(
                    subject,
                    MetaDocsViewNavigation.Title(model, subject),
                    ChildNodes(subject)))
                .ToArray();
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
        builder.AppendLine("        <div class=\"topbar-actions\">");
        builder.AppendLine("          <button class=\"menu-button\" type=\"button\" aria-controls=\"reference-navigation\" aria-expanded=\"false\">Menu</button>");
        builder.AppendLine("          <a class=\"home-button\" href=\"https://metametabi.com\">Home</a>");
        builder.AppendLine("        </div>");
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
        foreach (var child in tree.Children)
        {
            AppendPanelsForNode(builder, model, child, Array.Empty<string>());
        }

        builder.AppendLine("      </main>");
        builder.AppendLine("    </div>");
        return builder.ToString();
    }

    private static void AppendSidebar(StringBuilder builder, ReferenceTree tree)
    {
        builder.AppendLine("      <aside class=\"sidebar\" id=\"reference-navigation\" aria-label=\"Reference navigation\">");
        builder.AppendLine("        <div class=\"side-kicker\">Reference</div>");
        foreach (var family in tree.Children)
        {
            builder.Append("        <div class=\"nav-product\">")
                .Append(Html(family.NavigationTitle))
                .AppendLine("</div>");
            foreach (var section in family.Children)
            {
                AppendSidebarSection(builder, section);
            }
        }

        builder.AppendLine("      </aside>");
    }

    private static void AppendSidebarSection(
        StringBuilder builder,
        ReferenceNode section)
    {
        if (IsGenericContentSubject(section.Subject))
        {
            builder.Append("        ");
            AppendSidebarSubjectLink(builder, section, "nav-surface");
            builder.AppendLine();
            return;
        }

        var groupPanelId = GroupPanelId(section);
        builder.Append("        <a class=\"nav-surface\" href=\"#")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(groupPanelId))
            .Append("\">")
            .Append(Html(section.NavigationTitle))
            .AppendLine("</a>");
        builder.AppendLine("        <ul class=\"nav-list\">");
        foreach (var child in section.Children.Where(static child =>
                     IsCliApplication(child.Subject) ||
                     IsModel(child.Subject) ||
                     IsGenericContentSubject(child.Subject)))
        {
            builder.Append("          <li>");
            AppendSidebarSubjectLink(builder, child, "nav-link");
            builder.AppendLine("</li>");
        }

        builder.AppendLine("        </ul>");
    }

    private static void AppendSidebarSubjectLink(
        StringBuilder builder,
        ReferenceNode node,
        string cssClass)
    {
        var panelId = PanelId(node.Subject);
        builder.Append("<a class=\"")
            .Append(Attr(cssClass))
            .Append("\" href=\"#")
            .Append(Attr(panelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(panelId))
            .Append("\">")
            .Append(Html(node.NavigationTitle))
            .Append("</a>");
    }

    private static void AppendHomePanel(StringBuilder builder, DocumentationView view, ReferenceTree tree)
    {
        builder.AppendLine("        <section class=\"panel is-active\" id=\"home\" data-panel=\"home\">");
        builder.AppendLine("          <div class=\"hero\">");
        builder.AppendLine("            <div class=\"eyebrow\">Reference</div>");
        builder.Append("            <h1>")
            .Append(Html(FirstNonEmpty(tree.Root.DisplayName, view.Title, view.Name, "Documentation")))
            .AppendLine("</h1>");
        builder.Append("            <p>")
            .Append(HtmlInline(FirstNonEmpty(SubjectSummary(null, tree.Root), tree.Root.Summary, view.Summary, "Modeled documentation for metadata-shaped things.")))
            .AppendLine("</p>");
        builder.AppendLine("          </div>");

        builder.AppendLine("          <section class=\"entry-grid\" aria-label=\"Reference entry points\">");
        foreach (var family in tree.Children)
        {
            foreach (var section in family.Children)
            {
                if (IsGenericContentSubject(section.Subject))
                {
                    AppendEntryCard(builder, section, family.Subject.DisplayName);
                }
                else
                {
                    AppendEntryCard(builder, family, section);
                }
            }
        }

        builder.AppendLine("          </section>");
        builder.AppendLine("        </section>");
    }

    private static void AppendEntryCard(
        StringBuilder builder,
        ReferenceNode family,
        ReferenceNode section)
    {
        var subjects = ReferenceSubjects(section);
        var groupPanelId = GroupPanelId(section);
        builder.Append("            <a class=\"entry-card\" href=\"#")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(groupPanelId))
            .AppendLine("\">");
        builder.Append("              <div class=\"label\">")
            .Append(Html(family.Subject.DisplayName))
            .AppendLine("</div>");
        builder.Append("              <h2>")
            .Append(Html(section.Subject.DisplayName))
            .AppendLine("</h2>");
        builder.Append("              <p>")
            .Append(subjects.Count)
            .Append(subjects.Count == 1 ? " reference" : " references")
            .AppendLine("</p>");
        builder.AppendLine("            </a>");
    }

    private static void AppendEntryCard(
        StringBuilder builder,
        ReferenceNode node,
        string label)
    {
        var panelId = PanelId(node.Subject);
        builder.Append("            <a class=\"entry-card\" href=\"#")
            .Append(Attr(panelId))
            .Append("\" data-panel-link=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        builder.Append("              <div class=\"label\">")
            .Append(Html(label))
            .AppendLine("</div>");
        builder.Append("              <h2>")
            .Append(Html(node.NavigationTitle))
            .AppendLine("</h2>");
        builder.Append("              <p>")
            .Append(HtmlInline(SubjectSummary(null, node.Subject)))
            .AppendLine("</p>");
        builder.AppendLine("            </a>");
    }

    private static void AppendPanelsForNode(
        StringBuilder builder,
        MetaDocsModel model,
        ReferenceNode node,
        IReadOnlyList<string> ancestors)
    {
        if (IsCliApplication(node.Subject))
        {
            AppendCliApplicationPanel(
                builder,
                model,
                AncestorOrDefault(ancestors, ^2, string.Empty),
                AncestorOrDefault(ancestors, ^1, string.Empty),
                node.Subject);
            return;
        }

        if (IsModel(node.Subject))
        {
            AppendModelPanel(
                builder,
                model,
                AncestorOrDefault(ancestors, ^2, string.Empty),
                AncestorOrDefault(ancestors, ^1, string.Empty),
                node.Subject);
            return;
        }

        if (ReferenceSubjects(node).Count > 0)
        {
            AppendGroupPanel(
                builder,
                model,
                AncestorOrDefault(ancestors, ^1, string.Empty),
                node);
        }
        else if (IsGenericContentSubject(node.Subject))
        {
            AppendGenericSubjectPanel(builder, model, node.Subject);
        }

        var nextAncestors = ancestors.Concat(new[] { node.Subject.DisplayName }).ToArray();
        foreach (var child in node.Children)
        {
            AppendPanelsForNode(builder, model, child, nextAncestors);
        }
    }

    private static string AncestorOrDefault(IReadOnlyList<string> ancestors, Index index, string fallback)
    {
        var offset = index.GetOffset(ancestors.Count);
        return offset >= 0 && offset < ancestors.Count ? ancestors[offset] : fallback;
    }

    private static void AppendGroupPanel(
        StringBuilder builder,
        MetaDocsModel model,
        string familyTitle,
        ReferenceNode section)
    {
        var subjects = ReferenceSubjects(section);
        var groupPanelId = GroupPanelId(section);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(groupPanelId))
            .Append("\" data-panel=\"")
            .Append(Attr(groupPanelId))
            .AppendLine("\">");
        builder.AppendLine("          <header class=\"panel-header\">");
        if (!string.IsNullOrWhiteSpace(familyTitle))
        {
            builder.Append("            <div class=\"breadcrumb\">")
                .Append(Html(familyTitle))
                .Append(" / ")
                .Append(Html(section.Subject.DisplayName))
                .AppendLine("</div>");
        }

        builder.Append("            <h2>")
            .Append(Html(string.IsNullOrWhiteSpace(familyTitle) ? section.Subject.DisplayName : $"{familyTitle} {section.Subject.DisplayName}"))
            .AppendLine("</h2>");
        builder.Append("            <p class=\"panel-lead\">")
            .Append(HtmlInline(SubjectSummary(model, section.Subject)))
            .AppendLine("</p>");
        builder.AppendLine("          </header>");
        builder.AppendLine("          <div class=\"card\">");
        builder.AppendLine("            <div class=\"card-header\">");
        builder.Append("              <h3>")
            .Append(Html(GroupItemTitle(subjects)))
            .AppendLine("</h3>");
        builder.AppendLine("              <p>Select an item from the navigation to open its reference page.</p>");
        builder.AppendLine("            </div>");
        builder.AppendLine("            <div class=\"card-body\">");

        if (subjects.Any(child => IsCliApplication(child.Subject)))
        {
            AppendCliGroupTable(builder, model, subjects);
        }
        else if (subjects.Any(child => IsModel(child.Subject)))
        {
            AppendModelGroupTable(builder, model, subjects);
        }

        builder.AppendLine("            </div>");
        builder.AppendLine("          </div>");
        builder.AppendLine("        </section>");
    }

    private static void AppendCliGroupTable(StringBuilder builder, MetaDocsModel model, IReadOnlyList<ReferenceNode> subjects)
    {
        builder.AppendLine("              <table class=\"summary-table\">");
        builder.AppendLine("                <thead><tr><th>CLI</th><th>Summary</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var node in subjects.Where(node => IsCliApplication(node.Subject)))
        {
            var subject = node.Subject;
            var panelId = PanelId(subject);
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td class=\"cmd\"><a href=\"#")
                .Append(Attr(panelId))
                .Append("\" data-panel-link=\"")
                .Append(Attr(panelId))
                .Append("\">")
                .Append(Html(subject.DisplayName))
                .AppendLine("</a></td>");
            builder.Append("                    <td>")
                .Append(HtmlInline(SubjectSummary(model, subject)))
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendModelGroupTable(StringBuilder builder, MetaDocsModel model, IReadOnlyList<ReferenceNode> subjects)
    {
        builder.AppendLine("              <table>");
        builder.AppendLine("                <thead><tr><th>Model</th><th>Entities</th><th>Properties</th><th>Relationships</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var node in subjects.Where(node => IsModel(node.Subject)))
        {
            var subject = node.Subject;
            var panelId = PanelId(subject);
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
        string familyTitle,
        string sectionTitle,
        DocumentationSubject application)
    {
        var commands = CliCommandSubjects(model, application);
        var panelId = PanelId(application);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(panelId))
            .Append("\" data-panel=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        AppendPanelHeader(builder, familyTitle, sectionTitle, application.DisplayName, SubjectSummary(model, application));
        AppendNarrativeSections(builder, model, application);

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

        AppendExamplesSection(builder, model, application, title: "General examples");

        builder.AppendLine("        </section>");
    }

    private static void AppendGenericSubjectPanel(
        StringBuilder builder,
        MetaDocsModel model,
        DocumentationSubject subject)
    {
        var panelId = PanelId(subject);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(panelId))
            .Append("\" data-panel=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        builder.AppendLine("          <header class=\"panel-header\">");
        builder.Append("            <h2>")
            .Append(Html(subject.DisplayName))
            .AppendLine("</h2>");
        var lead = SubjectLead(model, subject);
        if (!string.IsNullOrWhiteSpace(lead.Body))
        {
            AppendPanelLead(builder, lead, "            ");
        }

        builder.AppendLine("          </header>");
        AppendNarrativeSections(builder, model, subject);
        AppendExamplesSection(builder, model, subject, title: "Examples");
        builder.AppendLine("        </section>");
    }

    private static void AppendCommandOverviewTable(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationSubject> commands)
    {
        builder.AppendLine("              <table class=\"summary-table\">");
        builder.AppendLine("                <thead><tr><th>Command</th><th>Summary</th></tr></thead>");
        builder.AppendLine("                <tbody>");
        foreach (var command in commands)
        {
            builder.AppendLine("                  <tr>");
            builder.Append("                    <td class=\"cmd\">")
                .Append(Html(CommandUsageName(model, command)))
                .AppendLine("</td>");
            builder.Append("                    <td>")
                .Append(HtmlInline(SubjectSummary(model, command)))
                .AppendLine("</td>");
            builder.AppendLine("                  </tr>");
        }

        builder.AppendLine("                </tbody>");
        builder.AppendLine("              </table>");
    }

    private static void AppendCommandCard(StringBuilder builder, MetaDocsModel model, DocumentationSubject command)
    {
        var options = ChildSubjects(model, command, "CliOption");
        var examples = Examples(model, command);
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
                .Append(HtmlInline(summary))
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

        AppendNarrativeSections(builder, model, command, "              ");

        if (!string.IsNullOrWhiteSpace(guidance))
        {
            builder.AppendLine("              <section class=\"subsection\">");
            builder.AppendLine("                <h4 class=\"subsection-title\">Guidance</h4>");
            builder.Append("                <p class=\"note\">")
                .Append(HtmlInline(guidance))
                .AppendLine("</p>");
            builder.AppendLine("              </section>");
        }

        if (options.Length > 0)
        {
            AppendOptionsTable(builder, model, options);
        }

        if (examples.Length > 0)
        {
            AppendExamplesSection(builder, model, examples, "              ");
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
                .Append(Html(OptionValueDetails(model, option)))
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

    private static void AppendExamplesSection(
        StringBuilder builder,
        MetaDocsModel model,
        DocumentationSubject subject,
        string indent = "          ",
        string title = "Examples")
    {
        AppendExamplesSection(builder, model, Examples(model, subject), indent, title);
    }

    private static void AppendNarrativeSections(
        StringBuilder builder,
        MetaDocsModel model,
        DocumentationSubject subject,
        string indent = "          ")
    {
        foreach (var narrative in NarrativeSections(model, subject))
        {
            builder.Append(indent).AppendLine("<section class=\"subsection description-block\">");
            if (!string.IsNullOrWhiteSpace(narrative.Title))
            {
                builder.Append(indent).Append("  <h3 class=\"document-heading\">")
                    .Append(Html(narrative.Title))
                    .AppendLine("</h3>");
            }

            AppendFormattedText(builder, narrative.Body!, narrative.BodyFormat, indent + "  ");
            builder.Append(indent).AppendLine("</section>");
        }
    }

    private static void AppendExamplesSection(
        StringBuilder builder,
        MetaDocsModel model,
        IReadOnlyList<DocumentationExample> examples,
        string indent,
        string title = "Examples")
    {
        if (examples.Count == 0)
        {
            return;
        }

        builder.Append(indent).AppendLine("<section class=\"subsection examples\">");
        builder.Append(indent).Append("  <h4 class=\"subsection-title\">")
            .Append(Html(title))
            .AppendLine("</h4>");
        foreach (var example in examples)
        {
            builder.Append(indent).AppendLine("  <article class=\"example-block\">");
            builder.Append(indent).Append("    <h5>")
                .Append(Html(example.Title))
                .AppendLine("</h5>");
            if (!string.IsNullOrWhiteSpace(example.Summary))
            {
                builder.Append(indent).Append("    <p class=\"note\">")
                    .Append(HtmlInline(example.Summary!))
                    .AppendLine("</p>");
            }

            foreach (var section in ExampleSections(model, example))
            {
                if (!string.IsNullOrWhiteSpace(section.Title))
                {
                    builder.Append(indent).Append("    <h6>")
                        .Append(Html(section.Title!))
                        .AppendLine("</h6>");
                }

                if (!string.IsNullOrWhiteSpace(section.Body))
                {
                    AppendFormattedText(builder, section.Body!, section.BodyFormat, indent + "    ");
                }

                foreach (var code in ExampleCodes(model, section))
                {
                    if (!string.IsNullOrWhiteSpace(code.Title))
                    {
                        builder.Append(indent).Append("    <div class=\"code-title\">")
                            .Append(Html(code.Title!))
                            .AppendLine("</div>");
                    }

                    builder.Append(indent).Append("    <pre><code");
                    if (!string.IsNullOrWhiteSpace(code.Language))
                    {
                        builder.Append(" class=\"language-").Append(Attr(code.Language!)).Append('"');
                    }

                    builder.Append('>')
                        .Append(Html(code.Code))
                        .AppendLine("</code></pre>");
                }
            }

            builder.Append(indent).AppendLine("  </article>");
        }

        builder.Append(indent).AppendLine("</section>");
    }

    private static void AppendFormattedText(
        StringBuilder builder,
        string value,
        string bodyFormat,
        string indent)
    {
        if (string.Equals(bodyFormat, "Markdown", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(indent).AppendLine("<div class=\"markdown-content\">");
            builder.Append(MetaDocsMarkdown.ToHtml(value).TrimEnd()).AppendLine();
            builder.Append(indent).AppendLine("</div>");
            return;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            var paragraph = new StringBuilder();
            while (index < lines.Length &&
                   !string.IsNullOrWhiteSpace(lines[index]))
            {
                if (paragraph.Length > 0)
                {
                    paragraph.Append(' ');
                }

                paragraph.Append(lines[index].Trim());
                index++;
            }

            if (paragraph.Length > 0)
            {
                builder.Append(indent).Append("<p>")
                    .Append(Html(paragraph.ToString()))
                    .AppendLine("</p>");
            }
        }
    }

    private static void AppendPanelLead(StringBuilder builder, FormattedText lead, string indent)
    {
        if (string.Equals(lead.BodyFormat, "Markdown", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(indent).AppendLine("<div class=\"panel-lead markdown-content\">");
            builder.Append(MetaDocsMarkdown.ToHtml(lead.Body).TrimEnd()).AppendLine();
            builder.Append(indent).AppendLine("</div>");
            return;
        }

        builder.Append(indent).Append("<p class=\"panel-lead\">")
            .Append(Html(lead.Body))
            .AppendLine("</p>");
    }

    private static string OptionValueDetails(MetaDocsModel model, DocumentationSubject option)
    {
        var valueName = FindFact(model, option, "Cli", "ValueName");
        var allowedValues = FindFact(model, option, "Cli", "AllowedValues");
        if (string.IsNullOrWhiteSpace(allowedValues))
        {
            return valueName;
        }

        return string.IsNullOrWhiteSpace(valueName)
            ? allowedValues
            : $"{valueName}: {allowedValues}";
    }

    private static void AppendModelPanel(
        StringBuilder builder,
        MetaDocsModel model,
        string familyTitle,
        string sectionTitle,
        DocumentationSubject modelSubject)
    {
        var entities = ChildSubjects(model, modelSubject, "Entity");
        var propertyCount = entities.Sum(entity => ChildSubjects(model, entity, "Property").Length);
        var relationshipCount = entities.Sum(entity => ChildSubjects(model, entity, "Relationship").Length);
        var panelId = PanelId(modelSubject);
        builder.Append("        <section class=\"panel\" id=\"")
            .Append(Attr(panelId))
            .Append("\" data-panel=\"")
            .Append(Attr(panelId))
            .AppendLine("\">");
        AppendPanelHeader(builder, familyTitle, sectionTitle, modelSubject.DisplayName, SubjectSummary(model, modelSubject));
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

        AppendNarrativeSections(builder, model, modelSubject);

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

        AppendExamplesSection(builder, model, modelSubject, title: "General examples");

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
        if (!string.IsNullOrWhiteSpace(summary))
        {
            builder.Append("            <p class=\"panel-lead\">")
                .Append(HtmlInline(summary))
                .AppendLine("</p>");
        }

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
                .Append(HtmlInline(summary))
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

        AppendNarrativeSections(builder, model, entity, "              ");

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

        AppendExamplesSection(builder, model, entity, "              ");

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
                .Append(HtmlInline(SubjectSummary(model, property)))
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
        builder.AppendLine("        const sidebar = document.getElementById('reference-navigation');");
        builder.AppendLine("        const menuButton = document.querySelector('.menu-button');");
        builder.AppendLine("        function setMenuOpen(open) {");
        builder.AppendLine("          if (!sidebar || !menuButton) return;");
        builder.AppendLine("          sidebar.classList.toggle('is-open', open);");
        builder.AppendLine("          menuButton.setAttribute('aria-expanded', open ? 'true' : 'false');");
        builder.AppendLine("        }");
        builder.AppendLine("        function focusViewer() {");
        builder.AppendLine("          if (viewer) viewer.focus({ preventScroll: true });");
        builder.AppendLine("        }");
        builder.AppendLine("        function normalizeHash() {");
        builder.AppendLine("          const raw = window.location.hash ? window.location.hash.slice(1) : 'home';");
        builder.AppendLine("          return raw || 'home';");
        builder.AppendLine("        }");
        builder.AppendLine("        const initialRoute = normalizeHash();");
        builder.AppendLine("        if (window.location.hash) {");
        builder.AppendLine("          history.replaceState(null, '', window.location.pathname + window.location.search);");
        builder.AppendLine("        }");
        builder.AppendLine("        function activate(id) {");
        builder.AppendLine("          const target = document.getElementById(id) || document.getElementById('home');");
        builder.AppendLine("          panels.forEach(panel => panel.classList.toggle('is-active', panel === target));");
        builder.AppendLine("          document.querySelectorAll('[data-panel-link]').forEach(link => {");
        builder.AppendLine("            link.classList.toggle('is-active', link.getAttribute('href') === '#' + target.id);");
        builder.AppendLine("          });");
        builder.AppendLine("          if (viewer) viewer.scrollTo(0, 0);");
        builder.AppendLine("          setMenuOpen(false);");
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
        builder.AppendLine("        if (menuButton) menuButton.addEventListener('click', () => {");
        builder.AppendLine("          setMenuOpen(!sidebar?.classList.contains('is-open'));");
        builder.AppendLine("        });");
        builder.AppendLine("        if (app) app.addEventListener('pointerdown', event => {");
        builder.AppendLine("          if (!(event.target instanceof Element) || event.target.closest('.sidebar')) return;");
        builder.AppendLine("          focusViewer();");
        builder.AppendLine("        });");
        builder.AppendLine("        document.addEventListener('keydown', event => {");
        builder.AppendLine("          if (event.key === 'Escape') setMenuOpen(false);");
        builder.AppendLine("        });");
        builder.AppendLine("        document.addEventListener('click', event => {");
        builder.AppendLine("          if (!(event.target instanceof Element)) return;");
        builder.AppendLine("          const panelLink = event.target.closest('[data-panel-link]');");
        builder.AppendLine("          if (panelLink) {");
        builder.AppendLine("            event.preventDefault();");
        builder.AppendLine("            const panelId = panelLink.getAttribute('href')?.slice(1) || 'home';");
        builder.AppendLine("            history.pushState(null, '', '#' + panelId);");
        builder.AppendLine("            activate(panelId);");
        builder.AppendLine("            setMenuOpen(false);");
        builder.AppendLine("            return;");
        builder.AppendLine("          }");
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
        builder.AppendLine("        window.addEventListener('popstate', () => activate(normalizeHash()));");
        builder.AppendLine("        activate(initialRoute);");
        builder.AppendLine("        history.replaceState(null, '', '#' + initialRoute);");
        builder.AppendLine("      })();");
        builder.AppendLine("    </script>");
        return builder.ToString();
    }

    private static IReadOnlyList<ReferenceNode> ReferenceSubjects(ReferenceNode section) =>
        section.Children
            .Where(static node => IsCliApplication(node.Subject) || IsModel(node.Subject))
            .ToArray();

    private static bool IsCliApplication(DocumentationSubject subject) =>
        MetaDocsVocabulary.IsSubjectType(subject, "CliApplication");

    private static bool IsModel(DocumentationSubject subject) =>
        MetaDocsVocabulary.IsSubjectType(subject, "Model");

    private static bool IsGenericContentSubject(DocumentationSubject subject) =>
        MetaDocsVocabulary.IsSubjectType(subject, "Guide");

    private static string GroupItemTitle(IReadOnlyList<ReferenceNode> subjects)
    {
        if (subjects.Any(static node => IsCliApplication(node.Subject)))
        {
            return "Command-line tools";
        }

        if (subjects.Any(static node => IsModel(node.Subject)))
        {
            return "Models";
        }

        return "Subjects";
    }

    private static string PanelId(DocumentationSubject subject)
    {
        var prefix = IsCliApplication(subject)
            ? "cli"
            : IsModel(subject)
                ? "model"
                : "subject";
        var stableName = IsModel(subject)
            ? FirstNonEmpty(subject.DisplayName.Replace(" model", string.Empty, StringComparison.OrdinalIgnoreCase), subject.NativeId, subject.Id)
            : IsCliApplication(subject)
                ? FirstNonEmpty(subject.DisplayName, subject.NativeId, subject.Id)
                : subject.Id;
        return $"{prefix}-{MetaDocsImportSession.NormalizeKey(stableName)}";
    }

    private static string GroupPanelId(ReferenceNode section) =>
        $"group-{MetaDocsImportSession.NormalizeKey(section.Subject.Id)}";

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

    private static string SubjectSummary(MetaDocsModel? model, DocumentationSubject subject) =>
        ShortSummary(FirstNonEmpty(model is null ? string.Empty : FindNarrative(model, subject, "Summary")?.Body, subject.Summary));

    private static FormattedText SubjectLead(MetaDocsModel model, DocumentationSubject subject)
    {
        var narrative = FindNarrative(model, subject, "Summary");
        return !string.IsNullOrWhiteSpace(narrative?.Body)
            ? new FormattedText(narrative.Body, narrative.BodyFormat)
            : new FormattedText(subject.Summary ?? string.Empty, "Markdown");
    }

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
                MetaDocsVocabulary.IsFactType(fact, "Usage") &&
                !string.IsNullOrWhiteSpace(fact.Value))
            .OrderBy(fact => fact.Name, StringComparer.OrdinalIgnoreCase)
            .Select(fact => fact.Value!)
            .ToArray();
        return usages.Length == 0 ? CommandUsageName(model, command) : string.Join(Environment.NewLine, usages);
    }

    private static string CommandGuidance(MetaDocsModel model, DocumentationSubject command)
    {
        var note = Facts(model, command)
            .Where(fact => MetaDocsVocabulary.IsFactType(fact, "Note"))
            .Select(fact => CleanProse(fact.Value))
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
        return ShortSummary(note);
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
        MetaDocsOrdering.ByPrevious(
                model.DocumentationNarrativeList
                    .Where(row => string.Equals(row.DocumentationSubject?.Id, subject.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(row => !string.IsNullOrWhiteSpace(row.Body))
                    .Where(row => !string.Equals(row.ReviewStatus, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
                    .Where(row => !string.Equals(row.ReviewStatus, "Deprecated", StringComparison.OrdinalIgnoreCase))
                    .Where(row => !string.Equals(row.ReviewStatus, "Ignored", StringComparison.OrdinalIgnoreCase)),
                static row => row.PreviousNarrative,
                static row => $"{row.Slot}:{row.Title}:{row.Id}")
            .ToArray();

    private static DocumentationNarrative[] NarrativeSections(MetaDocsModel model, DocumentationSubject subject) =>
        Narratives(model, subject)
            .Where(static row =>
                !string.Equals(row.Slot, "Summary", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Slot, "Example", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static DocumentationExample[] Examples(MetaDocsModel model, DocumentationSubject subject) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleList
                    .Where(row => ReferenceEquals(row.DocumentationSubject, subject))
                    .Where(row => !string.Equals(row.ReviewStatus, "MissingFromSource", StringComparison.OrdinalIgnoreCase)),
                static row => row.PreviousExample,
                static row => $"{row.Title}:{row.Id}")
            .ToArray();

    private static DocumentationExampleSection[] ExampleSections(MetaDocsModel model, DocumentationExample example) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleSectionList
                    .Where(row => ReferenceEquals(row.DocumentationExample, example)),
                static row => row.PreviousSection,
                static row => $"{row.Title}:{row.Id}")
            .ToArray();

    private static DocumentationExampleCode[] ExampleCodes(MetaDocsModel model, DocumentationExampleSection section) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationExampleCodeList
                    .Where(row => ReferenceEquals(row.DocumentationExampleSection, section)),
                static row => row.PreviousCode,
                static row => $"{row.Title}:{row.Id}")
            .ToArray();

    private static DocumentationFact[] Facts(MetaDocsModel model, DocumentationSubject subject) =>
        model.DocumentationFactList
            .Where(row =>
                string.Equals(row.DocumentationSubject?.Id, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static DocumentationSubject[] ChildSubjects(
        MetaDocsModel model,
        DocumentationSubject parent,
        string kind) =>
        MetaDocsOrdering.ByPrevious(
                model.DocumentationSubjectList
                    .Where(subject => string.Equals(subject.ParentSubject?.Id ?? string.Empty, parent.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(subject => MetaDocsVocabulary.IsSubjectType(subject, kind))
                    .Where(IsRenderable),
                static subject => subject.PreviousSubject,
                static subject => subject.DisplayName)
            .ToArray();

    private static EntityReference[] IncomingEntityReferences(MetaDocsModel model, DocumentationSubject entity)
    {
        return model.DocumentationRelationshipList
            .Where(relationship =>
                MetaDocsVocabulary.IsRelationshipType(relationship, "ReferencesEntity") &&
                string.Equals(relationship.ToSubject?.Id, entity.Id, StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.FromSubject)
            .Where(subject => subject is not null && IsRenderable(subject))
            .Cast<DocumentationSubject>()
            .Select(relationshipSubject =>
            {
                var sourceEntity = relationshipSubject.ParentSubject;
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
                MetaDocsVocabulary.IsRelationshipType(relationship, "ReferencesEntity") &&
                string.Equals(relationship.FromSubject?.Id, relationshipSubject.Id, StringComparison.OrdinalIgnoreCase))
            .Select(relationship => relationship.ToSubject?.Id)
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
            .Where(subject => subject.ParentSubject is not null)
            .GroupBy(subject => subject.ParentSubject!.Id, StringComparer.OrdinalIgnoreCase)
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

            foreach (var command in MetaDocsOrdering.ByPrevious(
                         children.Where(subject => MetaDocsVocabulary.IsSubjectType(subject, "CliCommand")),
                         static subject => subject.PreviousSubject,
                         static subject => subject.DisplayName))
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
                string.Equals(row.DocumentationSubject?.Id, subject.Id, StringComparison.OrdinalIgnoreCase) &&
                MetaDocsVocabulary.IsFactType(row, kind) &&
                string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(row.Status, "MissingFromSource", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Value)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ResolveCss(MetaDocsModel model)
    {
        var asset = MetaDocsOrdering.ByPrevious(
                model.DocumentationThemeAssetList
                    .Where(row => MetaDocsVocabulary.IsThemeAssetType(row, "Css")),
                static row => row.PreviousAsset,
                static row => row.Name)
            .FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.Content));
        return asset?.Content ?? string.Empty;
    }

    private static string ResolveBrandMarkSvg(MetaDocsModel model)
    {
        var asset = model.DocumentationThemeAssetList
            .Where(row => string.Equals(row.Id, "theme:metametabi-static:asset:brand-mark", StringComparison.OrdinalIgnoreCase))
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
        MetaDocsOrdering.ByPrevious(
                model.DocumentationTemplateList
                    .Where(row => MetaDocsVocabulary.IsTemplateType(row, "SiteShell")),
                static row => row.PreviousTemplate,
                static row => row.Name)
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

    private static string ResolveSiteTitle(DocumentationView view, DocumentationSubject root)
    {
        var title = FirstNonEmpty(root.DisplayName, view.Title, view.Name, "meta + meta-bi");
        return title.EndsWith(" reference", StringComparison.OrdinalIgnoreCase)
            ? title[..^" reference".Length]
            : title;
    }

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

    private static string HtmlInline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var html = MetaDocsMarkdown.ToHtml(value).Trim();
        if (html.StartsWith("<p>", StringComparison.Ordinal) &&
            html.EndsWith("</p>", StringComparison.Ordinal))
        {
            return html[3..^4];
        }

        return html;
    }

    private static string Attr(string? value) =>
        Html(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed record ReferenceTree(
        DocumentationSubject Root,
        IReadOnlyList<ReferenceNode> Children);

    private sealed record ReferenceNode(
        DocumentationSubject Subject,
        string NavigationTitle,
        IReadOnlyList<ReferenceNode> Children);

    private sealed record FormattedText(string Body, string BodyFormat);

    private sealed record EntityReference(
        DocumentationSubject SourceEntity,
        DocumentationSubject Relationship);
}
