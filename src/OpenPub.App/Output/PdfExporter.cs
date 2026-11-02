using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Output;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App.Output;

/// <summary>Reglages de l'export PDF.</summary>
public sealed record PdfExportOptions
{
    /// <summary>Pages a exporter (numeros a partir de 1) ; null = toutes.</summary>
    public IReadOnlyList<int>? Pages { get; init; }
    /// <summary>Fond perdu en points (0 = aucun ; 3 mm = <see cref="PrintSheet.DefaultBleed"/>).</summary>
    public double Bleed { get; init; }
    public bool CropMarks { get; init; }
    /// <summary>Profil PDF/X-4 (intention de sortie CMJN, couleurs RVB etiquetees sRGB).</summary>
    public bool PdfX4 { get; init; }
    /// <summary>Profil ICC CMJN de l'imprimeur (PDF/X-4) ; null = profil trouve dans Windows (FOGRA39 de preference).</summary>
    public string? OutputProfile { get; init; }
    public string? Title { get; init; }
    /// <summary>Resolution des effets rasterises (ombres floues, masques).</summary>
    public double RasterDpi { get; init; } = 300;
}

/// <summary>
/// Export PDF direct (PDFsharp, licence MIT), sans imprimante virtuelle : chaque page est dessinee par
/// <see cref="PageRenderer.DrawPage"/> dans un <see cref="DrawingGroup"/>, puis traduite par <see cref="DrawingToPdf"/>.
/// </summary>
public static class PdfExporter
{
    public static string ColorDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "spool", "drivers", "color");

    /// <summary>Profils CMJN installes, les plus courants en Europe d'abord (FOGRA39 / ISO Coated v2).</summary>
    public static List<string> CmykProfiles()
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(ColorDir)) return list;
            foreach (var f in Directory.EnumerateFiles(ColorDir).Where(f => f.EndsWith(".icc", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".icm", StringComparison.OrdinalIgnoreCase)))
                if (IccInfo(f) is { ColorSpace: "CMYK", Class: "prtr" }) list.Add(f);
        }
        catch (Exception) { }
        int Rank(string f)
        {
            var n = Path.GetFileName(f).ToLowerInvariant();
            return n.Contains("fogra39") ? 0 : n.Contains("isocoated") ? 1 : n.Contains("fogra") ? 2 : n.Contains("swop") ? 4 : 3;
        }
        return list.OrderBy(Rank).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Espace de couleur, classe et description d'un profil ICC (en-tete et balise « desc »).</summary>
    public static (string ColorSpace, string Class, string Description)? IccInfo(string path)
    {
        try
        {
            var d = File.ReadAllBytes(path);
            if (d.Length < 132 || Encoding.ASCII.GetString(d, 36, 4) != "acsp") return null;
            string cls = Encoding.ASCII.GetString(d, 12, 4), cs = Encoding.ASCII.GetString(d, 16, 4).Trim();
            string desc = Path.GetFileNameWithoutExtension(path);
            int n = (d[128] << 24) | (d[129] << 16) | (d[130] << 8) | d[131];
            for (int i = 0; i < n && 132 + 12 * i + 12 <= d.Length; i++)
            {
                int r = 132 + 12 * i;
                if (Encoding.ASCII.GetString(d, r, 4) != "desc") continue;
                int off = (d[r + 4] << 24) | (d[r + 5] << 16) | (d[r + 6] << 8) | d[r + 7];
                var type = Encoding.ASCII.GetString(d, off, 4);
                if (type == "desc")
                {
                    int len = (d[off + 8] << 24) | (d[off + 9] << 16) | (d[off + 10] << 8) | d[off + 11];
                    desc = Encoding.ASCII.GetString(d, off + 12, Math.Max(0, len - 1)).Trim();
                }
                else if (type == "mluc")
                {
                    int recLen = (d[off + 16] << 24) | (d[off + 17] << 16) | (d[off + 18] << 8) | d[off + 19];
                    int sOff = (d[off + 24] << 24) | (d[off + 25] << 16) | (d[off + 26] << 8) | d[off + 27];
                    desc = Encoding.BigEndianUnicode.GetString(d, off + sOff, recLen).Trim('\0', ' ');
                }
                break;
            }
            return (cs, cls, desc);
        }
        catch (Exception) { return null; }
    }

    public static string SrgbProfile => Path.Combine(ColorDir, "sRGB Color Space Profile.icm");

    /// <summary>Dessine une page dans un arbre Drawing (sans les reperes d'edition).</summary>
    public static DrawingGroup Render(EditorSession session, Page page)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open()) PageRenderer.DrawPage(dc, page, session, 1.0, editorMarks: false);
        group.Freeze();
        return group;
    }

    public static PdfStats Export(EditorSession session, string path, PdfExportOptions o)
    {
        PdfFontResolver.Install();
        var stats = new PdfStats();
        var doc = new PdfDocument();
        doc.Options.CompressContentStreams = true;
        doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        var now = DateTime.Now;
        doc.Info.Title = o.Title ?? Path.GetFileNameWithoutExtension(path);
        doc.Info.Creator = "OpenPub " + Update.UpdateService.CurrentVersion;
        doc.Info.CreationDate = now;
        doc.Info.ModificationDate = now;
        var fonts = new PdfFontCache();
        var images = new PdfImageCache();
        var numbers = o.Pages ?? Enumerable.Range(1, session.Doc.Pages.Count).ToList();
        PdfDictionary? srgb = null;
        if (o.PdfX4) srgb = SetupPdfX(doc, o, now);

        foreach (var n in numbers)
        {
            var page = session.Doc.Pages[n - 1];
            var sheet = new PrintSheet(page.Width, page.Height, Math.Max(0, o.Bleed), o.CropMarks);
            var pdfPage = doc.AddPage();
            pdfPage.Width = XUnit.FromPoint(sheet.MediaWidth);
            pdfPage.Height = XUnit.FromPoint(sheet.MediaHeight);
            if (o.Bleed > 0 || o.CropMarks || o.PdfX4)
            {
                // boites en coordonnees PDF (origine en bas a gauche ; marges symetriques)
                var (tx, ty, tw, th) = sheet.Trim;
                pdfPage.TrimBox = new PdfRectangle(new XRect(tx, sheet.MediaHeight - ty - th, tw, th));
                var (bx, by, bw, bh) = sheet.BleedBox;
                pdfPage.BleedBox = new PdfRectangle(new XRect(bx, sheet.MediaHeight - by - bh, bw, bh));
            }
            // fond perdu : objets prolonges le temps du dessin (la mise en page du texte, deja calculee, ne change pas)
            session.Layout(page);
            var restore = o.Bleed > 0 ? BleedExtender.Apply(page, o.Bleed) : null;
            DrawingGroup drawing;
            try { drawing = Render(session, page); }
            finally { restore?.Invoke(); }

            using (var g = XGraphics.FromPdfPage(pdfPage, XGraphicsUnit.Point))
            {
                var state = g.Save();
                g.TranslateTransform(sheet.Margin, sheet.Margin);
                var writer = new DrawingToPdf(g, doc, fonts, images, stats)
                {
                    RasterDpi = o.RasterDpi,
                    PageClip = o.Bleed > 0
                        ? (new Rect(0, 0, page.Width, page.Height), new Rect(-o.Bleed, -o.Bleed, page.Width + 2 * o.Bleed, page.Height + 2 * o.Bleed))
                        : null,
                };
                try { writer.Draw(drawing); }
                finally { writer.Detach(doc); }
                g.Restore(state);
                if (o.CropMarks)
                {
                    var pen = new XPen(XColors.Black, PrintSheet.MarkWeight);
                    foreach (var (x1, y1, x2, y2) in sheet.MarkLines()) g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
            // liens hypertextes actifs (PDF/X-4 interdit les annotations dans la zone imprimee)
            if (!o.PdfX4)
                foreach (var (r, url) in MainWindow.LinkAreas(session, page))
                    pdfPage.AddWebLink(new PdfRectangle(new XRect(sheet.Margin + r.X, sheet.MediaHeight - sheet.Margin - r.Y - r.Height, r.Width, r.Height)), url);
            if (srgb is not null)
            {
                // couleurs RVB du rendu (DeviceRGB) interpretees comme sRGB : exige par PDF/X-4 avec une sortie CMJN
                var cs = new PdfDictionary(doc);
                cs.Elements["/DefaultRGB"] = new PdfArray(doc, new PdfName("/ICCBased"), srgb.Reference!);
                pdfPage.Resources.Elements["/ColorSpace"] = cs;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        var bytes = ms.ToArray();
        // PDFsharp ecrit a l'enregistrement ses propres metadonnees XMP et un groupe de transparence DeviceRGB :
        // ceux du PDF/X-4 sont poses par mise a jour incrementale
        if (o.PdfX4) bytes = PdfIncrementalUpdate.ForPdfX(bytes, Encoding.UTF8.GetBytes(Xmp(doc, now)));
        File.WriteAllBytes(path, bytes);
        return stats;
    }

    /// <summary>
    /// PDF/X-4 : PDF 1.6, intention de sortie CMJN (profil ICC incorpore), cles GTS_PDFXVersion et Trapped,
    /// TrimBox et BleedBox sur chaque page, polices incorporees (metadonnees XMP : voir <see cref="Xmp"/>). Renvoie le profil sRGB.
    /// </summary>
    static PdfDictionary SetupPdfX(PdfDocument doc, PdfExportOptions o, DateTime now)
    {
        var profile = o.OutputProfile ?? CmykProfiles().FirstOrDefault()
                      ?? throw new InvalidOperationException("Aucun profil ICC CMJN n'est installé : choisissez le profil fourni par l'imprimeur.");
        var info = IccInfo(profile) ?? throw new InvalidOperationException("Profil ICC illisible : " + profile);
        if (info.ColorSpace != "CMYK") throw new InvalidOperationException("Le profil de sortie doit être un profil CMJN d'imprimante.");
        doc.Version = 16;
        doc.Info.Elements.SetString("/GTS_PDFXVersion", "PDF/X-4");
        doc.Info.Elements.SetName("/Trapped", "/False");

        var icc = new PdfDictionary(doc);
        icc.Elements.SetInteger("/N", 4);
        icc.CreateStream(File.ReadAllBytes(profile));
        doc.Internals.AddObject(icc);
        var intent = new PdfDictionary(doc);
        intent.Elements.SetName("/Type", "/OutputIntent");
        intent.Elements.SetName("/S", "/GTS_PDFX");
        intent.Elements.SetString("/OutputConditionIdentifier", info.Description);
        intent.Elements.SetString("/OutputCondition", info.Description);
        intent.Elements.SetString("/Info", info.Description);
        intent.Elements.SetString("/RegistryName", "http://www.color.org");
        intent.Elements.SetReference("/DestOutputProfile", icc);
        doc.Internals.AddObject(intent);
        doc.Internals.Catalog.Elements["/OutputIntents"] = new PdfArray(doc, intent.Reference!);

        var srgb = new PdfDictionary(doc);
        srgb.Elements.SetInteger("/N", 3);
        srgb.CreateStream(File.ReadAllBytes(SrgbProfile));
        doc.Internals.AddObject(srgb);

        return srgb;
    }

    /// <summary>Metadonnees XMP du PDF/X-4, coherentes avec le dictionnaire Info (titre, dates, producteur).</summary>
    static string Xmp(PdfDocument doc, DateTime now)
    {
        string date = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
        static string E(string? s) => System.Security.SecurityElement.Escape(s ?? "") ?? "";
        return $"""
            <?xpacket begin="{'\uFEFF'}" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                xmlns:pdf="http://ns.adobe.com/pdf/1.3/" xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/" xmlns:pdfxid="http://www.npes.org/pdfx/ns/id/">
               <dc:format>application/pdf</dc:format>
               <dc:title><rdf:Alt><rdf:li xml:lang="x-default">{E(doc.Info.Title)}</rdf:li></rdf:Alt></dc:title>
               <xmp:CreateDate>{date}</xmp:CreateDate>
               <xmp:ModifyDate>{date}</xmp:ModifyDate>
               <xmp:MetadataDate>{date}</xmp:MetadataDate>
               <xmp:CreatorTool>{E(doc.Info.Creator)}</xmp:CreatorTool>
               <pdf:Producer>{E(doc.Info.Producer)}</pdf:Producer>
               <pdf:Trapped>False</pdf:Trapped>
               <xmpMM:DocumentID>uuid:{Guid.NewGuid()}</xmpMM:DocumentID>
               <xmpMM:InstanceID>uuid:{Guid.NewGuid()}</xmpMM:InstanceID>
               <xmpMM:VersionID>1</xmpMM:VersionID>
               <xmpMM:RenditionClass>default</xmpMM:RenditionClass>
               <pdfxid:GTS_PDFXVersion>PDF/X-4</pdfxid:GTS_PDFXVersion>
              </rdf:Description>
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
    }

    /// <summary>Resume d'un PDF relu par PDFsharp (diagnostic, auto-test).</summary>
    public sealed record PdfSummary(int Pages, List<(double W, double H)> Sizes, List<(double W, double H)?> TrimSizes,
        HashSet<string> EmbeddedFonts, HashSet<string> MissingFonts, int Images, bool OutputIntent, string? PdfXVersion);

    public static PdfSummary Inspect(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var sizes = new List<(double, double)>();
        var trims = new List<(double, double)?>();
        var embedded = new HashSet<string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        int images = 0;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var p in doc.Pages)
        {
            sizes.Add((Math.Round(p.Width.Point, 2), Math.Round(p.Height.Point, 2)));
            trims.Add(p.Elements.ContainsKey("/TrimBox") ? (Math.Round(p.TrimBox.Width, 2), Math.Round(p.TrimBox.Height, 2)) : null);
            var res = p.Elements.GetDictionary("/Resources");
            if (res?.Elements.GetDictionary("/Font") is { } fontDict)
                foreach (var key in fontDict.Elements.Keys)
                {
                    if (fontDict.Elements.GetDictionary(key) is not { } f || !seen.Add(f)) continue;
                    var name = f.Elements.GetName("/BaseFont");
                    // police composite (Type0) : descripteur dans la police descendante
                    var desc = f.Elements.GetDictionary("/FontDescriptor");
                    if (desc is null && f.Elements.GetArray("/DescendantFonts") is { Elements.Count: > 0 } arr && arr.Elements.GetDictionary(0) is { } df)
                        desc = df.Elements.GetDictionary("/FontDescriptor");
                    bool emb = desc is not null && (desc.Elements.ContainsKey("/FontFile2") || desc.Elements.ContainsKey("/FontFile") || desc.Elements.ContainsKey("/FontFile3"));
                    (emb ? embedded : missing).Add(name.TrimStart('/'));
                }
            if (res?.Elements.GetDictionary("/XObject") is { } xo)
                foreach (var key in xo.Elements.Keys)
                    if (xo.Elements.GetDictionary(key) is { } x && x.Elements.GetName("/Subtype") == "/Image" && seen.Add(x)) images++;
        }
        bool oi = doc.Internals.Catalog.Elements.ContainsKey("/OutputIntents");
        var pdfx = doc.Info.Elements.GetString("/GTS_PDFXVersion");
        return new PdfSummary(doc.PageCount, sizes, trims, embedded, missing, images, oi, string.IsNullOrEmpty(pdfx) ? null : pdfx);
    }
}
