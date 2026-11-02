using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.App.Rendering;

/// <summary>Images du paquet (chargees une fois, figees) et versions retouchees (recadrage, couleurs) en cache.</summary>
public sealed class ImageCache
{
    readonly OpubPackage _package;
    readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, BitmapSource> _processed = new();
    static readonly Dictionary<string, BitmapSource> Shadows = new();

    public ImageCache(OpubPackage package) => _package = package;

    public BitmapSource? Get(string? src)
    {
        if (string.IsNullOrEmpty(src)) return null;
        if (_cache.TryGetValue(src, out var b)) return b;
        var path = _package.ResolveAsset(src);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".wmf" or ".emf" && File.Exists(Path.ChangeExtension(path, ".png")))
            path = Path.ChangeExtension(path, ".png");
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            b = bmp;
        }
        catch (Exception)
        {
            b = null;   // image illisible : un cadre gris est dessine a la place
        }
        _cache[src] = b;
        return b;
    }

    /// <summary>Image recadree et retouchee (couleurs, luminosite, contraste).</summary>
    public BitmapSource? Get(string? src, ImageFx? fx)
    {
        var bmp = Get(src);
        if (bmp is null || fx is null || (!fx.HasCrop && !fx.HasPixelEffect)) return bmp;
        var key = src + "|" + fx.PixelKey();
        if (_processed.TryGetValue(key, out var done)) return done;
        BitmapSource result = bmp;
        if (fx.HasCrop)
        {
            int x = (int)Math.Round(fx.CropLeft * bmp.PixelWidth), y = (int)Math.Round(fx.CropTop * bmp.PixelHeight);
            int w = Math.Max(1, (int)Math.Round((1 - fx.CropLeft - fx.CropRight) * bmp.PixelWidth));
            int h = Math.Max(1, (int)Math.Round((1 - fx.CropTop - fx.CropBottom) * bmp.PixelHeight));
            w = Math.Min(w, bmp.PixelWidth - x); h = Math.Min(h, bmp.PixelHeight - y);
            if (w > 0 && h > 0) result = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
        }
        if (fx.HasPixelEffect) result = ApplyPixels(result, fx);
        if (result.CanFreeze && !result.IsFrozen) result.Freeze();
        _processed[key] = result;
        return result;
    }

    static BitmapSource ApplyPixels(BitmapSource src, ImageFx fx)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[h * stride];
        conv.CopyPixels(px, stride, 0);
        double bright = fx.Brightness * 255 * 0.6;
        double contrast = 1 + fx.Contrast * (fx.Contrast > 0 ? 2 : 1);     // -1..1 -> 0..3
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            // luminosite puis contraste (autour du gris moyen)
            r = (r - 128) * contrast + 128 + bright;
            g = (g - 128) * contrast + 128 + bright;
            b = (b - 128) * contrast + 128 + bright;
            switch (fx.Recolor)
            {
                case "gray":
                {
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = g = b = l;
                    break;
                }
                case "bw":
                {
                    double l = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = g = b = l >= 128 ? 255 : 0;
                    break;
                }
                case "sepia":
                {
                    double nr = 0.393 * r + 0.769 * g + 0.189 * b;
                    double ng = 0.349 * r + 0.686 * g + 0.168 * b;
                    double nb = 0.272 * r + 0.534 * g + 0.131 * b;
                    r = nr; g = ng; b = nb;
                    break;
                }
                case "washout":
                {
                    // filigrane (comme Publisher) : image tres eclaircie et peu contrastee
                    r = 255 - (255 - r) * 0.28; g = 255 - (255 - g) * 0.28; b = 255 - (255 - b) * 0.28;
                    break;
                }
            }
            px[i] = (byte)Math.Clamp(b, 0, 255);
            px[i + 1] = (byte)Math.Clamp(g, 0, 255);
            px[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }
        var wb = BitmapSource.Create(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null, px, stride);
        wb.Freeze();
        return wb;
    }

    /// <summary>
    /// Silhouette floue d'une forme (ombre portee), rendue une fois puis mise en cache. La bitmap couvre
    /// le cadre de la forme agrandi de la marge de flou ; elle se dessine en <paramref name="drawRect"/>.
    /// </summary>
    public static BitmapSource Shadow(Geometry silhouette, Rect bounds, ShadowFx fx, out Rect drawRect)
    {
        double pad = fx.Blur * 2 + 1;
        drawRect = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + 2 * pad, bounds.Height + 2 * pad);
        const double scale = 2;            // 2 pixels par point
        var key = FormattableString.Invariant($"{silhouette}|{bounds.Width:0.#}x{bounds.Height:0.#}|{fx.Color}|{fx.Opacity:0.##}|{fx.Blur:0.#}");
        if (Shadows.TryGetValue(key, out var cached)) return cached;
        var dv = new DrawingVisual { Effect = new BlurEffect { Radius = fx.Blur * scale, KernelType = KernelType.Gaussian } };
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(pad - bounds.X, pad - bounds.Y));
            dc.DrawGeometry(PageRenderer.Brush(fx.Color, fx.Opacity), null, silhouette);
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(drawRect.Width * scale), (int)Math.Ceiling(drawRect.Height * scale), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        if (Shadows.Count > 200) Shadows.Clear();
        Shadows[key] = rtb;
        return rtb;
    }
}
