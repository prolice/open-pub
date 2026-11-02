using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.App.Controls;
using OpenPub.App.Rendering;
using OpenPub.Core.Editing;
using OpenPub.Core.Geometry;
using OpenPub.Core.Imaging;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

/// <summary>
/// Lot 9 : bibliotheque de formes (galeries par categorie), remplissages (degrade, motif, image, transparence),
/// effets (lueur, bords adoucis, ombre interieure), bordures decoratives, modification des points, WordArt,
/// espaces reserves, permutation d'images, legende, compression des images.
/// </summary>
public partial class MainWindow
{
    // ================================================================ galeries de formes

    /// <summary>Menu Insertion ▸ Formes : une grille de vignettes par categorie.</summary>
    void ShowShapesMenu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom, MaxWidth = 340 };
        LastMenu = menu;
        foreach (var cat in ShapeLibrary.Categories)
        {
            menu.Items.Add(PlainItem(new TextBlock
            {
                Text = cat, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(8, 6, 8, 2), Foreground = (Brush)FindResource("SelectedText"),
            }));
            var grid = new WrapPanel { Width = 312, Margin = new Thickness(6, 0, 6, 2) };
            foreach (var d in ShapeLibrary.All.Where(x => x.Category == cat))
            {
                var b = new Button
                {
                    Style = (Style)FindResource("RSmall"), Content = ShapeGeometries.Icon(d.Id, (Brush)FindResource("Accent"), 22, 18),
                    ToolTip = d.Label, Margin = new Thickness(1), Padding = new Thickness(4, 3, 4, 3),
                };
                var id = d.Id;
                b.Click += (_, _) => { menu.IsOpen = false; SetDrawTool(id); };
                grid.Children.Add(b);
            }
            menu.Items.Add(PlainItem(grid));
        }
        menu.IsOpen = true;
    }

    /// <summary>Element de menu sans surbrillance (titre de categorie, grille de vignettes).</summary>
    static MenuItem PlainItem(UIElement content)
    {
        var tpl = new ControlTemplate(typeof(MenuItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        tpl.VisualTree.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        return new MenuItem { Header = content, Template = tpl, Focusable = false };
    }

    /// <summary>Galerie de l'onglet Dessin : formes courantes, puis « Plus de formes ».</summary>
    void FillShapeGallery()
    {
        foreach (var id in ShapeGeometries.Favorites)
        {
            var label = ShapeLibrary.Find(id)?.Label ?? id;
            var b = new ToggleButton
            {
                Style = (Style)FindResource("RSmall"), Content = ShapeGeometries.Icon(id, (Brush)FindResource("Accent")), ToolTip = label,
                CommandParameter = id, Margin = new Thickness(1), Padding = new Thickness(4, 3, 4, 3),
            };
            b.Click += DrawTool_Click;
            ShapeGallery.Children.Add(b);
        }
        var more = new Button
        {
            Style = (Style)FindResource("RSmall"), Content = new TextBlock { Text = "⋯", FontSize = 14, Width = 20, TextAlignment = TextAlignment.Center },
            ToolTip = "Plus de formes : toutes les formes, par catégorie",
            Margin = new Thickness(1), Padding = new Thickness(4, 3, 4, 3),
        };
        more.Click += (_, _) => ShowShapesMenu(more);
        ShapeGallery.Children.Add(more);
    }

    /// <summary>Liste « Forme » du ruban, avec les categories en intertitres.</summary>
    void FillGeometryBox()
    {
        GeometryBox.Items.Add(new ComboBoxItem { Content = "Aucun cadre / tracé d'origine", Tag = "" });
        foreach (var cat in ShapeLibrary.Categories)
        {
            GeometryBox.Items.Add(new ComboBoxItem { Content = cat, Tag = "#" + cat, IsEnabled = false, FontWeight = FontWeights.Bold });
            foreach (var d in ShapeLibrary.All.Where(x => x.Category == cat))
                GeometryBox.Items.Add(new ComboBoxItem { Content = d.Label, Tag = d.Id, Padding = new Thickness(14, 2, 4, 2) });
        }
    }

    /// <summary>Etat des boutons du lot 9 dans les onglets contextuels.</summary>
    void RefreshShapeTools(Shape s)
    {
        bool wa = s.IsWordArt;
        WordArtTextButton.Visibility = WarpButton.Visibility = wa ? Visibility.Visible : Visibility.Collapsed;
        EditPointsToggle.Visibility = wa || s.IsText || s.IsImage ? Visibility.Collapsed : Visibility.Visible;
        EditPointsToggle.IsChecked = _views.Any(v => v.PointEditShape == s);
        GeometryBox.IsEnabled = !wa;
        if (_session is not null && DecorRenderer.FillBrush(s.Fill, _session.Images) is { } fb) FillSwatch.Background = fb;
        SwapButton.IsEnabled = _session?.Selection.Count(x => x.IsImage) == 2;
    }

    /// <summary>Aide contextuelle d'une forme : WordArt, modification des points, sinon format de la forme.</summary>
    string ShapeHelpTopic(Shape s) =>
        s.IsWordArt ? Help.HelpService.WordArt
        : _views.Any(v => v.PointEditShape == s) ? Help.HelpService.EditPoints
        : Help.HelpService.ShapeFormat;

    void HookShapeEvents(PageView view)
    {
        view.PlaceholderClicked += (_, s) => FillPlaceholder(s);
        view.WordArtEditRequested += (_, s) => EditWordArt(s);
        view.PointEditChanged += v =>
        {
            EditPointsToggle.IsChecked = v.PointEditShape is not null;
            StatusText.Text = v.PointEditShape is not null
                ? "Modifier les points : glissez un sommet ou une poignée ; Ctrl + clic sur le contour ajoute un sommet, sur un sommet le supprime ; double-clic : lisse / angle ; Échap pour terminer."
                : "";
        };
    }

    // ================================================================ menus utilitaires

    static MenuItem Item(string header, Action act, UIElement? icon = null, bool check = false)
    {
        var it = new MenuItem { Header = header, Icon = icon, IsChecked = check, Padding = new Thickness(4, 4, 4, 4) };
        it.Click += (_, _) => act();
        return it;
    }

    /// <summary>Couleur de base pour les propositions (couleur actuelle de l'objet, sinon bleu).</summary>
    string BaseColor() => Sel?.Fill?.Color ?? "#5b9bd5";

    /// <summary>Eclaircit (k &gt; 0) ou assombrit (k &lt; 0) une couleur.</summary>
    static string Mix(string hex, double k)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); } catch (FormatException) { c = Colors.Gray; }
        byte F(byte v) => (byte)Math.Clamp(k >= 0 ? v + (255 - v) * k : v * (1 + k), 0, 255);
        return $"#{F(c.R):x2}{F(c.G):x2}{F(c.B):x2}";
    }

    static Fill Grad(string type, double angle, params (double Pos, string Color)[] stops) => new()
    {
        Color = stops[0].Color, GradientType = type == "radial" ? "radial" : null, Angle = type == "radial" ? null : angle,
        Stops = stops.Select(s => new GradientStopFx { Pos = s.Pos, Color = s.Color }).ToList(),
    };

    /// <summary>Degrades proposes, calcules a partir de la couleur actuelle (sauf les degrades prets a l'emploi).</summary>
    internal List<(string Label, Fill Fill)> GradientPresets()
    {
        var c = BaseColor();
        return new List<(string, Fill)>
        {
            ("Clair vers foncé", Grad("linear", 90, (0, Mix(c, 0.55)), (1, Mix(c, -0.35)))),
            ("Foncé vers clair", Grad("linear", 90, (0, Mix(c, -0.35)), (1, Mix(c, 0.55)))),
            ("De gauche à droite", Grad("linear", 0, (0, c), (1, "#ffffff"))),
            ("En diagonale", Grad("linear", 45, (0, Mix(c, 0.4)), (1, Mix(c, -0.45)))),
            ("Radial (centre clair)", Grad("radial", 0, (0, "#ffffff"), (1, c))),
            ("Coucher de soleil", Grad("linear", 90, (0, "#ffc000"), (0.5, "#ed7d31"), (1, "#c00000"))),
            ("Océan", Grad("linear", 90, (0, "#8fd8f8"), (0.5, "#0070c0"), (1, "#002060"))),
            ("Arc-en-ciel", Grad("linear", 0, (0, "#e81416"), (0.2, "#ffa500"), (0.4, "#faeb36"), (0.6, "#79c314"), (0.8, "#487de7"), (1, "#70369d"))),
        };
    }

    /// <summary>Applique un remplissage (copie) aux objets selectionnes, en gardant leur transparence.</summary>
    void ApplyFill(Fill f) => ChangeSel(x => { var n = f.Clone(); n.Opacity = x.Fill?.Opacity; x.Fill = n; }, x => !x.IsImage || x.IsPlaceholder);

    // ================================================================ remplissage

    void FillMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { } s) return;
        var menu = NewMenu(sender);
        menu.Items.Add(Item("Couleur unie…", () => FillColor_Click(sender, e)));
        var grad = new MenuItem { Header = "Dégradé" };
        foreach (var (label, f) in GradientPresets())
            grad.Items.Add(Item(label, () => ApplyFill(f), DecorRenderer.Swatch(DecorRenderer.FillBrush(f, null))));
        grad.Items.Add(new Separator());
        grad.Items.Add(Item("Dégradé personnalisé…", () => OpenFillWindow("gradient")));
        menu.Items.Add(grad);
        var pat = new MenuItem { Header = "Motif" };
        string fg = s.Fill?.PatternColor ?? Mix(BaseColor(), -0.35), bg = s.Fill?.Pattern is null ? "#ffffff" : s.Fill.Color ?? "#ffffff";
        foreach (var (id, label) in FillPatterns.All)
        {
            var pid = id;
            pat.Items.Add(Item(label, () => ApplyFill(new Fill { Color = bg, Pattern = pid, PatternColor = fg }),
                DecorRenderer.Swatch(DecorRenderer.Pattern(id, bg, fg)), s.Fill?.Pattern == id));
        }
        pat.Items.Add(new Separator());
        pat.Items.Add(Item("Couleurs du motif…", () => OpenFillWindow("pattern")));
        menu.Items.Add(pat);
        menu.Items.Add(Item("Image…", () => { if (PickImage() is { } file) ApplyFill(new Fill { Color = "#ffffff", Image = _session.Package.AddAsset(file) }); }));
        var tr = new MenuItem { Header = "Transparence du remplissage" };
        foreach (var t in new[] { 0, 25, 50, 75, 90 })
        {
            double v = t / 100.0;
            tr.Items.Add(Item($"{t} %", () => ChangeSel(x => { if (x.Fill is { } f) f.Opacity = v <= 0 ? null : 1 - v; }),
                check: Math.Abs((1 - (s.Fill?.Opacity ?? 1)) - v) < 0.01));
        }
        menu.Items.Add(tr);
        var lt = new MenuItem { Header = "Transparence du contour" };
        foreach (var t in new[] { 0, 25, 50, 75 })
        {
            double v = t / 100.0;
            lt.Items.Add(Item($"{t} %", () => ChangeSel(x => { if (x.Line is { } l) l.Opacity = v <= 0 ? null : 1 - v; }),
                check: Math.Abs((1 - (s.Line?.Opacity ?? 1)) - v) < 0.01));
        }
        menu.Items.Add(lt);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Options de remplissage…", () => OpenFillWindow(null)));
        menu.Items.Add(Item("Aucun remplissage", () => ChangeSel(x => x.Fill = null, x => !x.IsImage || x.IsPlaceholder)));
        menu.IsOpen = true;
    }

    void OpenFillWindow(string? page)
    {
        if (_session is null || Sel is not { } s) return;
        var dlg = new FillWindow(s.Fill, s.Line?.Opacity, _session.Images, (c, t) => PickColor(c, false, t),
            () => PickImage() is { } f ? _session.Package.AddAsset(f) : null, page) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var fill = dlg.ResultFill;
        var lineOpacity = dlg.ResultLineOpacity;
        ChangeSel(x =>
        {
            x.Fill = fill?.Clone();
            if (x.Line is { } l) l.Opacity = lineOpacity;
        });
    }

    // ================================================================ effets

    void EffectsMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { } s) return;
        var menu = NewMenu(sender);
        var glow = new MenuItem { Header = "Lueur" };
        glow.Items.Add(Item("Aucune", () => SetEffects(fx => fx.GlowColor = null), check: s.Effects?.HasGlow != true));
        foreach (var (label, color) in new[] { ("Jaune", "#ffd23f"), ("Bleu", "#5b9bd5"), ("Rose", "#e83e8c"), ("Vert", "#70ad47"), ("Blanc", "#ffffff"), ("Couleur de l'objet", BaseColor()) })
            foreach (var size in new[] { 5.0, 10, 18 })
            {
                var (c, z) = (color, size);
                if (size != 10 && label != "Jaune" && label != "Bleu") continue;      // menu court : quelques tailles seulement
                glow.Items.Add(Item($"{label}, {size:0} pt", () => SetEffects(fx => { fx.GlowColor = c; fx.GlowSize = z; fx.GlowOpacity = 0.6; }),
                    new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = PageRenderer.Brush(color), Stroke = Brushes.Gray, StrokeThickness = 0.5 }));
            }
        menu.Items.Add(glow);
        var soft = new MenuItem { Header = "Bords adoucis" };
        foreach (var r in new[] { 0, 2.5, 5, 10, 25 })
        {
            double v = r;
            soft.Items.Add(Item(r == 0 ? "Aucun" : $"{r.ToString("0.#", CultureInfo.CurrentCulture)} pt", () => SetEffects(fx => fx.SoftEdge = v),
                check: Math.Abs((s.Effects?.SoftEdge ?? 0) - v) < 0.01));
        }
        menu.Items.Add(soft);
        var inner = new MenuItem { Header = "Ombre intérieure" };
        inner.Items.Add(Item("Aucune", () => SetEffects(fx => fx.InnerShadow = null), check: s.Effects?.InnerShadow is null));
        foreach (var (label, angle, dist) in new[] { ("En haut à gauche", 45.0, 4.0), ("En bas à droite", 225.0, 4.0), ("En haut", 90.0, 4.0), ("Au centre (tout autour)", 0.0, 0.0) })
        {
            var (a, d) = (angle, dist);
            inner.Items.Add(Item(label, () => SetEffects(fx => fx.InnerShadow = new ShadowFx { Angle = a, Distance = d, Blur = 6, Opacity = 0.45 })));
        }
        menu.Items.Add(inner);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Options des effets…", OpenEffectsWindow));
        menu.Items.Add(Item("Retirer les effets", () => ChangeSel(x => x.Effects = null)));
        menu.IsOpen = true;
    }

    void SetEffects(Action<EffectsFx> change) => ChangeSel(x =>
    {
        var fx = x.Effects?.Clone() ?? new EffectsFx();
        change(fx);
        x.Effects = fx.IsEmpty ? null : fx;
    });

    void OpenEffectsWindow()
    {
        if (Sel is not { } s) return;
        var dlg = new EffectsWindow(s.Effects, (c, t) => PickColor(c, false, t)) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var fx = dlg.Result;
        ChangeSel(x => x.Effects = fx?.Clone());
    }

    // ================================================================ bordures decoratives

    void BorderArtMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { } s) return;
        var menu = NewMenu(sender);
        var cur = s.BorderArt;
        menu.Items.Add(Item("Aucune bordure", () => ChangeSel(x => x.BorderArt = null), check: cur is null));
        menu.Items.Add(new Separator());
        foreach (var (id, label) in BorderArtLayout.Patterns)
        {
            var pid = id;
            menu.Items.Add(Item(label, () => ChangeSel(x =>
            {
                var b = x.BorderArt?.Clone() ?? new BorderArtFx();
                b.Pattern = pid;
                x.BorderArt = b;
            }), DecorRenderer.MotifIcon(id, PageRenderer.Brush(cur?.Color ?? "#d13438")), cur?.Pattern == id));
        }
        menu.Items.Add(new Separator());
        var size = new MenuItem { Header = "Taille des motifs" };
        foreach (var z in new[] { 8.0, 12, 14, 18, 24, 32 })
        {
            double v = z;
            size.Items.Add(Item($"{z:0} pt", () => ChangeSel(x => { if (x.BorderArt is { } b) b.Size = v; }), check: Math.Abs((cur?.Size ?? 0) - v) < 0.01));
        }
        menu.Items.Add(size);
        menu.Items.Add(Item("Couleur…", () =>
        {
            var (ok, c) = PickColor(cur?.Color ?? "#d13438", false, "Couleur de la bordure");
            if (ok && c is not null) ChangeSel(x => { x.BorderArt ??= new BorderArtFx(); x.BorderArt.Color = c; });
        }));
        menu.Items.Add(Item("Couleur alternée…", () =>
        {
            var (ok, c) = PickColor(cur?.Color2, true, "Seconde couleur (motifs alternés)");
            if (ok) ChangeSel(x => { x.BorderArt ??= new BorderArtFx(); x.BorderArt.Color2 = c; });
        }));
        menu.IsOpen = true;
    }

    // ================================================================ modifier les points

    void EditPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { } s || SelPage is not { } page) return;
        var view = _views.FirstOrDefault(v => v.Page == page);
        if (view is null) return;
        if (view.PointEditShape is not null) { view.EndPointEdit(); return; }
        if (!view.BeginPointEdit(s))
        {
            EditPointsToggle.IsChecked = false;
            StatusText.Text = "Les points se modifient sur une forme ou un tracé (pas sur une zone de texte, une image ou un WordArt).";
            return;
        }
        // la conversion en trace peut avoir recree l'objet : la vue garde la reference a jour
        view.Focus();
    }

    // ================================================================ WordArt

    /// <summary>Styles de WordArt proposes a l'insertion.</summary>
    internal static readonly (string Label, Func<Shape, Shape> Style)[] WordArtStyles =
    {
        ("Bleu dégradé, ombre", s => { s.Fill = Grad("linear", 90, (0, "#8fc1ec"), (1, "#1f4e79")); s.Shadow = new ShadowFx { Opacity = 0.3, Distance = 3, Blur = 3 }; return s; }),
        ("Jaune, contour orange", s => { s.Fill = new Fill { Color = "#ffc000" }; s.Line = new LineStyle { Color = "#c55a11", Weight = 1.5 }; return s; }),
        ("Arc-en-ciel", s => { s.Fill = Grad("linear", 0, (0, "#e81416"), (0.2, "#ffa500"), (0.4, "#faeb36"), (0.6, "#79c314"), (0.8, "#487de7"), (1, "#70369d")); s.Line = new LineStyle { Color = "#333333", Weight = 0.5 }; return s; }),
        ("Rouge, ombre portée", s => { s.Fill = new Fill { Color = "#d13438" }; s.Shadow = new ShadowFx { Opacity = 0.45, Distance = 4, Blur = 2 }; return s; }),
        ("Bleu nuit, lueur dorée", s => { s.Fill = new Fill { Color = "#1f2a44" }; s.Effects = new EffectsFx { GlowColor = "#ffd23f", GlowSize = 8, GlowOpacity = 0.7 }; return s; }),
        ("Contour seul", s => { s.Fill = null; s.Line = new LineStyle { Color = "#7030a0", Weight = 1.5 }; return s; }),
    };

    void WordArtMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        for (int i = 0; i < WordArtStyles.Length; i++)
        {
            int k = i;
            var sample = new Shape { Kind = ShapeKind.WordArt, W = 120, H = 30, WordArt = new WordArtFx { Text = "Abc", Family = "Arial Black" } };
            WordArtStyles[i].Style(sample);
            var icon = DecorRenderer.WordArtGeometry(sample, _session) is { } g
                ? new System.Windows.Shapes.Path { Data = g, Fill = DecorRenderer.FillBrush(sample.Fill, null), Stroke = sample.Line is { } l ? PageRenderer.Brush(l.Color) : null, StrokeThickness = 0.6, Stretch = Stretch.Uniform, Width = 40, Height = 16 }
                : null;
            menu.Items.Add(Item(WordArtStyles[i].Label, () => InsertWordArt(k), icon));
        }
        menu.IsOpen = true;
    }

    /// <summary>Insere un WordArt (style choisi) au centre de la page active et ouvre sa fenetre de texte.</summary>
    internal Shape? InsertWordArt(int style, string? text = null, bool edit = true)
    {
        if (_session is null || ActivePage() is not { } page) return null;
        var s = new Shape
        {
            Kind = ShapeKind.WordArt, X = page.Width / 2 - 160, Y = page.Height / 2 - 30, W = 320, H = 60,
            WordArt = new WordArtFx { Text = text ?? "Votre texte ici", Family = "Arial Black" },
        };
        WordArtStyles[Math.Clamp(style, 0, WordArtStyles.Length - 1)].Style(s);
        // proportions naturelles du texte (largeur bornee a la page)
        s.W = Math.Min(page.Width * 0.85, DecorRenderer.NaturalWidth(s.WordArt, _session, s.H));
        s.X = page.Width / 2 - s.W / 2;
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
        if (edit) EditWordArt(s);
        return s;
    }

    void EditWordArt(Shape s)
    {
        if (_session is null || s.WordArt is null || _session.PageOf(s) is not { } page) return;
        var dlg = new WordArtWindow(s, _session) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is not { } wa) return;
        var before = s.WordArt;
        _session.Change(page, () =>
        {
            ApplyWordArt(s, wa);
            // texte droit ou en vague dont le texte change : le cadre garde sa hauteur et reprend les proportions du texte
            if (wa.Warp is WordArtWarps.None or WordArtWarps.Wave && (before?.Text != wa.Text || before?.Family != wa.Family || before?.Bold != wa.Bold))
            {
                double cx = s.X + s.W / 2, w = Math.Min(page.Width * 0.95, DecorRenderer.NaturalWidth(wa, _session, s.H));
                s.X = cx - w / 2; s.W = w;
            }
        });
        RefreshProperties();
    }

    /// <summary>Nouveau texte ou nouvelle deformation : le cadre prend des proportions adaptees si la deformation change.</summary>
    static void ApplyWordArt(Shape s, WordArtFx wa)
    {
        var old = s.WordArt?.Warp;
        s.WordArt = wa.Clone();
        if (old == wa.Warp) return;
        double cx = s.X + s.W / 2, cy = s.Y + s.H / 2;
        (double w, double h) = wa.Warp switch
        {
            WordArtWarps.Circle => (Math.Max(s.W, s.H) * 0.7, Math.Max(s.W, s.H) * 0.7),
            WordArtWarps.ArcUp or WordArtWarps.ArcDown => (s.W, Math.Max(s.H, s.W * 0.45)),
            _ => (s.W, Math.Min(s.H, s.W * 0.3)),
        };
        s.X = cx - w / 2; s.Y = cy - h / 2; s.W = w; s.H = h;
    }

    void WordArtEdit_Click(object sender, RoutedEventArgs e) { if (Sel is { IsWordArt: true } s) EditWordArt(s); }

    void WarpMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { IsWordArt: true } s) return;
        var menu = NewMenu(sender);
        foreach (var (id, label) in WordArtWarps.All)
        {
            var w = id;
            menu.Items.Add(Item(label, () =>
            {
                if (SelPage is not { } page || _session is null) return;
                var wa = s.WordArt!.Clone();
                wa.Warp = w;
                _session.Change(page, () => ApplyWordArt(s, wa));
                RefreshProperties();
            }, check: s.WordArt?.Warp == id));
        }
        menu.IsOpen = true;
    }

    // ================================================================ espaces reserves, permutation, legende

    /// <summary>Insertion ▸ Espace réservé : cadre d'image vide, a remplir plus tard (clic sur son bouton).</summary>
    internal Shape? AddPlaceholder(Page? page = null)
    {
        if (_session is null || (page ?? ActivePage()) is not { } pg) return null;
        var s = new Shape { Kind = ShapeKind.Image, X = pg.Width / 2 - 100, Y = pg.Height / 2 - 75, W = 200, H = 150 };
        _session.Change(pg, () => { pg.Shapes.Add(s); Renumber(pg); });
        _session.Select(s, pg);
        return s;
    }

    void AddPlaceholder_Click(object sender, RoutedEventArgs e) => AddPlaceholder();

    /// <summary>Remplit un espace reserve (ou remplace l'image) : l'image remplit le cadre sans deformation.</summary>
    internal void FillPlaceholder(Shape s, string? file = null)
    {
        if (_session is null || _session.PageOf(s) is not { } page) return;
        file ??= PickImage();
        if (file is null) return;
        var src = _session.Package.AddAsset(file);
        var bmp = _session.Images.Get(src);
        _session.Change(page, () =>
        {
            s.Src = src;
            s.Mime = null;
            if (bmp is not null) s.ImageFx = ImageFit.WithCover(s.ImageFx, bmp.PixelWidth, bmp.PixelHeight, s.W, s.H);
        });
        _session.Select(s, page);
    }

    /// <summary>Outils image ▸ Vider le cadre : l'image redevient un espace reserve (meme cadre).</summary>
    void ClearImage_Click(object sender, RoutedEventArgs e) =>
        ChangeSel(x => { x.Src = null; x.Mime = null; if (x.ImageFx is { } fx) { fx.CropLeft = fx.CropTop = fx.CropRight = fx.CropBottom = 0; if (fx.IsEmpty) x.ImageFx = null; } }, x => x.IsImage);

    /// <summary>Echange les images de deux cadres selectionnes ; chaque cadre garde sa place, sa taille et sa forme.</summary>
    internal bool SwapImages()
    {
        if (_session is null || SelPage is not { } page) return false;
        var pics = _session.Selection.Where(x => x.IsImage).ToList();
        if (pics.Count != 2) { StatusText.Text = "Sélectionnez exactement deux images (Ctrl + clic) pour les permuter."; return false; }
        var (a, b) = (pics[0], pics[1]);
        _session.Change(page, () =>
        {
            (a.Src, b.Src) = (b.Src, a.Src);
            (a.Mime, b.Mime) = (b.Mime, a.Mime);
            foreach (var x in new[] { a, b })
            {
                if (_session.Images.Get(x.Src) is { } bmp) x.ImageFx = ImageFit.WithCover(x.ImageFx, bmp.PixelWidth, bmp.PixelHeight, x.W, x.H);
                else if (x.ImageFx is { } fx) { fx.CropLeft = fx.CropTop = fx.CropRight = fx.CropBottom = 0; if (fx.IsEmpty) x.ImageFx = null; }
            }
        });
        StatusText.Text = "Images permutées.";
        return true;
    }

    void SwapImages_Click(object sender, RoutedEventArgs e) => SwapImages();

    /// <summary>
    /// Legende d'image : zone de texte sous l'image, groupee avec elle (le texte de la page contourne alors le groupe).
    /// Sans image selectionnee, un espace reserve est cree avec sa legende.
    /// </summary>
    internal Shape? AddCaption()
    {
        if (_session is null) return null;
        var page = SelPage ?? ActivePage();
        if (page is null) return null;
        var img = Sel is { IsImage: true } sel && _session.Selection.Count == 1 ? sel : null;
        var shapes = new List<Shape>();
        if (img is null)
        {
            img = new Shape { Kind = ShapeKind.Image, X = page.Width / 2 - 100, Y = page.Height / 2 - 90, W = 200, H = 150 };
            shapes.Add(img);
        }
        var cap = new Shape
        {
            Kind = ShapeKind.Text, X = img.X, Y = img.Y + img.H + 3, W = img.W, H = 22, Padding = new[] { 2.0, 2.85, 2.0, 2.85 }, AutoFit = "grow",
            Paragraphs = new List<Paragraph>
            {
                new() { Align = "center", Runs = { new Run { Text = "Légende de l'image", Font = new FontSpec { Family = "Arial", Size = 9, Italic = true, Color = "#404040", Lang = "fr" } } } },
            },
        };
        shapes.Add(cap);
        _session.Change(page, () =>
        {
            page.Shapes.AddRange(shapes);
            ShapeOps.Group(new List<Shape> { img, cap });
            Renumber(page);
        });
        _session.SelectMany(new[] { img, cap }, cap, page);
        StatusText.Text = "Légende ajoutée et groupée avec l'image (double-clic sur la légende pour l'écrire).";
        return cap;
    }

    void AddCaption_Click(object sender, RoutedEventArgs e) => AddCaption();

    // ================================================================ compression des images

    /// <summary>
    /// Reduit a 300 ppp les images plus grandes que necessaire (d'apres leur plus grande taille sur la page). Les
    /// nouvelles images remplacent les anciennes dans le document (annulable jusqu'a l'enregistrement, qui retire les
    /// anciennes du paquet). Renvoie le nombre d'images reduites.
    /// </summary>
    internal int CompressImages(bool ask = true, double dpi = 300)
    {
        if (_session is null) return 0;
        var plan = ImageCompression.Plan(ImageCompression.Uses(_session.Doc), src => DecorRenderer.PixelSize(_session.Images, src), dpi);
        if (plan.Count == 0)
        {
            if (ask) MessageBox.Show(this, $"Toutes les images sont déjà à la bonne résolution ({dpi:0} ppp ou moins).", "Compresser les images", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
        long before = plan.Sum(t => FileSize(_session.Package.ResolveAsset(t.Src)));
        if (ask && MessageBox.Show(this,
                $"{plan.Count} image(s) sont plus grandes que nécessaire pour une impression à {dpi:0} ppp ({before / 1024.0 / 1024:0.0} Mo).\n\n" +
                "Les réduire pour alléger le fichier ? L'aspect à l'écran et à l'impression ne change pas.",
                "Compresser les images", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 0;
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long after = 0;
        foreach (var t in plan)
        {
            if (_session.Images.Get(t.Src) is not { } bmp) continue;
            var scaled = new TransformedBitmap(bmp, new ScaleTransform((double)t.NewWidth / bmp.PixelWidth, (double)t.NewHeight / bmp.PixelHeight));
            var ext = Path.GetExtension(t.Src).ToLowerInvariant();
            bool jpeg = ext is ".jpg" or ".jpeg" || (ext is not ".png" and not ".gif" && !HasAlpha(bmp));
            BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 88 } : new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            enc.Save(ms);
            var name = Path.GetFileNameWithoutExtension(t.Src) + $"-{dpi:0}ppp" + (jpeg ? ".jpg" : ".png");
            renamed[t.Src] = _session.Package.AddAsset(ms.ToArray(), name);
            after += ms.Length;
        }
        if (renamed.Count == 0) return 0;
        _session.BeginChange();
        foreach (var s in _session.Doc.AllShapes())
        {
            if (s.Src is { } src && renamed.TryGetValue(src, out var ns)) { s.Src = ns; s.Mime = null; }
            if (s.Fill?.Image is { } fi && renamed.TryGetValue(fi, out var nf)) s.Fill.Image = nf;
        }
        _session.EndChange();
        foreach (var p in _session.Doc.Pages) _session.Invalidate(p);
        StatusText.Text = $"{renamed.Count} image(s) compressée(s) : {before / 1024.0 / 1024:0.0} Mo → {after / 1024.0 / 1024:0.0} Mo (effectif à l'enregistrement).";
        RefreshProperties();
        return renamed.Count;
    }

    static long FileSize(string path) { try { return new FileInfo(path).Length; } catch (IOException) { return 0; } }

    static bool HasAlpha(BitmapSource b) =>
        b.Format == PixelFormats.Bgra32 || b.Format == PixelFormats.Pbgra32 || b.Format == PixelFormats.Prgba64 || b.Format == PixelFormats.Rgba64;

    void CompressImages_Click(object sender, RoutedEventArgs e) => CompressImages();
}
