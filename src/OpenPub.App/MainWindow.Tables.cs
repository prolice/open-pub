using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.App.Controls;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using OpenPub.Layout;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Tableaux : insertion (Insertion ▸ Tableau), onglet contextuel « Outils de tableau » (lignes, colonnes, fusion,
// bordures, remplissage, styles), collage d'un tableau copie depuis Excel ou Word.
public partial class MainWindow
{
    Shape? SelTable => Sel is { IsTable: true } t ? t : null;

    /// <summary>Cellule en cours d'ecriture dans le tableau selectionne (ligne, colonne), ou null.</summary>
    (int Row, int Col)? CurrentCell() =>
        _session?.TextEdit is { Owner: { } owner } ed && owner == SelTable && PageLayouter.CellOf(ed.Head) is { } info ? (info.Row, info.Col) : null;

    void TableButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu(sender);
        foreach (var (r, c) in new[] { (2, 2), (3, 3), (4, 3), (5, 4), (6, 4), (8, 5) })
        {
            int rows = r, cols = c;
            Item(menu, $"Tableau de {rows} lignes × {cols} colonnes", () => InsertTable(rows, cols));
        }
        menu.Items.Add(new Separator());
        Item(menu, "Calendrier du mois…", () => InsertCalendar());
        Item(menu, "Tableau personnalisé…", () => CustomTable());
        menu.IsOpen = true;
    }

    void InsertTable(int rows, int cols, string? style = "bleu")
    {
        if (_session is null || ActivePage() is not { } page) return;
        var m = Snapper.Margins(_session.Doc);
        double w = Math.Min(page.Width - m[1] - m[3], 90 * cols), h = 22 * rows;
        var s = TableOps.Create(rows, cols, (page.Width - w) / 2, Math.Max(m[0], page.Height / 2 - h / 2), w, h);
        if (style is not null) TableOps.ApplyStyle(s.Table!, style);
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.BeginCellEdit(page, s, 0, 0);
        _views.FirstOrDefault(v => v.Page == page)?.Focus();
        StatusText.Text = "Tableau inséré : écrivez dans la cellule, Tab passe à la suivante (une ligne s'ajoute au bout du tableau).";
    }

    void CustomTable()
    {
        var dlg = new Window
        {
            Title = "Insérer un tableau", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("UiFont"), Background = (Brush)FindResource("Chrome"), Foreground = (Brush)FindResource("Ink"),
        };
        var rows = new TextBox { Width = 60, Text = "4" }; var cols = new TextBox { Width = 60, Text = "3" };
        var style = new ComboBox { Width = 240 };
        foreach (var st in TableOps.Styles) style.Items.Add(new ComboBoxItem { Content = st.Label, Tag = st.Id });
        style.SelectedIndex = 1;
        var root = new StackPanel { Margin = new Thickness(18) };
        StackPanel Row(string l, UIElement c) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) }; sp.Children.Add(new TextBlock { Text = l, Width = 110, VerticalAlignment = VerticalAlignment.Center }); sp.Children.Add(c); return sp; }
        root.Children.Add(Row("Lignes", rows)); root.Children.Add(Row("Colonnes", cols)); root.Children.Add(Row("Style", style));
        var ok = new Button { Content = "Insérer", IsDefault = true, MinWidth = 90, Style = (Style)FindResource("PillButton"), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dlg.DialogResult = true;
        root.Children.Add(ok);
        dlg.Content = root;
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(rows.Text, out var r) || !int.TryParse(cols.Text, out var c) || r < 1 || c < 1 || r > 200 || c > 50)
        { StatusText.Text = "Tableau : 1 à 200 lignes, 1 à 50 colonnes."; return; }
        InsertTable(r, c, (string)((ComboBoxItem)style.SelectedItem).Tag);
    }

    /// <summary>Calendrier du mois (tableau 7 colonnes, en-tete des jours, cases numerotees).</summary>
    void InsertCalendar()
    {
        if (_session is null || ActivePage() is not { } page) return;
        var today = DateTime.Today;
        var first = new DateTime(today.Year, today.Month, 1);
        int offset = ((int)first.DayOfWeek + 6) % 7;            // lundi = 0
        int days = DateTime.DaysInMonth(today.Year, today.Month);
        int weeks = (offset + days + 6) / 7;
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var m = Snapper.Margins(_session.Doc);
        double w = page.Width - m[1] - m[3];
        var s = TableOps.Create(weeks + 2, 7, m[3], m[0] + 40, w, 26 * (weeks + 2));
        var t = s.Table!;
        TableOps.Merge(t, 0, 0, 0, 6);
        var title = fr.TextInfo.ToTitleCase(first.ToString("MMMM yyyy", fr));
        void Set(int r, int c, string text, bool bold = false, string align = "center")
        {
            var f = TableOps.DefaultFont.Clone(); f.Bold = bold; if (r == 0) f.Size = 16;
            t.Cells[r][c].Paragraphs = new List<Paragraph> { new() { Align = align, Runs = { new Run { Text = text, Font = f } } } };
        }
        Set(0, 0, title, true);
        var names = new[] { "lun.", "mar.", "mer.", "jeu.", "ven.", "sam.", "dim." };
        for (int c = 0; c < 7; c++) Set(1, c, names[c], true);
        for (int d = 1; d <= days; d++) { int idx = offset + d - 1; Set(2 + idx / 7, idx % 7, d.ToString(fr), false, "right"); }
        for (int r = 2; r < t.RowCount; r++) t.Rows[r] = 46;
        TableOps.ApplyStyle(t, "bleu");
        for (int c = 0; c < 7; c++) t.Cells[1][c].Fill = new Fill { Color = "#dbeaff" };
        TableOps.SyncSize(s);
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
        StatusText.Text = $"Calendrier de {title} inséré.";
    }

    // ---------------------------------------------------------------- outils de tableau

    /// <summary>Modifie le tableau selectionne (annulable) ; l'ecriture eventuelle reprend dans la cellule indiquee.</summary>
    void ChangeTable(Action<Shape, TableData> change, (int Row, int Col)? resumeAt = null)
    {
        if (_session is null || SelTable is not { } s || SelPage is not { } page) return;
        _session.EndTextEdit();
        _session.Change(page, () => { change(s, s.Table!); TableOps.SyncSize(s); });
        if (resumeAt is { } rc && rc.Row < s.Table!.RowCount && rc.Col < s.Table.ColCount) _session.BeginCellEdit(page, s, rc.Row, rc.Col);
        else _session.Select(s, page);
        RefreshProperties();
    }

    bool RequireCell(out (int Row, int Col) cell)
    {
        if (CurrentCell() is { } c) { cell = c; return true; }
        cell = (0, 0);
        StatusText.Text = "Double-cliquez d'abord dans une cellule du tableau.";
        return false;
    }

    void TableRowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelTable is null) return;
        var menu = NewMenu(sender);
        var cur = CurrentCell();
        Item(menu, "Insérer une ligne au-dessus", () => { if (RequireCell(out var c)) ChangeTable((s, _) => TableOps.InsertRow(s, c.Row), (c.Row + 1, c.Col)); });
        Item(menu, "Insérer une ligne au-dessous", () => { if (RequireCell(out var c)) ChangeTable((s, t) => TableOps.InsertRow(s, c.Row + t.Cells[c.Row][c.Col].RowSpan), c); });
        Item(menu, "Insérer une colonne à gauche", () => { if (RequireCell(out var c)) ChangeTable((s, _) => TableOps.InsertCol(s, c.Col), (c.Row, c.Col + 1)); });
        Item(menu, "Insérer une colonne à droite", () => { if (RequireCell(out var c)) ChangeTable((s, t) => TableOps.InsertCol(s, c.Col + t.Cells[c.Row][c.Col].ColSpan), c); });
        menu.Items.Add(new Separator());
        Item(menu, "Supprimer la ligne", () => { if (RequireCell(out var c)) ChangeTable((s, _) => TableOps.DeleteRow(s, c.Row)); });
        Item(menu, "Supprimer la colonne", () => { if (RequireCell(out var c)) ChangeTable((s, _) => TableOps.DeleteCol(s, c.Col)); });
        menu.Items.Add(new Separator());
        Item(menu, "Colonnes de même largeur", () => ChangeTable((_, t) => { double w = t.Cols.Sum() / t.ColCount; for (int i = 0; i < t.ColCount; i++) t.Cols[i] = w; }, cur));
        Item(menu, "Lignes de même hauteur", () => ChangeTable((_, t) => { double h = TableOps.RowHeights(t).Average(); for (int i = 0; i < t.Rows.Count; i++) t.Rows[i] = h; t.ActualRows = null; }, cur));
        menu.IsOpen = true;
    }

    void TableMergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelTable is null) return;
        var menu = NewMenu(sender);
        Item(menu, "Fusionner avec la cellule de droite", () =>
        {
            if (!RequireCell(out var c)) return;
            ChangeTable((_, t) =>
            {
                var cell = t.Cells[c.Row][c.Col];
                int c1 = c.Col + cell.ColSpan;
                if (c1 < t.ColCount) TableOps.Merge(t, c.Row, c.Col, c.Row + cell.RowSpan - 1, c1 + t.Cells[c.Row][c1].ColSpan - 1);
            }, c);
        });
        Item(menu, "Fusionner avec la cellule du dessous", () =>
        {
            if (!RequireCell(out var c)) return;
            ChangeTable((_, t) =>
            {
                var cell = t.Cells[c.Row][c.Col];
                int r1 = c.Row + cell.RowSpan;
                if (r1 < t.RowCount) TableOps.Merge(t, c.Row, c.Col, r1 + t.Cells[r1][c.Col].RowSpan - 1, c.Col + cell.ColSpan - 1);
            }, c);
        });
        Item(menu, "Fusionner toute la ligne", () => { if (RequireCell(out var c)) ChangeTable((_, t) => TableOps.Merge(t, c.Row, 0, c.Row, t.ColCount - 1), (c.Row, 0)); });
        menu.Items.Add(new Separator());
        Item(menu, "Fractionner la cellule", () => { if (RequireCell(out var c)) ChangeTable((_, t) => TableOps.Split(t, c.Row, c.Col), c); });
        menu.IsOpen = true;
    }

    void TableBordersButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelTable is not { } s) return;
        var menu = NewMenu(sender);
        var cur = CurrentCell();
        var color = s.Table!.Border?.Color ?? "#9aa3b5";
        foreach (var (label, which) in new[] { ("Toutes les bordures", "all"), ("Bordures extérieures", "outside"), ("Bordures intérieures", "inside"), ("Lignes horizontales", "horizontal"), ("Aucune bordure", "none") })
        {
            var w = which;
            Item(menu, label, () => ChangeTable((_, t) => TableOps.SetBorders(t, w, w == "none" ? null : new LineStyle { Color = color, Weight = t.Border?.Weight ?? 0.75 }), cur));
        }
        menu.Items.Add(new Separator());
        Item(menu, "Couleur des bordures…", () =>
        {
            var (ok, c) = PickColor(color, false, "Couleur des bordures");
            if (ok && c is not null) ChangeTable((_, t) => TableOps.SetBorders(t, "all", new LineStyle { Color = c, Weight = t.Border?.Weight ?? 0.75 }), cur);
        });
        foreach (var wt in new[] { 0.5, 0.75, 1.5, 2.25 })
        {
            var weight = wt;
            Item(menu, $"Épaisseur {weight.ToString("0.##", CultureInfo.CurrentCulture)} pt", () => ChangeTable((_, t) => TableOps.SetBorders(t, "all", new LineStyle { Color = t.Border?.Color ?? color, Weight = weight }), cur));
        }
        menu.IsOpen = true;
    }

    void TableFill_Click(object sender, RoutedEventArgs e)
    {
        if (SelTable is not { } s) return;
        var cur = CurrentCell();
        var (ok, c) = PickColor(cur is { } rc ? s.Table!.Cells[rc.Row][rc.Col].Fill?.Color : null, true, cur is null ? "Remplissage de toutes les cellules" : "Remplissage de la cellule");
        if (!ok) return;
        ChangeTable((_, t) =>
        {
            var fill = c is null ? null : new Fill { Color = c };
            if (cur is { } one) t.Cells[one.Row][one.Col].Fill = fill;
            else foreach (var cell in t.Cells.SelectMany(r => r)) cell.Fill = fill is null ? null : new Fill { Color = c };
        }, cur);
    }

    void TableStyleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelTable is not { } s) return;
        var menu = NewMenu(sender);
        var cur = CurrentCell();
        foreach (var st in TableOps.Styles)
        {
            var id = st.Id;
            var sw = new Border { Width = 18, Height = 12, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = Rendering.PageRenderer.Brush(st.Border), Background = Rendering.PageRenderer.Brush(st.Header ?? st.Band ?? "#ffffff") };
            Item(menu, st.Label, () => ChangeTable((_, t) => TableOps.ApplyStyle(t, id), cur), s.Table!.Style == id, sw);
        }
        menu.IsOpen = true;
    }

    void TableVAlign_Click(object sender, RoutedEventArgs e)
    {
        var v = (string)((Button)sender).CommandParameter;
        var cur = CurrentCell();
        ChangeTable((_, t) =>
        {
            var val = v == "top" ? null : v;
            if (cur is { } one) t.Cells[one.Row][one.Col].VAlign = val;
            else foreach (var cell in t.Cells.SelectMany(r => r)) cell.VAlign = val;
        }, cur);
    }

    /// <summary>Texte tabule colle (Excel, Word) : un tableau. Renvoie vrai s'il a ete cree.</summary>
    bool PasteAsTable(Page page, string text)
    {
        if (_session is null || !text.Contains('\t') || !text.Trim().Contains('\n')) return false;
        var m = Snapper.Margins(_session.Doc);
        var cols = text.Replace("\r\n", "\n").Split('\n').Max(l => l.Split('\t').Length);
        double w = Math.Min(page.Width - m[1] - m[3], 100 * cols);
        if (TableOps.FromTabbedText(text, (page.Width - w) / 2, page.Height / 3, w) is not { } s) return false;
        TableOps.ApplyStyle(s.Table!, "simple");
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
        StatusText.Text = $"Tableau collé : {s.Table!.RowCount} lignes × {s.Table.ColCount} colonnes.";
        return true;
    }
}
