using OpenPub.Core.Model;

namespace OpenPub.Core.Geometry;

/// <summary>Motif pose le long du cadre : centre (repere du cadre), direction (degres) et rang (alternance des couleurs).</summary>
public readonly record struct BorderTile(double X, double Y, double AngleDeg, int Index);

/// <summary>
/// Bordures decoratives (BorderArt) : petite bibliotheque de motifs dessines en code (libres de droits), repetes le
/// long du cadre d'un objet. Les motifs reutilisent les formes de <see cref="ShapeLibrary"/>.
/// </summary>
public static class BorderArtLayout
{
    public static readonly (string Id, string Label)[] Patterns =
    {
        ("hearts", "Cœurs"), ("stars", "Étoiles"), ("dots", "Pois"), ("diamonds", "Losanges"), ("triangles", "Triangles"),
        ("flowers", "Fleurs"), ("suns", "Soleils"), ("moons", "Lunes"), ("clouds", "Nuages"), ("chevrons", "Chevrons"),
        ("checker", "Damier"), ("lightning", "Éclairs"),
    };

    /// <summary>
    /// Centres des motifs : un dans chaque coin, puis repartis regulierement sur chaque bord (le motif est tourne
    /// vers l'exterieur : 0 en haut, 90 a droite, 180 en bas, 270 a gauche). Les motifs restent dans le cadre.
    /// </summary>
    public static List<BorderTile> Tiles(double w, double h, double size)
    {
        var list = new List<BorderTile>();
        if (size <= 0.5 || w < size || h < size) return list;
        double half = size / 2, x0 = half, y0 = half, x1 = w - half, y1 = h - half;
        int nx = Math.Max(0, (int)Math.Round((x1 - x0) / size) - 1), ny = Math.Max(0, (int)Math.Round((y1 - y0) / size) - 1);
        int k = 0;
        list.Add(new BorderTile(x0, y0, 0, k++));
        for (int i = 1; i <= nx; i++) list.Add(new BorderTile(x0 + (x1 - x0) * i / (nx + 1), y0, 0, k++));
        list.Add(new BorderTile(x1, y0, 90, k++));
        for (int i = 1; i <= ny; i++) list.Add(new BorderTile(x1, y0 + (y1 - y0) * i / (ny + 1), 90, k++));
        list.Add(new BorderTile(x1, y1, 180, k++));
        for (int i = 1; i <= nx; i++) list.Add(new BorderTile(x1 - (x1 - x0) * i / (nx + 1), y1, 180, k++));
        list.Add(new BorderTile(x0, y1, 270, k++));
        for (int i = 1; i <= ny; i++) list.Add(new BorderTile(x0, y1 - (y1 - y0) * i / (ny + 1), 270, k++));
        return list;
    }

    /// <summary>Contour d'un motif dans une cellule (non tournee) ; motif inconnu = pois.</summary>
    public static List<PathSegment> Motif(string? pattern, Box cell)
    {
        // le motif occupe 80 % de la cellule (un leger espace entre deux motifs), sauf le damier
        double k = pattern == "checker" ? 1 : 0.8;
        var b = new Box(cell.X + cell.W * (1 - k) / 2, cell.Y + cell.H * (1 - k) / 2, cell.W * k, cell.H * k);
        switch (pattern)
        {
            case "flowers":
            {
                var p = new PathBuilder();
                for (int i = 0; i < 6; i++)
                {
                    double t = i * Math.PI / 3;
                    p.Ellipse(b.Cx + Math.Cos(t) * b.W * 0.32, b.Cy + Math.Sin(t) * b.H * 0.32, b.W * 0.15, b.H * 0.15);
                }
                return p.Ellipse(b.Cx, b.Cy, b.W * 0.12, b.H * 0.12).Segments;
            }
            case "checker":
                return new PathBuilder().Poly((b.X, b.Y), (b.X + b.W, b.Y), (b.X + b.W, b.Y + b.H), (b.X, b.Y + b.H)).Segments;
        }
        var shape = pattern switch
        {
            "hearts" => ShapeGeometry.Heart, "stars" => ShapeGeometry.Star, "diamonds" => ShapeGeometry.Diamond,
            "triangles" => ShapeGeometry.Triangle, "suns" => "sun", "moons" => "moon", "clouds" => "cloud",
            "chevrons" => "chevron", "lightning" => "lightning", _ => ShapeGeometry.Ellipse,
        };
        return ShapeLibrary.Outline(shape, b);
    }
}
