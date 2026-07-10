using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MetaDocs.Core;

internal static class MetaDocsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown) =>
        Markdown.ToHtml(markdown, Pipeline);

    public static string ToPlainText(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var builder = new StringBuilder();
        AppendBlocks(builder, document);
        return builder.ToString().Trim();
    }

    private static void AppendBlocks(StringBuilder builder, ContainerBlock blocks)
    {
        foreach (var block in blocks)
        {
            AppendBlock(builder, block);
        }
    }

    private static void AppendBlock(StringBuilder builder, Block block)
    {
        switch (block)
        {
            case ListBlock list:
                AppendList(builder, list);
                break;
            case CodeBlock code:
                AppendBlockBreak(builder);
                builder.AppendLine(code.Lines.ToString().TrimEnd());
                AppendBlockBreak(builder);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                AppendBlockBreak(builder);
                AppendInlines(builder, leaf.Inline);
                AppendBlockBreak(builder);
                break;
            case ThematicBreakBlock:
                AppendBlockBreak(builder);
                builder.AppendLine("---");
                AppendBlockBreak(builder);
                break;
            case ContainerBlock container:
                AppendBlocks(builder, container);
                break;
        }
    }

    private static void AppendList(StringBuilder builder, ListBlock list)
    {
        AppendBlockBreak(builder);
        var number = int.TryParse(list.OrderedStart, out var orderedStart) ? orderedStart : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var itemBuilder = new StringBuilder();
            AppendBlocks(itemBuilder, item);
            var lines = itemBuilder.ToString().Trim().Split('\n');
            var marker = list.IsOrdered ? $"{number++}. " : "- ";
            builder.Append(marker).AppendLine(lines[0].TrimEnd());
            foreach (var line in lines.Skip(1))
            {
                builder.Append("  ").AppendLine(line.TrimEnd());
            }
        }

        AppendBlockBreak(builder);
    }

    private static void AppendInlines(StringBuilder builder, ContainerInline container)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded);
                    break;
                case LinkInline link:
                    var start = builder.Length;
                    AppendInlines(builder, link);
                    if (builder.Length == start)
                    {
                        builder.Append(link.Url);
                    }
                    else if (!string.IsNullOrWhiteSpace(link.Url) &&
                             !string.Equals(builder.ToString(start, builder.Length - start), link.Url, StringComparison.Ordinal))
                    {
                        builder.Append(" (").Append(link.Url).Append(')');
                    }
                    break;
                case ContainerInline nested:
                    AppendInlines(builder, nested);
                    break;
            }
        }
    }

    private static void AppendBlockBreak(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        if (builder[^1] != '\n')
        {
            builder.AppendLine();
        }

        if (builder.Length < 2 || builder[^2] != '\n')
        {
            builder.AppendLine();
        }
    }
}
