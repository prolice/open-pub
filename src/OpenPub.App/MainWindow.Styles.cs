using System.Windows;
using System.Windows.Controls;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.App;

// Styles de texte (Accueil ▸ Styles) et jeux de couleurs et de polices (Creation de page ▸ Jeux).
public partial class MainWindow
{
    bool _updatingStyles;

    /// <summary>Remplit la liste des styles de paragraphe et selectionne celui du texte courant.</summary>
    void RefreshStyleBox()
    {
        if (StyleBox is null || _session is null) return;
        _updatingStyles = true;
        var styles = (_session.Doc.Styles ?? StyleOps.Defaults()).Where(s => s.Kind == "paragraph").ToList();
        if (StyleBox.Items.Count != styles.Count || !StyleBox.Items.Cast<ComboBoxItem>().Select(i => (string)i.Tag).SequenceEqual(styles.Select(s => s.Id)))
        {
            StyleBox.Items.Clear();
            foreach (var st in styles) StyleBox.Items.Add(new ComboBoxItem { Content = st.Name, Tag = st.Id });
        }
        var cur = CurrentParagraph()?.Style;
        StyleBox.SelectedItem = StyleBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur);
        _updatingStyles = false;
    }

    void StyleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingStyles || _session is null || StyleBox.SelectedItem is not ComboBoxItem it) return;
        var styles = StyleOps.Ensure(_session.Doc);
        if (styles.FirstOrDefault(s => s.Id == (string)it.Tag) is { } st) ApplyTextStyle(st);
    }

    /// <summary>Applique un style a la selection de texte (ou a tout le texte des zones selectionnees).</summary>
    void ApplyTextStyle(TextStyle st)
    {
        if (_session is null) return;
        if (_session.TextEdit is { } ed)
        {
            var (a, b) = st.Kind == "character" ? ed.FormatRange() : (ed.SelStart, ed.SelEnd);
            _session.Change(ed.Page, () => { StyleOps.Ensure(_session.Doc); StyleOps.Apply(ed.Paras, a, b, st); });
            RefocusPage();
        }
        else if (SelPage is { } page && SelectedStories() is { Count: > 0 } stories)
            _session.Change(page, () =>
            {
                StyleOps.Ensure(_session.Doc);
                foreach (var paras in stories) StyleOps.Apply(paras, new TextPos(0, 0), StoryEditor.End(paras), st);
            });
        else { StatusText.Text = "Sélectionnez une zone de texte, ou double-cliquez dans un texte."; return; }
        StatusText.Text = $"Style « {st.Name} » appliqué.";
        RefreshFormat();
    }

    void StylesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        var doc = _session.Doc;
        var styles = doc.Styles ?? StyleOps.Defaults();
        foreach (var cs in styles.Where(s => s.Kind == "character"))
        {
            var c = cs;
            Item(menu, $"Appliquer « {c.Name} » à la sélection", () => ApplyTextStyle(StyleOps.Ensure(doc).First(x => x.Id == c.Id)));
        }
        menu.Items.Add(new Separator());
        Item(menu, "Nouveau style d'après le texte…", NewStyleFromText);
        var current = StyleOps.Find(doc, CurrentParagraph()?.Style);
        Item(menu, current is null ? "Modifier un style…" : $"Modifier le style « {current.Name} »…", () => EditStyle(current));
        var del = new MenuItem { Header = "Supprimer un style" };
        foreach (var st in styles)
        {
            var s = st;
            var it = new MenuItem { Header = s.Name };
            it.Click += (_, _) =>
            {
                _session.BeginChange();
                StyleOps.Ensure(doc);
                StyleOps.Delete(doc, s.Id);
                _session.EndChange();
                RefreshStyleBox();
                StatusText.Text = $"Style « {s.Name} » supprimé (le texte garde sa mise en forme).";
            };
            del.Items.Add(it);
        }
        menu.Items.Add(del);
        menu.IsOpen = true;
    }

    void NewStyleFromText()
    {
        if (_session is null || CurrentParagraph() is not { } p || CurrentFont() is not { } f)
        {
            StatusText.Text = "Placez le curseur dans le texte modèle (ou sélectionnez sa zone).";
            return;
        }
        var probe = new TextStyle { Name = "Mon style", Kind = "paragraph", Font = new FontSpec { Family = f.Family, Size = f.Size, Bold = f.Bold, Italic = f.Italic, Underline = f.Underline, Color = f.Color },
            Align = p.Align, LineHeight = p.LineHeight, SpaceBefore = p.SpaceBefore, SpaceAfter = p.SpaceAfter };
        var dlg = new StyleWindow(probe, true, _session.Fonts.AvailableFamilies, c => PickColor(c, false, "Couleur du style") is (true, { } col) ? col : null) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var r = dlg.Result;
        _session.BeginChange();
        var st = StyleOps.FromText(_session.Doc, r.Name, r.Kind, new Paragraph { Align = r.Align, LineHeight = r.LineHeight, SpaceBefore = r.SpaceBefore, SpaceAfter = r.SpaceAfter, IndentLeft = p.IndentLeft, FirstIndent = p.FirstIndent }, r.Font!);
        _session.EndChange();
        ApplyTextStyle(st);
        RefreshStyleBox();
    }

    void EditStyle(TextStyle? st)
    {
        if (_session is null) return;
        var doc = _session.Doc;
        if (st is null)
        {
            // choisir le style a modifier
            var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            LastMenu = menu;
            foreach (var s in doc.Styles ?? StyleOps.Defaults())
            {
                var id = s.Id;
                Item(menu, s.Name, () => EditStyle(StyleOps.Ensure(doc).First(x => x.Id == id)));
            }
            menu.IsOpen = true;
            return;
        }
        _session.EndTextEdit();
        _session.BeginChange();
        StyleOps.Ensure(doc);
        var target = doc.Styles!.First(x => x.Id == st.Id);
        var dlg = new StyleWindow(target, false, _session.Fonts.AvailableFamilies, c => PickColor(c, false, "Couleur du style") is (true, { } col) ? col : null) { Owner = this };
        bool ok = dlg.ShowDialog() == true;
        int n = ok ? StyleOps.Reapply(doc, target) : 0;
        _session.EndChange();                 // annule : rien n'a change, l'entree d'historique disparait
        if (!ok) return;
        _session.StructureChanged();
        RefreshStyleBox();
        StatusText.Text = $"Style « {target.Name} » modifié : {n} élément(s) mis à jour.";
    }

    // ---------------------------------------------------------------- jeux de couleurs et de polices

    void ColorSchemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        var cur = _session.Doc.Theme?.Name ?? "OpenPub";
        foreach (var (name, colors) in StyleOps.ColorSchemes)
        {
            var n = name; var cs = colors;
            var sw = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in cs) sw.Children.Add(new Border { Width = 10, Height = 12, Background = Rendering.PageRenderer.Brush(c), Margin = new Thickness(0, 0, 1, 0) });
            Item(menu, name, () => ApplyColorScheme(n, cs), n == cur, sw);
        }
        menu.Items.Add(new Separator());
        Item(menu, "Personnaliser le jeu de couleurs…", CustomColorScheme);
        menu.IsOpen = true;
    }

    void ApplyColorScheme(string name, IReadOnlyList<string> colors)
    {
        if (_session is null) return;
        _session.EndTextEdit();
        int n = 0;
        _session.BeginChange();
        n = StyleOps.ApplyColorScheme(_session.Doc, name, colors);
        _session.EndChange();
        _session.StructureChanged();
        StatusText.Text = $"Jeu de couleurs « {name} » : {n} couleur(s) du document remplacée(s).";
    }

    void CustomColorScheme()
    {
        if (_session is null) return;
        var cur = (_session.Doc.Theme?.Colors is { Count: 5 } c ? c : StyleOps.ColorSchemes[0].Colors.ToList()).ToList();
        var labels = new[] { "Couleur principale (textes, titres)", "Accent 1", "Accent 2", "Accent 3", "Accent 4" };
        for (int i = 0; i < 5; i++)
        {
            var (ok, col) = PickColor(cur[i], false, labels[i]);
            if (!ok) return;
            if (col is not null) cur[i] = col;
        }
        ApplyColorScheme("Personnalisé", cur);
    }

    void FontSchemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        var cur = (_session.Doc.Theme?.HeadingFont ?? "Georgia", _session.Doc.Theme?.BodyFont ?? "Arial");
        foreach (var (name, heading, body) in StyleOps.FontSchemes)
        {
            var h = heading; var b = body;
            var it = Item(menu, name, () =>
            {
                _session.EndTextEdit();
                _session.BeginChange();
                int n = StyleOps.ApplyFontScheme(_session.Doc, h, b);
                _session.EndChange();
                _session.StructureChanged();
                StatusText.Text = $"Jeu de polices « {h} / {b} » : {n} segment(s) de texte mis à jour.";
            }, cur == (heading, body));
            it.FontFamily = new System.Windows.Media.FontFamily(heading);
        }
        menu.IsOpen = true;
    }
}
