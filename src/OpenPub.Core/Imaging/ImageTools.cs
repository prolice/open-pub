using OpenPub.Core.Model;

namespace OpenPub.Core.Imaging;

/// <summary>Ajustement d'une image a un cadre.</summary>
public static class ImageFit
{
    /// <summary>
    /// Recadrage centre pour que l'image remplisse le cadre sans deformation (parts retirees a gauche, en haut, a droite,
    /// en bas, comme <see cref="ImageFx.CropLeft"/>...).
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) CoverCrop(double imageW, double imageH, double frameW, double frameH)
    {
        if (imageW <= 0 || imageH <= 0 || frameW <= 0 || frameH <= 0) return (0, 0, 0, 0);
        double ri = imageW / imageH, rf = frameW / frameH;
        if (Math.Abs(ri - rf) < 1e-6) return (0, 0, 0, 0);
        if (ri > rf)
        {
            double cut = (1 - rf / ri) / 2;
            return (cut, 0, cut, 0);
        }
        double c = (1 - ri / rf) / 2;
        return (0, c, 0, c);
    }

    /// <summary>Applique un recadrage « remplir » aux retouches d'une image (les autres retouches sont gardees).</summary>
    public static ImageFx? WithCover(ImageFx? fx, double imageW, double imageH, double frameW, double frameH)
    {
        var (l, t, r, b) = CoverCrop(imageW, imageH, frameW, frameH);
        var n = fx?.Clone() ?? new ImageFx();
        n.CropLeft = l; n.CropTop = t; n.CropRight = r; n.CropBottom = b;
        return n.IsEmpty ? null : n;
    }
}

/// <summary>Utilisation d'une image du paquet : taille sur la page (points) et part de l'image visible dans le cadre.</summary>
public sealed record ImageUse(string Src, double WidthPt, double HeightPt, double VisibleW = 1, double VisibleH = 1, bool Cover = false, bool Tile = false);

/// <summary>Image a reduire : taille actuelle et nouvelle taille (pixels).</summary>
public sealed record CompressionTarget(string Src, int Width, int Height, int NewWidth, int NewHeight);

/// <summary>
/// Compression des images : on calcule, pour chaque image du paquet, la resolution utile d'apres la plus grande taille a
/// laquelle elle est affichee (recadrage compris) ; une image plus grande que necessaire est reduite a cette resolution
/// (300 ppp par defaut, la qualite d'impression).
/// </summary>
public static class ImageCompression
{
    /// <summary>Utilisations des images d'un document (images, sauf espaces reserves, et remplissages par image).</summary>
    public static List<ImageUse> Uses(PubDocument doc)
    {
        var list = new List<ImageUse>();
        foreach (var s in doc.AllShapes())
        {
            if (s.IsImage && !string.IsNullOrEmpty(s.Src))
            {
                var fx = s.ImageFx;
                double vw = fx is null ? 1 : 1 - fx.CropLeft - fx.CropRight, vh = fx is null ? 1 : 1 - fx.CropTop - fx.CropBottom;
                list.Add(new ImageUse(s.Src!, s.W, s.H, Math.Max(0.01, vw), Math.Max(0.01, vh)));
            }
            if (s.Fill?.Image is { Length: > 0 } img)
                list.Add(new ImageUse(img, s.W, s.H, Cover: true, Tile: s.Fill.ImageTile == true));
        }
        return list;
    }

    /// <param name="pixelSize">Taille en pixels d'une image du paquet (null si illisible).</param>
    /// <param name="margin">Une image n'est reduite que si elle depasse la resolution utile de ce facteur.</param>
    public static List<CompressionTarget> Plan(IEnumerable<ImageUse> uses, Func<string, (int W, int H)?> pixelSize, double dpi = 300, double margin = 1.15)
    {
        var result = new List<CompressionTarget>();
        foreach (var g in uses.GroupBy(u => u.Src, StringComparer.OrdinalIgnoreCase))
        {
            // une mosaique garde ses pixels (la taille de ses carreaux en depend)
            if (g.Any(u => u.Tile) || pixelSize(g.Key) is not { } px || px.W <= 0 || px.H <= 0) continue;
            double scale = 0;
            foreach (var u in g)
            {
                double vw = u.VisibleW, vh = u.VisibleH;
                if (u.Cover)
                {
                    var (l, t, r, b) = ImageFit.CoverCrop(px.W, px.H, u.WidthPt, u.HeightPt);
                    vw = 1 - l - r; vh = 1 - t - b;
                }
                double needW = u.WidthPt / 72 * dpi / Math.Max(0.01, vw), needH = u.HeightPt / 72 * dpi / Math.Max(0.01, vh);
                scale = Math.Max(scale, Math.Max(needW / px.W, needH / px.H));
            }
            if (scale <= 0 || scale * margin >= 1) continue;
            int nw = Math.Max(16, (int)Math.Ceiling(px.W * scale)), nh = Math.Max(16, (int)Math.Ceiling(px.H * scale));
            if (nw >= px.W || nh >= px.H) continue;
            result.Add(new CompressionTarget(g.Key, px.W, px.H, nw, nh));
        }
        return result;
    }
}
