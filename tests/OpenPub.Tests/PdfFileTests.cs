using System.Text;
using System.Text.RegularExpressions;
using OpenPub.Core.Output;

namespace OpenPub.Tests;

/// <summary>Outils de fichiers pour le PDF : polices sfnt (collections .ttc) et mise a jour incrementale.</summary>
public class PdfFileTests
{
    /// <summary>Police sfnt minimale : une table 'name' (famille, style) et des tables factices.</summary>
    static byte[] Sfnt(string family, string style, params (string Tag, byte[] Data)[] extra)
    {
        byte[] Name()
        {
            var f = Encoding.BigEndianUnicode.GetBytes(family);
            var s = Encoding.BigEndianUnicode.GetBytes(style);
            var ms = new MemoryStream();
            void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
            U16(0); U16(2); U16(6 + 2 * 12);
            U16(3); U16(1); U16(0x409); U16(1); U16(f.Length); U16(0);
            U16(3); U16(1); U16(0x409); U16(2); U16(s.Length); U16(f.Length);
            ms.Write(f); ms.Write(s);
            return ms.ToArray();
        }
        var tables = new List<(string Tag, byte[] Data)> { ("name", Name()) };
        tables.AddRange(extra);
        return Assemble(tables, 0);
    }

    /// <summary>En-tete + repertoire + tables (decalages a partir de <paramref name="baseOffset"/>).</summary>
    static byte[] Assemble(List<(string Tag, byte[] Data)> tables, int baseOffset)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void U32(int v) { U16(v >> 16); U16(v & 0xffff); }
        U32(0x00010000); U16(tables.Count); U16(0); U16(0); U16(0);
        int off = baseOffset + 12 + 16 * tables.Count;
        foreach (var (tag, data) in tables)
        {
            ms.Write(Encoding.ASCII.GetBytes(tag)); U32(0); U32(off); U32(data.Length);
            off += (data.Length + 3) & ~3;
        }
        foreach (var (_, data) in tables) { ms.Write(data); for (int i = data.Length; i % 4 != 0; i++) ms.WriteByte(0); }
        return ms.ToArray();
    }

    [Fact]
    public void Une_police_est_extraite_d_une_collection_ttc()
    {
        var glyf = new byte[] { 1, 2, 3, 4, 5 };
        var cff = new byte[] { 9, 9, 9 };
        var a = Sfnt("Alpha", "Regular", ("glyf", glyf));
        // collection de deux polices : en-tete ttcf, puis chaque police avec des decalages absolus
        int headerLen = 12 + 4 * 2;
        var aAbs = Reassemble(a, headerLen);
        var bAbs = Reassemble(Sfnt("Beta", "Bold", ("CFF ", cff)), headerLen + aAbs.Length);
        var ttc = new MemoryStream();
        ttc.Write(Encoding.ASCII.GetBytes("ttcf"));
        ttc.Write(new byte[] { 0, 1, 0, 0, 0, 0, 0, 2 });
        void U32(int v) => ttc.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
        U32(headerLen); U32(headerLen + aAbs.Length);
        ttc.Write(aAbs); ttc.Write(bAbs);
        var data = ttc.ToArray();

        Assert.True(SfntFont.IsCollection(data));
        Assert.Equal(2, SfntFont.FaceCount(data));
        Assert.Equal(1, SfntFont.FindFace(data, "Beta", "Bold"));
        Assert.Equal(0, SfntFont.FindFace(data, "Alpha", null));
        var beta = SfntFont.ExtractFace(data, 1);
        Assert.False(SfntFont.IsCollection(beta));
        Assert.Equal(("Beta", "Bold"), SfntFont.Names(beta));
        Assert.True(SfntFont.HasCffOutlines(beta));
        var alpha = SfntFont.ExtractFace(data, 0);
        Assert.False(SfntFont.HasCffOutlines(alpha));
        var t = SfntFont.Tables(alpha).Single(x => x.Tag == "glyf");
        Assert.Equal(glyf, alpha.AsSpan(t.Offset, t.Length).ToArray());
    }

    [Fact]
    public void Une_licence_restreinte_interdit_l_incorporation()
    {
        static byte[] Os2(int fsType) { var t = new byte[78]; t[8] = (byte)(fsType >> 8); t[9] = (byte)fsType; return t; }
        Assert.True(SfntFont.EmbeddingRestricted(Sfnt("A", "Regular", ("OS/2", Os2(0x0002)))));
        Assert.False(SfntFont.EmbeddingRestricted(Sfnt("A", "Regular", ("OS/2", Os2(0x0008)))));   // modification interdite seulement
        Assert.False(SfntFont.EmbeddingRestricted(Sfnt("A", "Regular", ("OS/2", Os2(0)))));        // installable
        Assert.False(SfntFont.EmbeddingRestricted(Sfnt("A", "Regular")));
    }

    /// <summary>Recopie une police simple en decalant ses tables (pour la placer dans une collection).</summary>
    static byte[] Reassemble(byte[] sfnt, int baseOffset) =>
        Assemble(SfntFont.Tables(sfnt).Select(t => (t.Tag, sfnt.AsSpan(t.Offset, t.Length).ToArray())).ToList(), baseOffset);

    /// <summary>PDF minimal (table de references classique), comme ceux de PDFsharp.</summary>
    static byte[] MiniPdf()
    {
        var sb = new StringBuilder("%PDF-1.6\n");
        var offs = new List<int>();
        void Obj(string body) { offs.Add(sb.Length); sb.Append($"{offs.Count} 0 obj\n{body}\nendobj\n"); }
        Obj("<</Type/Catalog/Pages 2 0 R/Metadata 4 0 R>>");
        Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>");
        Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Resources<</ColorSpace<</DefaultRGB[/ICCBased 5 0 R]>>>>/Group<</CS/DeviceRGB/S/Transparency>>>>");
        Obj("<</Type/Metadata/Subtype/XML/Length 3>>\nstream\nabc\nendstream");
        Obj("<</N 3/Length 2>>\nstream\nxy\nendstream");
        int xref = sb.Length;
        sb.Append($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<</ID[<AB><AB>]/Root 1 0 R/Size {offs.Count + 1}>>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact]
    public void La_mise_a_jour_incrementale_remplace_les_metadonnees_et_etalonne_le_groupe()
    {
        var pdf = MiniPdf();
        var xmp = Encoding.UTF8.GetBytes("<x:xmpmeta>PDF/X-4</x:xmpmeta>");
        var res = PdfIncrementalUpdate.ForPdfX(pdf, xmp);
        // le fichier d'origine est conserve tel quel au debut
        Assert.Equal(pdf, res.AsSpan(0, pdf.Length).ToArray());
        var text = Encoding.Latin1.GetString(res);
        var added = text[pdf.Length..];
        Assert.Contains("6 0 obj\n<</Type/Metadata/Subtype/XML/Length " + xmp.Length, added);
        Assert.Contains("1 0 obj\n<</Type/Catalog/Pages 2 0 R/Metadata 6 0 R>>", added);
        Assert.Contains("/Group<</CS[/ICCBased 5 0 R]/S/Transparency>>", added);
        Assert.Equal(1, PdfIncrementalUpdate.LastCalibratedGroups);
        // nouvelle table : chaque decalage pointe bien sur son objet, et le trailer chaine l'ancienne table
        int xref = int.Parse(Regex.Match(text[text.LastIndexOf("startxref")..], @"\d+").Value);
        var table = text[xref..text.LastIndexOf("trailer")];
        foreach (Match m in Regex.Matches(table, @"(\d+) 1\n(\d{10}) (\d{5}) n \n"))
            Assert.StartsWith($"{m.Groups[1].Value} {int.Parse(m.Groups[3].Value)} obj", text[int.Parse(m.Groups[2].Value)..]);
        Assert.Matches(@"/Size 7/Root 1 0 R/ID\[<AB><AB>\]/Prev \d+", text[text.LastIndexOf("trailer")..]);
    }
}
