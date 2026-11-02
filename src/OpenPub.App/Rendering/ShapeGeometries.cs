using System.Windows;
using System.Windows.Media;
using OpenPub.Core.Model;

namespace OpenPub.App.Rendering;

/// <summary>Formes automatiques, recalculees a partir du cadre de l'objet (elles suivent donc le redimensionnement).</summary>
public static class ShapeGeometries
{
    public static readonly (string Id, string Label)[] Catalog =
    {
        (ShapeGeometry.Rect, "Rectangle"),
        (ShapeGeometry.RoundRect, "Rectangle à coins arrondis"),
        (ShapeGeometry.Ellipse, "Ellipse / cercle"),
        (ShapeGeometry.Triangle, "Triangle"),
        (ShapeGeometry.Diamond, "Losange"),
        (ShapeGeometry.Hexagon, "Hexagone"),
        (ShapeGeometry.Star, "Étoile"),
        (ShapeGeometry.Arrow, "Flèche"),
        (ShapeGeometry.Bubble, "Bulle de dialogue"),
        (ShapeGeometry.Heart, "Cœur"),
    };

    public static double DefaultRadius(Rect r) => Math.Min(r.Width, r.Height) * 0.15;

    public static Geometry Build(string geometry, Rect r, double? cornerRadius)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        Geometry g = geometry switch
        {
            ShapeGeometry.RoundRect => RoundRect(r, Math.Min(cornerRadius ?? DefaultRadius(r), Math.Min(w, h) / 2)),
            ShapeGeometry.Ellipse => new EllipseGeometry(r),
            ShapeGeometry.Triangle => Poly((x + w / 2, y), (x + w, y + h), (x, y + h)),
            ShapeGeometry.Diamond => Poly((x + w / 2, y), (x + w, y + h / 2), (x + w / 2, y + h), (x, y + h / 2)),
            ShapeGeometry.Hexagon => Poly((x + w * 0.25, y), (x + w * 0.75, y), (x + w, y + h / 2), (x + w * 0.75, y + h), (x + w * 0.25, y + h), (x, y + h / 2)),
            ShapeGeometry.Star => Star(r, 5, 0.382),
            ShapeGeometry.Arrow => Poly((x, y + h * 0.3), (x + w * 0.6, y + h * 0.3), (x + w * 0.6, y), (x + w, y + h / 2),
                                        (x + w * 0.6, y + h), (x + w * 0.6, y + h * 0.7), (x, y + h * 0.7)),
            ShapeGeometry.Bubble => Bubble(r),
            ShapeGeometry.Heart => Heart(r),
            _ => new RectangleGeometry(r),
        };
        if (g.CanFreeze) g.Freeze();
        return g;
    }

    static Geometry RoundRect(Rect r, double radius) => new RectangleGeometry(r, radius, radius);

    static Geometry Poly(params (double X, double Y)[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
        }
        return g;
    }

    static Geometry Star(Rect r, int branches, double inner)
    {
        var pts = new (double, double)[branches * 2];
        double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        for (int i = 0; i < branches * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / branches;
            double k = i % 2 == 0 ? 1 : inner;
            pts[i] = (cx + Math.Cos(a) * r.Width / 2 * k, cy + Math.Sin(a) * r.Height / 2 * k);
        }
        return Poly(pts);
    }

    static Geometry Bubble(Rect r)
    {
        // rectangle arrondi + pointe en bas a gauche (bulle de BD)
        double x = r.X, y = r.Y, w = r.Width, h = r.Height * 0.8, rad = Math.Min(w, h) * 0.2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x + rad, y), true, true);
            c.LineTo(new Point(x + w - rad, y), true, true);
            c.ArcTo(new Point(x + w, y + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(x + w, y + h - rad), true, true);
            c.ArcTo(new Point(x + w - rad, y + h), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(x + w * 0.42, y + h), true, true);
            c.LineTo(new Point(x + w * 0.18, r.Bottom), true, true);       // pointe
            c.LineTo(new Point(x + w * 0.26, y + h), true, true);
            c.LineTo(new Point(x + rad, y + h), true, true);
            c.ArcTo(new Point(x, y + h - rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(x, y + rad), true, true);
            c.ArcTo(new Point(x + rad, y), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, true);
        }
        return g;
    }

    static Geometry Heart(Rect r)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x + w / 2, y + h), true, true);
            c.BezierTo(new Point(x - w * 0.1, y + h * 0.55), new Point(x + w * 0.05, y - h * 0.1), new Point(x + w / 2, y + h * 0.25), true, true);
            c.BezierTo(new Point(x + w * 0.95, y - h * 0.1), new Point(x + w * 1.1, y + h * 0.55), new Point(x + w / 2, y + h), true, true);
        }
        return g;
    }
}
