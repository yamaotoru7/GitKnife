using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GitKnife.Core.Logging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Inline = System.Windows.Documents.Inline;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Markdown を読み取り専用で見せる（Update Available のリリースノート）。解析は Markdig、表示は FlowDocument。色はテーマに従う。
/// 見出し、段落、箇条書き（入れ子も）、引用、コード、区切り線、太字、斜体、インラインコード、リンクを扱う。
/// 画像は読み込まず代替テキストを出す。リンクは http と https だけ開く。HTML は解釈せずそのまま文字で出す。
/// </summary>
internal static class MarkdownView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAutoLinks().UseCjkFriendlyEmphasis().Build();

    public static FrameworkElement Create(string markdown, string automationId)
    {
        var document = new FlowDocument { FontSize = 12.5, PagePadding = new Thickness(8, 6, 8, 2), TextAlignment = TextAlignment.Left };
        document.SetResourceReference(FlowDocument.FontFamilyProperty, "Gk.UiFont");
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Gk.Text");
        foreach (var block in Markdown.Parse(markdown, Pipeline))
            if (Convert(block) is { } converted) document.Blocks.Add(converted);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = document,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(viewer, automationId);
        var border = new Border { Child = viewer, Height = 187, BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Gk.Input.Background");
        border.SetResourceReference(Border.BorderBrushProperty, "Gk.Input.Border");
        return border;
    }

    private static WpfBlock? Convert(MdBlock block) => block switch
    {
        HeadingBlock h => Heading(h),
        ParagraphBlock p => Paragraph(p.Inline),
        ListBlock l => BulletOrNumberList(l),
        QuoteBlock q => Quote(q),
        LeafBlock { Lines.Count: > 0 } code and (CodeBlock or HtmlBlock) => Code(code),
        ThematicBreakBlock => Rule(),
        _ => null,
    };

    private static Paragraph Heading(HeadingBlock h)
    {
        var p = Paragraph(h.Inline);
        p.FontWeight = FontWeights.SemiBold;
        p.FontSize = h.Level switch { 1 => 16, 2 => 14.5, _ => 13 };
        p.Margin = new Thickness(0, 4, 0, 6);
        return p;
    }

    private static Paragraph Paragraph(ContainerInline? inlines)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
        AddInlines(p.Inlines, inlines);
        return p;
    }

    private static List BulletOrNumberList(ListBlock l)
    {
        var list = new List { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(18, 0, 0, 0), MarkerStyle = l.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
        if (l.IsOrdered && int.TryParse(l.OrderedStart, out var start) && start > 0) list.StartIndex = start;
        foreach (var item in l.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
                if (Convert(child) is { } converted) listItem.Blocks.Add(converted);
            foreach (var p in listItem.Blocks.OfType<Paragraph>()) p.Margin = new Thickness(0, 0, 0, 3);
            list.ListItems.Add(listItem);
        }
        return list;
    }

    private static Section Quote(QuoteBlock q)
    {
        var section = new Section { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 0, 0, 0), BorderThickness = new Thickness(2, 0, 0, 0) };
        section.SetResourceReference(TextElement.ForegroundProperty, "Gk.Text.Dim");
        section.SetResourceReference(WpfBlock.BorderBrushProperty, "Gk.Input.Border");
        foreach (var child in q)
            if (Convert(child) is { } converted) section.Blocks.Add(converted);
        return section;
    }

    private static Paragraph Code(LeafBlock code)
    {
        var p = new Paragraph(new Run(code.Lines.ToString().TrimEnd())) { Margin = new Thickness(0, 0, 0, 6) };
        p.SetResourceReference(TextElement.FontFamilyProperty, "Gk.CodeFont");
        return p;
    }

    private static Paragraph Rule()
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 8), FontSize = 1, BorderThickness = new Thickness(0, 1, 0, 0) };
        p.SetResourceReference(WpfBlock.BorderBrushProperty, "Gk.Input.Border");
        return p;
    }

    private static void AddInlines(InlineCollection target, ContainerInline? inlines)
    {
        if (inlines is null) return;
        foreach (var inline in inlines)
            if (Convert(inline) is { } converted) target.Add(converted);
    }

    private static Inline? Convert(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline text: return new Run(text.Content.ToString());
            case HtmlEntityInline entity: return new Run(entity.Transcoded.ToString());
            case HtmlInline html: return new Run(html.Tag);
            case LineBreakInline lineBreak: return lineBreak.IsHard ? new LineBreak() : new Run(" ");
            case CodeInline code:
                var run = new Run(code.Content);
                run.SetResourceReference(TextElement.FontFamilyProperty, "Gk.CodeFont");
                return run;
            case AutolinkInline auto: return Link(auto.Url, new Run(auto.Url));
            case LinkInline link:
                var content = new Span();
                AddInlines(content.Inlines, link);
                return Link(link.Url, content);
            case EmphasisInline emphasis:
                var span = new Span();
                if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                else span.FontStyle = FontStyles.Italic;
                AddInlines(span.Inlines, emphasis);
                return span;
            case ContainerInline container:
                var group = new Span();
                AddInlines(group.Inlines, container);
                return group;
            default: return null;
        }
    }

    /// <summary>http と https はブラウザで開くリンクに、それ以外（相対パスや file: など）は文字のままにする。</summary>
    private static Inline Link(string? url, Inline content)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return content;
        var link = new Hyperlink(content) { ToolTip = uri.AbsoluteUri };
        link.SetResourceReference(TextElement.ForegroundProperty, "Gk.Link");
        link.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Warning($"Opening a link failed: {ex.Message}"); }
        };
        return link;
    }
}
