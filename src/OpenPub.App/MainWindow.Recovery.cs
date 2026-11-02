using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenPub.Core.Io;

namespace OpenPub.App;

// Surete des documents : copie de secours automatique, recuperation apres une fermeture inattendue,
// detection d'un fichier modifie par un autre programme.
public partial class MainWindow
{
    /// <summary>Copies de secours actives (application normale ; jamais pendant les tests).</summary>
    public bool EnableRecovery { get; set; }

    /// <summary>Intervalle de l'enregistrement automatique.</summary>
    public static TimeSpan AutoSaveInterval { get; } = TimeSpan.FromMinutes(5);

    internal RecoveryStore Recovery { get; set; } = new(RecoveryStore.DefaultDir);
    string _sessionId = Guid.NewGuid().ToString("N")[..8];
    string? _recoveryId;
    DispatcherTimer? _autoSaveTimer;

    void EnsureAutoSaveTimer()
    {
        if (_autoSaveTimer is not null) return;
        _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutoSaveInterval };
        _autoSaveTimer.Tick += (_, _) => AutoSaveNow();
        _autoSaveTimer.Start();
    }

    /// <summary>Enregistre une copie de secours si le document a des modifications non enregistrees.</summary>
    internal bool AutoSaveNow()
    {
        if (!EnableRecovery || _session is not { Modified: true } s || !AppSettings.Load().AutoSave) return false;
        if (s.TextEdit is not null) return false;      // pas pendant la frappe : on retentera au prochain passage
        try
        {
            var id = RecoveryStore.IdFor(s.Package.FilePath, _sessionId);
            if (_recoveryId is not null && _recoveryId != id) Recovery.Delete(_recoveryId);       // « Enregistrer sous » entre-temps
            var title = s.Package.FilePath is { } f ? Path.GetFileName(f) : (s.Doc.Source ?? "Sans titre");
            Recovery.Save(s.Package, id, title);
            _recoveryId = id;
            StatusText.Text = $"Copie de secours enregistrée à {DateTime.Now:HH:mm}.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Copie de secours impossible : " + ex.Message;
            return false;
        }
    }

    void DiscardRecovery()
    {
        if (_recoveryId is null) return;
        Recovery.Delete(_recoveryId);
        _recoveryId = null;
    }

    /// <summary>Au demarrage : propose les copies de secours laissees par une fermeture inattendue.</summary>
    public void OfferRecovery()
    {
        if (!EnableRecovery) return;
        EnsureAutoSaveTimer();
        foreach (var entry in Recovery.List())
        {
            var when = entry.SavedUtc.ToLocalTime().ToString("dddd d MMMM 'à' HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
            var r = MessageBox.Show(this,
                $"OpenPub n'a pas été fermé normalement. Une copie de secours de « {entry.Title} » a été enregistrée {when}.\n\n" +
                "Oui : ouvrir la copie récupérée (pensez à l'enregistrer)\nNon : supprimer cette copie\nAnnuler : décider au prochain démarrage",
                "Récupérer un document", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.No) { Recovery.Delete(entry.Id); continue; }
            OpenRecovered(entry);
            return;
        }
    }

    /// <summary>Ouvre une copie de secours : le document retrouve son fichier d'origine et reste « modifie ».</summary>
    internal void OpenRecovered(RecoveryEntry entry)
    {
        OpenFile(entry.CopyPath, remember: false);
        if (_session is null || _session.Package.FilePath != Path.GetFullPath(entry.CopyPath)) return;
        _session.Package.Retarget(entry.OriginalPath is { } o && Directory.Exists(Path.GetDirectoryName(o)) ? o : null);
        // la copie garde son identifiant : les prochaines copies la remplacent, l'enregistrement la supprime
        if (entry.Id.StartsWith("sans-titre-")) _sessionId = entry.Id["sans-titre-".Length..];
        _recoveryId = entry.Id;
        _session.MarkModified();
        RecordFileStamp();
        UpdateTitle();
        StatusText.Text = "Document récupéré : enregistrez-le (Ctrl+S) pour garder ces modifications.";
    }

    // ---------------------------------------------------------------- fichier modifie ailleurs

    (DateTime Time, long Size)? _fileStamp;
    bool _externalChangeWarned;

    static (DateTime, long)? Stamp(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        var fi = new FileInfo(path);
        return (fi.LastWriteTimeUtc, fi.Length);
    }

    void RecordFileStamp()
    {
        _fileStamp = Stamp(_session?.Package.FilePath);
        _externalChangeWarned = false;
    }

    bool FileChangedOnDisk()
    {
        var path = _session?.Package.FilePath;
        if (path is null || _fileStamp is null) return false;
        var now = Stamp(path);
        return now is null || now != _fileStamp;
    }

    void OnActivatedCheckFile(object? sender, EventArgs e)
    {
        if (_externalChangeWarned || !FileChangedOnDisk()) return;
        _externalChangeWarned = true;
        StatusText.Text = "Attention : le fichier a été modifié par un autre programme depuis son ouverture. Enregistrer remplacera cette autre version.";
    }
}
