using OpenPub.Core.Io;

namespace OpenPub.Tests;

/// <summary>Lot 5 : copies de secours, recuperation, version precedente (.bak).</summary>
public class RecoveryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "openpub-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    static OpubPackage Demo() => OpubPackage.ImportFolder(Path.GetDirectoryName(Repo.DemoJson)!);

    [Fact]
    public void Copie_de_secours_puis_recuperation()
    {
        var store = new RecoveryStore(Path.Combine(_dir, "rec"));
        using var pkg = Demo();
        var doc = Path.Combine(_dir, "demo.opub");
        Directory.CreateDirectory(_dir);
        pkg.Save(doc);
        pkg.Document.Pages[0].Shapes.RemoveAt(0);            // modification non enregistree
        var id = RecoveryStore.IdFor(pkg.FilePath, "s1");
        Assert.Equal(id, RecoveryStore.IdFor(doc.ToUpperInvariant(), "autre"));   // meme fichier : meme copie
        store.Save(pkg, id, "demo.opub");
        Assert.Equal(Path.GetFullPath(doc), pkg.FilePath);  // le document garde son fichier

        var entry = Assert.Single(store.List());
        Assert.Equal(Path.GetFullPath(doc), entry.OriginalPath);
        Assert.Equal("demo.opub", entry.Title);
        using var back = OpubPackage.Open(entry.CopyPath);
        Assert.Equal(pkg.Document.Pages[0].Shapes.Count, back.Document.Pages[0].Shapes.Count);
        Assert.True(back.Document.AllShapes().Where(s => s.Src is not null).All(s => File.Exists(back.ResolveAsset(s.Src!))));
        back.Retarget(entry.OriginalPath);
        Assert.Equal(Path.GetFullPath(doc), back.FilePath);

        store.Delete(entry.Id);
        Assert.Empty(store.List());
        Assert.StartsWith("sans-titre-s1", RecoveryStore.IdFor(null, "s1"));
    }

    [Fact]
    public void Enregistrer_garde_la_version_precedente()
    {
        using var pkg = Demo();
        Directory.CreateDirectory(_dir);
        var doc = Path.Combine(_dir, "doc.opub");
        pkg.Save(doc, keepBackup: true);
        Assert.False(File.Exists(doc + ".bak"));             // premier enregistrement : rien a garder
        int before = pkg.Document.Pages.Count;
        pkg.Document.Pages.RemoveAt(0);
        pkg.Save(doc, keepBackup: true);
        Assert.True(File.Exists(doc + ".bak"));
        using var old = OpubPackage.Open(doc + ".bak");
        Assert.Equal(before, old.Document.Pages.Count);
    }
}
