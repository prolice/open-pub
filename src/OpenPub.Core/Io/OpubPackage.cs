using System.IO.Compression;
using OpenPub.Core.Model;

namespace OpenPub.Core.Io;

/// <summary>
/// Paquet .opub : archive ZIP contenant document.json, assets/ (images) et fonts/ (polices
/// non installees sur le poste, ex. polices cloud d'Office). A l'ouverture, le paquet est
/// decompresse dans un dossier de travail ; les chemins "assets/..." y sont relatifs.
/// </summary>
public sealed class OpubPackage : IDisposable
{
    public const string DocumentEntry = "document.json";

    public PubDocument Document { get; set; }
    /// <summary>Dossier de travail (contenu decompresse).</summary>
    public string WorkDir { get; }
    /// <summary>Fichier .opub d'origine (null pour un import pas encore enregistre).</summary>
    public string? FilePath { get; private set; }

    OpubPackage(PubDocument doc, string workDir, string? filePath)
    {
        Document = doc;
        WorkDir = workDir;
        FilePath = filePath;
    }

    static string NewWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OpenPub", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Ouvre un .opub, ou un document.json (dossier converti par tools/pub2json.py).</summary>
    public static OpubPackage Open(string path, IEnumerable<string>? fontSearchDirs = null)
    {
        if (Path.GetFileName(path).Equals(DocumentEntry, StringComparison.OrdinalIgnoreCase))
            return ImportFolder(Path.GetDirectoryName(Path.GetFullPath(path))!, fontSearchDirs);

        var work = NewWorkDir();
        ZipFile.ExtractToDirectory(path, work);
        var doc = DocumentJson.Load(Path.Combine(work, DocumentEntry));
        return new OpubPackage(doc, work, Path.GetFullPath(path));
    }

    /// <summary>
    /// Importe un dossier converti (document.json + assets/) : copie dans un dossier de travail,
    /// remplace les images WMF/EMF par leur conversion PNG si elle existe, et embarque les
    /// polices du document introuvables dans le systeme mais presentes dans fontSearchDirs.
    /// </summary>
    public static OpubPackage ImportFolder(string dir, IEnumerable<string>? fontSearchDirs = null)
    {
        var work = NewWorkDir();
        var doc = DocumentJson.Load(Path.Combine(dir, DocumentEntry));
        foreach (var s in doc.AllShapes().Where(s => s.Src is not null))
        {
            var src = s.Src!;
            var ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext is ".wmf" or ".emf" && File.Exists(Path.Combine(dir, Path.ChangeExtension(src, ".png"))))
                s.Src = src = Path.ChangeExtension(src, ".png").Replace('\\', '/');
            var from = Path.Combine(dir, src);
            if (!File.Exists(from)) continue;
            var to = Path.Combine(work, src);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }
        if (fontSearchDirs is not null)
            FontEmbedder.EmbedMissing(doc, fontSearchDirs, Path.Combine(work, "fonts"));
        DocumentJson.Save(doc, Path.Combine(work, DocumentEntry));
        return new OpubPackage(doc, work, null);
    }

    public string ResolveAsset(string relative) => Path.Combine(WorkDir, relative.Replace('/', Path.DirectorySeparatorChar));

    public string FontsDir => Path.Combine(WorkDir, "fonts");

    /// <summary>Ajoute une image au paquet et renvoie son chemin relatif.</summary>
    public string AddAsset(string sourceFile)
    {
        var assets = Path.Combine(WorkDir, "assets");
        Directory.CreateDirectory(assets);
        var name = $"img-{DateTime.Now:yyyyMMddHHmmss}-{Path.GetFileName(sourceFile)}";
        File.Copy(sourceFile, Path.Combine(assets, name), overwrite: true);
        return "assets/" + name;
    }

    /// <summary>Enregistre le paquet (ecriture atomique : fichier temporaire puis remplacement).</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("Aucun chemin d'enregistrement");
        DocumentJson.Save(Document, Path.Combine(WorkDir, DocumentEntry));
        RemoveUnusedAssets();
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        ZipFile.CreateFromDirectory(WorkDir, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);
        File.Move(tmp, path, overwrite: true);
        FilePath = Path.GetFullPath(path);
    }

    void RemoveUnusedAssets()
    {
        var assets = Path.Combine(WorkDir, "assets");
        if (!Directory.Exists(assets)) return;
        var used = Document.AllShapes().Where(s => s.Src is not null)
            .Select(s => Path.GetFullPath(ResolveAsset(s.Src!))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(assets))
            if (!used.Contains(Path.GetFullPath(f))) File.Delete(f);
    }

    public void Dispose()
    {
        try { Directory.Delete(WorkDir, recursive: true); } catch { /* dossier temporaire : sans gravite */ }
    }
}
