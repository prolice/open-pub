using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

public static class Repo
{
    /// <summary>Racine du depot (dossier contenant OpenPub.sln).</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenPub.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("OpenPub.sln introuvable");
        }
    }

    /// <summary>Document de demonstration neutre (aussi utilise pour les captures du site et de l'aide).</summary>
    public static string DemoJson => Path.Combine(Root, "tools", "site-template", "demo", "document.json");
}

public class DocumentJsonTests
{
    [Fact]
    public void Charge_le_document_de_demonstration()
    {
        var doc = DocumentJson.Load(Repo.DemoJson);
        Assert.Equal(2, doc.Pages.Count);
        Assert.InRange(doc.PageWidth, 595.0, 595.5);     // A4
        var text = doc.AllShapes().Where(s => s.IsStoryHead).SelectMany(s => s.Paragraphs!).Sum(p => p.PlainText.Length);
        Assert.InRange(text, 900, 1300);
        Assert.Contains(doc.AllShapes(), s => s.Paragraphs?.Any(p => p.PlainText == "publication") == true);
        Assert.Contains(doc.AllShapes(), s => s.Geometry == ShapeGeometry.Star);
        Assert.Contains(doc.AllShapes(), s => s.Line?.ArrowEnd == true);
        Assert.Contains(doc.AllShapes(), s => s.IsImage && s.Rotation != 0 && s.Shadow is not null);
    }

    [Fact]
    public void Aller_retour_JSON_sans_perte()
    {
        var doc = DocumentJson.Load(Repo.DemoJson);
        var once = DocumentJson.Serialize(doc);
        var twice = DocumentJson.Serialize(DocumentJson.Parse(once));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Chaines_de_zones_coherentes()
    {
        // chaine de trois zones (comme les colonnes d'un article) : seule la tete porte le texte
        var json = """
        {"pages":[{"index":1,"width":595,"height":842,"shapes":[
          {"kind":"text","x":36,"y":100,"w":160,"h":600,"z":1,"chain":"c1","chainIndex":0,"paragraphs":[{"runs":[{"text":"Article"}]}]},
          {"kind":"text","x":213,"y":100,"w":160,"h":600,"z":2,"chain":"c1","chainIndex":1},
          {"kind":"text","x":390,"y":100,"w":160,"h":600,"z":3,"chain":"c1","chainIndex":2}]}]}
        """;
        var doc = DocumentJson.Parse(json);
        Assert.Equal(new[] { 1, 2, 3 }, doc.Pages[0].ChainFrames("c1").Select(f => f.Z));
        foreach (var page in doc.Pages)
            foreach (var chain in page.Shapes.Where(s => s.Chain is not null).Select(s => s.Chain!).Distinct())
            {
                var frames = page.ChainFrames(chain);
                Assert.True(frames.Count >= 2);
                Assert.True(frames[0].Paragraphs!.Count > 0, "la tete de chaine porte le texte");
                Assert.All(frames.Skip(1), f => Assert.Empty(f.Paragraphs!));
            }
    }
}

public class UndoStackTests
{
    [Fact]
    public void Annuler_puis_retablir()
    {
        var doc = new PubDocument { Pages = { new Page { Index = 1, Shapes = { new Shape { X = 10 } } } } };
        var undo = new UndoStack();
        undo.Push(doc);
        doc.Pages[0].Shapes[0].X = 50;

        var back = undo.Undo(doc)!;
        Assert.Equal(10, back.Pages[0].Shapes[0].X);
        Assert.True(undo.CanRedo);

        var again = undo.Redo(back)!;
        Assert.Equal(50, again.Pages[0].Shapes[0].X);
    }

    [Fact]
    public void Une_action_sans_effet_ne_laisse_pas_d_entree()
    {
        var doc = new PubDocument { Pages = { new Page { Index = 1 } } };
        var undo = new UndoStack();
        undo.Push(doc);
        undo.DropLastIfUnchanged(doc);
        Assert.False(undo.CanUndo);
    }
}

public class PackageTests
{
    [Fact]
    public void Import_puis_enregistrement_et_reouverture()
    {
        var dir = Path.GetDirectoryName(Repo.DemoJson)!;
        var target = Path.Combine(Path.GetTempPath(), $"openpub-test-{Guid.NewGuid():N}.opub");
        try
        {
            using (var pkg = OpubPackage.ImportFolder(dir))
            {
                Assert.True(File.Exists(pkg.ResolveAsset("assets/paysage.jpg")), "image copiee dans le paquet");
                pkg.Document.Pages[0].Shapes[1].X += 5;
                pkg.Save(target);
            }
            using var reopened = OpubPackage.Open(target);
            Assert.Equal(2, reopened.Document.Pages.Count);
            Assert.All(reopened.Document.AllShapes().Where(s => s.Src is not null),
                s => Assert.True(File.Exists(reopened.ResolveAsset(s.Src!)), s.Src));
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }
}

public class ShapeBoundsTests
{
    [Fact]
    public void Emprise_d_un_objet_tourne()
    {
        var s = new Shape { X = 0, Y = 0, W = 100, H = 20, Rotation = 90 };
        var (x, y, w, h) = s.Bounds();
        Assert.Equal(20, w, 3);
        Assert.Equal(100, h, 3);
        Assert.Equal(40, x, 3);          // centre (50, 10) conserve
        Assert.Equal(-40, y, 3);
        s.Rotation = 0;
        Assert.Equal((0.0, 0.0, 100.0, 20.0), s.Bounds());
    }
}
