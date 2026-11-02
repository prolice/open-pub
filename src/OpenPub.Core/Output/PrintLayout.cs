using System.Globalization;
using OpenPub.Core.Model;

namespace OpenPub.Core.Output;

/// <summary>
/// Logique des sorties sans dependance a WPF : choix des pages, imposition en livret, recto verso manuel,
/// fond perdu et traits de coupe. Unites : points (1/72 de pouce), origine en haut a gauche.
/// </summary>
public static class PageRange
{
    /// <summary>
    /// Lit une liste de pages « 1-3; 5, 8- » (numeros a partir de 1, dans l'ordre saisi, sans doublon).
    /// Vide ou « toutes » = toutes les pages. Renvoie null et un message en francais si la saisie est invalide.
    /// </summary>
    public static List<int>? Parse(string? text, int pageCount, out string? error)
    {
        error = null;
        var all = Enumerable.Range(1, Math.Max(0, pageCount)).ToList();
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("toutes", StringComparison.OrdinalIgnoreCase)) return all;
        var result = new List<int>();
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            int a, b;
            var dash = part.IndexOfAny(new[] { '-', '–', '—' });
            if (dash < 0)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out a)) { error = $"« {part} » n'est pas un numéro de page."; return null; }
                b = a;
            }
            else
            {
                var left = part[..dash].Trim();
                var right = part[(dash + 1)..].Trim();
                a = left.Length == 0 ? 1 : int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? l : -1;
                b = right.Length == 0 ? pageCount : int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : -1;
                if (a < 0 || b < 0) { error = $"« {part} » n'est pas une plage de pages valide (exemple : 2-4)."; return null; }
                if (b < a) (a, b) = (b, a);
            }
            if (a < 1 || b > pageCount) { error = $"La page {(a < 1 ? a : b)} n'existe pas : le document a {pageCount} page(s)."; return null; }
            for (int p = a; p <= b; p++) if (!result.Contains(p)) result.Add(p);
        }
        if (result.Count == 0) { error = "Aucune page choisie."; return null; }
        return result;
    }
}

/// <summary>Une face de feuille : deux pages cote a cote (0 = page blanche).</summary>
public readonly record struct SheetSide(int Sheet, bool Front, int Left, int Right);

/// <summary>Imposition en livret : feuilles pliees en deux, 2 pages par face, dans l'ordre de pliage.</summary>
public static class Booklet
{
    /// <summary>Nombre de pages du livret : multiple de 4 superieur ou egal (pages blanches ajoutees a la fin).</summary>
    public static int PaddedCount(int pages) => pages <= 0 ? 0 : (pages + 3) / 4 * 4;

    /// <summary>
    /// Faces a imprimer, dans l'ordre (recto puis verso de chaque feuille). Pour 8 pages : 8-1, 2-7, 6-3, 4-5.
    /// Les numeros superieurs a <paramref name="pages"/> valent 0 (page blanche).
    /// </summary>
    public static List<SheetSide> Sides(int pages)
    {
        int n = PaddedCount(pages);
        int Real(int p) => p <= pages ? p : 0;
        var sides = new List<SheetSide>();
        for (int s = 0; s < n / 4; s++)
        {
            sides.Add(new SheetSide(s + 1, true, Real(n - 2 * s), Real(1 + 2 * s)));
            sides.Add(new SheetSide(s + 1, false, Real(2 + 2 * s), Real(n - 1 - 2 * s)));
        }
        return sides;
    }
}

/// <summary>Recto verso manuel : d'abord toutes les faces avant, puis (pile retournee) toutes les faces arriere.</summary>
public static class ManualDuplex
{
    /// <summary>
    /// Separe une suite de faces (recto, verso, recto, verso...) en deux passages. Les versos sont completes par
    /// une face blanche (<paramref name="blank"/>) pour que chaque feuille de la pile en recoive une ;
    /// <paramref name="reverseBacks"/> inverse l'ordre du second passage (imprimantes qui empilent face dessus).
    /// </summary>
    public static (List<T> Fronts, List<T> Backs) Split<T>(IReadOnlyList<T> faces, T blank, bool reverseBacks = false)
    {
        var fronts = new List<T>();
        var backs = new List<T>();
        for (int i = 0; i < faces.Count; i++) (i % 2 == 0 ? fronts : backs).Add(faces[i]);
        while (backs.Count < fronts.Count) backs.Add(blank);
        if (reverseBacks) backs.Reverse();
        return (fronts, backs);
    }
}

/// <summary>Geometrie d'une page de PDF pour l'imprimeur : fond perdu et traits de coupe autour du format fini.</summary>
public sealed record PrintSheet(double PageWidth, double PageHeight, double Bleed, bool CropMarks)
{
    public const double PtPerMm = 72 / 25.4;
    /// <summary>Fond perdu habituel des imprimeurs : 3 mm.</summary>
    public static readonly double DefaultBleed = 3 * PtPerMm;
    /// <summary>Traits de coupe : 5 mm de long, a 3 mm du format fini (au moins le fond perdu).</summary>
    public static readonly double MarkLength = 5 * PtPerMm;
    public static readonly double MarkGap = 3 * PtPerMm;
    public const double MarkWeight = 0.25;

    /// <summary>Distance entre le bord du format fini et le debut d'un trait de coupe.</summary>
    public double MarkOffset => Math.Max(Bleed, MarkGap);

    /// <summary>Marge de la feuille PDF autour du format fini.</summary>
    public double Margin => CropMarks ? MarkOffset + MarkLength + 2 : Bleed;

    public double MediaWidth => PageWidth + 2 * Margin;
    public double MediaHeight => PageHeight + 2 * Margin;

    /// <summary>Format fini (TrimBox) dans la feuille : x, y, largeur, hauteur.</summary>
    public (double X, double Y, double W, double H) Trim => (Margin, Margin, PageWidth, PageHeight);

    /// <summary>Zone de fond perdu (BleedBox) : format fini agrandi du fond perdu.</summary>
    public (double X, double Y, double W, double H) BleedBox => (Margin - Bleed, Margin - Bleed, PageWidth + 2 * Bleed, PageHeight + 2 * Bleed);

    /// <summary>Les 8 traits de coupe (deux par coin), dans le prolongement des bords du format fini.</summary>
    public List<(double X1, double Y1, double X2, double Y2)> MarkLines()
    {
        var lines = new List<(double, double, double, double)>();
        if (!CropMarks) return lines;
        var (x, y, w, h) = Trim;
        double o = MarkOffset, l = MarkLength;
        foreach (var cx in new[] { x, x + w })
            foreach (var cy in new[] { y, y + h })
            {
                double sx = cx == x ? -1 : 1, sy = cy == y ? -1 : 1;
                lines.Add((cx + sx * o, cy, cx + sx * (o + l), cy));   // horizontal, dans le prolongement du bord haut ou bas
                lines.Add((cx, cy + sy * o, cx, cy + sy * (o + l)));   // vertical, dans le prolongement du bord gauche ou droit
            }
        return lines;
    }
}

/// <summary>Nouvelle position d'un objet etendu dans le fond perdu (et recadrage d'image corrige).</summary>
public sealed record BleedAdjust(double X, double Y, double W, double H, double CropLeft, double CropTop, double CropRight, double CropBottom);

/// <summary>
/// Fond perdu : les objets dont un bord touche le bord de la page (formes pleines, images) sont prolonges dans le fond
/// perdu, pour qu'aucun liseré blanc n'apparaisse apres la coupe. Les images gardent leurs proportions : on
/// decouvre d'abord la partie recadree de l'image, puis, s'il n'y en a pas assez, l'image est tres legerement agrandie.
/// </summary>
public static class BleedExtender
{
    /// <summary>Tolerance (points) pour considerer qu'un bord touche la page.</summary>
    public const double Tolerance = 1.0;

    public static BleedAdjust? Compute(Shape s, double pageW, double pageH, double bleed, double tol = Tolerance)
    {
        if (bleed <= 0 || s.IsText || Math.Abs(s.Rotation % 360) > 0.01 || s.W <= 0 || s.H <= 0) return null;
        // seuls les rectangles se prolongent sans deformer le dessin (une etoile ou un cercle serait etire)
        bool rectangular = s.Geometry is null or ShapeGeometry.Rect;
        if (!rectangular) return null;
        if (s.IsImage)
        {
            if (s.Clip is { Count: > 6 } || s.Tile || s.ImageFx is { ReflectionSize: > 0 }) return null;
        }
        else
        {
            // formes : seulement les rectangles pleins (pas les traces importes ni les lignes)
            if (s.Fill?.Color is null || (s.Path is { Count: > 0 } && s.Geometry is null) || s.Kind == ShapeKind.Ellipse) return null;
        }
        double eL = s.X <= tol ? s.X + bleed : 0;
        double eT = s.Y <= tol ? s.Y + bleed : 0;
        double eR = s.X + s.W >= pageW - tol ? pageW + bleed - (s.X + s.W) : 0;
        double eB = s.Y + s.H >= pageH - tol ? pageH + bleed - (s.Y + s.H) : 0;
        eL = Math.Max(0, eL); eT = Math.Max(0, eT); eR = Math.Max(0, eR); eB = Math.Max(0, eB);
        if (eL + eT + eR + eB < 1e-6) return null;
        var fx = s.ImageFx;
        double cl = fx?.CropLeft ?? 0, ct = fx?.CropTop ?? 0, cr = fx?.CropRight ?? 0, cb = fx?.CropBottom ?? 0;
        double nx = s.X - eL, ny = s.Y - eT, nw = s.W + eL + eR, nh = s.H + eT + eB;
        if (!s.IsImage) return new BleedAdjust(nx, ny, nw, nh, cl, ct, cr, cb);

        // image retournee : le bord affiche a gauche est le bord droit de la source
        double sL = eL, sR = eR, sT = eT, sB = eB;
        if (fx is { FlipH: true }) (sL, sR) = (sR, sL);
        if (fx is { FlipV: true }) (sT, sB) = (sB, sT);
        // part de la source par point affiche ; point fixe de l'image : le bord oppose au cote prolonge
        // (le centre si les deux cotes ou aucun le sont), pour deplacer le moins possible ce qui reste visible
        double fxw = (1 - cl - cr) / s.W, fyh = (1 - ct - cb) / s.H;
        double ax = sL > 0 && sR <= 0 ? s.W : sR > 0 && sL <= 0 ? 0 : s.W / 2;
        double ay = sT > 0 && sB <= 0 ? s.H : sB > 0 && sT <= 0 ? 0 : s.H / 2;
        double ux = cl + ax * fxw, uy = ct + ay * fyh;
        double dl = ax + sL, dr = s.W - ax + sR, dt = ay + sT, db = s.H - ay + sB;
        // agrandissement minimal k (le meme sur les deux axes) pour que la zone etendue reste dans l'image source
        double k = 1;
        k = Math.Max(k, dl * fxw / Math.Max(ux, 1e-9));
        k = Math.Max(k, dr * fxw / Math.Max(1 - ux, 1e-9));
        k = Math.Max(k, dt * fyh / Math.Max(uy, 1e-9));
        k = Math.Max(k, db * fyh / Math.Max(1 - uy, 1e-9));
        double ncl = ux - dl * fxw / k, ncr = 1 - (ux + dr * fxw / k);
        double nct = uy - dt * fyh / k, ncb = 1 - (uy + db * fyh / k);
        static double C(double v) => Math.Clamp(Math.Round(v, 9), 0, 1);
        return new BleedAdjust(nx, ny, nw, nh, C(ncl), C(nct), C(ncr), C(ncb));
    }

    /// <summary>
    /// Applique le fond perdu aux objets d'une page (temporairement, le temps de l'export) ;
    /// l'action renvoyee remet chaque objet exactement dans son etat d'origine.
    /// </summary>
    public static Action Apply(Page page, double bleed)
    {
        var undo = new List<Action>();
        foreach (var s in page.Shapes)
        {
            if (Compute(s, page.Width, page.Height, bleed) is not { } a) continue;
            double x = s.X, y = s.Y, w = s.W, h = s.H;
            var fx = s.ImageFx;
            s.X = a.X; s.Y = a.Y; s.W = a.W; s.H = a.H;
            if (s.IsImage)
            {
                var nfx = fx?.Clone() ?? new ImageFx();
                nfx.CropLeft = a.CropLeft; nfx.CropTop = a.CropTop; nfx.CropRight = a.CropRight; nfx.CropBottom = a.CropBottom;
                s.ImageFx = nfx;
            }
            undo.Add(() => { s.X = x; s.Y = y; s.W = w; s.H = h; s.ImageFx = fx; });
        }
        return () => { foreach (var u in undo) u(); };
    }
}
