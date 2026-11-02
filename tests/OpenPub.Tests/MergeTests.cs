using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

/// <summary>Lot 12 : publipostage.</summary>
public class MergeTests
{
    [Fact]
    public void Lire_une_liste_csv_ou_excel()
    {
        var csv = MergeOps.Parse("Prénom;Nom;Ville\r\nAlice;Martin;Lyon\r\n\"Jean; Paul\";Durand;\"Saint-\"\"Étienne\"\"\"\r\n", "liste");
        Assert.Equal(new[] { "Prénom", "Nom", "Ville" }, csv.Columns);
        Assert.Equal(2, csv.Rows.Count);
        Assert.Equal("Jean; Paul", csv.Rows[1][0]);                    // separateur entre guillemets
        Assert.Equal("Saint-\"Étienne\"", csv.Rows[1][2]);             // guillemets doubles
        var tsv = MergeOps.Parse("A\tB\n1\t2\n3\n");
        Assert.Equal(new[] { "3", "" }, tsv.Rows[1]);                  // colonnes manquantes : vides
        Assert.Empty(MergeOps.Parse("").Columns);
    }

    [Fact]
    public void Fusionner_un_document()
    {
        var font = new FontSpec { Family = "Arial", Size = 10 };
        var paras = new List<Paragraph> { new() { Runs = { new Run { Text = "Bonjour ", Font = font } } } };
        var end = StoryEditor.InsertField(paras, new TextPos(0, 8), MergeOps.Prefix + "Prénom", font);
        StoryEditor.Insert(paras, end, " !", font);
        var doc = new PubDocument();
        doc.Pages.Add(new Page { Index = 1, Width = 100, Height = 100, Shapes = { new Shape { Kind = ShapeKind.Text, Paragraphs = paras } } });
        doc.Merge = MergeOps.Parse("Prénom\nAlice\nBruno\nChloé");
        Assert.True(MergeOps.HasFields(doc));
        Assert.Equal("Bruno", MergeOps.Value(doc.Merge, MergeOps.Prefix + "Prénom", 1));
        Assert.Equal("«Prénom»", MergeOps.Value(doc.Merge, MergeOps.Prefix + "Prénom", -1));
        Assert.Equal("«Age»", MergeOps.Value(doc.Merge, MergeOps.Prefix + "Age", 0));
        var merged = MergeOps.Merged(doc);
        Assert.Equal(3, merged.Pages.Count);
        Assert.Equal(new[] { 1, 2, 3 }, merged.Pages.Select(p => p.Index));
        Assert.Equal("Bonjour Chloé !", merged.Pages[2].Shapes[0].Paragraphs![0].PlainText);
        Assert.All(merged.AllShapes().SelectMany(s => s.Paragraphs!).SelectMany(p => p.Runs), r => Assert.Null(r.Field));
        Assert.Null(merged.Merge);
        Assert.Equal("Bonjour # !", doc.Pages[0].Shapes[0].Paragraphs![0].PlainText);   // l'original garde ses champs
    }
}
