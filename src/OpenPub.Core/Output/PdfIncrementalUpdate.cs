using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenPub.Core.Output;

/// <summary>
/// Mise a jour incrementale d'un PDF (ajout en fin de fichier, mecanisme standard du format) pour le PDF/X-4 :
/// la bibliotheque PDF ecrit a l'enregistrement ses propres metadonnees XMP et un groupe de transparence DeviceRGB
/// sur chaque page ; on remplace les metadonnees (qui doivent annoncer la norme) et on etalonne l'espace de fusion
/// des pages sur le profil deja utilise par DefaultRGB.
/// Limite : PDF a table de references classique (« xref » / « trailer »), sans flux d'objets, comme ceux de PDFsharp.
/// </summary>
public static class PdfIncrementalUpdate
{
    static readonly Encoding Latin1 = Encoding.Latin1;
    static readonly Regex ObjHead = new(@"(?<![0-9])(\d+)\s+(\d+)\s+obj\s*<<", RegexOptions.Compiled);
    static readonly Regex DefaultRgb = new(@"/DefaultRGB\s*\[\s*/ICCBased\s+(\d+\s+\d+\s+R)\s*\]", RegexOptions.Compiled);
    static readonly Regex GroupCs = new(@"(/Group\s*<<[^>]*?/CS\s*)/DeviceRGB", RegexOptions.Compiled);

    /// <summary>Nombre d'objets de page dont le groupe de transparence a ete etalonne au dernier appel (diagnostic).</summary>
    public static int LastCalibratedGroups { get; private set; }

    public static byte[] ForPdfX(byte[] pdf, byte[] xmp)
    {
        var text = Latin1.GetString(pdf);
        int sx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        int tr = text.LastIndexOf("trailer", StringComparison.Ordinal);
        if (sx < 0 || tr < 0 || tr > sx) throw new InvalidDataException("PDF sans table de références classique.");
        var prev = long.Parse(Regex.Match(text[(sx + 9)..], @"\d+").Value, CultureInfo.InvariantCulture);
        var trailer = text[tr..sx];
        int size = int.Parse(Regex.Match(trailer, @"/Size\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+(\d+)\s+R");
        if (!root.Success) throw new InvalidDataException("Catalogue introuvable.");
        int rootNum = int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture), rootGen = int.Parse(root.Groups[2].Value, CultureInfo.InvariantCulture);
        var info = Regex.Match(trailer, @"/Info\s+\d+\s+\d+\s+R");
        var id = Regex.Match(trailer, @"/ID\s*\[[^\]]*\]");

        // objets a dictionnaire seul (sans flux) : derniere definition de chacun
        var dicts = new Dictionary<(int Num, int Gen), string>();
        foreach (Match m in ObjHead.Matches(text))
        {
            int start = m.Index + m.Length - 2;
            int end = text.IndexOf("endobj", start, StringComparison.Ordinal);
            if (end < 0) continue;
            var body = text[start..end].TrimEnd();
            if (body.Contains("stream", StringComparison.Ordinal)) continue;
            dicts[(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))] = body;
        }
        if (!dicts.TryGetValue((rootNum, rootGen), out var catalog)) throw new InvalidDataException("Objet catalogue introuvable.");
        int metaNum = size;
        catalog = Regex.IsMatch(catalog, @"/Metadata\s+\d+\s+\d+\s+R")
            ? Regex.Replace(catalog, @"/Metadata\s+\d+\s+\d+\s+R", $"/Metadata {metaNum} 0 R")
            : catalog[..catalog.LastIndexOf(">>", StringComparison.Ordinal)] + $"/Metadata {metaNum} 0 R>>";
        var changed = new List<((int Num, int Gen) Key, string Body)> { ((rootNum, rootGen), catalog) };
        int groups = 0;
        foreach (var (key, body) in dicts)
        {
            if (!Regex.IsMatch(body, @"/Type\s*/Page(?![s\w])") || DefaultRgb.Match(body) is not { Success: true } icc || !GroupCs.IsMatch(body)) continue;
            changed.Add((key, GroupCs.Replace(body, $"$1[/ICCBased {icc.Groups[1].Value}]")));
            groups++;
        }
        LastCalibratedGroups = groups;

        using var ms = new MemoryStream();
        ms.Write(pdf);
        void W(string s) => ms.Write(Latin1.GetBytes(s));
        if (pdf.Length > 0 && pdf[^1] != '\n') W("\n");
        var offsets = new SortedDictionary<int, (long Off, int Gen)>();
        offsets[metaNum] = (ms.Position, 0);
        W($"{metaNum} 0 obj\n<</Type/Metadata/Subtype/XML/Length {xmp.Length}>>\nstream\n");
        ms.Write(xmp);
        W("\nendstream\nendobj\n");
        foreach (var ((num, gen), body) in changed)
        {
            offsets[num] = (ms.Position, gen);
            W($"{num} {gen} obj\n{body}\nendobj\n");
        }
        long xref = ms.Position;
        var sb = new StringBuilder("xref\n0 1\n0000000000 65535 f \n");
        foreach (var (num, (off, gen)) in offsets)
            sb.Append(CultureInfo.InvariantCulture, $"{num} 1\n{off:D10} {gen:D5} n \n");
        W(sb.ToString());
        W($"trailer\n<</Size {metaNum + 1}/Root {rootNum} {rootGen} R{(info.Success ? "/" + info.Value.TrimStart('/') : "")}{(id.Success ? id.Value : "")}/Prev {prev}>>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
