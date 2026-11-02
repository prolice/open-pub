using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.App;

// Onglet Accueil ▸ Police et Paragraphe : styles de caractere (barre, exposant, majuscules, surlignage,
// espacement), listes, retraits, interligne, boite Paragraphe, reproduction de la mise en forme.
public partial class MainWindow
{
    // ---------------------------------------------------------------- caracteres

    void Strike_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Strike, (f, v) => f.Strike = v);
    void Superscript_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Script == "super", (f, v) => f.Script = v ? "super" : null);
    void Subscript_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Script == "sub", (f, v) => f.Script = v ? "sub" : null);

    ContextMenu NewMenu(object sender)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        LastMenu = menu;
        return menu;
    }

    static MenuItem Item(ContextMenu menu, string header, Action act, bool check = false, object? icon = null)
    {
        var it = new MenuItem { Header = header, IsCheckable = false, Icon = icon };
        if (check) it.FontWeight = FontWeights.Bold;
        it.Click += (_, _) => act();
        menu.Items.Add(it);
        return it;
    }

    void CapsButton_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentFont()?.Caps;
        var menu = NewMenu(sender);
        Item(menu, "Normal", () => ApplyFont(f => f.Caps = null), cur is null);
        Item(menu, "TOUT EN MAJUSCULES", () => ApplyFont(f => f.Caps = "all"), cur == "all");
        Item(menu, "Petites majuscules", () => ApplyFont(f => f.Caps = "small"), cur == "small");
        menu.IsOpen = true;
    }

    void Highlight_Click(object sender, RoutedEventArgs e)
    {
        var (ok, c) = PickColor(CurrentFont()?.Highlight ?? "#fff200", true, "Couleur de surlignage");
        if (ok) ApplyFont(f => f.Highlight = c);
    }

    void TrackingButton_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentFont()?.Tracking ?? 0;
        var menu = NewMenu(sender);
        foreach (var (label, v) in new[] { ("Très serré", -100.0), ("Serré", -50.0), ("Normal", 0.0), ("Large", 50.0), ("Très large", 100.0) })
        {
            var val = v;
            Item(menu, label, () => ApplyFont(f => f.Tracking = val == 0 ? null : val), Math.Abs(cur - v) < 0.5);
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- paragraphes

    /// <summary>Paragraphe de reference : sous le curseur pendant l'edition, sinon le premier de la zone.</summary>
    Paragraph? CurrentParagraph()
    {
        if (_session?.TextEdit is { } ed) return ed.CurrentParagraph();
        return Sel is { IsText: true } s && StoryHead(s)?.Paragraphs is { Count: > 0 } paras ? paras[0] : null;
    }

    static readonly string[] BulletSymbols = { "•", "◦", "▪", "–", "✓", "➢", "★" };
    static readonly (string Format, string Label)[] NumberFormats =
        { ("1.", "1. 2. 3."), ("1)", "1) 2) 3)"), ("a.", "a. b. c."), ("a)", "a) b) c)"), ("A.", "A. B. C."), ("i.", "i. ii. iii."), ("I.", "I. II. III.") };

    void SetList(ListStyle? style)
    {
        ApplyParagraph(p =>
        {
            if (style is null)
            {
                if (p.List is null) return;
                p.List = null; p.IndentLeft = null; p.FirstIndent = null;
                return;
            }
            int lv = p.List?.Level ?? 0;
            p.List = style.Clone();
            p.List.Level = lv;
            // retrait suspendu : la puce dans la marge, le texte aligne
            if (!((p.IndentLeft ?? 0) > 0 && (p.FirstIndent ?? 0) < 0)) { p.IndentLeft = 18 * (lv + 1); p.FirstIndent = -18; }
        });
    }

    void BulletsButton_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentParagraph()?.List;
        var menu = NewMenu(sender);
        Item(menu, "Aucune puce", () => SetList(null), cur is null);
        menu.Items.Add(new Separator());
        foreach (var sym in BulletSymbols)
        {
            var sy = sym;
            Item(menu, $"{sy}   Puce « {sy} »", () => SetList(new ListStyle { Kind = "bullet", Symbol = sy }), cur is { Kind: "bullet" } && cur.Symbol == sy);
        }
        menu.IsOpen = true;
    }

    void NumberingButton_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentParagraph()?.List;
        var menu = NewMenu(sender);
        Item(menu, "Aucune numérotation", () => SetList(null), cur is null);
        menu.Items.Add(new Separator());
        foreach (var (fmt, label) in NumberFormats)
        {
            var f = fmt;
            Item(menu, label, () => SetList(new ListStyle { Kind = "number", Format = f, Symbol = null }), cur is { Kind: "number" } && cur.Format == f);
        }
        menu.Items.Add(new Separator());
        Item(menu, "Recommencer la numérotation à 1", () => ApplyParagraph(p => { if (p.List is { Kind: "number" } l) l.Start = 1; }));
        Item(menu, "Continuer la numérotation", () => ApplyParagraph(p => { if (p.List is { Kind: "number" } l) l.Start = null; }));
        menu.IsOpen = true;
    }

    void IndentMore_Click(object sender, RoutedEventArgs e) => ShiftIndent(+1);
    void IndentLess_Click(object sender, RoutedEventArgs e) => ShiftIndent(-1);

    void ShiftIndent(int dir)
    {
        if (CurrentParagraph() is { List: not null } && _session?.TextEdit is { } ed) { ed.ShiftListLevel(dir); RefocusPage(); return; }
        ApplyParagraph(p =>
        {
            if (p.List is { } l)
            {
                l.Level = Math.Clamp(l.Level + dir, 0, 8);
                p.IndentLeft = 18 * (l.Level + 1); p.FirstIndent = -18;
                return;
            }
            // pas de 1,27 cm, comme les tabulations par defaut
            var v = Math.Max(0, (p.IndentLeft ?? 0) + dir * 36);
            p.IndentLeft = v < 0.01 ? null : v;
        });
    }

    void LineSpacingButton_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentParagraph()?.LineSpacingFactor() ?? 1;
        var menu = NewMenu(sender);
        foreach (var v in new[] { 1.0, 1.15, 1.5, 2.0, 2.5, 3.0 })
        {
            var val = v;
            Item(menu, val.ToString("0.0#", CultureInfo.CurrentCulture), () =>
                ApplyParagraph(p => p.LineHeight = Math.Abs(val - 1) < 0.001 ? null : (val * 100).ToString("0.####", CultureInfo.InvariantCulture) + "%"),
                Math.Abs(cur - v) < 0.001);
        }
        menu.Items.Add(new Separator());
        Item(menu, "Ajouter un espace avant le paragraphe", () => ApplyParagraph(p => p.SpaceBefore = (p.SpaceBefore ?? 0) + 6));
        Item(menu, "Ajouter un espace après le paragraphe", () => ApplyParagraph(p => p.SpaceAfter = (p.SpaceAfter ?? 0) + 6));
        Item(menu, "Supprimer les espaces avant et après", () => ApplyParagraph(p => { p.SpaceBefore = null; p.SpaceAfter = null; }));
        menu.Items.Add(new Separator());
        Item(menu, "Options de paragraphe…", () => ParagraphDialog_Click(sender, e));
        menu.IsOpen = true;
    }

    void ParagraphDialog_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentParagraph() is not { } model) return;
        var dlg = new ParagraphWindow(model) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } apply) ApplyParagraph(apply);
        RefocusPage();
    }

    void DropCap_Click(object sender, RoutedEventArgs e)
    {
        var cur = CurrentParagraph()?.DropCap ?? 0;
        var menu = NewMenu(sender);
        Item(menu, "Aucune lettrine", () => ApplyParagraph(p => p.DropCap = null), cur < 2);
        foreach (var n in new[] { 2, 3, 4, 5 })
        {
            var lines = n;
            Item(menu, $"Lettrine sur {n} lignes", () => ApplyParagraph(p => p.DropCap = lines), cur == n);
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- reproduire la mise en forme

    FontSpec? _painterFont;
    Paragraph? _painterPara;

    void FormatPainter_Click(object sender, RoutedEventArgs e)
    {
        if (_painterFont is not null) { EndPainter(); return; }
        if (CurrentFont() is not { } f) { StatusText.Text = "Placez d'abord le curseur dans le texte dont il faut copier la mise en forme."; return; }
        _painterFont = f.Clone();
        _painterPara = CurrentParagraph() is { } p ? ShapeOps.Clone(p) : null;
        if (FormatPainterButton is not null) FormatPainterButton.IsChecked = true;
        StatusText.Text = "Reproduire la mise en forme : sélectionnez le texte (ou cliquez une zone de texte) à mettre en forme. Échap pour annuler.";
    }

    void EndPainter()
    {
        _painterFont = null; _painterPara = null;
        if (FormatPainterButton is not null) FormatPainterButton.IsChecked = false;
    }

    /// <summary>Applique la mise en forme memorisee (appele apres une selection de texte ou un clic sur une zone).</summary>
    void ApplyPainter()
    {
        if (_painterFont is not { } f || _session is null) return;
        var para = _painterPara;
        EndPainter();
        ApplyFont(x =>
        {
            x.Family = f.Family; x.Size = f.Size; x.Bold = f.Bold; x.Italic = f.Italic; x.Underline = f.Underline; x.Strike = f.Strike;
            x.Color = f.Color; x.Script = f.Script; x.Caps = f.Caps; x.Highlight = f.Highlight; x.Tracking = f.Tracking;
        });
        if (para is not null)
            ApplyParagraph(p =>
            {
                p.Align = para.Align; p.LineHeight = para.LineHeight; p.SpaceBefore = para.SpaceBefore; p.SpaceAfter = para.SpaceAfter;
                p.IndentLeft = para.IndentLeft; p.IndentRight = para.IndentRight; p.FirstIndent = para.FirstIndent;
                p.List = para.List?.Clone(); p.Tabs = para.Tabs?.Select(t => t.Clone()).ToList();
            });
        StatusText.Text = "Mise en forme reproduite.";
    }
}
