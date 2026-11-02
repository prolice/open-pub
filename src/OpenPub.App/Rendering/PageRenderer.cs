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
        dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, page.Width, page.Height));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, page.Width, page.Height)));
        var layout = session.Layout(page);
        foreach (var s in page.Shapes)
        {
            bool rotated = PushRotation(dc, s);
            switch (s.Kind)
            {
                case ShapeKind.Image: DrawImage(dc, s, session); break;
                case ShapeKind.Text: DrawText(dc, s, layout, session, pixelsPerDip, editorMarks); break;
                default: DrawShape(dc, s); break;
            }
            if (rotated) dc.Pop();
        }
        overlay?.Invoke(dc);       // ex. surbrillance de la selection de texte (semi-transparente)
        dc.Pop();
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

    static void DrawText(DrawingContext dc, Shape s, PageLayoutResult layout, EditorSession session, double ppd, bool marks)
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
            foreach (var p in line.Pieces)
            {
                if (p.Text.Length == 0) continue;
                var ft = new FormattedText(p.Text, Fr, FlowDirection.LeftToRight, session.Fonts.Typeface(p.Font), p.SizePt,
                    Brush(p.Font.Color), ppd);
                if (p.Font.Underline) ft.SetTextDecorations(TextDecorations.Underline);
                dc.DrawText(ft, new Point(p.X, line.Baseline - ft.Baseline));
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
