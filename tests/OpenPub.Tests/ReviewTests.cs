using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

/// <summary>Lot 4 : rechercher / remplacer, statistiques, typographie francaise.</summary>
public class ReviewTests
{
    static List<Paragraph> Story(params string[] lines) => lines.Select(t => new Paragraph
    {
        Runs = { new Run { Text = t, Font = new FontSpec { Family = "Arial", Size = 10, Bold = t.StartsWith('#') } } },
    }).ToList();

    [Fact]
    public void Recherche_sans_casse_ni_accents_et_mot_entier()
    {
        var s = Story("L'été est là. Ete, étendue", "Fête de l'été");
        Assert.Equal(5, TextSearch.Find(s, "ete", false, false).Count());          // été, Ete, ét(endue), (F)ête, été
        Assert.Equal(3, TextSearch.Find(s, "ete", false, true).Count());           // mots entiers : été, Ete, été
        Assert.Equal(2, TextSearch.Find(s, "été", true, true).Count());
        Assert.Single(TextSearch.Find(s, "Fête", true, true));
        Assert.Empty(TextSearch.Find(s, "Fêt", true, true));
        var first = TextSearch.Find(s, "ete", false, false).First();
        Assert.Equal(new TextMatch(0, 2, 3), first);
    }

    [Fact]
    public void Remplacer_tout_garde_la_mise_en_forme()
    {
        var s = Story("chat noir, chat blanc", "#chat gras");
        int n = TextSearch.ReplaceAll(s, "chat", "chien", false, true);
        Assert.Equal(3, n);
        Assert.Equal("chien noir, chien blanc", s[0].PlainText);
        Assert.Equal("#chien gras", s[1].PlainText);
        Assert.True(s[1].Runs.All(r => r.Font!.Bold));
        Assert.Equal(0, TextSearch.ReplaceAll(s, "chat", "x", false, true));
    }

    [Fact]
    public void Compte_les_mots()
    {
        Assert.Equal(7, TextSearch.CountWords(Story("L’arc-en-ciel brille, 2 fois !", "Fin  du   texte")));
    }

    [Theory]
    [InlineData("", '"', "«\u00A0", 0)]
    [InlineData("dit ", '"', "«\u00A0", 0)]
    [InlineData("Bonjour", '"', "\u00A0»", 0)]
    [InlineData("l", '\'', "’", 0)]
    [InlineData("Quoi ", '?', "\u202F?", 1)]
    [InlineData("Note ", ':', "\u00A0:", 1)]
    public void Typographie_francaise(string before, char typed, string insert, int delete)
    {
        var r = FrenchTypography.Correct(before, typed);
        Assert.NotNull(r);
        Assert.Equal(insert, r!.Value.Insert);
        Assert.Equal(delete, r.Value.DeleteBefore);
    }

    [Fact]
    public void Pas_de_correction_hors_contexte()
    {
        Assert.Null(FrenchTypography.Correct("http", ':'));
        Assert.Null(FrenchTypography.Correct("Quoi", '?'));
        Assert.Null(FrenchTypography.Correct("a", 'b'));
    }
}
