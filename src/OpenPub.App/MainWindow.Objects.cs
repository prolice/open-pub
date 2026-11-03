using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.App.Controls;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Onglet Accueil ▸ Objets et Format ▸ Organiser : selection multiple, suppression, ordre, groupes,
// alignement, verrouillage, copier / couper / coller.
public partial class MainWindow
{
    public static readonly RoutedUICommand GroupCommand = new("Grouper", "Group", typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.G, ModifierKeys.Control) });
    public static readonly RoutedUICommand UngroupCommand = new("Dissocier", "Ungroup", typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift) });

    bool AnyLockedSel => _session?.Selection.Any(x => x.Locked) == true;

    /// <summary>Objets selectionnes (page de la selection), dans l'ordre d'empilement.</summary>
    List<Shape> SelectedShapes() =>
        _session is null || SelPage is not { } page ? new List<Shape>() : page.Shapes.Where(_session.IsSelected).ToList();

    // ---------------------------------------------------------------- suppression

    void Delete_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        // la tete d'une chaine porte le texte : elle ne part qu'avec toutes ses zones
        foreach (var chain in sel.Where(s => s.Chain is not null && s.IsStoryHead).Select(s => s.Chain!).Distinct())
            if (!page.ChainFrames(chain).All(sel.Contains))
            {
                MessageBox.Show(this, "Cette zone est la première d'une chaîne de zones liées : supprimez d'abord les zones suivantes, ou sélectionnez toute la chaîne.",
                    "OpenPub", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        _session.Change(page, () =>
        {
            foreach (var s in sel) page.Shapes.Remove(s);
            foreach (var chain in sel.Where(s => s.Chain is not null).Select(s => s.Chain!).Distinct())
            {
                var rest = page.ChainFrames(chain);
                for (int i = 0; i < rest.Count; i++) rest[i].ChainIndex = i;
                if (rest.Count == 1) { rest[0].Chain = null; rest[0].ChainIndex = null; }
            }
            // un groupe reduit a un objet est dissous
            foreach (var g in page.Shapes.Where(s => s.Group is not null).GroupBy(s => s.Group).Where(g => g.Count() == 1))
                foreach (var s in g) s.Group = null;
            Renumber(page);
        });
        _session.Select(null, page);
    }

    // ---------------------------------------------------------------- ordre d'empilement

    void BringFront_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        _session.Change(page, () => { foreach (var s in sel) { page.Shapes.Remove(s); page.Shapes.Add(s); } Renumber(page); });
    }

    void SendBack_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        _session.Change(page, () =>
        {
            foreach (var s in sel) page.Shapes.Remove(s);
            // reste au-dessus d'un eventuel fond de page
            int idx = page.Shapes.FindLastIndex(x => x.Background) + 1;
            page.Shapes.InsertRange(Math.Min(idx, page.Shapes.Count), sel);
            Renumber(page);
        });
    }

    void BringForward_Click(object sender, RoutedEventArgs e) => StepOrder(+1);
    void SendBackward_Click(object sender, RoutedEventArgs e) => StepOrder(-1);

    void StepOrder(int dir)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        _session.Change(page, () => ShapeOps.StepOrder(page, sel, dir));
    }

    // ---------------------------------------------------------------- dupliquer, groupes, verrouillage

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        var copies = ShapeOps.Paste(ShapeOps.Copy(page, sel), 12);
        _session.Change(page, () => { page.Shapes.AddRange(copies); Renumber(page); });
        _session.SelectMany(copies, copies[^1], page);
    }

    void Group_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _session is { TextEdit: null, MultiSelection: true } &&
                       !_session.Selection.All(x => x.Group is not null && x.Group == _session.Selection[0].Group);
    void Ungroup_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _session is { TextEdit: null } && _session.Selection.Any(x => x.Group is not null);

    void Group_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count < 2) return;
        _session.Change(page, () =>
        {
            ShapeOps.Group(sel);
            // les membres d'un groupe se suivent dans l'ordre d'empilement (au niveau du plus haut)
            int top = sel.Max(page.Shapes.IndexOf);
            foreach (var s in sel) page.Shapes.Remove(s);
            page.Shapes.InsertRange(Math.Min(top - sel.Count + 1, page.Shapes.Count), sel);
            Renumber(page);
        });
        _session.SelectMany(sel, Sel, page);
        StatusText.Text = $"{sel.Count} objets groupés : un clic sélectionne le groupe, un double-clic un de ses objets.";
    }

    void Ungroup_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var groups = SelectedShapes().Where(s => s.Group is not null).Select(s => s.Group).Distinct().ToList();
        var members = page.Shapes.Where(s => groups.Contains(s.Group)).ToList();
        if (members.Count == 0) return;
        _session.Change(page, () => ShapeOps.Ungroup(members));
        _session.SelectMany(members, Sel, page);
        StatusText.Text = "Groupe dissocié.";
    }

    void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        bool lockIt = !sel.All(s => s.Locked);
        _session.Change(page, () => { foreach (var s in sel) s.Locked = lockIt; });
        RefreshProperties();
        StatusText.Text = lockIt ? "Position verrouillée : l'objet ne peut plus être déplacé ni redimensionné." : "Position déverrouillée.";
    }

    // ---------------------------------------------------------------- aligner et repartir

    /// <summary>Aligner un seul objet (ou groupe) : par rapport aux marges de la page, comme Publisher.</summary>
    bool _alignToMargins;

    void AlignButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        LastMenu = menu;
        var sel = SelectedShapes();
        int units = sel.Select(s => s.Group ?? s.GetHashCode().ToString()).Distinct().Count();
        bool has = sel.Count > 0 && !AnyLockedSel;
        void Add(string header, string glyph, Action act, bool enabled)
        {
            var it = new MenuItem { Header = header, IsEnabled = enabled };
            it.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Foreground = (Brush)FindResource("IconBrush") };
            it.Click += (_, _) => act();
            menu.Items.Add(it);
        }
        Add("Aligner à gauche", "\uE8E4", () => AlignSel(AlignEdge.Left), has);
        Add("Centrer horizontalement", "\uE8E3", () => AlignSel(AlignEdge.Center), has);
        Add("Aligner à droite", "\uE8E2", () => AlignSel(AlignEdge.Right), has);
        menu.Items.Add(new Separator());
        Add("Aligner en haut", "\uE70E", () => AlignSel(AlignEdge.Top), has);
        Add("Centrer verticalement", "\uE76F", () => AlignSel(AlignEdge.Middle), has);
        Add("Aligner en bas", "\uE70D", () => AlignSel(AlignEdge.Bottom), has);
        menu.Items.Add(new Separator());
        Add("Répartir horizontalement", "\uE76F", () => DistributeSel(true), has && units >= 3);
        Add("Répartir verticalement", "\uE76F", () => DistributeSel(false), has && units >= 3);
        menu.Items.Add(new Separator());
        var margins = new MenuItem { Header = "Par rapport aux marges de la page", IsCheckable = true, IsChecked = _alignToMargins || units == 1 };
        margins.IsEnabled = units > 1;
        margins.Click += (_, _) => _alignToMargins = margins.IsChecked;
        menu.Items.Add(margins);
        menu.IsOpen = true;
    }

    void AlignSel(AlignEdge edge)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        bool single = sel.Select(s => s.Group ?? s.GetHashCode().ToString()).Distinct().Count() == 1;
        (double, double, double, double)? box = null;
        if (single || _alignToMargins)
        {
            var m = Snapper.Margins(_session.Doc);
            box = (m[3], m[0], page.Width - m[1] - m[3], page.Height - m[0] - m[2]);
        }
        _session.Change(page, () => ShapeOps.Align(sel, edge, box));
        RefreshProperties();
    }

    void DistributeSel(bool horizontal)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        _session.Change(page, () => ShapeOps.Distribute(sel, horizontal));
        RefreshProperties();
    }

    // ---------------------------------------------------------------- tout selectionner

    void SelectAll_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session is { TextEdit: null };

    void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page) return;
        var all = page.Shapes.Where(s => !s.Background && !(s.Kind is not (ShapeKind.Text or ShapeKind.Image) && s.W >= page.Width * 0.95 && s.H >= page.Height * 0.95)).ToList();
        _session.SelectMany(all, all.LastOrDefault(), page);
    }

    // ---------------------------------------------------------------- presse-papiers

    void Copy_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session is { TextEdit: null, Selected: not null };
    void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = false;
        if (_session is not { TextEdit: null }) return;
        try
        {
            e.CanExecute = Clipboard.ContainsData(Core.Editing.ShapeClip.Format) || Clipboard.ContainsImage()
                        || Clipboard.ContainsFileDropList() || Clipboard.ContainsText();
        }
        catch { /* presse-papiers occupe par une autre application */ }
    }

    void Copy_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelection(cut: false);
    void Cut_Executed(object sender, ExecutedRoutedEventArgs e) => CopySelection(cut: true);

    void CopySelection(bool cut)
    {
        if (_session is null || SelPage is not { } page) return;
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        var clip = ShapeOps.Copy(page, sel);
        clip.SourceWorkDir = _session.Package.WorkDir;
        foreach (var src in clip.Shapes.SelectMany(s => new[] { s.Src, s.Fill?.Image }).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct())
        {
            var file = _session.Package.ResolveAsset(src);
            if (File.Exists(file) && new FileInfo(file).Length < 40_000_000)
                clip.Assets[src] = Convert.ToBase64String(File.ReadAllBytes(file));
        }
        var data = new DataObject();
        data.SetData(Core.Editing.ShapeClip.Format, clip.ToJson());
        // texte brut des zones de texte, pour coller dans une autre application
        var text = string.Join("\r\n\r\n", sel.Where(s => s.IsText && s.IsStoryHead && s.Paragraphs is not null)
            .Select(s => string.Join("\r\n", s.Paragraphs!.Select(p => p.PlainText))));
        if (text.Length > 0) data.SetText(text);
        // le presse-papiers peut etre momentanement occupe par une autre application : quelques essais
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(data, copy: true); break; }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                if (attempt >= 9) { StatusText.Text = "Presse-papiers indisponible : " + ex.Message; return; }
                System.Threading.Thread.Sleep(50);
            }
        }
        if (cut) Delete_Executed(this, null!);
        StatusText.Text = $"{sel.Count} objet(s) {(cut ? "coupé(s)" : "copié(s)")}.";
    }

    void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page) return;
        try
        {
            if (Clipboard.GetData(Core.Editing.ShapeClip.Format) is string json && Core.Editing.ShapeClip.FromJson(json) is { } clip)
            {
                PasteShapes(page, clip);
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                var images = Clipboard.GetFileDropList().Cast<string>()
                    .Where(f => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
                if (images.Count > 0) { foreach (var f in images) InsertImage(page, _session.Package.AddAsset(f)); return; }
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bmp)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                enc.Save(ms);
                InsertImage(page, _session.Package.AddAsset(ms.ToArray(), "image-collee.png"));
                return;
            }
            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text && !PasteAsTable(page, text)) PasteText(page, text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            StatusText.Text = "Presse-papiers indisponible : " + ex.Message;
        }
    }

    void PasteShapes(Page page, Core.Editing.ShapeClip clip)
    {
        if (_session is null) return;
        // images d'un autre document : recopiees dans ce paquet
        bool sameDoc = string.Equals(clip.SourceWorkDir, _session.Package.WorkDir, StringComparison.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>();
        foreach (var s in clip.Shapes.Where(s => s.Src is not null))
        {
            var src = s.Src!;
            if (sameDoc && File.Exists(_session.Package.ResolveAsset(src))) continue;
            if (!renamed.TryGetValue(src, out var ns) && clip.Assets.TryGetValue(src, out var b64))
                renamed[src] = ns = _session.Package.AddAsset(Convert.FromBase64String(b64), Path.GetFileName(src));
            if (ns is not null) s.Src = ns;
        }
        // images de remplissage (lot 9)
        foreach (var f in clip.Shapes.Select(s => s.Fill).Where(f => !string.IsNullOrEmpty(f?.Image)))
        {
            var src = f!.Image!;
            if (sameDoc && File.Exists(_session.Package.ResolveAsset(src))) continue;
            if (!renamed.TryGetValue(src, out var ns) && clip.Assets.TryGetValue(src, out var b64))
                renamed[src] = ns = _session.Package.AddAsset(Convert.FromBase64String(b64), Path.GetFileName(src));
            if (ns is not null) f.Image = ns;
        }
        // decalage si les objets colles tomberaient exactement sur des objets existants (coller plusieurs fois)
        double offset = 0;
        while (offset < 120 && clip.Shapes.Any(c => page.Shapes.Any(x => Math.Abs(x.X - (c.X + offset)) < 0.5 && Math.Abs(x.Y - (c.Y + offset)) < 0.5)))
            offset += 12;
        var shapes = ShapeOps.Paste(clip, offset);
        _session.Change(page, () => { page.Shapes.AddRange(shapes); Renumber(page); });
        _session.SelectMany(shapes, shapes.LastOrDefault(), page);
        StatusText.Text = $"{shapes.Count} objet(s) collé(s).";
    }

    void InsertImage(Page page, string src)
    {
        if (_session is null) return;
        var bmp = _session.Images.Get(src);
        double w = 200, h = bmp is null ? 150 : 200.0 * bmp.PixelHeight / Math.Max(1, bmp.PixelWidth);
        var s = new Shape { Kind = ShapeKind.Image, Src = src, X = page.Width / 2 - w / 2, Y = page.Height / 2 - h / 2, W = w, H = h };
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
    }

    void PasteText(Page page, string text)
    {
        if (_session is null) return;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var font = new FontSpec { Family = "Cavolini", Size = 12, Color = "#000000", Lang = "fr" };
        var s = new Shape
        {
            Kind = ShapeKind.Text, X = page.Width / 2 - 120, Y = page.Height / 2 - 60, W = 240, H = Math.Min(400, 30 + 16 * lines.Length),
            Padding = new[] { 2.85, 2.85, 2.85, 2.85 }, AutoFit = Shape.NewAutoFit,
            Paragraphs = lines.Select(l => new Paragraph { Align = "left", Runs = { new Run { Text = l, Font = font.Clone() } } }).ToList(),
        };
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
    }
}
