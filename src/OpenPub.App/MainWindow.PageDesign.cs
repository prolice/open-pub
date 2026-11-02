using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.App.Controls;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Onglet Creation de page : nouveau document, mise en page (format, marges, grille), pages maitres, arriere-plan,
// numerotation et champs, double page, regles et reperes.
public partial class MainWindow
{
    bool _editMasters;
    bool _spread;

    /// <summary>Mode « pages maitres » : les vues affichent et modifient les pages maitres.</summary>
    public bool EditingMasters => _editMasters;

    /// <summary>Affichage en double page (planches).</summary>
    public bool SpreadView
    {
        get => _spread;
        set { _spread = value; if (_session is not null) { BuildPages(); Dispatcher.BeginInvoke(() => ZoomFit_Click(this, new RoutedEventArgs()), System.Windows.Threading.DispatcherPriority.Loaded); } }
    }

    /// <summary>Pages affichees : celles du document, ou les pages maitres en mode pages maitres.</summary>
    List<Page> PageList => _session is null ? new List<Page>() : _editMasters ? (_session.Doc.Masters ??= new List<Page>()) : _session.Doc.Pages;

    /// <summary>Identifiants uniques des pages maitres (A, B, C...).</summary>
    void EnsureMasterIds()
    {
        if (_session?.Doc.Masters is not { } ms) return;
        var used = new HashSet<string>();
        foreach (var m in ms)
        {
            if (m.Id is null || used.Contains(m.Id))
                m.Id = Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString()).First(id => !used.Contains(id) && ms.All(x => x == m || x.Id != id));
            used.Add(m.Id);
            m.Width = m.Width > 0 ? m.Width : _session.Doc.PageWidth;
            m.Height = m.Height > 0 ? m.Height : _session.Doc.PageHeight;
        }
    }

    Page NewMaster()
    {
        var doc = _session!.Doc;
        var m = new Page { Width = doc.PageWidth, Height = doc.PageHeight, Name = "Page maître" };
        (doc.Masters ??= new List<Page>()).Add(m);
        for (int i = 0; i < doc.Masters.Count; i++) doc.Masters[i].Index = i + 1;
        EnsureMasterIds();
        return m;
    }

    void SetMasterMode(bool on)
    {
        if (_session is null) return;
        _session.EndTextEdit();
        CancelLink();
        if (on && _session.Doc.Masters is not { Count: > 0 })
        {
            _session.BeginChange();
            NewMaster();
            _session.EndChange();
        }
        _editMasters = on;
        _session.Select(null);
        MastersToggle.IsChecked = on;
        MasterBanner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _session.StructureChanged();
        StatusText.Text = on ? "Pages maîtres : ce que vous placez ici apparaît sous chaque page qui les utilise (bandeau, numéro de page…)." : "";
    }

    void MastersToggle_Click(object sender, RoutedEventArgs e) => SetMasterMode(MastersToggle.IsChecked == true);
    void CloseMasters_Click(object sender, RoutedEventArgs e) => SetMasterMode(false);

    void NewMaster_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (!_editMasters) SetMasterMode(true);
        _session.BeginChange();
        var m = NewMaster();
        _session.EndChange();
        _session.StructureChanged();
        _session.Select(null, m);
        StatusText.Text = $"Page maître {m.Id} ajoutée.";
    }

    void ApplyMaster_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        var doc = _session.Doc;
        var page = _editMasters ? null : ActivePage();
        var masters = doc.Masters ?? new List<Page>();
        if (masters.Count == 0)
        {
            Item(menu, "Créer une page maître…", () => SetMasterMode(true));
            menu.IsOpen = true;
            return;
        }
        string Label(Page m) => $"{m.Id}{(m.Name is { Length: > 0 } n ? " — " + n : "")}";
        if (page is not null)
        {
            var current = doc.MasterOf(page);
            foreach (var m in masters)
            {
                var mm = m;
                Item(menu, $"Page active : {Label(m)}", () => ChangePages(new[] { page }, p => { p.Master = mm.Id; p.IgnoreMaster = false; }), current == m);
            }
            Item(menu, "Page active : aucune page maître", () => ChangePages(new[] { page }, p => p.IgnoreMaster = true), current is null);
            menu.Items.Add(new Separator());
        }
        foreach (var m in masters)
        {
            var mm = m;
            Item(menu, $"Toutes les pages : {Label(m)}", () => ChangePages(doc.Pages, p => { p.Master = mm.Id; p.IgnoreMaster = false; }));
        }
        if (masters.Count >= 2)
        {
            menu.Items.Add(new Separator());
            Item(menu, $"Pages impaires : {Label(masters[0])} · paires : {Label(masters[1])}",
                () => ChangePages(doc.Pages, p => { p.IgnoreMaster = false; p.Master = doc.Pages.IndexOf(p) % 2 == 0 ? masters[0].Id : masters[1].Id; }));
        }
        menu.IsOpen = true;
    }

    /// <summary>Modifie des pages (annulable) et les redessine.</summary>
    void ChangePages(IEnumerable<Page> pages, Action<Page> change)
    {
        if (_session is null) return;
        var list = pages.ToList();
        _session.BeginChange();
        foreach (var p in list) change(p);
        _session.EndChange();
        _session.StructureChanged();
    }

    // ---------------------------------------------------------------- arriere-plan

    void BackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page) return;
        var menu = NewMenu(sender);
        Item(menu, "Couleur d'arrière-plan…", () =>
        {
            var (ok, c) = PickColor(page.BackColor ?? "#ffffff", true, "Couleur d'arrière-plan");
            if (ok) ChangePages(new[] { page }, p => p.BackColor = c);
        });
        Item(menu, "Couleur pour toutes les pages…", () =>
        {
            var (ok, c) = PickColor(page.BackColor ?? "#ffffff", true, "Couleur d'arrière-plan de toutes les pages");
            if (ok) ChangePages(PageList, p => p.BackColor = c);
        });
        Item(menu, "Image d'arrière-plan…", () =>
        {
            if (PickImage() is not { } file) return;
            var src = _session.Package.AddAsset(file);
            _session.Change(page, () =>
            {
                page.Shapes.RemoveAll(x => x.IsImage && x.Background);
                page.Shapes.Insert(0, new Shape { Kind = ShapeKind.Image, Src = src, X = 0, Y = 0, W = page.Width, H = page.Height, Background = true });
                Renumber(page);
            });
        });
        Item(menu, "Retirer l'arrière-plan", () =>
        {
            _session.Change(page, () => { page.BackColor = null; page.Shapes.RemoveAll(x => x.IsImage && x.Background); Renumber(page); });
            _session.StructureChanged();
        });
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- numerotation et champs

    void PageNumbersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        Item(menu, "Insérer : numéro de page", () => InsertField("page"));
        Item(menu, "Insérer : nombre de pages", () => InsertField("pages"));
        Item(menu, "Insérer : « Page X sur Y »", () => InsertPageXofY());
        Item(menu, "Insérer : date (mise à jour automatique)", () => InsertField("date"));
        menu.Items.Add(new Separator());
        Item(menu, "Ajouter les numéros en bas au centre (page maître)", () => AddPageNumbers("bottom-center"));
        Item(menu, "Ajouter les numéros en bas à droite (page maître)", () => AddPageNumbers("bottom-right"));
        Item(menu, "Ajouter les numéros en haut à droite (page maître)", () => AddPageNumbers("top-right"));
        menu.Items.Add(new Separator());
        Item(menu, "Format de la numérotation…", () => NumberingDialog());
        menu.IsOpen = true;
    }

    void InsertField(string field)
    {
        if (!RequireTextEdit()) return;
        _session!.TextEdit!.InsertField(field);
        RefocusPage();
    }

    void InsertPageXofY()
    {
        if (!RequireTextEdit()) return;
        var ed = _session!.TextEdit!;
        ed.Type("Page ");
        ed.InsertField("page");
        ed.Type(" sur ");
        ed.InsertField("pages");
        RefocusPage();
    }

    /// <summary>Zone de texte « numero de page » sur la page maitre (creee au besoin) : elle apparait sur toutes les pages.</summary>
    void AddPageNumbers(string where)
    {
        if (_session is null) return;
        var doc = _session.Doc;
        _session.BeginChange();
        var master = doc.Masters is { Count: > 0 } ms ? (_editMasters && ActivePage() is { } a && ms.Contains(a) ? a : ms[0]) : NewMaster();
        var m = Snapper.Margins(doc);
        double w = 120, h = 22;
        double x = where.EndsWith("center") ? master.Width / 2 - w / 2 : master.Width - m[1] - w;
        double y = where.StartsWith("top") ? Math.Max(4, m[0] / 2 - h / 2) : master.Height - Math.Max(h + 4, m[2] / 2 + h / 2);
        var font = new FontSpec { Family = "Arial", Size = 10, Color = "#52617e", Lang = "fr" };
        var p = new Paragraph { Align = where.EndsWith("center") ? "center" : "right", Runs = { new Run { Text = "", Font = font } } };
        var paras = new List<Paragraph> { p };
        Core.Editing.StoryEditor.InsertField(paras, new Core.Editing.TextPos(0, 0), "page", font);
        master.Shapes.Add(new Shape { Kind = ShapeKind.Text, X = x, Y = y, W = w, H = h, Padding = new[] { 1.0, 1.0, 1.0, 1.0 }, Paragraphs = paras, AutoFit = "none" });
        for (int i = 0; i < master.Shapes.Count; i++) master.Shapes[i].Z = i + 1;
        foreach (var pg in doc.Pages) pg.IgnoreMaster = false;
        _session.EndChange();
        _session.StructureChanged();
        StatusText.Text = $"Numéros de page ajoutés sur la page maître {master.Id} : ils s'affichent sur toutes les pages.";
    }

    void NumberingDialog()
    {
        if (_session is null) return;
        var doc = _session.Doc;
        var page = _editMasters ? null : ActivePage();
        var dlg = new Window
        {
            Title = "Numérotation des pages", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("UiFont"), Background = (Brush)FindResource("Chrome"), Foreground = (Brush)FindResource("Ink"),
        };
        var root = new StackPanel { Margin = new Thickness(18), Width = 340 };
        var style = new ComboBox { Width = 160, Margin = new Thickness(0, 2, 0, 8) };
        foreach (var (lbl, tag) in new[] { ("1, 2, 3", "1"), ("i, ii, iii", "i"), ("I, II, III", "I"), ("a, b, c", "a"), ("A, B, C", "A") })
            style.Items.Add(new ComboBoxItem { Content = lbl, Tag = tag });
        style.SelectedItem = style.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (doc.Numbering?.Style ?? "1")) ?? style.Items[0];
        var start = new TextBox { Width = 60, Text = (doc.Numbering?.Start ?? 1).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 2, 0, 8) };
        var restart = new CheckBox { Content = page is null ? "" : $"Recommencer la numérotation à la page {page.Index} :", IsChecked = page?.NumberStart is not null, Visibility = page is null ? Visibility.Collapsed : Visibility.Visible };
        var restartAt = new TextBox { Width = 60, Text = (page?.NumberStart ?? 1).ToString(CultureInfo.CurrentCulture), Margin = new Thickness(22, 4, 0, 0), Visibility = restart.Visibility, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = "Style des numéros" }); root.Children.Add(style);
        root.Children.Add(new TextBlock { Text = "Premier numéro du document" }); root.Children.Add(start);
        root.Children.Add(restart); root.Children.Add(restartAt);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Style = (Style)FindResource("PillButton") };
        ok.Click += (_, _) => dlg.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dlg.Content = root;
        if (dlg.ShowDialog() != true) return;
        if (!int.TryParse(start.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var st) || st < 0) st = 1;
        int.TryParse(restartAt.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var ra);
        var styleTag = (string)((ComboBoxItem)style.SelectedItem).Tag;
        _session.BeginChange();
        doc.Numbering = st == 1 && styleTag == "1" ? null : new PageNumbering { Start = st, Style = styleTag == "1" ? null : styleTag };
        if (page is not null) page.NumberStart = restart.IsChecked == true ? Math.Max(0, ra) : null;
        _session.EndChange();
        _session.StructureChanged();
    }

    // ---------------------------------------------------------------- mise en page et nouveau document

    void PageSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var doc = _session.Doc;
        var first = doc.Pages.FirstOrDefault();
        var dlg = new PageSetupWindow(false, first?.Width ?? doc.PageWidth, first?.Height ?? doc.PageHeight, doc.Margins,
            doc.Grid?.Columns ?? 1, doc.Grid?.Rows ?? 1, doc.Grid?.Gutter ?? 14.17) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _session.EndTextEdit();
        _session.BeginChange();
        ApplyPageSize(doc, dlg.PageWidth, dlg.PageHeight, dlg.ScaleObjects);
        doc.Margins = dlg.Margins;
        doc.Grid = dlg.GridColumns == 1 && dlg.GridRows == 1 ? null : new LayoutGrid { Columns = dlg.GridColumns, Rows = dlg.GridRows, Gutter = dlg.GridGutter };
        _session.EndChange();
        _session.StructureChanged();
        Dispatcher.BeginInvoke(() => ZoomFit_Click(this, new RoutedEventArgs()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Nouvelle taille pour toutes les pages (et pages maitres) ; les objets sont mis a l'echelle et centres si demande.</summary>
    internal static void ApplyPageSize(PubDocument doc, double w, double h, bool scaleObjects)
    {
        foreach (var page in doc.Pages.Concat(doc.Masters ?? new List<Page>()))
        {
            if (Math.Abs(page.Width - w) < 0.01 && Math.Abs(page.Height - h) < 0.01) continue;
            if (scaleObjects && page.Width > 0 && page.Height > 0)
            {
                double k = Math.Min(w / page.Width, h / page.Height);
                double ox = (w - page.Width * k) / 2, oy = (h - page.Height * k) / 2;
                // les fonds de page couvrent toute la nouvelle page
                foreach (var bg in page.Shapes.Where(s => s.Background)) PageView.MoveShape(bg, new Rect(0, 0, w, h));
                PageView.FitShapes(page.Shapes.Where(s => !s.Background), new Rect(0, 0, page.Width, page.Height), new Rect(ox, oy, page.Width * k, page.Height * k));
            }
            page.Width = w; page.Height = h;
        }
        doc.PageWidth = w; doc.PageHeight = h;
    }

    void NewDocument_Click()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new PageSetupWindow(true, 595.276, 841.89, null) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var pkg = OpubPackage.CreateBlank(dlg.PageWidth, dlg.PageHeight, dlg.PageCount, dlg.Margins);
        if (dlg.GridColumns > 1 || dlg.GridRows > 1) pkg.Document.Grid = new LayoutGrid { Columns = dlg.GridColumns, Rows = dlg.GridRows, Gutter = dlg.GridGutter };
        LoadPackage(pkg);
        StatusText.Text = "Nouveau document : tracez une zone de texte (Insertion) ou insérez une image pour commencer.";
    }

    // ---------------------------------------------------------------- affichage

    void SpreadToggle_Click(object sender, RoutedEventArgs e) => SpreadView = SpreadToggle.IsChecked == true;

    void RulersToggle_Click(object sender, RoutedEventArgs e)
    {
        var v = RulersToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RulerTop.Visibility = RulerLeft.Visibility = RulerCorner.Visibility = v;
        UpdateRulers();
    }
}
