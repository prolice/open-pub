using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OpenPub.App.Controls;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Onglet Format ▸ Zone de texte, Liaison, Habillage : liens entre zones, colonnes, marges interieures,
// alignement vertical, ajustement automatique, habillage du texte autour des objets.
public partial class MainWindow
{
    // ---------------------------------------------------------------- liens entre zones

    void CreateLink_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { IsText: true } s || SelPage is not { } page) return;
        StartLink(page, s);
    }

    /// <summary>Commence le mode « créer un lien » depuis la derniere zone de la chaine de s.</summary>
    void StartLink(Page page, Shape s)
    {
        if (_session is null) return;
        _session.EndTextEdit();
        var last = s.Chain is null ? s : page.ChainFrames(s.Chain)[^1];
        _session.LinkSource = last;
        _session.Select(last, page);
        StatusText.Text = "Créer un lien : cliquez sur une zone de texte vide de cette page pour y faire couler la suite du texte (Échap pour annuler).";
    }

    void CancelLink()
    {
        if (_session is null || _session.LinkSource is null) return;
        _session.LinkSource = null;
        StatusText.Text = "";
    }

    void OnLinkTargetPicked(PageView view, Shape? target)
    {
        if (_session?.LinkSource is not { } source) return;
        _session.LinkSource = null;
        var page = view.Page;
        if (target is null) { StatusText.Text = "Lien annulé."; return; }
        if (!page.Shapes.Contains(source))
        {
            MessageBox.Show(this, "Les zones liées doivent être sur la même page.", "OpenPub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!target.IsText || target == source || (target.Chain is not null && target.Chain == source.Chain))
        {
            StatusText.Text = "Lien annulé : choisissez une autre zone de texte.";
            return;
        }
        if (target.Chain is not null || (target.Paragraphs?.Any(p => p.PlainText.Trim().Length > 0) ?? false))
        {
            MessageBox.Show(this, "La zone choisie doit être vide et ne pas être déjà liée : la suite du texte va y couler.",
                "OpenPub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _session.Change(page, () =>
        {
            var chain = source.Chain ?? "c" + Guid.NewGuid().ToString("N")[..8];
            if (source.Chain is null) { source.Chain = chain; source.ChainIndex = 0; }
            var frames = page.ChainFrames(chain);
            frames.Insert(frames.IndexOf(source) + 1, target);
            target.Chain = chain;
            target.Paragraphs = new List<Paragraph>();
            target.AutoFit = null;
            for (int i = 0; i < frames.Count; i++) frames[i].ChainIndex = i;
        });
        _session.Select(target, page);
        StatusText.Text = "Zones liées : le texte continue dans la zone choisie.";
    }

    void OnOverflowClicked(PageView view, Shape s)
    {
        _session?.Select(s, view.Page);
        StartLink(view.Page, s);
    }

    void BreakLink_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { IsText: true, Chain: not null } s || SelPage is not { } page) return;
        var frames = page.ChainFrames(s.Chain!);
        int i = frames.IndexOf(s);
        if (i == frames.Count - 1) { StatusText.Text = "Cette zone est la dernière de la chaîne : aucun lien à rompre après elle."; return; }
        _session.Change(page, () =>
        {
            // les zones suivantes redeviennent des zones vides independantes ; le texte reste dans les zones gardees
            foreach (var f in frames.Skip(i + 1)) { f.Chain = null; f.ChainIndex = null; f.FlowFrom = null; f.Paragraphs = new List<Paragraph>(); }
            if (i == 0) { s.Chain = null; s.ChainIndex = null; }
        });
        RefreshProperties();
        StatusText.Text = "Lien rompu : le texte qui ne tient plus est signalé par l'indicateur « A… ».";
    }

    void NextFrame_Click(object sender, RoutedEventArgs e) => StepFrame(+1);
    void PrevFrame_Click(object sender, RoutedEventArgs e) => StepFrame(-1);

    void StepFrame(int dir)
    {
        if (_session is null || Sel is not { IsText: true, Chain: not null } s || SelPage is not { } page) return;
        var frames = page.ChainFrames(s.Chain!);
        int i = frames.IndexOf(s) + dir;
        if (i >= 0 && i < frames.Count) _session.Select(frames[i], page);
    }

    // ---------------------------------------------------------------- zone de texte

    void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { IsText: true } s) return;
        var menu = NewMenu(sender);
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var cols = n;
            Item(menu, n == 1 ? "Une colonne" : $"{n} colonnes", () => ChangeSel(x => x.Columns = cols, x => x.IsText), s.Columns == n);
        }
        menu.Items.Add(new Separator());
        foreach (var (label, cm) in new[] { ("Espacement étroit (0,25 cm)", 0.25), ("Espacement normal (0,5 cm)", 0.5), ("Espacement large (1 cm)", 1.0) })
        {
            var gap = cm / CmPerPoint;
            Item(menu, label, () => ChangeSel(x => x.ColumnGap = gap, x => x.IsText),
                Math.Abs((s.ColumnGap > 0 ? s.ColumnGap : Layout.PageLayouter.DefaultColumnGap) - gap) < 0.5);
        }
        menu.IsOpen = true;
    }

    void MarginsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { IsText: true } s) return;
        var menu = NewMenu(sender);
        foreach (var (label, cm) in new[] { ("Aucune", 0.0), ("Étroites (0,1 cm)", 0.1), ("Normales (0,2 cm)", 0.2), ("Larges (0,5 cm)", 0.5), ("Très larges (1 cm)", 1.0) })
        {
            var pt = cm / CmPerPoint;
            Item(menu, label, () => ChangeSel(x => x.Padding = new[] { pt, pt, pt, pt }, x => x.IsText),
                s.Padding is { Length: 4 } p && p.All(v => Math.Abs(v - pt) < 0.3));
        }
        menu.IsOpen = true;
    }

    void VAlign_Click(object sender, RoutedEventArgs e)
    {
        var v = (string)((Button)sender).CommandParameter;
        ChangeSel(x => x.VAlign = v == "top" ? null : v, x => x.IsText);
    }

    void AutoFitButton_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { IsText: true } s || SelPage is not { } page || StoryHead(s) is not { } head) return;
        var menu = NewMenu(sender);
        void Set(string? v) => ChangeSel(_ => { head.AutoFit = v; }, x => x == s);
        Item(menu, "Réduire le texte en cas de débordement", () => Set("shrink"), head.ShrinksText);
        var grow = Item(menu, "Agrandir la zone pour contenir le texte", () => Set("grow"), head.AutoFit == "grow");
        grow.IsEnabled = head.Chain is null;
        Item(menu, "Ne pas ajuster (indicateur de débordement)", () => Set("none"), head.AutoFit == "none");
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- habillage

    void WrapButton_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { } s) return;
        var menu = NewMenu(sender);
        foreach (var (label, v) in new (string, string?)[]
                 { ("Automatique", null), ("Aucun (le texte passe dessous)", "none"), ("Carré", "square"), ("Serré (suit le contour)", "tight"), ("Haut et bas", "topbottom") })
        {
            var val = v;
            Item(menu, label, () => ChangeSel(x => x.Wrap = val), s.Wrap == v);
        }
        menu.Items.Add(new Separator());
        double cur = s.WrapDistance ?? Layout.PageLayouter.Gap;
        foreach (var cm in new[] { 0.1, 0.2, 0.3, 0.5, 1.0 })
        {
            var pt = cm / CmPerPoint;
            Item(menu, $"Distance du texte : {cm.ToString("0.0#", CultureInfo.CurrentCulture)} cm",
                () => ChangeSel(x => x.WrapDistance = Math.Abs(pt - Layout.PageLayouter.Gap) < 0.1 ? null : pt), Math.Abs(cur - pt) < 0.3);
        }
        menu.IsOpen = true;
    }
}
