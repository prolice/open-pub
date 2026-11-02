using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.App.Rendering;

/// <summary>
/// Rendu du lot 9 (formes, effets et texte decoratif) : remplissages en degrade, motif ou image ; lueur, bords
/// adoucis, ombre interieure ; texte WordArt ; bordures decoratives ; espaces reserves pour images. Appele par
/// <see cref="PageRenderer"/> ; coordonnees de page, dans le repere (tourne) de l'objet.
/// </summary>
public static class DecorRenderer
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ---------------------------------------------------------------- remplissages

    /// <summary>Pinceau de remplissage d'un objet (couleur unie, degrade, motif ou image) ; null = pas de remplissage.</summary>
    public static Brush? FillBrush(Fill? f, ImageCache? images)
    {
        if (f is null) return null;
        double op = Math.Clamp(f.Opacity ?? 1, 0, 1);
        if (f.Image is { Length: > 0 } src && images?.Get(src) is { } bmp)
        {
            var ib = new ImageBrush(bmp) { Opacity = op };
            if (f.ImageTile == true)
            {
                // carreaux a 150 ppp
                ib.TileMode = TileMode.Tile;
                ib.Stretch = Stretch.Fill;
                ib.ViewportUnits = BrushMappingMode.Absolute;
                ib.Viewport = new Rect(0, 0, Math.Max(2, bmp.PixelWidth * 72.0 / 150), Math.Max(2, bmp.PixelHeight * 72.0 / 150));
            }
            else ib.Stretch = Stretch.UniformToFill;
            ib.Freeze();
            return ib;
        }
        if (f.HasGradient) return Gradient(f, op);
        if (f.Pattern is { } pat) return Pattern(pat, f.Color, f.PatternColor ?? "#000000", op);
        return f.Color is { } c ? PageRenderer.Brush(c, op) : null;
    }

    static Color ColorOf(string? hex, double opacity = 1)
    {
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex ?? "#000000"); }
        catch (FormatException) { c = Colors.Black; }
        c.A = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        return c;
    }

    static Brush Gradient(Fill f, double opacity)
    {
        var stops = new GradientStopCollection();
        foreach (var s in f.Stops!.OrderBy(s => s.Pos)) stops.Add(new GradientStop(ColorOf(s.Color, s.Opacity ?? 1), Math.Clamp(s.Pos, 0, 1)));
        GradientBrush b;
        if (f.IsRadial) b = new RadialGradientBrush(stops) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.7, RadiusY = 0.7 };
        else
        {
            double a = (f.Angle ?? 90) * Math.PI / 180, cx = Math.Cos(a) / 2, cy = Math.Sin(a) / 2;
            b = new LinearGradientBrush(stops, new Point(0.5 - cx, 0.5 - cy), new Point(0.5 + cx, 0.5 + cy));
        }
        b.Opacity = opacity;
        b.Freeze();
        return b;
    }

    static readonly Dictionary<string, Brush> Patterns = new();

    /// <summary>Motif en carreaux de 8 points (motif en <paramref name="fg"/> sur le fond <paramref name="bg"/>).</summary>
    public static Brush Pattern(string id, string? bg, string fg, double opacity = 1)
    {
        var key = FormattableString.Invariant($"{id}|{bg}|{fg}|{opacity:0.###}");
        if (Patterns.TryGetValue(key, out var cached)) return cached;
        var ink = PageRenderer.Brush(fg);
        var pen = new Pen(ink, 1.1) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
        var thin = new Pen(ink, 0.8);
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(bg is null ? Brushes.Transparent : PageRenderer.Brush(bg), null, new Rect(0, 0, 8, 8));
            switch (id)
            {
                case "dots": dc.DrawEllipse(ink, null, new Point(4, 4), 1.7, 1.7); break;
                case "smallDots": dc.DrawEllipse(ink, null, new Point(2, 2), 0.8, 0.8); dc.DrawEllipse(ink, null, new Point(6, 6), 0.8, 0.8); break;
                case "horizontal": dc.DrawRectangle(ink, null, new Rect(0, 3.3, 8, 1.4)); break;
                case "vertical": dc.DrawRectangle(ink, null, new Rect(3.3, 0, 1.4, 8)); break;
                case "diagUp":
                    dc.DrawLine(pen, new Point(0, 8), new Point(8, 0)); dc.DrawLine(pen, new Point(-1, 1), new Point(1, -1)); dc.DrawLine(pen, new Point(7, 9), new Point(9, 7));
                    break;
                case "diagDown":
                    dc.DrawLine(pen, new Point(0, 0), new Point(8, 8)); dc.DrawLine(pen, new Point(-1, 7), new Point(1, 9)); dc.DrawLine(pen, new Point(7, -1), new Point(9, 1));
                    break;
                case "grid": dc.DrawRectangle(ink, null, new Rect(0, 0, 8, 0.8)); dc.DrawRectangle(ink, null, new Rect(0, 0, 0.8, 8)); break;
                case "diagGrid":
                    dc.DrawLine(thin, new Point(0, 8), new Point(8, 0)); dc.DrawLine(thin, new Point(0, 0), new Point(8, 8));
                    break;
                case "checker": dc.DrawRectangle(ink, null, new Rect(0, 0, 4, 4)); dc.DrawRectangle(ink, null, new Rect(4, 4, 4, 4)); break;
                case "zigzag":
                {
                    var z = new StreamGeometry();
                    using (var c = z.Open()) { c.BeginFigure(new Point(0, 6), false, false); c.PolyLineTo(new[] { new Point(2, 2), new Point(4, 6), new Point(6, 2), new Point(8, 6) }, true, true); }
                    dc.DrawGeometry(null, thin, z);
                    break;
                }
                case "bricks":
                    dc.DrawRectangle(ink, null, new Rect(0, 0, 8, 0.7)); dc.DrawRectangle(ink, null, new Rect(0, 4, 8, 0.7));
                    dc.DrawRectangle(ink, null, new Rect(0, 0, 0.7, 4)); dc.DrawRectangle(ink, null, new Rect(4, 4, 0.7, 4));
                    break;
                case "waves":
                {
                    var wv = new StreamGeometry();
                    using (var c = wv.Open()) { c.BeginFigure(new Point(0, 4), false, false); c.BezierTo(new Point(2, 1.5), new Point(2, 1.5), new Point(4, 4), true, true); c.BezierTo(new Point(6, 6.5), new Point(6, 6.5), new Point(8, 4), true, true); }
                    dc.DrawGeometry(null, thin, wv);
                    break;
                }
            }
        }
        var brush = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 8, 8),
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 8, 8), Opacity = opacity,
        };
        brush.Freeze();
        if (Patterns.Count > 200) Patterns.Clear();
        Patterns[key] = brush;
        return brush;
    }

    /// <summary>Contour d'un objet (avec sa transparence).</summary>
    public static Pen? PenOf(LineStyle? l)
    {
        if (l is null) return null;
        var pen = new Pen(PageRenderer.Brush(l.Color, l.Opacity ?? 1), Math.Max(l.Weight, 0.25));
        if (l.Dash) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return pen;
    }

    // ---------------------------------------------------------------- effets

    /// <summary>
    /// Corps d'une forme avec ses effets : lueur (dessous), bords adoucis (masque d'opacite), ombre interieure (dessus).
    /// L'ombre portee est dessinee avant, par l'appelant.
    /// </summary>
    public static void DrawBody(DrawingContext dc, Shape s, Geometry g, Brush? fill, Pen? pen)
    {
        var silhouette = fill is not null || pen is null ? g : g.GetWidenedPathGeometry(pen);
        Glow(dc, s, silhouette);
        bool soft = PushSoftEdge(dc, s, silhouette);
        dc.DrawGeometry(fill, pen, g);
        if (soft) dc.Pop();
        InnerShadow(dc, s, g);
    }

    /// <summary>Lueur : silhouette elargie et floutee, de la couleur choisie, sous l'objet.</summary>
    public static void Glow(DrawingContext dc, Shape s, Geometry silhouette)
    {
        if (s.Effects is not { HasGlow: true } fx) return;
        var b = silhouette.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return;
        var grown = Geometry.Combine(silhouette, silhouette.GetWidenedPathGeometry(new Pen(Brushes.Black, fx.GlowSize)), GeometryCombineMode.Union, null);
        var shadow = new ShadowFx { Color = fx.GlowColor!, Opacity = fx.GlowOpacity, Blur = Math.Max(1, fx.GlowSize / 2), Distance = 0 };
        var bmp = ImageCache.Shadow(grown, grown.Bounds, shadow, out var rect);
        dc.DrawImage(bmp, rect);
    }

    /// <summary>Bords adoucis : masque d'opacite (silhouette retrecie puis floutee). Renvoie vrai s'il faut un Pop.</summary>
    public static bool PushSoftEdge(DrawingContext dc, Shape s, Geometry silhouette)
    {
        if (s.Effects is not { SoftEdge: > 0 } fx) return false;
        var b = silhouette.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return false;
        double r = Math.Min(fx.SoftEdge, Math.Min(b.Width, b.Height) / 2);
        var shrunk = Geometry.Combine(silhouette, silhouette.GetWidenedPathGeometry(new Pen(Brushes.Black, r)), GeometryCombineMode.Exclude, null);
        var bmp = ImageCache.Shadow(shrunk, b, new ShadowFx { Color = "#000000", Opacity = 1, Blur = r / 2 }, out var rect);
        var mask = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = rect, Stretch = Stretch.Fill };
        mask.Freeze();
        dc.PushOpacityMask(mask);
        return true;
    }

    /// <summary>Ombre interieure : l'exterieur de la silhouette, flou et decale, vu a travers la silhouette.</summary>
    public static void InnerShadow(DrawingContext dc, Shape s, Geometry silhouette)
    {
        if (s.Effects?.InnerShadow is not { Opacity: > 0 } fx) return;
        var b = silhouette.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return;
        double pad = fx.Blur * 2 + fx.Distance + 2;
        var outer = new RectangleGeometry(new Rect(b.X - pad, b.Y - pad, b.Width + 2 * pad, b.Height + 2 * pad));
        var inverse = Geometry.Combine(outer, silhouette, GeometryCombineMode.Exclude, null);
        var bmp = ImageCache.Shadow(inverse, inverse.Bounds, fx, out var rect);
        double a = (fx.Angle - s.Rotation) * Math.PI / 180;
        rect.Offset(Math.Cos(a) * fx.Distance, Math.Sin(a) * fx.Distance);
        dc.PushClip(silhouette);
        dc.DrawImage(bmp, rect);
        dc.Pop();
    }

    // ---------------------------------------------------------------- objets particuliers

    /// <summary>
    /// Dessine les objets propres au lot 9 (WordArt, espace reserve pour image) ; renvoie faux pour les autres objets,
    /// que <see cref="PageRenderer"/> dessine normalement.
    /// </summary>
    public static bool DrawSpecial(DrawingContext dc, Shape s, EditorSession session, bool marks)
    {
        if (s.IsWordArt) { DrawWordArt(dc, s, session); return true; }
        if (s.IsPlaceholder) { DrawPlaceholder(dc, s, marks); return true; }
        return false;
    }

    /// <summary>Bouton « insérer une image » d'un espace reserve (repere de l'objet).</summary>
    public static Rect PlaceholderButton(Shape s)
    {
        double r = Math.Clamp(Math.Min(s.W, s.H) / 4, 6, 18);
        return new Rect(s.X + s.W / 2 - r, s.Y + s.H / 2 - r, 2 * r, 2 * r);
    }

    static void DrawPlaceholder(DrawingContext dc, Shape s, bool marks)
    {
        var rect = new Rect(s.X, s.Y, Math.Max(s.W, 0.1), Math.Max(s.H, 0.1));
        var clip = PageRenderer.ImageClip(s);
        // a l'impression et a l'export, un espace reserve vide n'apparait pas (seul son contour, s'il en a un)
        if (marks)
        {
            dc.DrawGeometry(PageRenderer.Brush("#eef2f8"), new Pen(PageRenderer.Brush("#8aa0c0"), 0.8) { DashStyle = DashStyles.Dash }, clip);
            var btn = PlaceholderButton(s);
            dc.DrawEllipse(PageRenderer.Brush("#ffffff"), new Pen(PageRenderer.Brush("#8aa0c0"), 0.8), new Point(btn.X + btn.Width / 2, btn.Y + btn.Height / 2), btn.Width / 2, btn.Height / 2);
            var icon = new FormattedText("\uEB9F", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe Fluent Icons, Segoe MDL2 Assets"), btn.Width * 0.5, PageRenderer.Brush("#4f6b94"), 1);
            dc.DrawText(icon, new Point(btn.X + (btn.Width - icon.Width) / 2, btn.Y + (btn.Height - icon.Height) / 2));
            if (s.H > btn.Height + 26 && s.W > 90)
            {
                var label = new FormattedText("Cliquez pour insérer une image", Fr, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                    Math.Min(9, s.W / 18), PageRenderer.Brush("#4f6b94"), 1) { TextAlignment = TextAlignment.Center, MaxTextWidth = s.W - 8 };
                dc.DrawText(label, new Point(s.X + 4, btn.Bottom + 4));
            }
        }
        if (PenOf(s.Line) is { } pen) dc.DrawGeometry(null, pen, clip);
        _ = rect;
    }

    static readonly Dictionary<string, Geometry> WordArtCache = new();

    /// <summary>Contour du texte WordArt (glyphes places dans le cadre) ; null si le texte est vide.</summary>
    public static Geometry? WordArtGeometry(Shape s, EditorSession session)
    {
        if (s.WordArt is not { } wa || string.IsNullOrWhiteSpace(wa.Text)) return null;
        var key = FormattableString.Invariant($"{wa.Text}|{wa.Family}|{wa.Bold}|{wa.Italic}|{wa.Warp}|{s.X:0.##}|{s.Y:0.##}|{s.W:0.##}|{s.H:0.##}");
        if (WordArtCache.TryGetValue(key, out var cached)) return cached;
        var tf = session.Fonts.Typeface(new FontSpec { Family = wa.Family, Bold = wa.Bold, Italic = wa.Italic });
        const double em = 100;
        var glyphs = new List<(Geometry Geo, double Adv)>();
        var it = StringInfo.GetTextElementEnumerator(wa.Text.Replace("\r", "").Replace('\n', ' '));
        double top = 0, bottom = 0;
        while (it.MoveNext())
        {
            var el = (string)it.Current;
            var ft = new FormattedText(el, Fr, FlowDirection.LeftToRight, tf, em, Brushes.Black, 1);
            var geo = ft.BuildGeometry(new Point(0, -ft.Baseline));
            if (!geo.Bounds.IsEmpty) { top = Math.Min(top, geo.Bounds.Top); bottom = Math.Max(bottom, geo.Bounds.Bottom); }
            glyphs.Add((geo, ft.WidthIncludingTrailingWhitespace / em));
        }
        if (glyphs.Count == 0 || top >= 0) return null;
        // hauteur de l'encre (et non de l'interligne) : le texte remplit exactement le cadre
        double ascent = -top / em, descent = Math.Max(0.02, bottom / em);
        var layout = TextOnPath.Layout(glyphs.Select(g => g.Adv).ToList(), ascent, descent, wa.Warp, Math.Max(s.W, 1), Math.Max(s.H, 1));
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        double k = layout.Size / em;
        for (int i = 0; i < glyphs.Count && i < layout.Glyphs.Count; i++)
        {
            var (geo, adv) = glyphs[i];
            if (geo.Bounds.IsEmpty) continue;
            var p = layout.Glyphs[i];
            var tr = new TransformGroup();
            tr.Children.Add(new TranslateTransform(-adv * em / 2, 0));
            tr.Children.Add(new ScaleTransform(k * p.ScaleX, k * p.ScaleY));
            tr.Children.Add(new RotateTransform(p.AngleDeg));
            tr.Children.Add(new TranslateTransform(s.X + p.X, s.Y + p.Y));
            var g = geo.Clone();
            g.Transform = tr;
            group.Children.Add(g);
        }
        // un seul trace (et non un groupe) : sa description textuelle sert de cle au cache des ombres
        var path = PathGeometry.CreateFromGeometry(group);
        path.FillRule = FillRule.Nonzero;
        path.Freeze();
        if (WordArtCache.Count > 300) WordArtCache.Clear();
        WordArtCache[key] = path;
        return path;
    }

    /// <summary>
    /// Largeur pour laquelle le texte droit (ou en vague) n'est ni etire ni comprime a la hauteur donnee : le cadre
    /// d'un nouveau WordArt prend les proportions naturelles du texte.
    /// </summary>
    public static double NaturalWidth(WordArtFx wa, EditorSession session, double h)
    {
        var tf = session.Fonts.Typeface(new FontSpec { Family = wa.Family, Bold = wa.Bold, Italic = wa.Italic });
        var ft = new FormattedText(wa.Text.Replace("\r", "").Replace('\n', ' '), Fr, FlowDirection.LeftToRight, tf, 100, Brushes.Black, 1);
        var ink = ft.BuildGeometry(new Point(0, 0)).Bounds;
        if (ink.IsEmpty || ink.Height <= 0) return h * 4;
        double k = wa.Warp == WordArtWarps.Wave ? 0.55 : 1;
        return Math.Max(h, ft.WidthIncludingTrailingWhitespace * (h * k / ink.Height) * (wa.Warp == WordArtWarps.Wave ? 1.15 : 1));
    }

    static void DrawWordArt(DrawingContext dc, Shape s, EditorSession session)
    {
        var g = WordArtGeometry(s, session);
        if (g is null) return;
        var fill = FillBrush(s.Fill, session.Images);
        var pen = PenOf(s.Line);
        if (pen is not null) { pen = pen.Clone(); pen.LineJoin = PenLineJoin.Round; pen.Freeze(); }
        if (fill is null && pen is null) fill = PageRenderer.Brush("#000000");
        PageRenderer.DrawShadow(dc, s, fill is not null ? g : g.GetWidenedPathGeometry(pen!));
        DrawBody(dc, s, g, fill, pen);
    }

    // ---------------------------------------------------------------- bordures decoratives

    static readonly Dictionary<string, Geometry> Motifs = new();

    /// <summary>Bordure decorative : motif repete le long du cadre, dessine par-dessus l'objet.</summary>
    public static void DrawBorderArt(DrawingContext dc, Shape s)
    {
        if (s.BorderArt is not { Size: > 0.5 } ba) return;
        var key = FormattableString.Invariant($"{ba.Pattern}|{ba.Size:0.##}");
        if (!Motifs.TryGetValue(key, out var motif))
        {
            motif = PageRenderer.PathGeometry(BorderArtLayout.Motif(ba.Pattern, new Box(-ba.Size / 2, -ba.Size / 2, ba.Size, ba.Size)));
            Motifs[key] = motif;
        }
        var b1 = PageRenderer.Brush(ba.Color);
        var b2 = ba.Color2 is { } c2 ? PageRenderer.Brush(c2) : b1;
        foreach (var t in BorderArtLayout.Tiles(s.W, s.H, ba.Size))
        {
            // damier d'une seule couleur : une case sur deux reste vide
            if (ba.Pattern == "checker" && ba.Color2 is null && t.Index % 2 == 1) continue;
            dc.PushTransform(new TranslateTransform(s.X + t.X, s.Y + t.Y));
            dc.PushTransform(new RotateTransform(t.AngleDeg));
            dc.DrawGeometry(t.Index % 2 == 0 ? b1 : b2, null, motif);
            dc.Pop(); dc.Pop();
        }
    }

    /// <summary>Vignette d'un motif de bordure (menus).</summary>
    public static System.Windows.Shapes.Path MotifIcon(string pattern, Brush fill, double size = 16) => new()
    {
        Data = PageRenderer.PathGeometry(BorderArtLayout.Motif(pattern, new Box(0, 0, 16, 16))), Fill = fill,
        Stretch = Stretch.Uniform, Width = size, Height = size,
    };

    /// <summary>Vignette d'un remplissage (menus, fenetre de remplissage).</summary>
    public static System.Windows.Shapes.Rectangle Swatch(Brush? b, double w = 28, double h = 18) => new()
    {
        Width = w, Height = h, Fill = b ?? Brushes.Transparent, Stroke = PageRenderer.Brush("#9aa3b2"), StrokeThickness = 0.8,
    };

    /// <summary>Image du paquet sous forme de bitmap (taille en pixels).</summary>
    public static (int W, int H)? PixelSize(ImageCache images, string src) =>
        images.Get(src) is BitmapSource b ? (b.PixelWidth, b.PixelHeight) : null;
}
