using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.App.Controls;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

/// <summary>Format des images exportees.</summary>
public enum ImageFormat { Png, Jpeg }

/// <summary>Sorties images (PNG, JPEG) par page ; l'impression est dans <see cref="Output.PrintService"/>, le PDF dans <see cref="Output.PdfExporter"/>.</summary>
public static class Exporter
{
    /// <summary>Vue d'une page hors fenetre (sans reperes d'edition), prete a etre dessinee ou imprimee.</summary>
    public static PageView MakeView(EditorSession session, Page page, double zoom = 1)
    {
        var view = new PageView(session, page) { Interactive = false, Zoom = zoom };
        var size = new Size(page.Width * PageView.DipPerPoint * zoom, page.Height * PageView.DipPerPoint * zoom);
        view.Measure(size);
        view.Arrange(new Rect(size));
        view.UpdateLayout();
        return view;
    }

    public static List<string> ExportPng(EditorSession session, string dir, double dpi = 150) =>
        ExportImages(session, dir, ImageFormat.Png, dpi, null);

    /// <summary>
    /// Exporte des pages en images (une par page, « page-N.png » ou « .jpg »). <paramref name="pages"/> : numeros a
    /// partir de 1 (null = toutes) ; <paramref name="singleFile"/> : chemin exact quand une seule page est exportee.
    /// </summary>
    public static List<string> ExportImages(EditorSession session, string dir, ImageFormat format, double dpi, IReadOnlyList<int>? pages,
        string? singleFile = null, int jpegQuality = 92)
    {
        var files = new List<string>();
        var numbers = pages ?? Enumerable.Range(1, session.Doc.Pages.Count).ToList();
        foreach (var n in numbers)
        {
            var page = session.Doc.Pages[n - 1];
            var bmp = RenderPage(session, page, dpi);
            BitmapEncoder enc = format == ImageFormat.Jpeg ? new JpegBitmapEncoder { QualityLevel = jpegQuality } : new PngBitmapEncoder();
            // JPEG : pas de transparence, on aplatit sur du blanc (le fond de la page l'est deja)
            enc.Frames.Add(BitmapFrame.Create(format == ImageFormat.Jpeg ? new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0) : bmp));
            var path = singleFile is not null && numbers.Count == 1 ? singleFile
                : Path.Combine(dir, $"page-{page.Index}.{(format == ImageFormat.Jpeg ? "jpg" : "png")}");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var fs = File.Create(path)) enc.Save(fs);
            files.Add(path);
        }
        return files;
    }

    /// <summary>Image d'une page a la resolution demandee (points par pouce).</summary>
    public static BitmapSource RenderPage(EditorSession session, Page page, double dpi)
    {
        var view = MakeView(session, page);
        int w = (int)Math.Ceiling(page.Width * PageView.DipPerPoint * dpi / 96);
        int h = (int)Math.Ceiling(page.Height * PageView.DipPerPoint * dpi / 96);
        var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(view);
        rtb.Freeze();
        return rtb;
    }
}
