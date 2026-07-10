using MetaDocs;

namespace MetaDocs.Core;

public static class MetaDocsDefaults
{
    public static DocumentationWorkspace EnsureDocumentationWorkspace(
        MetaDocsModel model,
        string id,
        string name,
        string workspaceType)
    {
        var type = MetaDocsVocabulary.EnsureWorkspaceType(model, workspaceType);
        var workspace = model.DocumentationWorkspaceList.FirstOrDefault(row =>
            string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
        if (workspace is not null)
        {
            workspace.DocumentationWorkspaceType = type;
            return workspace;
        }

        workspace = new DocumentationWorkspace
        {
            Id = id,
            Name = name,
            DocumentationWorkspaceType = type,
            Summary = "Documentation overlay workspace.",
        };
        model.DocumentationWorkspaceList.Add(workspace);
        return workspace;
    }

    public static DocumentationTheme EnsureDefaultTheme(MetaDocsModel model)
    {
        var shellTemplateType = MetaDocsVocabulary.EnsureTemplateType(model, "SiteShell");
        var navigationRegionType = MetaDocsVocabulary.EnsureTemplateRegionType(model, "Navigation");
        var contentRegionType = MetaDocsVocabulary.EnsureTemplateRegionType(model, "Content");
        var scriptRegionType = MetaDocsVocabulary.EnsureTemplateRegionType(model, "Script");
        var referenceLayoutType = MetaDocsVocabulary.EnsureLayoutType(model, "Reference");
        var subjectComponentType = MetaDocsVocabulary.EnsureComponentTemplateType(model, "Subject");
        var cssAssetType = MetaDocsVocabulary.EnsureThemeAssetType(model, "Css");
        var imageAssetType = MetaDocsVocabulary.EnsureThemeAssetType(model, "Image");
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
                DocumentationTemplateType = shellTemplateType,
                Html = DefaultShellTemplate,
                SourceUrl = "modeled",
            };
            model.DocumentationTemplateList.Add(template);

            var navigationRegion = new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:navigation",
                DocumentationTemplate = template,
                Name = "navigation",
                DocumentationTemplateRegionType = navigationRegionType,
            };
            model.DocumentationTemplateRegionList.Add(navigationRegion);
            var contentRegion = new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:content",
                DocumentationTemplate = template,
                Name = "content",
                DocumentationTemplateRegionType = contentRegionType,
                PreviousRegion = navigationRegion,
            };
            model.DocumentationTemplateRegionList.Add(contentRegion);
            model.DocumentationTemplateRegionList.Add(new DocumentationTemplateRegion
            {
                Id = "template:metametabi-static:shell:region:script",
                DocumentationTemplate = template,
                Name = "script",
                DocumentationTemplateRegionType = scriptRegionType,
                PreviousRegion = contentRegion,
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
                DocumentationLayoutType = referenceLayoutType,
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
                DocumentationComponentTemplateType = subjectComponentType,
                TemplateText = "Subject rows render from DocumentationSubject, DocumentationFact, and DocumentationNarrative.",
            });
        }

        DocumentationThemeAsset? cssAsset = null;
        if (!model.DocumentationThemeAssetList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:asset:css", StringComparison.OrdinalIgnoreCase)))
        {
            cssAsset = new DocumentationThemeAsset
            {
                Id = "theme:metametabi-static:asset:css",
                DocumentationTheme = theme,
                Name = "site.css",
                DocumentationThemeAssetType = cssAssetType,
                MediaType = "text/css",
                Href = string.Empty,
                Content = DefaultCss,
                Hash = string.Empty,
            };
            model.DocumentationThemeAssetList.Add(cssAsset);
        }
        else
        {
            cssAsset = model.DocumentationThemeAssetList.First(row =>
                string.Equals(row.Id, "theme:metametabi-static:asset:css", StringComparison.OrdinalIgnoreCase));
        }

        if (!model.DocumentationThemeAssetList.Any(row =>
                string.Equals(row.Id, "theme:metametabi-static:asset:brand-mark", StringComparison.OrdinalIgnoreCase)))
        {
            model.DocumentationThemeAssetList.Add(new DocumentationThemeAsset
            {
                Id = "theme:metametabi-static:asset:brand-mark",
                DocumentationTheme = theme,
                Name = "metametabi-mark.svg",
                DocumentationThemeAssetType = imageAssetType,
                MediaType = "image/svg+xml",
                Href = MetametabiBrandMarkHref,
                Content = MetametabiBrandMarkSvg,
                Hash = "metametabi.com-inline-nav-svg-20260608",
                PreviousAsset = cssAsset,
            });
        }

        return theme;
    }

    public static DocumentationView EnsureDefaultView(MetaDocsModel model)
    {
        var viewType = MetaDocsVocabulary.EnsureViewType(model, "Site");
        var view = model.DocumentationViewList.FirstOrDefault(row =>
            string.Equals(row.Id, "view:default", StringComparison.OrdinalIgnoreCase));
        if (view is not null)
        {
            view.DocumentationViewType = viewType;
            return view;
        }

        view = new DocumentationView
        {
            Id = "view:default",
            Name = "Default",
            DocumentationViewType = viewType,
            Title = "Contents",
            Summary = "Documentation subjects in this workspace.",
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
        :root{color-scheme:light;--bg:#fafaf9;--panel:#fff;--text:#0a0a0a;--muted:#555;--subtle:#999;--line:rgba(10,10,10,.08);--line-soft:rgba(10,10,10,.055);--accent:#374151;--accent-soft:#f1f4f7;--code-bg:#f8fafc;--radius:8px;--shadow:0 1px 2px rgba(10,10,10,.025),0 8px 22px rgba(10,10,10,.03);--shell-max:1680px;--edge:max(24px,calc((100vw - var(--shell-max))/2));--nav-width:260px;--mono:ui-monospace,SFMono-Regular,Menlo,Consolas,"Liberation Mono",monospace;--sans:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
        *{box-sizing:border-box}
        html,body{height:100%}
        body{margin:0;font-family:var(--sans);font-feature-settings:"cv02","cv03","cv04","cv11","ss01","ss03";font-optical-sizing:auto;color:var(--text);background:var(--bg);line-height:1.5;overflow:hidden}
        a{color:inherit;text-decoration:none}
        code,pre{font-family:var(--mono)}
        .topbar{height:60px;background:rgba(250,250,249,.94);border-bottom:1px solid rgba(0,0,0,.06);display:flex;justify-content:center;position:relative;z-index:20;backdrop-filter:blur(24px)}
        .topbar-inner{width:min(var(--shell-max),calc(100vw - 48px));display:flex;align-items:center;justify-content:space-between}
        .brand{display:inline-flex;align-items:center;gap:10px;font-size:14px;font-weight:600;letter-spacing:0;line-height:1;color:#0a0a0a}
        .brand-mark{width:22px;height:22px;display:block;flex-shrink:0}
        .topbar-actions{display:flex;align-items:center;gap:8px}
        .menu-button{display:none;height:36px;align-items:center;justify-content:center;padding:0 14px;border:1px solid var(--line);border-radius:6px;background:var(--panel);color:var(--text);font:600 13px/1 var(--sans);cursor:pointer}
        .menu-button:hover{background:var(--accent-soft)}
        .home-button{height:36px;display:inline-flex;justify-content:center;align-items:center;padding:0 20px;border-radius:6px;background:#0a0a0a;color:#fff;font-size:13px;font-weight:500;line-height:1}
        .app{height:calc(100vh - 60px);width:100%;display:grid;grid-template-columns:calc(var(--edge) + var(--nav-width)) minmax(0,1fr);overflow:hidden}
        .sidebar{height:100%;overflow-y:auto;scrollbar-gutter:stable;padding:34px 18px 48px var(--edge);scrollbar-width:thin;scrollbar-color:#b8c5d4 transparent}
        .sidebar::-webkit-scrollbar{width:8px}.sidebar::-webkit-scrollbar-track{background:transparent}.sidebar::-webkit-scrollbar-thumb{background:#c2cedb;border-radius:999px;border:2px solid var(--bg)}
        .side-kicker{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.22em;margin:0 0 22px}
        .nav-product{font-size:14px;font-weight:700;margin:24px 0 8px}.nav-product:first-of-type{margin-top:0}
        .nav-surface{display:block;color:#1f2937;font-size:13px;font-weight:700;margin:12px 0 6px 10px;border-radius:8px;padding:4px 9px}
        .nav-list{list-style:none;margin:0 0 0 22px;padding:0;display:grid;gap:3px}
        .nav-link{display:block;padding:5px 9px;border-radius:8px;color:var(--muted);font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .nav-link:hover,.nav-surface:hover{background:#f1f4f7;color:var(--text)}
        .nav-link.is-active,.nav-surface.is-active{color:#1f2937;background:var(--accent-soft);font-weight:600}
        .viewer{height:100%;overflow-y:auto;scrollbar-gutter:stable;padding:34px var(--edge) 64px 40px}
        .viewer:focus{outline:0}
        .panel{display:none;width:100%;max-width:1240px}.panel.is-active{display:block}
        .hero{background:transparent;padding:36px 0 44px;border-bottom:1px solid var(--line);margin-bottom:28px}
        .eyebrow{color:var(--subtle);font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.22em;margin-bottom:18px}
        h1{font-size:64px;line-height:.98;letter-spacing:0;margin:0 0 18px}
        .hero p,.panel-lead{max-width:900px;color:#294057;font-size:16px;line-height:1.65;margin:0}.panel-lead.markdown-content p:last-child,.panel-lead.markdown-content ul:last-child,.panel-lead.markdown-content ol:last-child{margin-bottom:0}
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
        .model-entity-card{border-color:var(--line-soft);box-shadow:none;margin-top:8px;border-radius:8px}.model-entity-card summary{list-style:none}.model-entity-card summary::-webkit-details-marker{display:none}.entity-summary{display:grid;grid-template-columns:24px minmax(0,1fr) auto;gap:10px;align-items:center;padding:9px 14px;cursor:pointer;border-bottom:1px solid transparent}.model-entity-card[open] .entity-summary{border-bottom-color:var(--line-soft)}.entity-toggle{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;color:#4b5563;font-family:var(--mono);font-size:12px;font-weight:600}.entity-toggle::before{content:"+"}.model-entity-card[open] .entity-toggle::before{content:"-"}.entity-main{min-width:0;display:grid;gap:1px}.entity-name{font-weight:700;color:var(--text);font-size:14px;line-height:1.25;overflow-wrap:anywhere}.entity-summary-text{color:var(--muted);font-size:12.5px;line-height:1.35;overflow-wrap:anywhere}.entity-counts{color:var(--subtle);font-size:10.5px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;white-space:nowrap}
        .entity-body{display:grid;grid-template-columns:24px minmax(0,1fr);column-gap:10px;padding:14px 14px 16px;overflow-x:auto}.entity-body-inner{grid-column:2;min-width:0}
        .cli-command-card{border-color:var(--line-soft);box-shadow:none;margin-top:8px;border-radius:8px}.cli-command-card summary{list-style:none}.cli-command-card summary::-webkit-details-marker{display:none}.command-summary{display:grid;grid-template-columns:24px minmax(0,1fr) auto;gap:10px;align-items:center;padding:9px 14px;cursor:pointer;border-bottom:1px solid transparent}.cli-command-card[open] .command-summary{border-bottom-color:var(--line-soft)}.command-toggle{width:24px;height:24px;display:inline-flex;align-items:center;justify-content:center;color:#4b5563;font-family:var(--mono);font-size:12px;font-weight:600}.command-toggle::before{content:"+"}.cli-command-card[open] .command-toggle::before{content:"-"}.command-main{min-width:0;display:grid;gap:1px}.command-name{font-family:var(--mono);font-weight:600;color:var(--text);font-size:13px;line-height:1.35;overflow-wrap:anywhere}.command-summary-text{color:var(--muted);font-size:12.5px;line-height:1.35;overflow-wrap:anywhere}.command-counts{color:var(--subtle);font-size:10.5px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;white-space:nowrap}
        .command-body{display:grid;grid-template-columns:24px minmax(0,1fr);column-gap:10px;padding:14px 14px 16px;overflow-x:auto}.command-body-inner{grid-column:2;min-width:0}
        .card-body{padding:14px 16px 16px;overflow-x:auto}.subsection{margin-top:18px}.subsection:first-child{margin-top:0}.subsection-title{margin:0 0 8px;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.08em}
        .description-block{max-width:900px;margin-top:36px}.panel-header+.description-block{margin-top:4px}.document-heading{margin:0 0 18px;padding-bottom:8px;border-bottom:1px solid var(--line);font-size:24px;line-height:1.3;font-weight:650;letter-spacing:0;text-transform:none}.markdown-content{color:#20252b;font-size:16px;line-height:1.7}.markdown-content p{margin:0 0 16px}.markdown-content ul,.markdown-content ol{margin:0 0 18px;padding-left:28px}.markdown-content li{padding-left:3px}.markdown-content li+li{margin-top:4px}.markdown-content h2,.markdown-content h3,.markdown-content h4{letter-spacing:0;line-height:1.3;margin:32px 0 16px}.markdown-content h2{padding-bottom:8px;border-bottom:1px solid var(--line);font-size:24px}.markdown-content h3{font-size:20px}.markdown-content h4{font-size:17px}.markdown-content strong{font-weight:700;color:#111827}.markdown-content a{color:#0f4c81;text-decoration:underline;text-underline-offset:2px}.markdown-content blockquote{margin:20px 0;padding:2px 18px;border-left:3px solid #b8c5d4;color:var(--muted)}.markdown-content hr{height:1px;border:0;background:var(--line);margin:28px 0}.markdown-content code,.panel-lead code,.entry-card code{padding:.15em .38em;border-radius:4px;background:#eef1f4;color:#1f2937;font-size:.9em}.markdown-content pre{margin:0 0 18px}.markdown-content pre code{padding:0;background:transparent;color:inherit;font-size:inherit}
        .example-block{border:1px solid var(--line-soft);border-radius:8px;background:#fff;padding:12px 14px;margin:0 0 10px}.example-block h5{font-size:14px;margin:0 0 6px}.example-block h6{font-size:12px;margin:10px 0 5px;color:#374151}.example-block p{color:var(--muted);font-size:14px;margin:0 0 8px}.code-title{font-size:12px;font-weight:700;color:#374151;margin:10px 0 5px}
        table{width:100%;border-collapse:collapse;font-size:12.5px;background:#fff;border:1px solid var(--line-soft);border-radius:8px;overflow:hidden}th,td{text-align:left;vertical-align:top;border-bottom:1px solid var(--line-soft);padding:8px 10px}th{color:var(--subtle);font-size:10.5px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;background:#fbfdff}td{color:#425064}tr:last-child td{border-bottom:0}
        .cmd,.opt{font-family:var(--mono);color:#1f2937;font-weight:600;white-space:nowrap}.entity-ref-link{font-weight:600;color:#0f355f;text-decoration:none}.entity-ref-link:hover{text-decoration:underline}
        pre{margin:0 0 10px;padding:12px 14px;background:var(--code-bg);border:1px solid var(--line);border-radius:8px;overflow-x:auto;font-size:13px}.note{color:var(--muted);font-size:14px;margin:0}
        details.inline-details{border-top:1px solid var(--line-soft);padding-top:14px;margin-top:18px}details.inline-details summary{cursor:pointer;color:var(--muted);font-size:13px;font-weight:600}
        @media(max-width:900px){:root{--edge:16px}.topbar-inner{width:calc(100vw - 32px)}.menu-button{display:inline-flex}.home-button{padding:0 14px}.app{height:calc(100vh - 60px);display:block;overflow:hidden}.sidebar{position:fixed;inset:60px 0 0 0;z-index:15;height:calc(100vh - 60px);padding:24px var(--edge) 48px;background:var(--bg);transform:translateX(-100%);transition:transform .18s ease;visibility:hidden}.sidebar.is-open{transform:translateX(0);visibility:visible}.viewer{height:100%;overflow-y:auto;scrollbar-gutter:stable;padding:24px var(--edge) 56px}.panel{max-width:none}.hero{padding:18px 0 30px;margin-bottom:22px}h1{font-size:42px}.panel-header{padding-bottom:22px;margin-bottom:22px}.panel-header h2{font-size:34px}.entry-grid{grid-template-columns:1fr}.entity-summary,.command-summary{grid-template-columns:24px minmax(0,1fr)}.entity-counts,.command-counts{grid-column:2;white-space:normal}.entity-body,.command-body{grid-template-columns:24px minmax(0,1fr)}}
        @media(max-width:640px){table:not(.summary-table){min-width:620px}.card-body,.entity-body,.command-body{overscroll-behavior-inline:contain}.summary-table{display:block;min-width:0;border:0}.summary-table thead{display:none}.summary-table tbody,.summary-table tr,.summary-table td{display:block}.summary-table tr{padding:10px;border-bottom:1px solid var(--line-soft)}.summary-table tr:last-child{border-bottom:0}.summary-table td{border:0;padding:2px 0}.summary-table td+td{color:var(--muted)}.summary-table .cmd{white-space:normal;overflow-wrap:anywhere}}
        """;
}
