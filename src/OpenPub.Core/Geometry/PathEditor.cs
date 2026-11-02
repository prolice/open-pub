using OpenPub.Core.Model;

namespace OpenPub.Core.Geometry;

/// <summary>
/// Modification des points d'un trace (<see cref="Shape.Path"/>, coordonnees de page absolues) : deplacer un sommet
/// ou une poignee de courbe, ajouter, supprimer, passer d'un angle vif a un point lisse. Les traces sont d'abord
/// normalises (<see cref="Normalize"/>) : courbes quadratiques en cubiques, sous-traces fermes termines par un sommet
/// explicite sur le point de depart (son « alias »), pour que chaque sommet ait un segment entrant et sortant.
/// </summary>
public static class PathEditor
{
    static bool Draws(PathSegment s) => s.A is "L" or "C" or "Q" or "A" && s.X is not null && s.Y is not null;
    static (double X, double Y) Pt(PathSegment s) => (s.X ?? 0, s.Y ?? 0);
    static bool Same((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

    /// <summary>Transforme une forme (automatique, ellipse, rectangle) en trace modifiable ; renvoie faux si impossible.</summary>
    public static bool ToEditable(Shape s)
    {
        if (s.IsText || s.IsImage || s.IsWordArt) return false;
        if (s.Geometry is not null)
        {
            s.Path = ShapeLibrary.Outline(s);
            s.Geometry = null; s.Adjust = null; s.CornerRadius = null;
        }
        else if (s.Path is not { Count: >= 2 })
            s.Path = ShapeLibrary.Outline(s.Kind == ShapeKind.Ellipse ? ShapeGeometry.Ellipse : ShapeGeometry.Rect, Box.Of(s));
        s.Kind = ShapeKind.Path;
        Normalize(s.Path!);
        return true;
    }

    /// <summary>Courbes quadratiques en cubiques ; sous-traces fermes : sommet de fermeture explicite.</summary>
    public static void Normalize(List<PathSegment> path)
    {
        (double X, double Y) last = default, start = default;
        for (int i = 0; i < path.Count; i++)
        {
            var s = path[i];
            if (s.A == "M") { start = last = Pt(s); continue; }
            if (s.A == "Z")
            {
                if (!Same(last, start)) { path.Insert(i, new PathSegment { A = "L", X = start.X, Y = start.Y }); i++; }
                last = start;
                continue;
            }
            if (!Draws(s)) continue;
            if (s.A == "Q")
            {
                double qx = s.X1 ?? last.X, qy = s.Y1 ?? last.Y;
                var (px, py) = Pt(s);
                s.A = "C";
                s.X1 = last.X + 2.0 / 3 * (qx - last.X); s.Y1 = last.Y + 2.0 / 3 * (qy - last.Y);
                s.X2 = px + 2.0 / 3 * (qx - px); s.Y2 = py + 2.0 / 3 * (qy - py);
            }
            last = Pt(s);
        }
    }

    /// <summary>Debut (M) et fin (Z ou dernier segment) du sous-trace qui contient le segment i.</summary>
    static (int Start, int End, bool Closed) Figure(List<PathSegment> path, int i)
    {
        int m = i;
        while (m > 0 && path[m].A != "M") m--;
        int e = i;
        while (e + 1 < path.Count && path[e + 1].A != "M" && path[e + 1].A != "Z") e++;
        bool closed = e + 1 < path.Count && path[e + 1].A == "Z";
        return (m, e, closed);
    }

    /// <summary>Sommet de fermeture d'un sous-trace ferme (meme point que son M), ou -1.</summary>
    static int AliasOfStart(List<PathSegment> path, int m)
    {
        var (_, e, closed) = Figure(path, m);
        return closed && e > m && Same(Pt(path[e]), Pt(path[m])) ? e : -1;
    }

    static bool IsAlias(List<PathSegment> path, int i, out int m)
    {
        var f = Figure(path, i);
        m = f.Start;
        return f.Closed && i == f.End && i > f.Start && Same(Pt(path[i]), Pt(path[f.Start]));
    }

    /// <summary>Sommets modifiables (indices de segments) ; le sommet de fermeture n'est pas repete.</summary>
    public static List<int> Nodes(List<PathSegment> path)
    {
        var list = new List<int>();
        for (int i = 0; i < path.Count; i++)
            if ((path[i].A == "M" || Draws(path[i])) && !IsAlias(path, i, out _)) list.Add(i);
        return list;
    }

    public static (double X, double Y) NodePoint(List<PathSegment> path, int i) => Pt(path[i]);

    /// <summary>Segment qui arrive au sommet i (pour un depart de sous-trace ferme : le segment de fermeture).</summary>
    static int Incoming(List<PathSegment> path, int i)
    {
        if (path[i].A != "M") return i;
        return AliasOfStart(path, i);
    }

    /// <summary>Segment qui part du sommet i (-1 en fin de trace ouvert).</summary>
    static int Outgoing(List<PathSegment> path, int i)
    {
        if (IsAlias(path, i, out var m)) i = m;
        return i + 1 < path.Count && Draws(path[i + 1]) ? i + 1 : -1;
    }

    /// <summary>Poignees de courbe attachees au sommet : (segment, 1 = premier point de controle, 2 = second).</summary>
    public static List<(int Seg, int Which)> Controls(List<PathSegment> path, int i)
    {
        var list = new List<(int, int)>();
        int a = Incoming(path, i), b = Outgoing(path, i);
        if (a >= 0 && path[a].A == "C") list.Add((a, 2));
        if (b >= 0 && path[b].A == "C") list.Add((b, 1));
        return list;
    }

    public static (double X, double Y) ControlPoint(List<PathSegment> path, int seg, int which) =>
        which == 1 ? (path[seg].X1 ?? 0, path[seg].Y1 ?? 0) : (path[seg].X2 ?? 0, path[seg].Y2 ?? 0);

    static void SetControl(List<PathSegment> path, int seg, int which, double x, double y)
    {
        if (which == 1) { path[seg].X1 = x; path[seg].Y1 = y; } else { path[seg].X2 = x; path[seg].Y2 = y; }
    }

    /// <summary>Deplace un sommet ; ses poignees de courbe le suivent.</summary>
    public static void MoveNode(List<PathSegment> path, int i, double x, double y)
    {
        var (ox, oy) = Pt(path[i]);
        double dx = x - ox, dy = y - oy;
        int alias = path[i].A == "M" ? AliasOfStart(path, i) : -1;      // a trouver avant de deplacer le depart
        foreach (var (seg, which) in Controls(path, i))
        {
            var (cx, cy) = ControlPoint(path, seg, which);
            SetControl(path, seg, which, cx + dx, cy + dy);
        }
        path[i].X = x; path[i].Y = y;
        if (alias >= 0) { path[alias].X = x; path[alias].Y = y; }
    }

    /// <summary>Deplace une poignee de courbe ; point lisse : la poignee opposee reste alignee (sa longueur est gardee).</summary>
    public static void MoveControl(List<PathSegment> path, int node, int seg, int which, double x, double y)
    {
        bool smooth = IsSmooth(path, node);
        SetControl(path, seg, which, x, y);
        if (!smooth) return;
        var other = Controls(path, node).FirstOrDefault(c => c != (seg, which));
        if (other == default) return;
        var (nx, ny) = Pt(path[node]);
        var (ox, oy) = ControlPoint(path, other.Seg, other.Which);
        double len = Math.Sqrt((ox - nx) * (ox - nx) + (oy - ny) * (oy - ny));
        double vx = nx - x, vy = ny - y, vl = Math.Sqrt(vx * vx + vy * vy);
        if (vl < 1e-9) return;
        SetControl(path, other.Seg, other.Which, nx + vx / vl * len, ny + vy / vl * len);
    }

    /// <summary>Point lisse : deux poignees distinctes du sommet, alignees de part et d'autre.</summary>
    public static bool IsSmooth(List<PathSegment> path, int i)
    {
        var c = Controls(path, i);
        if (c.Count != 2) return false;
        var (nx, ny) = Pt(path[i]);
        var (ax, ay) = ControlPoint(path, c[0].Seg, c[0].Which);
        var (bx, by) = ControlPoint(path, c[1].Seg, c[1].Which);
        double ux = nx - ax, uy = ny - ay, vx = bx - nx, vy = by - ny;
        double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
        if (lu < 0.01 || lv < 0.01) return false;
        double cross = (ux * vy - uy * vx) / (lu * lv), dot = (ux * vx + uy * vy) / (lu * lv);
        return Math.Abs(cross) < 0.02 && dot > 0;
    }

    /// <summary>Point lisse (tangente parallele aux voisins, segments droits changes en courbes) ou angle vif.</summary>
    public static void SetSmooth(List<PathSegment> path, int i, bool smooth)
    {
        int a = Incoming(path, i), b = Outgoing(path, i);
        var n = Pt(path[i]);
        if (!smooth)
        {
            // angle vif : les poignees du sommet reviennent sur le sommet
            if (a >= 0 && path[a].A == "C") SetControl(path, a, 2, n.X, n.Y);
            if (b >= 0 && path[b].A == "C") SetControl(path, b, 1, n.X, n.Y);
            return;
        }
        (double X, double Y)? prev = a >= 0 ? StartOf(path, a) : null;
        (double X, double Y)? next = b >= 0 ? Pt(path[b]) : null;
        if (prev is null && next is null) return;
        var p = prev ?? n; var q = next ?? n;
        double tx = q.X - p.X, ty = q.Y - p.Y, tl = Math.Sqrt(tx * tx + ty * ty);
        if (tl < 1e-9) return;
        tx /= tl; ty /= tl;
        if (a >= 0)
        {
            var s0 = StartOf(path, a);
            double d = Dist(n, s0) / 3;
            if (path[a].A != "C") { path[a].A = "C"; path[a].X1 = s0.X + (n.X - s0.X) / 3; path[a].Y1 = s0.Y + (n.Y - s0.Y) / 3; }
            SetControl(path, a, 2, n.X - tx * d, n.Y - ty * d);
        }
        if (b >= 0)
        {
            var e = Pt(path[b]);
            double d = Dist(n, e) / 3;
            if (path[b].A != "C") { path[b].A = "C"; path[b].X2 = e.X + (n.X - e.X) / 3; path[b].Y2 = e.Y + (n.Y - e.Y) / 3; }
            SetControl(path, b, 1, n.X + tx * d, n.Y + ty * d);
        }
    }

    static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Point de depart du segment j (extremite du segment precedent).</summary>
    static (double X, double Y) StartOf(List<PathSegment> path, int j)
    {
        for (int k = j - 1; k >= 0; k--)
            if (path[k].A == "M" || Draws(path[k])) return Pt(path[k]);
        return Pt(path[j]);
    }

    /// <summary>Point d'un segment au parametre t (0 = debut, 1 = fin).</summary>
    public static (double X, double Y) PointOn(List<PathSegment> path, int j, double t)
    {
        var p0 = StartOf(path, j);
        var s = path[j];
        var p = Pt(s);
        double u = 1 - t;
        if (s.A == "C")
        {
            double x1 = s.X1 ?? p0.X, y1 = s.Y1 ?? p0.Y, x2 = s.X2 ?? p.X, y2 = s.Y2 ?? p.Y;
            return (u * u * u * p0.X + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * p.X,
                    u * u * u * p0.Y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * p.Y);
        }
        if (s.A == "Q")
        {
            double x1 = s.X1 ?? p0.X, y1 = s.Y1 ?? p0.Y;
            return (u * u * p0.X + 2 * u * t * x1 + t * t * p.X, u * u * p0.Y + 2 * u * t * y1 + t * t * p.Y);
        }
        return (p0.X + (p.X - p0.X) * t, p0.Y + (p.Y - p0.Y) * t);
    }

    /// <summary>Segment du trace le plus proche d'un point, parametre et distance.</summary>
    public static (int Seg, double T, double Dist) Nearest(List<PathSegment> path, double x, double y)
    {
        (int, double, double) best = (-1, 0, double.MaxValue);
        for (int j = 0; j < path.Count; j++)
        {
            if (!Draws(path[j])) continue;
            const int n = 32;
            var prev = PointOn(path, j, 0);
            for (int k = 1; k <= n; k++)
            {
                var cur = PointOn(path, j, (double)k / n);
                double d = PathTools.Distance((x, y), prev, cur, out var tt);
                if (d < best.Item3) best = (j, (k - 1 + tt) / n, d);
                prev = cur;
            }
        }
        return best;
    }

    /// <summary>Ajoute un sommet sur le segment j au parametre t (la forme ne change pas) ; renvoie son indice.</summary>
    public static int InsertNode(List<PathSegment> path, int j, double t)
    {
        t = Math.Clamp(t, 0.01, 0.99);
        var s = path[j];
        var p0 = StartOf(path, j);
        if (s.A == "C")
        {
            var c1 = (X: s.X1 ?? p0.X, Y: s.Y1 ?? p0.Y);
            var c2 = (X: s.X2 ?? s.X ?? 0, Y: s.Y2 ?? s.Y ?? 0);
            var p = Pt(s);
            (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            var a1 = Lerp(p0, c1); var bb = Lerp(c1, c2); var cc = Lerp(c2, p);
            var d = Lerp(a1, bb); var e = Lerp(bb, cc); var f = Lerp(d, e);
            path.Insert(j, new PathSegment { A = "C", X1 = a1.X, Y1 = a1.Y, X2 = d.X, Y2 = d.Y, X = f.X, Y = f.Y });
            s.X1 = e.X; s.Y1 = e.Y; s.X2 = cc.X; s.Y2 = cc.Y;
            return j;
        }
        var m = PointOn(path, j, t);
        path.Insert(j, new PathSegment { A = "L", X = m.X, Y = m.Y });
        return j;
    }

    /// <summary>Supprime un sommet (un sous-trace garde au moins 2 sommets, 3 s'il est ferme) ; renvoie faux sinon.</summary>
    public static bool DeleteNode(List<PathSegment> path, int i)
    {
        var (m, e, closed) = Figure(path, i);
        int alias = closed ? AliasOfStart(path, m) : -1;
        int count = Enumerable.Range(m, e - m + 1).Count(k => (path[k].A == "M" || Draws(path[k])) && k != alias);
        if (count <= (closed ? 3 : 2)) return false;
        if (path[i].A == "M")
        {
            // le sommet suivant devient le depart ; la fermeture le rejoint
            var next = path[i + 1];
            path.RemoveAt(i);
            next.A = "M"; next.X1 = next.Y1 = next.X2 = next.Y2 = null;
            if (alias >= 0) { var al = path[alias - 1]; al.X = next.X; al.Y = next.Y; }
            return true;
        }
        path.RemoveAt(i);
        return true;
    }

    /// <summary>
    /// Recalcule le cadre de l'objet d'apres son trace. Objet tourne : le trace et le cadre sont decales pour que
    /// rien ne bouge a l'ecran (la rotation se fait autour du centre du cadre, qui a change).
    /// </summary>
    public static void FitFrame(Shape s)
    {
        if (s.Path is null || PathTools.Bounds(s.Path) is not { } b) return;
        double tx = 0, ty = 0;
        if (Math.Abs(s.Rotation % 360) > 0.01)
        {
            double a = s.Rotation * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
            double dx = b.Cx - (s.X + s.W / 2), dy = b.Cy - (s.Y + s.H / 2);
            // t = (R - I)(c' - c)
            tx = c * dx - sn * dy - dx;
            ty = sn * dx + c * dy - dy;
            PathTools.Transform(s.Path, (x, y) => (x + tx, y + ty));
        }
        s.X = b.X + tx; s.Y = b.Y + ty; s.W = Math.Max(0.5, b.W); s.H = Math.Max(0.5, b.H);
    }
}
