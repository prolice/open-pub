using System.Windows;
using OpenPub.Core.Model;
using PathSegment = OpenPub.Core.Model.PathSegment;

namespace OpenPub.App.Editing;

/// <summary>
/// Construction des dessins libres : trace a main levee (simplifie puis lisse en courbes de Bezier),
/// forme libre (polygone) et lignes / fleches. Coordonnees en points de page.
/// </summary>
public static class DrawingFactory
{
    /// <summary>Simplification de Douglas-Peucker : retire les points a moins de <paramref name="eps"/> du trace.</summary>
    public static List<Point> Simplify(IReadOnlyList<Point> pts, double eps)
    {
        if (pts.Count < 3) return pts.ToList();
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int A, int B)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double max = 0; int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = DistanceToSegment(pts[i], pts[a], pts[b]);
                if (d > max) { max = d; idx = i; }
            }
            if (idx >= 0 && max > eps) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-9) return (p - a).Length;
        double t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    static Shape PathShape(List<PathSegment> path, IEnumerable<Point> extent, string color, double width)
    {
        var all = extent.ToList();
        double x0 = all.Min(p => p.X), y0 = all.Min(p => p.Y), x1 = all.Max(p => p.X), y1 = all.Max(p => p.Y);
        return new Shape
        {
            Kind = ShapeKind.Path, X = x0, Y = y0, W = Math.Max(1, x1 - x0), H = Math.Max(1, y1 - y0),
            Line = new LineStyle { Color = color, Weight = width }, Path = path,
        };
    }

    /// <summary>Trace a main levee : points bruts de la souris -> courbe lissee (Catmull-Rom convertie en Bezier).</summary>
    public static Shape? Freehand(IReadOnlyList<Point> raw, string color, double width, double tolerance = 0.8)
    {
        if (raw.Count < 2) return null;
        var p = Simplify(raw, tolerance);
        if (p.Count < 2) return null;
        var path = new List<PathSegment> { new() { A = "M", X = p[0].X, Y = p[0].Y } };
        var extent = new List<Point>(p);
        for (int i = 0; i < p.Count - 1; i++)
        {
            var p0 = p[Math.Max(0, i - 1)];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = p[Math.Min(p.Count - 1, i + 2)];
            var c1 = p1 + (p2 - p0) / 6;
            var c2 = p2 - (p3 - p1) / 6;
            path.Add(new PathSegment { A = "C", X1 = c1.X, Y1 = c1.Y, X2 = c2.X, Y2 = c2.Y, X = p2.X, Y = p2.Y });
            extent.Add(c1); extent.Add(c2);
        }
        return PathShape(path, extent, color, width);
    }

    /// <summary>Forme libre : polygone ouvert ou ferme (ferme = remplissable).</summary>
    public static Shape? Polygon(IReadOnlyList<Point> pts, bool closed, string color, double width)
    {
        if (pts.Count < 2) return null;
        var path = new List<PathSegment> { new() { A = "M", X = pts[0].X, Y = pts[0].Y } };
        foreach (var q in pts.Skip(1)) path.Add(new PathSegment { A = "L", X = q.X, Y = q.Y });
        if (closed && pts.Count >= 3) path.Add(new PathSegment { A = "Z" });
        var s = PathShape(path, pts, color, width);
        if (closed && pts.Count >= 3) s.Fill = new Fill { Color = "#dbe8f7" };
        return s;
    }

    /// <summary>Ligne ou fleche entre deux points.</summary>
    public static Shape Line(Point a, Point b, string color, double width, bool arrow)
    {
        var s = PathShape(new List<PathSegment> { new() { A = "M", X = a.X, Y = a.Y }, new() { A = "L", X = b.X, Y = b.Y } },
            new[] { a, b }, color, width);
        s.Line!.ArrowEnd = arrow;
        return s;
    }
}
