using OpenPub.Core.Model;

namespace OpenPub.Core.Geometry;

/// <summary>Position d'un glyphe : milieu de sa chasse sur la ligne de base, direction et etirement.</summary>
public readonly record struct GlyphPlacement(double X, double Y, double AngleDeg, double ScaleX, double ScaleY);

/// <summary>Resultat du placement : taille de police (points) et position de chaque glyphe (repere du cadre).</summary>
public sealed record WordArtLayout(double Size, List<GlyphPlacement> Glyphs);

/// <summary>
/// Placement des glyphes d'un texte decoratif (WordArt) dans un cadre : texte droit etire, arc vers le haut ou le bas,
/// cercle, vague. Les chasses et la hauteur sont donnees pour une police de taille 1 ; le resultat est dans le repere
/// du cadre (0,0 en haut a gauche). Calcul pur, sans WPF : le rendu construit le contour de chaque glyphe et
/// l'applique a cette position.
/// </summary>
public static class TextOnPath
{
    /// <param name="advances">Chasse de chaque glyphe pour une taille de 1.</param>
    /// <param name="ascent">Hauteur au-dessus de la ligne de base pour une taille de 1.</param>
    /// <param name="descent">Profondeur sous la ligne de base pour une taille de 1.</param>
    /// <param name="path">Ligne de base d'un texte « le long d'un trace » (repere du cadre), pour la deformation « path ».</param>
    public static WordArtLayout Layout(IReadOnlyList<double> advances, double ascent, double descent, string? warp, double w, double h,
        IReadOnlyList<(double X, double Y)>? path = null)
    {
        var glyphs = new List<GlyphPlacement>();
        double total = advances.Sum();
        if (advances.Count == 0 || total <= 1e-9 || w <= 0.5 || h <= 0.5) return new WordArtLayout(0, glyphs);
        double em = Math.Max(0.1, ascent + descent);
        switch (warp)
        {
            case WordArtWarps.ArcUp:
            case WordArtWarps.ArcDown:
                return Arc(advances, total, ascent, descent, em, w, h, up: warp == WordArtWarps.ArcUp);
            case WordArtWarps.Circle:
                return Circle(advances, total, ascent, em, w, h);
            case WordArtWarps.Path when path is { Count: >= 2 }:
            {
                // le long du trace : la plus grande taille qui laisse tenir le texte sur la ligne (et pas plus de 40 % du cadre)
                var line = new Polyline(path);
                if (line.Length < 1) return new WordArtLayout(0, glyphs);
                double size = Math.Min(line.Length * 0.96 / total, Math.Max(w, h) * 0.4 / em);
                return new WordArtLayout(size, Along(advances, total, size, line));
            }
            case WordArtWarps.Wave:
            {
                // vague : le texte occupe toute la largeur, la ligne de base ondule (une periode)
                double size = h * 0.55 / em, amp = (h - size * em) / 2;
                double sx = w / (total * size), x = 0;
                foreach (var a in advances)
                {
                    double cx = (x + a / 2) * size * sx;
                    double phase = 2 * Math.PI * cx / w;
                    double y = h / 2 + size * (ascent - descent) / 2 - amp * Math.Sin(phase);
                    double slope = -amp * Math.Cos(phase) * 2 * Math.PI / w;
                    glyphs.Add(new GlyphPlacement(cx, y, Math.Atan(slope) * 180 / Math.PI, sx, 1));
                    x += a;
                }
                return new WordArtLayout(size, glyphs);
            }
            default:
            {
                // texte droit : il remplit le cadre (etire comme le WordArt de Publisher)
                double size = h / em, sx = w / (total * size), x = 0;
                foreach (var a in advances)
                {
                    glyphs.Add(new GlyphPlacement((x + a / 2) * size * sx, ascent * size, 0, sx, 1));
                    x += a;
                }
                return new WordArtLayout(size, glyphs);
            }
        }
    }

    static Polyline EllipseArc(double cx, double cy, double rx, double ry, double t0, double t1, int n = 240)
    {
        var pts = new List<(double, double)>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            double t = t0 + (t1 - t0) * i / n;
            pts.Add((cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
        }
        return new Polyline(pts);
    }

    /// <summary>Glyphes centres sur la polyligne (au milieu de sa longueur), espacement d'origine a la taille donnee.</summary>
    static List<GlyphPlacement> Along(IReadOnlyList<double> advances, double total, double size, Polyline line)
    {
        var list = new List<GlyphPlacement>();
        double s = (line.Length - total * size) / 2;
        foreach (var a in advances)
        {
            var (x, y, ang) = line.At(s + a * size / 2);
            list.Add(new GlyphPlacement(x, y, ang, 1, 1));
            s += a * size;
        }
        return list;
    }

    static WordArtLayout Arc(IReadOnlyList<double> adv, double total, double ascent, double descent, double em, double w, double h, bool up)
    {
        // taille : la plus grande qui laisse tenir le texte sur la demi-ellipse
        double size = Math.Min(h * 0.42, w * 0.3) / em;
        for (int k = 0; k < 60; k++)
        {
            var line = ArcLine(size, ascent, descent, w, h, up);
            if (line is not null && total * size <= line.Length * 0.96) return new WordArtLayout(size, Along(adv, total, size, line));
            size *= 0.94;
        }
        var last = ArcLine(size, ascent, descent, w, h, up);
        return new WordArtLayout(size, last is null ? new List<GlyphPlacement>() : Along(adv, total, size, last));
    }

    /// <summary>Ligne de base de l'arc : moitie haute (lue de gauche a droite par-dessus) ou basse (par-dessous) d'une ellipse.</summary>
    static Polyline? ArcLine(double size, double ascent, double descent, double w, double h, bool up)
    {
        double g = size * (ascent + descent);
        double rx = w / 2 - g * 0.6, cx = w / 2;
        if (rx <= 1) return null;
        if (up)
        {
            double cy = h - size * descent, ry = cy - size * ascent;
            return ry <= 1 ? null : EllipseArc(cx, cy, rx, ry, Math.PI, 2 * Math.PI);
        }
        else
        {
            double cy = size * ascent, ry = h - size * descent - cy;
            // parcours de gauche a droite par le bas : angles de pi a 0 (decroissants)
            return ry <= 1 ? null : EllipseArc(cx, cy, rx, ry, Math.PI, 0);
        }
    }

    static WordArtLayout Circle(IReadOnlyList<double> adv, double total, double ascent, double em, double w, double h)
    {
        double size = Math.Min(w, h) * 0.16 / em;
        for (int k = 0; k < 60; k++)
        {
            double rx = w / 2 - size * ascent, ry = h / 2 - size * ascent;
            if (rx > 1 && ry > 1)
            {
                // tour complet depuis le bas, sens horaire : le milieu du parcours (et du texte) est en haut
                var line = EllipseArc(w / 2, h / 2, rx, ry, Math.PI / 2, Math.PI * 2.5, 480);
                if (total * size <= line.Length * 0.97) return new WordArtLayout(size, Along(adv, total, size, line));
            }
            size *= 0.94;
        }
        return new WordArtLayout(size, new List<GlyphPlacement>());
    }
}
