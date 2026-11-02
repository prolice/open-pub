using OpenPub.Core.Model;

namespace OpenPub.Layout;

/// <summary>
/// Met en page tout le texte d'une page : regroupe les zones en chaines, calcule l'habillage
/// de chaque zone et lance le moteur. Regles d'habillage reprises du prototype (tools/render_html.py).
/// </summary>
public sealed class PageLayouter
{
    readonly TextLayoutEngine _engine;
    public const double Gap = 2.88;              // distance texte-obstacle : 0,04 pouce, valeur par defaut de Publisher

    public PageLayouter(TextLayoutEngine engine) => _engine = engine;

    /// <summary>Espacement par defaut entre les colonnes d'une zone (0,5 cm).</summary>
    public const double DefaultColumnGap = 14.17;

    /// <summary>Resultat par zone de texte de la page (cle : la zone).</summary>
    public Dictionary<Shape, FrameLayout> LayoutPage(Page page, out List<ChainLayout> chains)
    {
        var map = new Dictionary<Shape, FrameLayout>();
        chains = new List<ChainLayout>();
        var obstacles = Obstacles(page);
        foreach (var head in page.Shapes.Where(s => s.IsStoryHead))
        {
            var frames = head.Chain is null ? new List<Shape> { head } : page.ChainFrames(head.Chain);
            // une zone a plusieurs colonnes devient autant de sous-zones enchainees
            var geoms = frames.SelectMany(f => Columns(f, obstacles)).ToList();
            bool shrink = head.AutoFit is null or "shrink";
            var layout = _engine.Layout(geoms, head.Paragraphs ?? new List<Paragraph>(), shrink);
            chains.Add(layout);
            foreach (var fl in layout.Frames)
            {
                if (map.TryGetValue(fl.Shape, out var existing)) existing.Lines.AddRange(fl.Lines);      // colonnes suivantes
                else map[fl.Shape] = fl;
            }
        }
        return map;
    }

    /// <summary>Geometrie de chaque colonne de la zone (une seule si la zone n'a pas de colonnes).</summary>
    public static IEnumerable<FrameGeometry> Columns(Shape s, List<Shape> obstacles)
    {
        int n = Math.Clamp(s.Columns, 1, 12);
        double cw = s.W - s.PadLeft - s.PadRight;
        if (n == 1 || Math.Abs(s.Rotation % 360) > 0.01) { yield return Geometry(s, obstacles); yield break; }
        double gap = s.ColumnGap > 0 ? s.ColumnGap : DefaultColumnGap;
        double colW = (cw - gap * (n - 1)) / n;
        if (colW < 10) { yield return Geometry(s, obstacles); yield break; }
        for (int k = 0; k < n; k++)
            yield return Geometry(s, obstacles, s.X + s.PadLeft + k * (colW + gap), colW);
    }

    /// <summary>Objets susceptibles de repousser le texte.</summary>
    public static List<Shape> Obstacles(Page page)
    {
        var imageGroups = page.Shapes.Where(s => s.IsImage && s.Group is not null).Select(s => s.Group!).ToHashSet();
        return page.Shapes.Where(s =>
            s.Wrap is "square" or "tight" or "topbottom" && !s.Background
            || s.Wrap is null && (
                (s.IsImage && !s.Background)
                // une zone de texte ne fait obstacle que si elle est groupee avec une image (legende)
                || (s.IsText && s.Group is not null && imageGroups.Contains(s.Group))
                || ((s.Kind is ShapeKind.Rect or ShapeKind.Path or ShapeKind.Ellipse) && s.Fill?.Color is not null && s.W < page.Width * 0.9)))
            .ToList();
    }

    public static double OverlapRatio(Shape a, Shape b)
    {
        var (ax, ay, aw, ah) = a.Bounds();
        var (bx, by, bw, bh) = b.Bounds();
        double w = Math.Min(ax + aw, bx + bw) - Math.Max(ax, bx);
        double h = Math.Min(ay + ah, by + bh) - Math.Max(ay, by);
        return w > 0 && h > 0 ? w * h / Math.Max(1e-6, aw * ah) : 0;
    }

    public static FrameGeometry Geometry(Shape s, List<Shape> obstacles) =>
        Geometry(s, obstacles, s.X + s.PadLeft, s.W - s.PadLeft - s.PadRight);

    /// <summary>Geometrie d'une bande verticale [cx, cx + cw] de la zone (la zone entiere, ou une de ses colonnes).</summary>
    public static FrameGeometry Geometry(Shape s, List<Shape> obstacles, double cx, double cw)
    {
        double cy = s.Y + s.PadTop;
        double ch = s.H - s.PadTop - s.PadBottom;
        var ex = new List<Exclusion>();
        // zone tournee : son texte est mis en page dans son propre repere, sans habillage
        bool rotated = Math.Abs(s.Rotation % 360) > 0.01;
        foreach (var obstacle in rotated ? new List<Shape>() : obstacles)
        {
            var im = obstacle;
            if (im == s || im.Z <= s.Z || (s.Chain is not null && im.Chain == s.Chain)) continue;
            var (ix, iy, iw, ih) = im.Bounds();
            if (!(ix < cx + cw && ix + iw > cx && iy < s.Y + s.H && iy + ih > cy)) continue;
            // habillage automatique : l'objet doit etre majoritairement pose dans la zone (un objet qui l'effleure ne la repousse pas)
            if (im.Wrap is null && OverlapRatio(im, s) < 0.5) continue;
            double gap = im.WrapDistance ?? Gap;
            // bandes horizontales de l'objet : une seule (carre), ou plusieurs qui suivent son contour (serre)
            foreach (var (bTop, bBottom, bLeft, bRight) in Bands(im, ix, iy, iw, ih))
            {
                double top = Math.Max(0, bTop - gap - cy);
                double bottom = Math.Min(ch, bBottom + gap - cy);   // borne au bas de la zone
                if (bottom <= top) continue;
                double left = Math.Max(0, bLeft - gap - cx), right = Math.Min(cw, bRight + gap - cx);
                if (right <= 0 || left >= cw) continue;
                bool leftSide = left < cw - right;
                double width = leftSide ? right : cw - left;
                // Publisher ne fait pas couler le texte dans une bande trop etroite a cote de l'objet ;
                // « haut et bas » : jamais de texte a cote de l'objet
                bool full = im.Wrap == "topbottom" || (im.Wrap != "tight" && cw - width < Math.Max(36, cw * 0.35));
                var e = full ? new Exclusion(top, bottom, 0, cw, true)
                             : leftSide ? new Exclusion(top, bottom, 0, right, false)
                                        : new Exclusion(top, bottom, left, cw, false);
                // emprise exacte de l'objet (sans distance d'habillage) pour les lignes vides
                ex.Add(e with { RawLeft = Math.Max(0, bLeft - cx), RawRight = Math.Min(cw, bRight - cx) });
            }
        }
        return new FrameGeometry { Shape = s, X = cx, Y = cy, Width = cw, Height = ch, Exclusions = ex };
    }

    /// <summary>
    /// Emprise de l'objet par bandes horizontales : le rectangle entier, ou (habillage serre) des bandes de 4 pt
    /// qui suivent une ellipse ou le polygone d'un trace.
    /// </summary>
    static IEnumerable<(double Top, double Bottom, double Left, double Right)> Bands(Shape im, double ix, double iy, double iw, double ih)
    {
        bool rotated = Math.Abs(im.Rotation % 360) > 0.01;
        bool ellipse = im.Kind == ShapeKind.Ellipse || im.Geometry == ShapeGeometry.Ellipse;
        var poly = im.Path is { Count: >= 3 } ? im.Path.Where(p => p.X is not null && p.Y is not null).Select(p => (X: p.X!.Value, Y: p.Y!.Value)).ToList()
                 : im.Clip is { Count: >= 3 } ? im.Clip.Where(p => p.X is not null && p.Y is not null).Select(p => (X: p.X!.Value, Y: p.Y!.Value)).ToList()
                 : null;
        if (im.Wrap != "tight" || rotated || (!ellipse && poly is null))
        {
            yield return (iy, iy + ih, ix, ix + iw);
            yield break;
        }
        const double step = 4;
        for (double y = iy; y < iy + ih - 0.01; y += step)
        {
            double y1 = Math.Min(iy + ih, y + step);
            double l = double.MaxValue, r = double.MinValue;
            if (ellipse)
            {
                double cxe = ix + iw / 2, cye = iy + ih / 2, a = iw / 2, b = ih / 2;
                // largeur maximale de l'ellipse sur la bande
                double yy = Math.Abs(cye - Math.Clamp(cye, y, y1)) / Math.Max(1e-6, b);
                double half = a * Math.Sqrt(Math.Max(0, 1 - yy * yy));
                l = cxe - half; r = cxe + half;
            }
            else
            {
                // aretes du polygone (points du trace) qui traversent la bande
                for (int k = 0; k < poly!.Count; k++)
                {
                    var p0 = poly[k]; var p1 = poly[(k + 1) % poly.Count];
                    double lo = Math.Max(y, Math.Min(p0.Y, p1.Y)), hi = Math.Min(y1, Math.Max(p0.Y, p1.Y));
                    if (lo > hi) continue;
                    foreach (var yv in new[] { lo, hi })
                    {
                        double t = Math.Abs(p1.Y - p0.Y) < 1e-9 ? 0 : (yv - p0.Y) / (p1.Y - p0.Y);
                        double xv = p0.X + (p1.X - p0.X) * Math.Clamp(t, 0, 1);
                        l = Math.Min(l, xv); r = Math.Max(r, xv);
                    }
                    if (Math.Abs(p1.Y - p0.Y) < 1e-9 && p0.Y >= y && p0.Y <= y1) { l = Math.Min(l, Math.Min(p0.X, p1.X)); r = Math.Max(r, Math.Max(p0.X, p1.X)); }
                }
            }
            if (r > l) yield return (y, y1, l, r);
        }
    }
}
