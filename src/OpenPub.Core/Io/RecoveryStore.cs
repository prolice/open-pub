using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenPub.Core.Io;

/// <summary>Copie de secours d'un document (enregistrement automatique).</summary>
public sealed record RecoveryEntry(string Id, string CopyPath, string? OriginalPath, string Title, DateTime SavedUtc);

/// <summary>
/// Copies de secours des documents ouverts, dans un dossier dedie (jamais a la place du fichier de l'utilisateur) :
/// « id.opub » + « id.json » (fichier d'origine, titre, date). Une copie restee apres la fermeture d'OpenPub
/// signale une fermeture inattendue : elle est proposee au demarrage suivant.
/// </summary>
public sealed class RecoveryStore
{
    public string Dir { get; }

    public RecoveryStore(string dir) => Dir = dir;

    /// <summary>Dossier par defaut : %LOCALAPPDATA%\OpenPub\Recovery.</summary>
    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenPub", "Recovery");

    /// <summary>Identifiant stable d'un document enregistre (meme fichier = meme copie) ; sinon propre a la session.</summary>
    public static string IdFor(string? originalPath, string sessionId)
    {
        if (originalPath is null) return "sans-titre-" + sessionId;
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(originalPath).ToLowerInvariant()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    sealed class Info
    {
        public string? Original { get; set; }
        public string Title { get; set; } = "";
        public DateTime SavedUtc { get; set; }
    }

    public void Save(OpubPackage package, string id, string title)
    {
        Directory.CreateDirectory(Dir);
        package.SaveCopy(Path.Combine(Dir, id + ".opub"));
        var info = new Info { Original = package.FilePath, Title = title, SavedUtc = DateTime.UtcNow };
        File.WriteAllText(Path.Combine(Dir, id + ".json"), JsonSerializer.Serialize(info));
    }

    /// <summary>Copies presentes, la plus recente en tete.</summary>
    public List<RecoveryEntry> List()
    {
        var res = new List<RecoveryEntry>();
        if (!Directory.Exists(Dir)) return res;
        foreach (var json in Directory.GetFiles(Dir, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(json);
            var copy = Path.Combine(Dir, id + ".opub");
            if (!File.Exists(copy)) { TryDelete(json); continue; }
            try
            {
                var info = JsonSerializer.Deserialize<Info>(File.ReadAllText(json)) ?? new Info();
                res.Add(new RecoveryEntry(id, copy, info.Original, info.Title, info.SavedUtc));
            }
            catch (JsonException) { TryDelete(json); }
        }
        return res.OrderByDescending(e => e.SavedUtc).ToList();
    }

    public void Delete(string id)
    {
        TryDelete(Path.Combine(Dir, id + ".opub"));
        TryDelete(Path.Combine(Dir, id + ".json"));
    }

    static void TryDelete(string f)
    {
        try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
