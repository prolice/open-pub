using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using OpenPub.App.Controls;
using OpenPub.App.Rendering;
using OpenPub.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;
using PubParagraph = OpenPub.Core.Model.Paragraph;
using PubRun = OpenPub.Core.Model.Run;

namespace OpenPub.App;

/// <summary>
/// Edition du texte d'une histoire (toutes les zones liees) dans un editeur de texte enrichi.
/// Conversion aller-retour : paragraphes/segments OpenPub &lt;-&gt; FlowDocument WPF. Les reglages de
/// paragraphe non editables ici (espacements, interligne, retraits) sont conserves par position.
/// </summary>
public partial class TextEditorWindow : Window
{
    readonly EditorSession _session;
    readonly List<PubParagraph> _original;
    bool _updatingToolbar;
    public List<PubParagraph>? Result { get; private set; }
    const double Dip = PageView.DipPerPoint;

    public TextEditorWindow(EditorSession session, List<PubParagraph> paragraphs)
    {
        InitializeComponent();
        _session = session;
        _original = paragraphs;
        FontBox.ItemsSource = session.Fonts.AvailableFamilies.ToList();
        SizeBox.ItemsSource = new[] { 6, 7, 8, 9, 9.5, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 60, 72 };
        Editor.Document = ToFlow(paragraphs);
        Loaded += (_, _) => { Editor.Focus(); Editor.CaretPosition = Editor.Document.ContentStart; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F1) { Help.HelpService.Open(Help.HelpService.TextEditor); e.Handled = true; } };
    }

    // ------------------------------------------------------------ OpenPub -> WPF

    FlowDocument ToFlow(List<PubParagraph> paras)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI") };
        foreach (var p in paras)
        {
            var wp = new WpfParagraph { Margin = new Thickness(0, 0, 0, 4), TextAlignment = Align(p.Align) };
            foreach (var r in p.Runs)
            {
                var parts = r.Text.Replace("\r", "").Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) wp.Inlines.Add(new LineBreak());
                    if (parts[i].Length > 0) wp.Inlines.Add(MakeRun(parts[i], r.Font));
                }
            }
            if (wp.Inlines.Count == 0 && p.Runs.FirstOrDefault()?.Font is { } f)
            {
                wp.FontFamily = _session.Fonts.Family(f.Family);
                wp.FontSize = (f.Size ?? 10) * Dip;
            }
            doc.Blocks.Add(wp);
        }
        if (doc.Blocks.Count == 0) doc.Blocks.Add(new WpfParagraph());
        return doc;
    }

    WpfRun MakeRun(string text, FontSpec? f)
    {
        f ??= new FontSpec { Family = "Arial", Size = 10 };
        var run = new WpfRun(text)
        {
            FontFamily = _session.Fonts.Family(f.Family),
            FontSize = (f.Size ?? 10) * Dip,
            FontWeight = f.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = f.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = PageRenderer.Brush(f.Color),
        };
        if (f.Underline || f.Strike)
        {
            var deco = new TextDecorationCollection();
            if (f.Underline) deco.Add(TextDecorations.Underline);
            if (f.Strike) deco.Add(TextDecorations.Strikethrough);
            run.TextDecorations = deco;
        }
        if (f.Highlight is { } hl) run.Background = PageRenderer.Brush(hl);
        if (f.Script is "super") run.BaselineAlignment = BaselineAlignment.Superscript;
        else if (f.Script is "sub") run.BaselineAlignment = BaselineAlignment.Subscript;
        if (f.Caps == "small") Typography.SetCapitals(run, FontCapitals.SmallCaps);
        run.Tag = f;          // conserve l'espacement des caracteres (non affiche ici)
        return run;
    }

    static TextAlignment Align(string? a) => a switch
    {
        "center" => TextAlignment.Center,
        "right" or "end" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    static string Align(TextAlignment a) => a switch
    {
        TextAlignment.Center => "center",
        TextAlignment.Right => "right",
        TextAlignment.Justify => "justify",
        _ => "left",
    };

    // ------------------------------------------------------------ WPF -> OpenPub

    List<PubParagraph> FromFlow()
    {
        var result = new List<PubParagraph>();
        int i = 0;
        foreach (var block in EnumerateParagraphs(Editor.Document.Blocks))
        {
            var model = _original.Count == 0 ? null : _original[Math.Min(i, _original.Count - 1)];
            var p = new PubParagraph
            {
                Align = Align(block.TextAlignment),
                LineHeight = model?.LineHeight,
                SpaceBefore = model?.SpaceBefore,
                SpaceAfter = model?.SpaceAfter,
                IndentLeft = model?.IndentLeft,
                IndentRight = model?.IndentRight,
                FirstIndent = model?.FirstIndent,
                List = model?.List?.Clone(),
                Style = model?.Style,
                Tabs = model?.Tabs?.Select(t => t.Clone()).ToList(),
                DropCap = i < _original.Count ? model?.DropCap : null,
            };
            var lang = model?.Runs.FirstOrDefault()?.Font?.Lang ?? "fr";
            foreach (var (text, font) in Collect(block.Inlines))
            {
                font.Lang = lang;
                if (p.Runs.Count > 0 && p.Runs[^1].Font is { } last && last.SameAs(font))
                    p.Runs[^1].Text += text;
                else
                    p.Runs.Add(new PubRun { Text = text, Font = font });
            }
            if (p.Runs.Count == 0)
            {
                // paragraphe vide : garder sa taille (sert d'espacement vertical)
                var f = FontOf(block);
                f.Lang = lang;
                p.Runs.Add(new PubRun { Text = "", Font = f });
            }
            result.Add(p);
            i++;
        }
        return result;
    }

    static IEnumerable<WpfParagraph> EnumerateParagraphs(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            if (b is WpfParagraph p) yield return p;
            else if (b is Section s) foreach (var x in EnumerateParagraphs(s.Blocks)) yield return x;
            else if (b is List l) foreach (var li in l.ListItems) foreach (var x in EnumerateParagraphs(li.Blocks)) yield return x;
        }
    }

    IEnumerable<(string Text, FontSpec Font)> Collect(InlineCollection inlines)
    {
        foreach (var inl in inlines)
        {
            switch (inl)
            {
                case WpfRun r when r.Text.Length > 0:
                    yield return (r.Text, FontOf(r));
                    break;
                case LineBreak lb:
                    yield return ("\n", FontOf(lb));
                    break;
                case Span sp:
                    foreach (var x in Collect(sp.Inlines)) yield return x;
                    break;
            }
        }
    }

    static FontSpec FontOf(TextElement e)
    {
        bool underline = e is Inline inl && (inl.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false);
        if (!underline && e is Inline child && child.Parent is Inline parent)
            underline = parent.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false;
        var color = (e.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
        var inline = e as Inline;
        bool strike = inline?.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;
        var bg = (e.Background as SolidColorBrush)?.Color;
        var caps = Typography.GetCapitals(e);
        return new FontSpec
        {
            Strike = strike,
            Highlight = bg is { } h && h.A > 0 ? $"#{h.R:x2}{h.G:x2}{h.B:x2}" : null,
            Script = inline?.BaselineAlignment switch { BaselineAlignment.Superscript => "super", BaselineAlignment.Subscript => "sub", _ => null },
            Caps = caps == FontCapitals.SmallCaps ? "small" : (e as FrameworkContentElement)?.Tag is FontSpec { Caps: "all" } ? "all" : null,
            Tracking = (e as FrameworkContentElement)?.Tag is FontSpec orig ? orig.Tracking : null,
            Family = FontResolver.NameOf(e.FontFamily),
            Size = Math.Round(e.FontSize / Dip * 2, MidpointRounding.AwayFromZero) / 2,
            Bold = e.FontWeight >= FontWeights.SemiBold,
            Italic = e.FontStyle != FontStyles.Normal,
            Underline = underline,
            Color = $"#{color.R:x2}{color.G:x2}{color.B:x2}",
        };
    }

    // ------------------------------------------------------------ barre d'outils

    void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _updatingToolbar = true;
        var fam = Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty);
        FontBox.Text = fam is FontFamily ff ? FontResolver.NameOf(ff) : "";
        var size = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        SizeBox.Text = size is double d ? (Math.Round(d / Dip * 2) / 2).ToString(CultureInfo.CurrentCulture) : "";
        if (Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush b) ColorSwatch.Background = b;
        _updatingToolbar = false;
    }

    void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        CountText.Text = $"{text.Replace("\r\n", "").Length} caractères";
    }

    void ApplyFont(string name)
    {
        if (_updatingToolbar || string.IsNullOrWhiteSpace(name)) return;
        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, _session.Fonts.Family(name.Trim()));
        Editor.Focus();
    }
    void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (FontBox.SelectedItem is string s) ApplyFont(s); }
    void FontBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyFont(FontBox.Text); e.Handled = true; } }

    void ApplySize(string text)
    {
        if (_updatingToolbar) return;
        if (double.TryParse(text.Replace('.', ','), NumberStyles.Float, CultureInfo.GetCultureInfo("fr-FR"), out var pt) && pt >= 1 && pt <= 500)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, pt * Dip);
            Editor.Focus();
        }
    }
    void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (SizeBox.SelectedItem is { } v) ApplySize(Convert.ToString(v, CultureInfo.GetCultureInfo("fr-FR"))!); }
    void SizeBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplySize(SizeBox.Text); e.Handled = true; } }

    void Color_Click(object sender, RoutedEventArgs e)
    {
        // palette simple : couleurs du document + couleurs de base
        var colors = _session.Doc.AllShapes().Where(s => s.Paragraphs is not null).SelectMany(s => s.Paragraphs!).SelectMany(p => p.Runs)
            .Select(r => r.Font?.Color).Where(c => c is not null).Select(c => c!)
            .Concat(new[] { "#000000", "#ffffff", "#c00000", "#ff0000", "#990000", "#008000", "#1f6fd1", "#66ccff", "#7f7f7f" })
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToList();
        var menu = new ContextMenu();
        foreach (var hex in colors)
        {
            var item = new MenuItem
            {
                Header = hex,
                Icon = new Border { Width = 16, Height = 16, Background = PageRenderer.Brush(hex), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
            };
            item.Click += (_, _) =>
            {
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, PageRenderer.Brush(hex));
                ColorSwatch.Background = PageRenderer.Brush(hex);
                Editor.Focus();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = FromFlow();
        DialogResult = true;
    }
}
