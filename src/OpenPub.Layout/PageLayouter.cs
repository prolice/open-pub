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

    /// <summary>Resultat par zone de texte de la page (cle : la zone).</summary>
    public Dictionary<Shape, FrameLayout> LayoutPage(Page page, out List<ChainLayout> chains)
    {
        var map = new Dictionary<Shape, FrameLayout>();
        chains = new List<ChainLayout>();
        var obstacles = Obstacles(page);
        foreach (var head in page.Shapes.Where(s => s.IsStoryHead))
        {
            var frames = head.Chain is null ? new List<Shape> { head } : page.ChainFrames(head.Chain);
            var geoms = frames.Select(f => Geometry(f, obstacles)).ToList();
            var layout = _engine.Layout(geoms, head.Paragraphs ?? new List<Paragraph>());
            chains.Add(layout);
            foreach (var fl in layout.Frames) map[fl.Shape] = fl;
        }
        return map;
    }

    /// <summary>Objets susceptibles de repousser le texte.</summary>
    public static List<Shape> Obstacles(Page page)
    {
        var imageGroups = page.Shapes.Where(s => s.IsImage && s.Group is not null).Select(s => s.Group!).ToHashSet();
        return page.Shapes.Where(s =>
            (s.IsImage && !s.Background)
            // une zone de texte ne fait obstacle que si elle est groupee avec une image (legende)
            || (s.IsText && s.Group is not null && imageGroups.Contains(s.Group))
            || ((s.Kind is ShapeKind.Rect or ShapeKind.Path or ShapeKind.Ellipse) && s.Fill?.Color is not null && s.W < page.Width * 0.9))
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

    public static FrameGeometry Geometry(Shape s, List<Shape> obstacles)
    {
        double cx = s.X + s.PadLeft, cy = s.Y + s.PadTop;
        double cw = s.W - s.PadLeft - s.PadRight, ch = s.H - s.PadTop - s.PadBottom;
        var ex = new List<Exclusion>();
        // zone tournee : son texte est mis en page dans son propre repere, sans habillage
        bool rotated = Math.Abs(s.Rotation % 360) > 0.01;
        foreach (var obstacle in rotated ? new List<Shape>() : obstacles)
        {
            var im = obstacle;
            if (im == s || im.Z <= s.Z || (s.Chain is not null && im.Chain == s.Chain)) continue;
            var (ix, iy, iw, ih) = im.Bounds();
            if (!(ix < cx + cw && ix + iw > cx && iy < s.Y + s.H && iy + ih > cy)) continue;
            // l'objet doit etre majoritairement pose dans la zone : un objet qui l'effleure ne la repousse pas
            if (OverlapRatio(im, s) < 0.5) continue;
            double top = Math.Max(0, iy - Gap - cy);
            double bottom = Math.Min(ch, iy + ih + Gap - cy);   // borne au bas de la zone
            if (bottom <= top) continue;
            double left = Math.Max(0, ix - Gap - cx), right = Math.Min(cw, ix + iw + Gap - cx);
            bool leftSide = left < cw - right;
            double width = leftSide ? right : cw - left;
            // Publisher ne fait pas couler le texte dans une bande trop etroite a cote de l'objet
            bool full = cw - width < Math.Max(36, cw * 0.35);
            var e = full ? new Exclusion(top, bottom, 0, cw, true)
                         : leftSide ? new Exclusion(top, bottom, 0, right, false)
                                    : new Exclusion(top, bottom, left, cw, false);
            // emprise exacte de l'objet (sans distance d'habillage) pour les lignes vides
            ex.Add(e with { RawLeft = Math.Max(0, ix - cx), RawRight = Math.Min(cw, ix + iw - cx) });
        }
        return new FrameGeometry { Shape = s, X = cx, Y = cy, Width = cw, Height = ch, Exclusions = ex };
    }
}
