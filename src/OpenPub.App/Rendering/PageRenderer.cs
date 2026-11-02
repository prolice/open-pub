using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenPub.Core.Model;
using PathSegment = OpenPub.Core.Model.PathSegment;

namespace OpenPub.App.Rendering;

/// <summary>Dessine une page en coordonnees de page (points). L'appelant applique l'echelle.</summary>
public static class PageRenderer
{
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    static readonly Dictionary<string, SolidColorBrush> Brushes = new();

    public static SolidColorBrush Brush(string? hex, double opacity = 1)
    {
        hex ??= "#000000";
        var key = hex + "|" + opacity.ToString(CultureInfo.InvariantCulture);
        if (Brushes.TryGetValue(key, out var b)) return b;
        Color c;
        try { c = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { c = Colors.Black; }
        b = new SolidColorBrush(c) { Opacity = Math.Clamp(opacity, 0, 1) };
        b.Freeze();
        Brushes[key] = b;
        return b;
    }

    public static void DrawPage(DrawingContext dc, Page page, EditorSession session, double pixelsPerDip, bool editorMarks,
        Action<DrawingContext>? overlay = null)
    {
        // arriere-plan : couleur de la page, sinon de sa page maitre, sinon blanc
        var master = session.Doc.MasterOf(page);
        var back = page.BackColor ?? master?.BackColor;
        dc.DrawRectangle(back is null ? System.Windows.Media.Brushes.White : Brush(back), null, new Rect(0, 0, page.Width, page.Height));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, page.Width, page.Height)));
        // page maitre dessous (objets communs, numero de cette page dans ses champs)
        if (master is not null) DrawShapes(dc, master, session.LayoutMaster(master, page), session, pixelsPerDip, false);
        DrawShapes(dc, page, session.Layout(page), session, pixelsPerDip, editorMarks);
        overlay?.Invoke(dc);       // ex. surbrillance de la selection de texte (semi-transparente)
        dc.Pop();
    }

    static void DrawShapes(DrawingContext dc, Page page, PageLayoutResult layout, EditorSession session, double pixelsPerDip, bool editorMarks)
    {
        foreach (var s in page.Shapes)
        {
            bool rotated = PushRotation(dc, s);
            switch (s.Kind)
            {
                case ShapeKind.Image: DrawImage(dc, s, session); break;
                case ShapeKind.Text: DrawText(dc, s, layout, session, pixelsPerDip, editorMarks, page); break;
                case ShapeKind.Table when s.Table is not null: DrawTable(dc, s, layout, session, pixelsPerDip, editorMarks, page); break;
                default: DrawShape(dc, s); break;
            }
            if (rotated) dc.Pop();
        }
    }

    /// <summary>Tableau : fond des cellules, texte (zones fantomes de la mise en page), bordures.</summary>
    static void DrawTable(DrawingContext dc, Shape s, PageLayoutResult layout, EditorSession session, double ppd, bool marks, Page page)
    {
        var t = s.Table!;
        var hs = Core.Editing.TableOps.RowHeights(t);
        DrawShadow(dc, s, new RectangleGeometry(new Rect(s.X, s.Y, s.W, s.H)));
        for (int r = 0; r < t.RowCount; r++)
            for (int c = 0; c < t.ColCount; c++)
            {
                var cell = t.Cells[r][c];
                if (cell.Hidden) continue;
                var (x, y, w, h) = Core.Editing.TableOps.CellRect(s, r, c, hs);
                if (cell.Fill?.Color is { } fc) dc.DrawRectangle(Brush(fc, cell.Fill.Opacity ?? 1), null, new Rect(x, y, w, h));
                var frame = Layout.PageLayouter.CellFrame(cell);
                if (layout.Frames.ContainsKey(frame)) DrawText(dc, frame, layout, session, ppd, false, page);
            }
        // reperes des cellules sans bordure (a l'ecran)
        var guide = marks ? new Pen(Brush("#7fa7e0", 0.6), 0.4) { DashStyle = DashStyles.Dot } : null;
        for (int r = 0; r < t.RowCount; r++)
            for (int c = 0; c < t.ColCount; c++)
            {
                var cell = t.Cells[r][c];
                if (cell.Hidden) continue;
                var (x, y, w, h) = Core.Editing.TableOps.CellRect(s, r, c, hs);
                void Edge(LineStyle? own, Point a, Point b)
                {
                    var st = own ?? t.Border;
                    if (st is { Weight: > 0 } && PenOf(st) is { } pen) dc.DrawLine(pen, a, b);
                    else if (guide is not null) dc.DrawLine(guide, a, b);
                }
                Edge(cell.Top, new Point(x, y), new Point(x + w, y));
                Edge(cell.Bottom, new Point(x, y + h), new Point(x + w, y + h));
                Edge(cell.Left, new Point(x, y), new Point(x, y + h));
                Edge(cell.Right, new Point(x + w, y), new Point(x + w, y + h));
            }
    }

    static Pen? PenOf(LineStyle? l)
    {
        if (l is null) return null;
        var pen = new Pen(Brush(l.Color), Math.Max(l.Weight, 0.25));
        if (l.Dash) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        return pen;
    }

    /// <summary>Applique la rotation de l'objet (autour de son centre) ; renvoie vrai s'il faut un Pop.</summary>
    public static bool PushRotation(DrawingContext dc, Shape s)
    {
        if (Math.Abs(s.Rotation % 360) < 0.01) return false;
        dc.PushTransform(new RotateTransform(s.Rotation, s.X + s.W / 2, s.Y + s.H / 2));
        return true;
    }

    public static Geometry? GeometryOf(Shape s)
    {
        if (s.Geometry is { } geo)
            return ShapeGeometries.Build(geo, new Rect(s.X, s.Y, Math.Max(s.W, 0.1), Math.Max(s.H, 0.1)), s.CornerRadius);
        if (s.Kind == ShapeKind.Ellipse)
            return new EllipseGeometry(new Rect(s.X, s.Y, s.W, s.H));
        if (s.Path is { Count: > 0 } path)
            return PathGeometry(path);
        return new RectangleGeometry(new Rect(s.X, s.Y, s.W, s.H));
    }

    public static StreamGeometry PathGeometry(List<PathSegment> path)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            Point start = default;
            foreach (var p in path)
            {
                var pt = new Point(p.X ?? 0, p.Y ?? 0);
                switch (p.A)
                {
                    case "M":
                        ctx.BeginFigure(pt, isFilled: true, isClosed: false);
                        start = pt; open = true;
                        break;
                    case "Z":
                        if (open) { ctx.LineTo(start, true, false); }
                        break;
                    case "C":
                        if (!open) { ctx.BeginFigure(pt, true, false); open = true; }
                        ctx.BezierTo(new Point(p.X1 ?? 0, p.Y1 ?? 0), new Point(p.X2 ?? 0, p.Y2 ?? 0), pt, true, true);
                        break;
                    case "Q":
                        if (!open) { ctx.BeginFigure(pt, true, false); open = true; }
                        ctx.QuadraticBezierTo(new Point(p.X1 ?? 0, p.Y1 ?? 0), pt, true, true);
                        break;
                    case "A":
                        if (!open) { ctx.BeginFigure(pt, true, false); open = true; }
                        ctx.ArcTo(pt, new Size(p.Rx ?? 0, p.Ry ?? 0), p.Rot ?? 0, (p.Large ?? 0) == 1,
                            (p.Sweep ?? 0) == 1 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
                        break;
                    default:
                        if (p.X is null) break;
                        if (!open) { ctx.BeginFigure(pt, true, false); open = true; start = pt; }
                        else ctx.LineTo(pt, true, false);
                        break;
                }
            }
        }
        g.Freeze();
        return g;
    }

    static void DrawShape(DrawingContext dc, Shape s)
    {
        var fill = s.Fill?.Color is { } c ? Brush(c, s.Fill.Opacity ?? 1) : null;
        var pen = PenOf(s.Line);
        if (fill is null && pen is null) return;
        var g = GeometryOf(s);
        if (g is null) return;
        if (pen is not null && s.Path is { Count: >= 2 })
        {
            // traces (lignes, main levee) : extremites et angles arrondis
            pen = pen.Clone();
            pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
        }
        DrawShadow(dc, s, fill is not null ? g : g.GetWidenedPathGeometry(pen!));
        dc.DrawGeometry(fill, pen, g);
        if (s.Line is { } l && pen is not null && s.Path is { Count: >= 2 } path && (l.ArrowStart || l.ArrowEnd))
        {
            var brush = Brush(l.Color);
            if (l.ArrowEnd && EndDirection(path, atEnd: true) is var (tipE, fromE)) DrawArrowHead(dc, brush, tipE, fromE, l.Weight);
            if (l.ArrowStart && EndDirection(path, atEnd: false) is var (tipS, fromS)) DrawArrowHead(dc, brush, tipS, fromS, l.Weight);
        }
    }

    /// <summary>Extremite d'un trace ouvert et point qui donne sa direction (point precedent ou point de controle).</summary>
    static (Point Tip, Point From)? EndDirection(List<PathSegment> path, bool atEnd)
    {
        var pts = path.Where(p => p.A != "Z" && p.X is not null).ToList();
        if (pts.Count < 2) return null;
        if (atEnd)
        {
            var last = pts[^1];
            var tip = new Point(last.X!.Value, last.Y!.Value);
            var from = last.A == "C" && last.X2 is not null ? new Point(last.X2.Value, last.Y2!.Value) : new Point(pts[^2].X!.Value, pts[^2].Y!.Value);
            return (tip, from);
        }
        var first = pts[0];
        var second = pts[1];
        var t = new Point(first.X!.Value, first.Y!.Value);
        var f = second.A == "C" && second.X1 is not null ? new Point(second.X1.Value, second.Y1!.Value) : new Point(second.X!.Value, second.Y!.Value);
        return (t, f);
    }

    static void DrawArrowHead(DrawingContext dc, Brush brush, Point tip, Point from, double weight)
    {
        var d = tip - from;
        if (d.Length < 0.01) return;
        d.Normalize();
        double len = 4 + weight * 3, half = 2 + weight * 1.5;
        var back = tip - d * len;
        var n = new Vector(-d.Y, d.X);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(tip, true, true);
            c.LineTo(back + n * half, true, true);
            c.LineTo(back - n * half, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(brush, null, g);
    }

    /// <summary>Ombre portee d'une silhouette (dessinee avant l'objet).</summary>
    static void DrawShadow(DrawingContext dc, Shape s, Geometry silhouette)
    {
        if (s.Shadow is not { } fx || fx.Opacity <= 0) return;
        var bounds = silhouette.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        var bmp = ImageCache.Shadow(silhouette, bounds, fx, out var rect);
        // la direction de l'ombre est fixe sur la page (comme Publisher) : on compense la rotation de l'objet,
        // car l'ombre est dessinee dans son repere tourne
        double a = (fx.Angle - s.Rotation) * Math.PI / 180;
        rect.Offset(Math.Cos(a) * fx.Distance, Math.Sin(a) * fx.Distance);
        dc.DrawImage(bmp, rect);
    }

    /// <summary>Forme de decoupe d'une image : forme automatique, detourage importe ou rectangle.</summary>
    public static Geometry ImageClip(Shape s)
    {
        var rect = new Rect(s.X, s.Y, Math.Max(s.W, 0.1), Math.Max(s.H, 0.1));
        if (s.Geometry is { } geo) return ShapeGeometries.Build(geo, rect, s.CornerRadius);
        if (s.Clip is { Count: > 6 }) return PathGeometry(s.Clip!);
        return new RectangleGeometry(rect);
    }

    public static void DrawImage(DrawingContext dc, Shape s, EditorSession session)
    {
        var rect = new Rect(s.X, s.Y, Math.Max(s.W, 0.1), Math.Max(s.H, 0.1));
        var fx = s.ImageFx;
        var bmp = session.Images.Get(s.Src, fx);
        var clip = ImageClip(s);
        DrawShadow(dc, s, clip);
        DrawImageBody(dc, s, rect, bmp, clip, fx);

        // reflet : image retournee sous l'originale, en degrade vers la transparence
        if (fx is { ReflectionSize: > 0 } && bmp is not null)
        {
            double gap = fx.ReflectionGap, top = rect.Bottom + gap, hh = rect.Height * Math.Min(1, fx.ReflectionSize);
            dc.PushClip(new RectangleGeometry(new Rect(rect.X - 1, top, rect.Width + 2, hh)));
            var mask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, top), EndPoint = new Point(0, top + hh),
                GradientStops = { new GradientStop(Color.FromArgb((byte)(255 * Math.Clamp(fx.ReflectionOpacity, 0, 1)), 0, 0, 0), 0), new GradientStop(Colors.Transparent, 1) },
            };
            dc.PushOpacityMask(mask);
            dc.PushTransform(new ScaleTransform(1, -1, 0, rect.Bottom + gap / 2));
            DrawImageBody(dc, s, rect, bmp, clip, fx, outline: false);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }

    static void DrawImageBody(DrawingContext dc, Shape s, Rect rect, System.Windows.Media.Imaging.BitmapSource? bmp, Geometry clip,
        ImageFx? fx, bool outline = true)
    {
        int pops = 0;
        dc.PushClip(clip); pops++;
        if (fx is { Transparency: > 0 }) { dc.PushOpacity(1 - Math.Clamp(fx.Transparency, 0, 1)); pops++; }
        if (fx is { FlipH: true } || fx is { FlipV: true })
        {
            dc.PushTransform(new ScaleTransform(fx.FlipH ? -1 : 1, fx.FlipV ? -1 : 1, rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            pops++;
        }
        if (bmp is not null) dc.DrawImage(bmp, rect);
        else dc.DrawRectangle(Brush("#e6e9ef"), new Pen(Brush("#9aa3b2"), 0.5), rect);
        for (int i = 0; i < pops; i++) dc.Pop();
        if (outline && PenOf(s.Line) is { } pen) dc.DrawGeometry(null, pen, clip);
    }

    static readonly Pen SpellPen = MakeSpellPen();
    static Pen MakeSpellPen() { var p = new Pen(new SolidColorBrush(Color.FromRgb(0xd1, 0x34, 0x38)), 0.6); p.Freeze(); return p; }

    /// <summary>Soulignement ondule rouge sous les mots inconnus du dictionnaire.</summary>
    static void DrawSpelling(DrawingContext dc, OpenPub.Layout.FrameLayout fl, List<Paragraph> paras, OpenPub.App.Editing.SpellService spell, EditorSession session)
    {
        foreach (var line in fl.Lines)
        {
            if (line.ParagraphIndex >= paras.Count) continue;
            var para = paras[line.ParagraphIndex];
            var errors = spell.Check(para.PlainText, para.Runs.FirstOrDefault(r => r.Text.Length > 0)?.Font?.Lang);
            foreach (var (st, len) in errors)
            {
                int a = st, b = st + len;
                if (b <= line.StartOffset || a >= line.EndOffset) continue;
                foreach (var p in line.Pieces)
                {
                    int pa = Math.Max(a, p.Start), pb = Math.Min(b, p.Start + p.Length);
                    if (pb <= pa) continue;
                    double x1 = p.X + p.Advance(session.Measurer, pa - p.Start), x2 = p.X + p.Advance(session.Measurer, pb - p.Start);
                    double y = line.Baseline + p.Dy + Math.Max(1.2, p.SizePt * 0.12);
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        // vague de 2 pt de periode
                        c.BeginFigure(new Point(x1, y), false, false);
                        bool up = true;
                        for (double x = x1 + 1; x <= x2 + 0.01; x += 1, up = !up) c.LineTo(new Point(Math.Min(x, x2), y + (up ? -0.7 : 0.7)), true, false);
                    }
                    dc.DrawGeometry(null, SpellPen, g);
                }
            }
        }
    }

    /// <summary>Dessine un morceau de texte : decalage (exposant, lettrine), interlettrage, souligne, barre.</summary>
    static void DrawPiece(DrawingContext dc, OpenPub.Layout.LaidPiece p, OpenPub.Layout.LaidLine line, EditorSession session, double ppd)
    {
        if (p.Text.Length == 0) return;
        var tf = session.Fonts.Typeface(p.Font);
        var brush = Brush(p.Font.Color);
        TextDecorationCollection? deco = null;
        if (p.Font.Underline || p.Font.Strike)
        {
            deco = new TextDecorationCollection();
            if (p.Font.Underline) deco.Add(TextDecorations.Underline);
            if (p.Font.Strike) deco.Add(TextDecorations.Strikethrough);
        }
        double baseline = line.Baseline + p.Dy;
        if (Math.Abs(p.Tracking) < 0.001)
        {
            var ft = new FormattedText(p.Text, Fr, FlowDirection.LeftToRight, tf, p.SizePt, brush, ppd);
            if (deco is not null) ft.SetTextDecorations(deco);
            dc.DrawText(ft, new Point(p.X, baseline - ft.Baseline));
            return;
        }
        // interlettrage : caractere par caractere
        double x = p.X;
        foreach (var ch in p.Text)
        {
            var ft = new FormattedText(ch.ToString(), Fr, FlowDirection.LeftToRight, tf, p.SizePt, brush, ppd);
            if (deco is not null) ft.SetTextDecorations(deco);
            dc.DrawText(ft, new Point(x, baseline - ft.Baseline));
            x += session.Measurer.Width(ch.ToString(), p.Font, p.SizePt) + p.Tracking;
        }
    }

    static void DrawText(DrawingContext dc, Shape s, PageLayoutResult layout, EditorSession session, double ppd, bool marks, Page page)
    {
        // cadre de la zone (forme automatique ou rectangle) : fond et contour
        var frameFill = s.Fill?.Color is { } c ? Brush(c, s.Fill.Opacity ?? 1) : null;
        var framePen = PenOf(s.Line);
        if (frameFill is not null || framePen is not null)
        {
            var frameGeo = GeometryOf(s) ?? new RectangleGeometry(new Rect(s.X, s.Y, s.W, s.H));
            DrawShadow(dc, s, frameGeo);
            dc.DrawGeometry(frameFill, framePen, frameGeo);
        }
        if (marks && framePen is null)
        {
            // contour discret des zones de texte, comme les reperes de Publisher
            var guide = new Pen(Brush("#7fa7e0", 0.6), 0.4) { DashStyle = DashStyles.Dot };
            dc.DrawRectangle(null, guide, new Rect(s.X, s.Y, s.W, s.H));
        }
        if (!layout.Frames.TryGetValue(s, out var fl)) return;
        dc.PushClip(new RectangleGeometry(new Rect(s.X - 2, s.Y - 2, s.W + 4, s.H + 4)));
        foreach (var line in fl.Lines)
        {
            // surlignage sous le texte
            foreach (var p in line.Pieces)
                if (p.Font.Highlight is { } hl && p.Width > 0)
                {
                    // hauteur des lettres (et non de tout l'interligne)
                    double sz = p.SizePt, top = line.Baseline + p.Dy - 0.9 * sz;
                    dc.DrawRectangle(Brush(hl), null, new Rect(p.X, top, p.Width, 1.15 * sz));
                }
            foreach (var p in line.Marks) DrawPiece(dc, p, line, session, ppd);
            foreach (var p in line.Pieces) DrawPiece(dc, p, line, session, ppd);
        }
        // fautes d'orthographe (a l'ecran seulement, jamais a l'impression ni a l'export)
        if (marks && session.Spell is { } spell)
        {
            var head = s.Chain is null ? s : page.ChainFrames(s.Chain).FirstOrDefault() ?? s;
            if (head.Paragraphs is { } paras) DrawSpelling(dc, fl, paras, spell, session);
        }
        dc.Pop();
        if (marks && layout.Overflows(s))
        {
            // indicateur de debordement (texte qui ne tient pas, meme reduit)
            var r = new Rect(s.X + s.W - 14, s.Y + s.H - 10, 14, 10);
            dc.DrawRectangle(Brush("#d13438"), null, r);
            var ft = new FormattedText("A…", Fr, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 7, System.Windows.Media.Brushes.White, ppd);
            dc.DrawText(ft, new Point(r.X + 2, r.Y + 0.5));
        }
    }
}
