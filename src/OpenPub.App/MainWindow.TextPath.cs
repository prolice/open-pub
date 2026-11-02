using System.Windows;
using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.App;

// Texte le long d'un trace libre (lot 12) : un trace (ligne, main levee, forme libre ouverte) devient un WordArt
// dont les lettres suivent ce trace.
public partial class MainWindow
{
    /// <summary>WordArt dont la ligne de base suit le trace (points en parts du cadre, qui suivent le redimensionnement).</summary>
    internal static Shape? TextAlongPath(Shape path, string text, string family = "Arial Black")
    {
        if (path.Path is not { Count: >= 2 } segs) return null;
        var fig = PathTools.Flatten(segs).OrderByDescending(f => f.Points.Count).FirstOrDefault();
        if (fig is null || fig.Points.Count < 2) return null;
        var pts = fig.Points;
        double x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y), x1 = pts.Max(p => p.X), y1 = pts.Max(p => p.Y);
        // marge pour la hauteur des lettres au-dessus de la ligne de base
        double pad = Math.Max(24, Math.Max(x1 - x0, y1 - y0) * 0.15);
        double fx = x0 - pad, fy = y0 - pad, fw = x1 - x0 + 2 * pad, fh = y1 - y0 + 2 * pad;
        var wa = new WordArtFx
        {
            Text = text, Family = family, Warp = WordArtWarps.Path,
            PathPoints = pts.Select(p => new[] { (p.X - fx) / fw, (p.Y - fy) / fh }).ToList(),
        };
        return new Shape
        {
            Kind = ShapeKind.WordArt, X = fx, Y = fy, W = fw, H = fh, WordArt = wa,
            Fill = new Fill { Color = path.Line?.Color ?? "#9935f4" },
        };
    }

    void TextOnPath_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || SelPage is not { } page || Sel is not { Path: { Count: >= 2 } } path || path.Kind == ShapeKind.WordArt)
        {
            StatusText.Text = "Sélectionnez d'abord une ligne ou un tracé (Dessin ▸ Crayon, Forme libre ou Ligne).";
            return;
        }
        var text = AskText("Texte sur le tracé", "Texte à écrire le long du tracé :", "Votre texte le long du tracé");
        if (text is null || TextAlongPath(path, text) is not { } wa) return;
        _session.Change(page, () =>
        {
            int i = page.Shapes.IndexOf(path);
            page.Shapes[i] = wa;                    // le texte remplace le trace, au meme plan
            Renumber(page);
        });
        _session.Select(wa, page);
        StatusText.Text = "Texte placé le long du tracé (Format ▸ WordArt pour le modifier).";
    }
}
