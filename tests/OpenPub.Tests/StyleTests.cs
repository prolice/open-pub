using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

/// <summary>Lot 8 : styles de texte, jeux de couleurs et de polices.</summary>
public class StyleTests
{
    static PubDocument DocWith(params string[] lines)
    {
        var d = new PubDocument();
        var page = new Page { Width = 100, Height = 100 };
        page.Shapes.Add(new Shape
        {
            Kind = ShapeKind.Text,
            Paragraphs = lines.Select(t => new Paragraph { Runs = { new Run { Text = t, Font = new FontSpec { Family = "Arial", Size = 10, Color = "#111c43" } } } }).ToList(),
        });
        d.Pages.Add(page);
        return d;
    }

    static List<Paragraph> Paras(PubDocument d) => d.Pages[0].Shapes[0].Paragraphs!;

    [Fact]
    public void Appliquer_puis_modifier_un_style_de_paragraphe()
    {
        var d = DocWith("Titre", "Texte", "Autre titre");
        var styles = StyleOps.Ensure(d);
        var t1 = styles.First(s => s.Id == "titre1");
        StyleOps.Apply(Paras(d), new TextPos(0, 0), new TextPos(0, 2), t1);
        StyleOps.Apply(Paras(d), new TextPos(2, 0), new TextPos(2, 0), t1);
        Assert.Equal("titre1", Paras(d)[0].Style);
        Assert.Null(Paras(d)[1].Style);
        Assert.Equal(24, Paras(d)[0].Runs[0].Font!.Size);
        Assert.True(Paras(d)[2].Runs[0].Font!.Bold);
        // modifier le style : tous les titres suivent
        t1.Font!.Size = 30;
        t1.Font.Color = "#c00000";
        Assert.Equal(2, StyleOps.Reapply(d, t1));
        Assert.All(new[] { Paras(d)[0], Paras(d)[2] }, p => Assert.Equal(30, p.Runs[0].Font!.Size));
        Assert.Equal(10, Paras(d)[1].Runs[0].Font!.Size);
    }

    [Fact]
    public void Style_de_caractere_sur_une_selection()
    {
        var d = DocWith("Un mot important ici");
        var accent = StyleOps.Ensure(d).First(s => s.Kind == "character");
        StyleOps.Apply(Paras(d), new TextPos(0, 7), new TextPos(0, 16), accent);
        var runs = Paras(d)[0].Runs;
        Assert.Equal(3, runs.Count);
        Assert.Equal("important", runs[1].Text);
        Assert.Equal("accent", runs[1].Style);
        Assert.True(runs[1].Font!.Bold);
        Assert.False(runs[0].Font!.Bold);
        StyleOps.Delete(d, "accent");
        Assert.Null(runs[1].Style);
        Assert.True(runs[1].Font!.Bold);                           // le texte garde sa mise en forme
    }

    [Fact]
    public void Nouveau_style_d_apres_le_texte()
    {
        var d = DocWith("Modèle");
        var p = Paras(d)[0];
        p.Align = "center"; p.SpaceAfter = 8;
        var st = StyleOps.FromText(d, "Mon titre !", "paragraph", p, p.Runs[0].Font!);
        Assert.Equal("montitre", st.Id);
        Assert.Equal("center", st.Align);
        Assert.Equal("montitre2", StyleOps.FromText(d, "Mon titre", "paragraph", p, p.Runs[0].Font!).Id);
    }

    [Fact]
    public void Jeu_de_couleurs_remplace_les_couleurs_du_jeu()
    {
        var d = DocWith("Texte");
        d.Pages[0].Shapes.Add(new Shape { Fill = new Fill { Color = "#087AFF" }, Line = new LineStyle { Color = "#123456" } });
        var ocean = StyleOps.ColorSchemes.First(c => c.Name == "Océan");
        int n = StyleOps.ApplyColorScheme(d, ocean.Name, ocean.Colors);
        Assert.True(n >= 2);
        Assert.Equal("#0b2a4a", Paras(d)[0].Runs[0].Font!.Color);   // principale
        Assert.Equal("#1565c0", d.Pages[0].Shapes[1].Fill!.Color);  // accent 1 (casse ignoree)
        Assert.Equal("#123456", d.Pages[0].Shapes[1].Line!.Color);  // couleur hors du jeu : inchangee
        Assert.Equal("Océan", d.Theme!.Name);
        // second changement : a partir du jeu courant
        var foret = StyleOps.ColorSchemes.First(c => c.Name == "Forêt");
        StyleOps.ApplyColorScheme(d, foret.Name, foret.Colors);
        Assert.Equal("#2e7d32", d.Pages[0].Shapes[1].Fill!.Color);
    }

    [Fact]
    public void Jeu_de_polices()
    {
        var d = DocWith("Corps");
        var styles = StyleOps.Ensure(d);
        StyleOps.Apply(Paras(d), new TextPos(0, 0), new TextPos(0, 0), styles.First(s => s.Id == "titre1"));
        Assert.Equal("Georgia", Paras(d)[0].Runs[0].Font!.Family);
        StyleOps.ApplyFontScheme(d, "Cambria", "Calibri");
        Assert.Equal("Cambria", Paras(d)[0].Runs[0].Font!.Family);
        Assert.Equal("Calibri", styles.First(s => s.Id == "normal").Font!.Family);
        Assert.Equal("openpub/2", PubFormat.Required(d));
    }
}
