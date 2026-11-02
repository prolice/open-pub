using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpenPub.Core.Io;
using OpenPub.Core.Output;

namespace OpenPub.App.Output;

/// <summary>
/// Modes en ligne de commande des sorties (outillage, tests, diagnostic) :
///   OpenPub.exe --pdf fichier.opub|document.json sortie.pdf [--pages 1-3] [--bleed [mm]] [--marks] [--pdfx [profil.icc]] [--dpi n]
///   OpenPub.exe --images fichier.opub|document.json dossier [--jpeg] [--dpi n] [--pages 1-3]
/// Le fichier d'entree n'est jamais modifie.
/// </summary>
public static class OutputCli
{
    static string? Option(string[] args, string name, bool valueOptional = false)
    {
        int i = Array.IndexOf(args, name);
        if (i < 0) return null;
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[i + 1];
        return valueOptional ? "" : null;
    }

    static double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    public static int Pdf(string[] args)
    {
        using var pkg = OpubPackage.Open(args[1], FontEmbedder.DefaultSearchDirs);
        using var session = new EditorSession(pkg);
        var pages = PageRange.Parse(Option(args, "--pages"), session.Doc.Pages.Count, out var err);
        if (pages is null) { Console.Error.WriteLine(err); return 1; }
        double bleed = 0;
        if (Option(args, "--bleed", valueOptional: true) is { } b) bleed = (b.Length == 0 ? 3 : Num(b)) * PrintSheet.PtPerMm;
        var o = new PdfExportOptions
        {
            Pages = pages, Bleed = bleed, CropMarks = args.Contains("--marks"),
            PdfX4 = args.Contains("--pdfx"), OutputProfile = Option(args, "--pdfx") is { Length: > 0 } icc ? icc : null,
            RasterDpi = Option(args, "--dpi") is { } d ? Num(d) : 300,
            Title = Path.GetFileNameWithoutExtension(args[1]) is "document" ? Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(args[1]))) : null,
        };
        var sw = Stopwatch.StartNew();
        var stats = PdfExporter.Export(session, args[2], o);
        sw.Stop();
        var sum = PdfExporter.Inspect(args[2]);
        Console.WriteLine($"OK -> {args[2]} : {sum.Pages} page(s), {new FileInfo(args[2]).Length / 1024.0:0} Ko, {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  rendu : {stats}");
        Console.WriteLine($"  feuille : {string.Join(", ", sum.Sizes.Distinct().Select(s => $"{s.W:0.##} x {s.H:0.##} pt"))}" +
                          (sum.TrimSizes.FirstOrDefault() is { } t ? $" ; format fini {t.W:0.##} x {t.H:0.##} pt" : ""));
        Console.WriteLine($"  polices incorporees : {string.Join(", ", sum.EmbeddedFonts.OrderBy(x => x))}");
        if (sum.MissingFonts.Count > 0) Console.WriteLine($"  POLICES NON INCORPOREES : {string.Join(", ", sum.MissingFonts)}");
        Console.WriteLine($"  images : {sum.Images}" + (sum.PdfXVersion is { } v ? $" ; {v}, intention de sortie : {(sum.OutputIntent ? "oui" : "NON")}" : ""));
        if (session.Fonts.Substituted.Count > 0) Console.WriteLine("  polices remplacees : " + string.Join(", ", session.Fonts.Substituted));
        return sum.MissingFonts.Count == 0 ? 0 : 1;
    }

    public static int Images(string[] args)
    {
        using var pkg = OpubPackage.Open(args[1], FontEmbedder.DefaultSearchDirs);
        using var session = new EditorSession(pkg);
        var pages = PageRange.Parse(Option(args, "--pages"), session.Doc.Pages.Count, out var err);
        if (pages is null) { Console.Error.WriteLine(err); return 1; }
        double dpi = Option(args, "--dpi") is { } d ? Num(d) : 150;
        var files = Exporter.ExportImages(session, args[2], args.Contains("--jpeg") ? ImageFormat.Jpeg : ImageFormat.Png, dpi, pages);
        foreach (var f in files) Console.WriteLine($"{f} ({new FileInfo(f).Length / 1024.0:0} Ko)");
        return 0;
    }
}
