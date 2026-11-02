using OpenPub.Core.Model;

namespace OpenPub.Core.Io;

/// <summary>
/// Embarque dans le paquet les polices du document qui ne sont pas installees sur le poste
/// (cas typique : polices "cloud" d'Office, utilisables seulement par Office).
/// </summary>
public static class FontEmbedder
{
    public static string SystemFontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    public static string UserFontsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
    public static string OfficeCloudFontsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "FontCache", "4", "CloudFonts");

    public static IEnumerable<string> DefaultSearchDirs => new[] { OfficeCloudFontsDir };

    public static HashSet<string> DocumentFamilies(PubDocument doc) =>
        doc.AllShapes().Where(s => s.Paragraphs is not null)
            .SelectMany(s => s.Paragraphs!).SelectMany(p => p.Runs)
            .Select(r => r.Font?.Family).Where(f => !string.IsNullOrEmpty(f)).Select(f => f!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Familles presentes dans les dossiers de polices installees (systeme + utilisateur).</summary>
    public static HashSet<string> InstalledFamilies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { SystemFontsDir, UserFontsDir })
            foreach (var f in SafeFiles(dir))
                if (FontFileInfo.TryRead(f, out var info)) set.Add(info.Family);
        return set;
    }

    public static List<string> EmbedMissing(PubDocument doc, IEnumerable<string> searchDirs, string targetDir)
    {
        var wanted = DocumentFamilies(doc);
        wanted.ExceptWith(InstalledFamilies());
        var copied = new List<string>();
        if (wanted.Count == 0) return copied;
        foreach (var dir in searchDirs)
            foreach (var f in SafeFiles(dir))
            {
                if (!FontFileInfo.TryRead(f, out var info) || !wanted.Contains(info.Family)) continue;
                Directory.CreateDirectory(targetDir);
                var name = $"{info.Family}-{info.Subfamily}{Path.GetExtension(f)}".Replace(' ', '_');
                File.Copy(f, Path.Combine(targetDir, name), overwrite: true);
                copied.Add(name);
            }
        return copied;
    }

    static IEnumerable<string> SafeFiles(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }
}

/// <summary>Lecture minimale des tables 'name', 'head' et 'OS/2' d'un fichier TrueType/OpenType.</summary>
public readonly record struct FontFileInfo(string Family, string Subfamily, double WinLineFactor, double WinAscentRatio)
{
    public static bool TryRead(string path, out FontFileInfo info)
    {
        info = default;
        try
        {
            var data = File.ReadAllBytes(path);
            return TryRead(data, out info);
        }
        catch { return false; }
    }

    public static bool TryRead(byte[] d, out FontFileInfo info)
    {
        info = default;
        if (d.Length < 12) return false;
        int U16(int o) => (d[o] << 8) | d[o + 1];
        uint U32(int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
        int numTables = U16(4);
        int name = -1, head = -1, os2 = -1;
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + 16 * i;
            if (rec + 16 > d.Length) return false;
            var tag = System.Text.Encoding.ASCII.GetString(d, rec, 4);
            int off = (int)U32(rec + 8);
            if (tag == "name") name = off; else if (tag == "head") head = off; else if (tag == "OS/2") os2 = off;
        }
        if (name < 0) return false;
        string? family = null, sub = null, familyAny = null, subAny = null;
        int count = U16(name + 2), stor = U16(name + 4);
        for (int j = 0; j < count; j++)
        {
            int r = name + 6 + 12 * j;
            int pid = U16(r), lid = U16(r + 4), nid = U16(r + 6), len = U16(r + 8), o = U16(r + 10);
            if (pid != 3 || (nid != 1 && nid != 2)) continue;
            var s = System.Text.Encoding.BigEndianUnicode.GetString(d, name + stor + o, len);
            // l'anglais (0x409) d'abord ; sinon les styles localises ("Kursiv"...) ne sont pas reconnus
            if (nid == 1) { familyAny ??= s; if (lid == 0x409) family = s; }
            else { subAny ??= s; if (lid == 0x409) sub = s; }
        }
        family ??= familyAny; sub ??= subAny ?? "Regular";
        if (family is null) return false;
        double factor = 1.17, ascent = 0.8;
        if (head >= 0 && os2 >= 0)
        {
            int upm = U16(head + 18);
            int winAsc = U16(os2 + 74), winDesc = U16(os2 + 76);
            if (upm > 0 && winAsc + winDesc > 0)
            {
                factor = (winAsc + winDesc) / (double)upm;
                ascent = winAsc / (double)(winAsc + winDesc);
            }
        }
        info = new FontFileInfo(family, sub, factor, ascent);
        return true;
    }
}
