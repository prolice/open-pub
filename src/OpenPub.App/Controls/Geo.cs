using System.Windows;
using OpenPub.Core.Model;

namespace OpenPub.App.Controls;

/// <summary>Passage entre coordonnees de page et repere propre d'un objet tourne (non tourne, meme origine).</summary>
public static class Geo
{
    public static Point Center(Shape s) => new(s.X + s.W / 2, s.Y + s.H / 2);

    public static Point Rotate(Point p, Point c, double degrees)
    {
        if (Math.Abs(degrees % 360) < 0.01) return p;
        double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = p.X - c.X, dy = p.Y - c.Y;
        return new Point(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
    }

    /// <summary>Point de page -> repere de l'objet (annule sa rotation).</summary>
    public static Point ToLocal(Shape s, Point p) => Rotate(p, Center(s), -s.Rotation);

    /// <summary>Repere de l'objet -> point de page.</summary>
    public static Point ToPage(Shape s, Point local) => Rotate(local, Center(s), s.Rotation);

    public static double Normalize(double deg)
    {
        deg %= 360;
        if (deg > 180) deg -= 360;
        if (deg <= -180) deg += 360;
        return Math.Abs(deg) < 1e-9 ? 0 : deg;
    }
}
