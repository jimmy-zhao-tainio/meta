using MetaDocs;

namespace MetaDocs.Core;

public static class MetaDocsDefaults
{
    public static DocumentationWorkspace EnsureDocumentationWorkspace(
        MetaDocsModel model,
        string id,
        string name,
        string kind)
    {
        var workspace = model.DocumentationWorkspaceList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (workspace is not null)
        {
            return workspace;
        }

        workspace = new DocumentationWorkspace
        {
            Id = id,
            Name = name,
            Kind = kind,
            Summary = "Documentation overlay workspace.",
        };
        model.DocumentationWorkspaceList.Add(workspace);
        return workspace;
    }

    public static DocumentationTheme EnsureDefaultTheme(MetaDocsModel model)
    {
        var theme = model.DocumentationThemeList.FirstOrDefault(row =>
            string.Equals(row.Id, "theme:metametabi-static", StringComparison.OrdinalIgnoreCase));
        if (theme is null)
        {
            theme = new DocumentationTheme
            {
                Id = "theme:metametabi-static",
                Name = "metametabi-static",
                Version = "0.1",
                RenderOptions = "static",
            };
            model.DocumentationThemeList.Add(theme);
        }

        if (!model.DocumentationTemplateList.Any(row =>
                string.Equals(row.Id, "template:metametabi-static:shell", StringComparison.OrdinalIgnoreCase)))
        {
            var template = new DocumentationTemplate
            {
                Id = "template:metametabi-static:shell",
                DocumentationTheme = theme,
                Name = "metametabi-static-shell",
                Kind = "SiteShell",
                Html = DefaultShellTemplate,
                SourceUrl = "modeled",
                Ordinal = "010",
            };
            model.DocumentationTemplateList.Add(template);

            model.DocumentationTemplateRegionList.Add(new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:navigation",
                DocumentationTemplate = template,
                Name = "navigation",
                RegionKind = "Navigation",
                Ordinal = "010",
            });
            model.DocumentationTemplateRegionList.Add(new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:content",
                DocumentationTemplate = template,
                Name = "content",
                RegionKind = "Content",
                Ordinal = "020",
            });
            model.DocumentationTemplateRegionList.Add(new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:script",
                DocumentationTemplate = template,
                Name = "script",
                RegionKind = "Script",
                Ordinal = "030",
            });
        }

        if (!model.DocumentationLayoutList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:layout:reference", StringComparison.OrdinalIgnoreCase)))
        {
            model.DocumentationLayoutList.Add(new DocumentationLayout
            {
                Id = "theme:metametabi-static:layout:reference",
                DocumentationTheme = theme,
                Name = "reference",
                LayoutKind = "Reference",
                Ordinal = "010",
            });
        }

        if (!model.DocumentationComponentTemplateList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:component:subject", StringComparison.OrdinalIgnoreCase)))
        {
            model.DocumentationComponentTemplateList.Add(new DocumentationComponentTemplate
            {
                Id = "theme:metametabi-static:component:subject",
                DocumentationTheme = theme,
                Name = "subject",
                ComponentKind = "Subject",
                TemplateText = "Subject rows render from DocumentationSubject, DocumentationFact, and DocumentationNarrative.",
                Ordinal = "010",
            });
        }

        if (!model.DocumentationThemeAssetList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:asset:css", StringComparison.OrdinalIgnoreCase)))
        {
            model.DocumentationThemeAssetList.Add(new DocumentationThemeAsset
            {
                Id = "theme:metametabi-static:asset:css",
                DocumentationTheme = theme,
                Name = "site.css",
                AssetKind = "Css",
                MediaType = "text/css",
                Href = string.Empty,
                Content = DefaultCss,
                Hash = string.Empty,
                Ordinal = "010",
            });
        }

        if (!model.DocumentationThemeAssetList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:asset:brand-mark", StringComparison.OrdinalIgnoreCase)))
        {
            model.DocumentationThemeAssetList.Add(new DocumentationThemeAsset
            {
                Id = "theme:metametabi-static:asset:brand-mark",
                DocumentationTheme = theme,
                Name = "metametabi-mark.svg",
                AssetKind = "Image",
                MediaType = "image/svg+xml",
                Href = MetametabiBrandMarkHref,
                Content = MetametabiBrandMarkSvg,
                Hash = "metametabi.com-inline-nav-svg-20260608",
                Ordinal = "020",
            });
        }

        return theme;
    }

    public static DocumentationView EnsureDefaultView(MetaDocsModel model)
    {
        var view = model.DocumentationViewList.FirstOrDefault(row =>
            string.Equals(row.Id, "view:default", StringComparison.OrdinalIgnoreCase));
        if (view is not null)
        {
            return view;
        }

        view = new DocumentationView
        {
            Id = "view:default",
            Name = "Default",
            Kind = "Site",
            Title = "meta + meta-bi reference",
            Summary = "Command-line and model references for the current public MetaDocs suite.",
            Ordinal = "010",
        };
        model.DocumentationViewList.Add(view);
        return view;
    }

    public const string DefaultShellTemplate =
        """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            {{favicon}}
            <title>{{title}}</title>
            <style>
        {{css}}
            </style>
          </head>
          <body>
        {{navigation}}
        {{content}}
        {{script}}
          </body>
        </html>
        """;

    public const string MetametabiBrandMarkHref = "";

    public const string MetametabiBrandMarkSvg =
        """
        <svg width="22" height="22" viewBox="0 0 22 22" fill="none" xmlns="http://www.w3.org/2000/svg">
          <circle cx="11" cy="11" r="11" fill="#0a0a0a"/>
          <polyline points="9.5,8 13.5,11 9.5,14" stroke="#cccccc" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
        </svg>
        """;

    public const string DefaultCss =
        """
        :root{color-scheme:light;--bg:#fafaf9;--panel:#fff;--text:#0a0a0a;--muted:#555;--subtle:#999;--line:rgba(10,10,10,.08);--line-soft:rgba(10,10,10,.055);--accent:#374151;--accent-soft:#f1f4f7;--code-bg:#f8fafc;--radius:10px;--shadow:0 1px 2px rgba(10,10,10,.025),0 8px 22px rgba(10,10,10,.03);--mono:ui-monospace,SFMono-Regular,Menlo,Consolas,"Liberation Mono",monospace;--sans:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
        *{box-sizing:border-box}
        body{margin:0;font-family:var(--sans);font-feature-settings:"cv02","cv03","cv04","cv11","ss01","ss03";font-optical-sizing:auto;color:var(--text);background:var(--bg);line-height:1.5;overflow:hidden}
        a{color:inherit;text-decoration:none}
        code,pre{font-family:var(--mono)}
        .topbar{height:60px;background:rgba(250,250,249,.9);border-bottom:1px solid rgba(0,0,0,.06);display:flex;justify-content:center;position:sticky;top:0;z-index:20;backdrop-filter:blur(24px)}
        .topbar-inner{width:min(1320px,calc(100vw - 64px));display:flex;align-items:center;justify-content:space-between}
        .brand{display:inline-flex;align-items:center;gap:10px;font-size:14px;font-weight:600;letter-spacing:-.02em;line-height:1;color:#0a0a0a}
        .brand-mark{width:22px;height:22px;display:block;flex-shrink:0}
        .home-button{height:36px;display:inline-flex;justify-content:center;align-items:center;padding:0 20px;border-radius:6px;background:#0a0a0a;color:#fff;font-size:13px;font-weight:500;line-height:1}
        .app{height:calc(100vh - 60px);width:min(1320px,calc(100vw - 64px));margin:0 auto;display:grid;grid-template-columns:300px minmax(0,1fr);gap:0}
        .sidebar{height:calc(100vh - 60px);overflow-y:auto;padding:38px 10px 48px 0;scrollbar-width:thin;scrollbar-color:#b8c5d4 transparent}
        .sidebar::-webkit-scrollbar{width:8px}.sidebar::-webkit-scrollbar-track{background:transparent}.sidebar::-webkit-scrollbar-thumb{background:#c2cedb;border-radius:999px;border:2px solid var(--bg)}
        .side-kicker{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.22em;margin:0 0 22px}
        .nav-product{font-size:14px;font-weight:700;margin:24px 0 8px}.nav-product:first-of-type{margin-top:0}
        .nav-surface{display:block;color:#1f2937;font-size:13px;font-weight:700;margin:12px 0 6px 10px;border-radius:8px;padding:4px 9px}
        .nav-list{list-style:none;margin:0 0 0 22px;padding:0;display:grid;gap:3px}
        .nav-link{display:block;padding:5px 9px;border-radius:8px;color:var(--muted);font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .nav-link:hover,.nav-surface:hover{background:#f1f4f7;color:var(--text)}
        .nav-link.is-active,.nav-surface.is-active{color:#1f2937;background:var(--accent-soft);font-weight:600}
        .viewer{height:calc(100vh - 60px);overflow-y:auto;padding:38px 18px 64px 34px;scrollbar-gutter:stable;scrollbar-width:thin;scrollbar-color:#b8c5d4 transparent}
        .viewer:focus{outline:0}
        .viewer::-webkit-scrollbar{width:10px}.viewer::-webkit-scrollbar-track{background:transparent}.viewer::-webkit-scrollbar-thumb{background:#c2cedb;border-radius:999px;border:2px solid var(--bg)}
        .panel{display:none;max-width:940px}.panel.is-active{display:block}
        .hero{background:transparent;padding:36px 0 44px;border-bottom:1px solid var(--line);margin-bottom:28px}
        .eyebrow{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.22em;margin-bottom:18px}
        h1{font-size:clamp(42px,6vw,68px);line-height:.95;letter-spacing:0;margin:0 0 18px}
        .hero p,.panel-lead{max-width:680px;color:#294057;font-size:16px;margin:0}
        .entry-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin-top:28px}
        .entry-card{background:var(--panel);border:1px solid var(--line);border-radius:var(--radius);padding:20px 22px;box-shadow:var(--shadow)}
        .entry-card .label{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.16em;margin-bottom:8px}
        .entry-card h2{font-size:23px;letter-spacing:0;margin:0 0 6px}.entry-card p{color:var(--muted);margin:0;font-size:14px}
        .panel-header{border-bottom:1px solid var(--line);padding:18px 0 28px;margin-bottom:22px}
        .breadcrumb{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.14em;margin-bottom:12px}
        .panel-header h2{font-size:40px;line-height:1;letter-spacing:0;margin:0 0 12px}
        .summary-row{display:flex;gap:8px;flex-wrap:wrap;margin:14px 0 24px}.pill{border:1px solid var(--line);background:#fff;color:var(--muted);border-radius:999px;padding:3px 9px;font-size:12px}
        .card{background:var(--panel);border:1px solid var(--line);border-radius:var(--radius);box-shadow:var(--shadow);margin-top:16px;overflow:hidden}
        .card-header{padding:18px 20px;border-bottom:1px solid var(--line-soft)}.card-header h3{margin:0 0 4px;font-size:18px;letter-spacing:0}.card-header p{margin:0;color:var(--muted);font-size:14px}
        .model-entity-card{border-color:var(--line-soft);box-shadow:none;margin-top:8px;border-radius:9px}.model-entity-card summary{list-style:none}.model-entity-card summary::-webkit-details-marker{display:none}.entity-summary{display:grid;grid-template-columns:24px minmax(0,1fr) auto;gap:10px;align-items:center;padding:9px 14px;cursor:pointer;border-bottom:1px solid transparent}.model-entity-card[open] .entity-summary{border-bottom-color:var(--line-soft)}.entity-toggle{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;color:#4b5563;font-family:var(--mono);font-size:12px;font-weight:600}.entity-toggle::before{content:"+"}.model-entity-card[open] .entity-toggle::before{content:"-"}.entity-main{min-width:0;display:grid;gap:1px}.entity-name{font-weight:700;color:var(--text);font-size:14px;line-height:1.25;overflow-wrap:anywhere}.entity-summary-text{color:var(--muted);font-size:12.5px;line-height:1.35;overflow-wrap:anywhere}.entity-counts{color:var(--subtle);font-size:10.5px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;white-space:nowrap}
        .entity-body{display:grid;grid-template-columns:24px minmax(0,1fr);column-gap:10px;padding:14px 14px 16px;overflow-x:auto}.entity-body-inner{grid-column:2;min-width:0}
        .cli-command-card{border-color:var(--line-soft);box-shadow:none;margin-top:8px;border-radius:9px}.cli-command-card summary{list-style:none}.cli-command-card summary::-webkit-details-marker{display:none}.command-summary{display:grid;grid-template-columns:24px minmax(0,1fr) auto;gap:10px;align-items:center;padding:9px 14px;cursor:pointer;border-bottom:1px solid transparent}.cli-command-card[open] .command-summary{border-bottom-color:var(--line-soft)}.command-toggle{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;color:#4b5563;font-family:var(--mono);font-size:12px;font-weight:600}.command-toggle::before{content:"+"}.cli-command-card[open] .command-toggle::before{content:"-"}.command-main{min-width:0;display:grid;gap:1px}.command-name{font-family:var(--mono);font-weight:600;color:var(--text);font-size:13px;line-height:1.35;overflow-wrap:anywhere}.command-summary-text{color:var(--muted);font-size:12.5px;line-height:1.35;overflow-wrap:anywhere}.command-counts{color:var(--subtle);font-size:10.5px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;white-space:nowrap}
        .command-body{display:grid;grid-template-columns:24px minmax(0,1fr);column-gap:10px;padding:14px 14px 16px;overflow-x:auto}.command-body-inner{grid-column:2;min-width:0}
        .card-body{padding:14px 16px 16px;overflow-x:auto}.subsection{margin-top:18px}.subsection:first-child{margin-top:0}.subsection-title{margin:0 0 8px;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.08em}
        table{width:100%;border-collapse:collapse;font-size:12.5px;background:#fff;border:1px solid var(--line-soft);border-radius:10px;overflow:hidden}th,td{text-align:left;vertical-align:top;border-bottom:1px solid var(--line-soft);padding:8px 10px}th{color:var(--subtle);font-size:10.5px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;background:#fbfdff}td{color:#425064}tr:last-child td{border-bottom:0}
        .cmd,.opt{font-family:var(--mono);color:#1f2937;font-weight:600;white-space:nowrap}.entity-ref-link{font-weight:600;color:#0f355f;text-decoration:none}.entity-ref-link:hover{text-decoration:underline}
        pre{margin:0 0 10px;padding:12px 14px;background:var(--code-bg);border:1px solid var(--line);border-radius:9px;overflow-x:auto;font-size:13px}.note{color:var(--muted);font-size:14px;margin:0}
        details.inline-details{border-top:1px solid var(--line-soft);padding-top:14px;margin-top:18px}details.inline-details summary{cursor:pointer;color:var(--muted);font-size:13px;font-weight:600}
        @media(max-width:900px){body{overflow:auto}.topbar-inner,.app{width:min(100vw - 32px,1320px)}.app{height:auto;grid-template-columns:1fr}.sidebar{height:auto;position:static;padding-bottom:20px;border-bottom:1px solid var(--line)}.viewer{height:auto;overflow:visible;padding:22px 0 64px}.entry-grid{grid-template-columns:1fr}.entity-summary,.command-summary{grid-template-columns:24px minmax(0,1fr)}.entity-counts,.command-counts{grid-column:2;white-space:normal}.entity-body,.command-body{grid-template-columns:24px minmax(0,1fr)}}
        """;
}
