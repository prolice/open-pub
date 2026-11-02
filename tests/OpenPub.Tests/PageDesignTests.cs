using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 6 : pages maitres, numerotation, champs, grille, format de fichier.</summary>
public class PageDesignTests
{
    static PubDocument Doc(int pages)
    {
        var d = new PubDocument();
        for (int i = 0; i < pages; i++) d.Pages.Add(new Page { Index = i + 1, Width = 100, Height = 100 });
        return d;
    }

    [Fact]
    public void Numerotation_style_et_redemarrage()
    {
        var d = Doc(5);
        Assert.Equal("3", d.PageNumber(d.Pages[2]));
        d.Numbering = new PageNumbering { Start = 0, Style = "i" };
        Assert.Equal("ii", d.PageNumber(d.Pages[2]));
        d.Pages[3].NumberStart = 1;
        d.Numbering = null;
        Assert.Equal(new[] { "1", "2", "3", "1", "2" }, d.Pages.Select(d.PageNumber));
        Assert.Equal("XIV", PageNumbering.Format(14, "I"));
        Assert.Equal("#", d.PageNumber(new Page()));          // page hors du document (page maitre)
    }

    [Fact]
    public void Page_maitre_utilisee()
    {
        var d = Doc(3);
        Assert.Null(d.MasterOf(d.Pages[0]));
        var a = new Page { Id = "A" }; var b = new Page { Id = "B" };
        d.Masters = new List<Page> { a, b };
        Assert.Same(a, d.MasterOf(d.Pages[0]));                // par defaut : la premiere
        d.Pages[1].Master = "B";
        Assert.Same(b, d.MasterOf(d.Pages[1]));
        d.Pages[2].IgnoreMaster = true;
        Assert.Null(d.MasterOf(d.Pages[2]));
        Assert.Null(d.MasterOf(a));                            // une page maitre ne s'affiche pas sous elle-meme
        d.Pages[0].Master = "Z";
        Assert.Same(a, d.MasterOf(d.Pages[0]));                // identifiant inconnu : la premiere
        a.Shapes.Add(new Shape { Kind = ShapeKind.Image, Src = "assets/logo.png" });
        Assert.Contains(d.AllShapes(), s => s.Src == "assets/logo.png");   // les images des maitres sont gardees a l'enregistrement
    }

    [Fact]
    public void Champ_numero_de_page_dans_le_texte()
    {
        var font = new FontSpec { Family = "Mono", Size = 10 };
        var paras = new List<Paragraph> { new() { Runs = { new Run { Text = "Page ", Font = font } } } };
        var end = StoryEditor.InsertField(paras, new TextPos(0, 5), "page", font);
        StoryEditor.Insert(paras, end, " fin", font);
        Assert.Equal(3, paras[0].Runs.Count);                     // le champ ne fusionne pas avec ses voisins
        Assert.Equal("page", paras[0].Runs[1].Field);
        Assert.Equal("Page # fin", paras[0].PlainText);

        var frame = new FrameGeometry { Shape = new Shape { Kind = ShapeKind.Text, W = 300, H = 50 }, Width = 300, Height = 50 };
        var engine = new TextLayoutEngine(new MonoMeasurer(), null);
        var line = engine.Layout(new[] { frame }, paras, false, f => f == "page" ? "12" : null).Frames[0].Lines[0];
        var field = line.Pieces.Single(p => p.Start == 5);
        Assert.Equal("12", field.Text);
        Assert.Equal(1, field.Length);                            // un seul caractere source : le curseur reste juste
        Assert.Equal(6, line.Pieces.First(p => p.Text == "fin").Start - 1);
        var raw = engine.Layout(new[] { frame }, paras).Frames[0].Lines[0];
        Assert.Contains(raw.Pieces, p => p.Text == "#");          // sans valeurs (page maitre) : « # »
    }

    [Fact]
    public void Grille_de_disposition()
    {
        var g = new LayoutGrid { Columns = 3, Rows = 2, Gutter = 10 };
        var (xs, ys) = g.Lines(0, 0, 320, 210);
        Assert.Equal(new double[] { 100, 110, 210, 220 }, xs);
        Assert.Equal(new double[] { 100, 110 }, ys);
    }

    [Fact]
    public void Format_de_fichier()
    {
        var d = Doc(2);
        Assert.Equal("openpub/1", PubFormat.Required(d));
        d.Pages[1].BackColor = "#ffeecc";
        Assert.Equal("openpub/2", PubFormat.Required(d));
        Assert.Equal(2, new PubDocument { Format = "openpub/2" }.FormatVersion);
        Assert.True(new PubDocument { Format = "openpub/9" }.FormatVersion > PubDocument.SupportedFormat);
    }
}
