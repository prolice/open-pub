using OpenPub.Core.Model;

namespace OpenPub.Core.Geometry;

/// <summary>Cadre d'un objet (points de page) ; Px/Py convertissent une part du cadre en coordonnee.</summary>
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Px(double f) => X + W * f;
    public double Py(double f) => Y + H * f;
    public double Cx => X + W / 2;
    public double Cy => Y + H / 2;
    public double Min => Math.Min(W, H);
    public static Box Of(Shape s) => new(s.X, s.Y, Math.Max(s.W, 0.1), Math.Max(s.H, 0.1));
}

/// <summary>Construction d'un trace (segments M/L/C/Q/Z en coordonnees absolues), arcs d'ellipse en courbes de Bezier.</summary>
public sealed class PathBuilder
{
    public List<PathSegment> Segments { get; } = new();
    (double X, double Y) _last;

    public PathBuilder M(double x, double y) { Segments.Add(new PathSegment { A = "M", X = x, Y = y }); _last = (x, y); return this; }
    public PathBuilder L(double x, double y) { Segments.Add(new PathSegment { A = "L", X = x, Y = y }); _last = (x, y); return this; }
    public PathBuilder C(double x1, double y1, double x2, double y2, double x, double y)
    {
        Segments.Add(new PathSegment { A = "C", X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X = x, Y = y });
        _last = (x, y);
        return this;
    }
    public PathBuilder Q(double x1, double y1, double x, double y)
    {
        Segments.Add(new PathSegment { A = "Q", X1 = x1, Y1 = y1, X = x, Y = y });
        _last = (x, y);
        return this;
    }
    public PathBuilder Z() { Segments.Add(new PathSegment { A = "Z" }); return this; }

    /// <summary>Polygone ferme.</summary>
    public PathBuilder Poly(IEnumerable<(double X, double Y)> pts)
    {
        bool first = true;
        foreach (var (x, y) in pts)
        {
            if (first) M(x, y); else L(x, y);
            first = false;
        }
        return first ? this : Z();
    }

    public PathBuilder Poly(params (double X, double Y)[] pts) => Poly((IEnumerable<(double X, double Y)>)pts);

    /// <summary>
    /// Arc d'ellipse (angles parametriques en degres, sens horaire a l'ecran pour un balayage positif), en cubiques
    /// d'au plus 90 degres. move : commence un nouveau sous-trace ; sinon une ligne rejoint le debut de l'arc si besoin.
    /// </summary>
    public PathBuilder Arc(double cx, double cy, double rx, double ry, double startDeg, double sweepDeg, bool move = false)
    {
        double t0 = startDeg * Math.PI / 180, sweep = sweepDeg * Math.PI / 180;
        var p0 = (X: cx + rx * Math.Cos(t0), Y: cy + ry * Math.Sin(t0));
        if (move || Segments.Count == 0) M(p0.X, p0.Y);
        else if (Math.Abs(_last.X - p0.X) > 1e-6 || Math.Abs(_last.Y - p0.Y) > 1e-6) L(p0.X, p0.Y);
        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
        double da = sweep / n, k = 4.0 / 3 * Math.Tan(da / 4);
        for (int i = 0; i < n; i++)
        {
            double a1 = t0 + i * da, a2 = a1 + da;
            double x1 = cx + rx * Math.Cos(a1), y1 = cy + ry * Math.Sin(a1);
            double x2 = cx + rx * Math.Cos(a2), y2 = cy + ry * Math.Sin(a2);
            C(x1 - k * rx * Math.Sin(a1), y1 + k * ry * Math.Cos(a1),
              x2 + k * rx * Math.Sin(a2), y2 - k * ry * Math.Cos(a2), x2, y2);
        }
        return this;
    }

    public PathBuilder Ellipse(double cx, double cy, double rx, double ry) => Arc(cx, cy, rx, ry, 0, 360, move: true).Z();
}

/// <summary>Sous-trace aplati en polyligne.</summary>
public sealed record FlatFigure(List<(double X, double Y)> Points, bool Closed);

/// <summary>Outils purs sur les traces : aplatissement, boite englobante, transformation, point le plus proche.</summary>
public static class PathTools
{
    /// <summary>Aplatit un trace en polylignes (courbes et arcs decoupes en <paramref name="steps"/> morceaux).</summary>
    public static List<FlatFigure> Flatten(IReadOnlyList<PathSegment> path, int steps = 16)
    {
        var figs = new List<FlatFigure>();
        List<(double X, double Y)>? cur = null;
        (double X, double Y) start = default, last = default;
        void Close(bool closed)
        {
            if (cur is { Count: > 0 }) figs.Add(new FlatFigure(cur, closed));
            cur = null;
        }
        foreach (var s in path)
        {
            if (s.A == "Z")
            {
                if (cur is not null) { Close(true); last = start; }
                continue;
            }
            if (s.X is null || s.Y is null) continue;
            var p = (X: s.X.Value, Y: s.Y.Value);
            if (s.A == "M" || cur is null)
            {
                Close(false);
                cur = new List<(double, double)> { p };
                start = last = p;
                if (s.A == "M") continue;
            }
            switch (s.A)
            {
                case "C":
                {
                    var c1 = (X: s.X1 ?? last.X, Y: s.Y1 ?? last.Y);
                    var c2 = (X: s.X2 ?? p.X, Y: s.Y2 ?? p.Y);
                    for (int i = 1; i <= steps; i++)
                    {
                        double t = (double)i / steps, u = 1 - t;
                        cur!.Add((u * u * u * last.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p.X,
                                  u * u * u * last.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p.Y));
                    }
                    break;
                }
                case "Q":
                {
                    var c = (X: s.X1 ?? last.X, Y: s.Y1 ?? last.Y);
                    for (int i = 1; i <= steps; i++)
                    {
                        double t = (double)i / steps, u = 1 - t;
                        cur!.Add((u * u * last.X + 2 * u * t * c.X + t * t * p.X, u * u * last.Y + 2 * u * t * c.Y + t * t * p.Y));
                    }
                    break;
                }
                case "A":
                    cur!.AddRange(ArcPoints(last, p, s.Rx ?? 0, s.Ry ?? 0, s.Rot ?? 0, (s.Large ?? 0) == 1, (s.Sweep ?? 0) == 1, steps));
                    break;
                default:
                    cur!.Add(p);
                    break;
            }
            last = p;
        }
        Close(false);
        return figs;
    }

    /// <summary>Points d'un arc SVG (parametrage par les extremites, conversion vers le centre).</summary>
    static IEnumerable<(double X, double Y)> ArcPoints((double X, double Y) p0, (double X, double Y) p1, double rx, double ry, double rotDeg,
        bool large, bool sweep, int steps)
    {
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-9 || ry < 1e-9 || (Math.Abs(p0.X - p1.X) < 1e-9 && Math.Abs(p0.Y - p1.Y) < 1e-9)) { yield return p1; yield break; }
        double phi = rotDeg * Math.PI / 180, cphi = Math.Cos(phi), sphi = Math.Sin(phi);
        double dx = (p0.X - p1.X) / 2, dy = (p0.Y - p1.Y) / 2;
        double x1 = cphi * dx + sphi * dy, y1 = -sphi * dx + cphi * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1) { double sq = Math.Sqrt(lambda); rx *= sq; ry *= sq; }
        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1, den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double coef = (large != sweep ? 1 : -1) * Math.Sqrt(Math.Max(0, num / Math.Max(den, 1e-12)));
        double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
        double cx = cphi * cx1 - sphi * cy1 + (p0.X + p1.X) / 2, cy = sphi * cx1 + cphi * cy1 + (p0.Y + p1.Y) / 2;
        double Ang(double ux, double uy, double vx, double vy) => Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        double t1 = Ang(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double dt = Ang((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && dt > 0) dt -= 2 * Math.PI;
        else if (sweep && dt < 0) dt += 2 * Math.PI;
        for (int i = 1; i <= steps; i++)
        {
            double t = t1 + dt * i / steps;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            yield return (cphi * ex - sphi * ey + cx, sphi * ex + cphi * ey + cy);
        }
    }

    /// <summary>Boite englobante exacte (courbes aplaties) ; null pour un trace vide.</summary>
    public static Box? Bounds(IReadOnlyList<PathSegment> path)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in Flatten(path, 24))
            foreach (var (x, y) in f.Points)
            {
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        return x0 > x1 ? null : new Box(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Applique une transformation a tous les points (extremites et points de controle) d'un trace.</summary>
    public static void Transform(IEnumerable<PathSegment> path, Func<double, double, (double X, double Y)> f)
    {
        foreach (var s in path)
        {
            if (s.X is { } x && s.Y is { } y) (s.X, s.Y) = f(x, y);
            if (s.X1 is { } x1 && s.Y1 is { } y1) (s.X1, s.Y1) = f(x1, y1);
            if (s.X2 is { } x2 && s.Y2 is { } y2) (s.X2, s.Y2) = f(x2, y2);
        }
    }

    /// <summary>Etire un trace pour que sa boite englobante soit exactement <paramref name="b"/>.</summary>
    public static List<PathSegment> FitTo(List<PathSegment> path, Box b)
    {
        if (Bounds(path) is not { } src || src.W < 1e-9 || src.H < 1e-9) return path;
        Transform(path, (x, y) => (b.X + (x - src.X) / src.W * b.W, b.Y + (y - src.Y) / src.H * b.H));
        return path;
    }

    public static double Distance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, out double t)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y, l2 = vx * vx + vy * vy;
        t = l2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2, 0, 1);
        double qx = a.X + vx * t - p.X, qy = a.Y + vy * t - p.Y;
        return Math.Sqrt(qx * qx + qy * qy);
    }
}

/// <summary>Polyligne parcourue par abscisse curviligne (placement de glyphes le long d'un trace).</summary>
public sealed class Polyline
{
    readonly List<(double X, double Y)> _pts;
    readonly double[] _cum;
    public double Length => _cum[^1];

    public Polyline(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count < 2) throw new ArgumentException("Une polyligne a au moins deux points.", nameof(pts));
        _pts = pts.ToList();
        _cum = new double[_pts.Count];
        for (int i = 1; i < _pts.Count; i++)
            _cum[i] = _cum[i - 1] + Math.Sqrt(Math.Pow(_pts[i].X - _pts[i - 1].X, 2) + Math.Pow(_pts[i].Y - _pts[i - 1].Y, 2));
    }

    /// <summary>Point et direction (degres) a la distance s du debut (bornee a la longueur).</summary>
    public (double X, double Y, double AngleDeg) At(double s)
    {
        s = Math.Clamp(s, 0, Length);
        int i = Array.BinarySearch(_cum, s);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, _pts.Count - 1);
        var a = _pts[i - 1]; var b = _pts[i];
        double seg = _cum[i] - _cum[i - 1], t = seg < 1e-12 ? 0 : (s - _cum[i - 1]) / seg;
        return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
    }
}
