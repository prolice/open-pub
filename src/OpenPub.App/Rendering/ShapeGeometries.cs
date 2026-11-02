using System.Windows;
using System.Windows.Media;
using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.App.Rendering;

/// <summary>
/// Formes automatiques, recalculees a partir du cadre de l'objet (elles suivent donc le redimensionnement). Le contour
/// vient de la bibliotheque du noyau (<see cref="ShapeLibrary"/>) ; ici il est seulement converti en geometrie WPF.
/// </summary>
public static class ShapeGeometries
{
    /// <summary>Toutes les formes (identifiant, libelle), dans l'ordre des categories.</summary>
    public static readonly (string Id, string Label)[] Catalog = ShapeLibrary.All.Select(d => (d.Id, d.Label)).ToArray();

    /// <summary>Formes de la galerie de l'onglet Dessin (14 formes courantes sur 3 rangees ; toutes les autres sont dans le menu « Plus de formes »).</summary>
    public static readonly string[] Favorites =
    {
        ShapeGeometry.Rect, ShapeGeometry.RoundRect, ShapeGeometry.Ellipse, ShapeGeometry.Triangle, ShapeGeometry.Star,
        ShapeGeometry.Heart, "cloud", "sun", ShapeGeometry.Arrow, "arrowLeftRight", "curvedArrow", ShapeGeometry.Bubble, "ovalCallout", "ribbon",
    };

    public static double DefaultRadius(Rect r) => Math.Min(r.Width, r.Height) * 0.15;

    public static Geometry Build(string geometry, Rect r, double? cornerRadius, IReadOnlyList<double>? adjust = null)
    {
        Geometry g = geometry == ShapeGeometry.Ellipse
            ? new EllipseGeometry(r)
            : PageRenderer.PathGeometry(ShapeLibrary.Outline(geometry, new Box(r.X, r.Y, r.Width, r.Height), adjust, cornerRadius));
        if (g.CanFreeze && !g.IsFrozen) g.Freeze();
        return g;
    }

    /// <summary>Vignette d'une forme (galeries, menus).</summary>
    public static System.Windows.Shapes.Path Icon(string id, Brush fill, double w = 20, double h = 16) => new()
    {
        Data = Build(id, new Rect(0, 0, 18, 14), null), Fill = fill, Stretch = Stretch.Uniform, Width = w, Height = h,
    };
}
