using System.Windows;
using OpenPub.Core.Model;

namespace OpenPub.App.Controls;

/// <summary>
/// Magnetisme : aligne un rectangle en mouvement sur les bords et le centre de la page, les marges
/// et les bords / centres des autres objets. Renvoie le decalage a appliquer et les reperes a afficher.
/// </summary>
public static class Snapper
{
    public static IEnumerable<double> TargetsX(Page page, PubDocument doc, Shape? exclude)
    {
        var m = Margins(doc);
        yield return 0; yield return page.Width / 2; yield return page.Width;
        yield return m[3]; yield return page.Width - m[1];
        foreach (var s in Others(page, exclude)) { yield return s.X; yield return s.X + s.W / 2; yield return s.X + s.W; }
    }

    public static IEnumerable<double> TargetsY(Page page, PubDocument doc, Shape? exclude)
    {
        var m = Margins(doc);
        yield return 0; yield return page.Height / 2; yield return page.Height;
        yield return m[0]; yield return page.Height - m[2];
        foreach (var s in Others(page, exclude)) { yield return s.Y; yield return s.Y + s.H / 2; yield return s.Y + s.H; }
    }

    static IEnumerable<Shape> Others(Page page, Shape? exclude) =>
        page.Shapes.Where(s => s != exclude && !s.Background && !(s.W >= page.Width * 0.95 && s.H >= page.Height * 0.95));

    /// <summary>Marges de la page (haut, droite, bas, gauche), en points ; 0,5 pouce par defaut.</summary>
    public static double[] Margins(PubDocument doc) => doc.Margins is { Length: 4 } m ? m : new[] { 36.0, 36.0, 36.0, 36.0 };

    /// <summary>Meilleur alignement d'une des valeurs mobiles sur une cible, dans la tolerance.</summary>
    public static double Best(IEnumerable<double> moving, IEnumerable<double> targets, double tol, List<double> guides)
    {
        var t = targets.ToList();
        double best = double.NaN, bestAbs = tol;
        foreach (var v in moving)
            foreach (var target in t)
            {
                var d = target - v;
                if (Math.Abs(d) <= bestAbs) { bestAbs = Math.Abs(d); best = d; }
            }
        guides.Clear();
        if (double.IsNaN(best)) return 0;
        foreach (var v in moving)
            foreach (var target in t)
                if (Math.Abs(target - (v + best)) < 0.01 && !guides.Contains(target)) guides.Add(target);
        return best;
    }

    public static Rect SnapMove(Rect r, Page page, PubDocument doc, Shape moving, double tol, List<double> gx, List<double> gy)
    {
        double dx = Best(new[] { r.Left, r.Left + r.Width / 2, r.Right }, TargetsX(page, doc, moving), tol, gx);
        double dy = Best(new[] { r.Top, r.Top + r.Height / 2, r.Bottom }, TargetsY(page, doc, moving), tol, gy);
        r.X += dx; r.Y += dy;
        return r;
    }

    /// <summary>Redimensionnement : seuls les bords tires par la poignee s'alignent.</summary>
    public static Rect SnapResize(Rect r, int handle, Page page, PubDocument doc, Shape moving, double tol, List<double> gx, List<double> gy)
    {
        double x0 = r.Left, y0 = r.Top, x1 = r.Right, y1 = r.Bottom;
        bool left = handle is 0 or 6 or 7, right = handle is 2 or 3 or 4, top = handle is 0 or 1 or 2, bottom = handle is 4 or 5 or 6;
        gx.Clear(); gy.Clear();
        if (left) x0 += Best(new[] { x0 }, TargetsX(page, doc, moving), tol, gx);
        if (right) x1 += Best(new[] { x1 }, TargetsX(page, doc, moving), tol, gx);
        if (top) y0 += Best(new[] { y0 }, TargetsY(page, doc, moving), tol, gy);
        if (bottom) y1 += Best(new[] { y1 }, TargetsY(page, doc, moving), tol, gy);
        return new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }
}
