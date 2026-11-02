using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

public class StoryEditorTests
{
    static FontSpec F(bool bold = false) => new() { Family = "Cavolini", Size = 12, Bold = bold, Color = "#000000" };

    static List<Paragraph> Story(params string[] paras) =>
        paras.Select(t => new Paragraph { Align = "justify", SpaceAfter = 4.8, Runs = { new Run { Text = t, Font = F() } } }).ToList();

    static string All(List<Paragraph> s) => string.Join("|", s.Select(p => p.PlainText));

    [Fact]
    public void Insertion_au_milieu_d_un_mot()
    {
        var s = Story("Bonjour");
        var pos = StoryEditor.Insert(s, new TextPos(0, 3), "XX");
        Assert.Equal("BonXXjour", All(s));
        Assert.Equal(new TextPos(0, 5), pos);
        Assert.Single(s[0].Runs);                       // meme mise en forme : segments fusionnes
    }

    [Fact]
    public void Entree_coupe_le_paragraphe_et_garde_ses_reglages()
    {
        var s = Story("Alors que la rentrée");
        var pos = StoryEditor.SplitParagraph(s, new TextPos(0, 5));
        Assert.Equal("Alors| que la rentrée", All(s));
        Assert.Equal(new TextPos(1, 0), pos);
        Assert.Equal("justify", s[1].Align);
        Assert.Equal(4.8, s[1].SpaceAfter);
    }

    [Fact]
    public void Coller_plusieurs_lignes_cree_des_paragraphes()
    {
        var s = Story("AB");
        var pos = StoryEditor.Insert(s, new TextPos(0, 1), "1\n2\n3");
        Assert.Equal("A1|2|3B", All(s));
        Assert.Equal(new TextPos(2, 1), pos);
    }

    [Fact]
    public void Suppression_sur_plusieurs_paragraphes_les_fusionne()
    {
        var s = Story("Premier", "Deuxième", "Troisième");
        var pos = StoryEditor.Delete(s, new TextPos(0, 3), new TextPos(2, 4));
        Assert.Equal("Presième", All(s));      // "Pre" + "Troisième"[4..]
        Assert.Equal(new TextPos(0, 3), pos);
    }

    [Fact]
    public void Retour_arriere_en_debut_de_paragraphe_fusionne_avec_le_precedent()
    {
        var s = Story("Un", "Deux");
        var prev = StoryEditor.Step(s, new TextPos(1, 0), -1);
        StoryEditor.Delete(s, prev, new TextPos(1, 0));
        Assert.Equal("UnDeux", All(s));
    }

    [Fact]
    public void Mise_en_gras_d_une_partie_coupe_les_segments()
    {
        var s = Story("Kazakhstan où");
        StoryEditor.ApplyFont(s, new TextPos(0, 0), new TextPos(0, 10), f => f.Bold = true);
        Assert.Equal(2, s[0].Runs.Count);
        Assert.True(s[0].Runs[0].Font!.Bold);
        Assert.Equal("Kazakhstan", s[0].Runs[0].Text);
        Assert.False(s[0].Runs[1].Font!.Bold);
        // la saisie apres le mot gras reprend le gras
        Assert.True(StoryEditor.FontAt(s, new TextPos(0, 10)).Bold);
        Assert.False(StoryEditor.FontAt(s, new TextPos(0, 11)).Bold);
    }

    [Fact]
    public void Un_paragraphe_vide_garde_sa_mise_en_forme()
    {
        var s = Story("Texte");
        StoryEditor.Delete(s, new TextPos(0, 0), new TextPos(0, 5));
        Assert.Single(s[0].Runs);
        Assert.Equal("", s[0].Runs[0].Text);
        Assert.Equal("Cavolini", s[0].Runs[0].Font!.Family);
    }

    [Fact]
    public void Texte_d_une_selection()
    {
        var s = Story("Alors que", "la rentrée");
        Assert.Equal("que\nla", StoryEditor.GetText(s, new TextPos(0, 6), new TextPos(1, 2)));
    }

    [Fact]
    public void Mot_sous_le_curseur()
    {
        var s = Story("Les compétitions ont repris");
        var (a, b) = StoryEditor.WordAt(s, new TextPos(0, 7));
        Assert.Equal("compétitions", StoryEditor.GetText(s, a, b));
    }
}

public class LayoutOffsetTests
{
    static readonly FontSpec F = new() { Family = "Mono", Size = 10, Color = "#000000" };

    [Fact]
    public void Chaque_morceau_connait_sa_position_source()
    {
        var text = "est malheureusement défaite deux fois.";
        var para = new Paragraph { Runs = { new Run { Text = text, Font = F } } };
        var frame = new FrameGeometry { Shape = new Shape { Kind = ShapeKind.Text, W = 70, H = 300 }, Width = 70, Height = 300 };
        var r = new TextLayoutEngine(new MonoMeasurer(), Hyphenator.French) { ShrinkOnOverflow = false }.Layout(new[] { frame }, new[] { para });
        foreach (var piece in r.Frames[0].Lines.SelectMany(l => l.Pieces))
        {
            var src = text.Substring(piece.Start, piece.Length);
            var shown = piece.Hyphen ? piece.Text[..^1] : piece.Text;
            Assert.Equal(src, shown);
        }
        // les lignes se suivent sans trou ni chevauchement dans le texte source
        var lines = r.Frames[0].Lines;
        Assert.Equal(0, lines[0].StartOffset);
        for (int i = 1; i < lines.Count; i++) Assert.Equal(lines[i - 1].EndOffset, lines[i].StartOffset);
        Assert.Equal(text.Length, lines[^1].EndOffset);
    }
}
