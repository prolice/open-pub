using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Police fictive a chasse fixe : chaque caractere mesure 0,5 x la taille.</summary>
sealed class MonoMeasurer : ITextMeasurer
{
    public double Width(string text, FontSpec font, double sizePt) => text.Length * sizePt * 0.5;
    public double LineFactor(FontSpec font) => 1.2;
    public double AscentRatio(FontSpec font) => 0.8;
}

public class HyphenatorTests
{
    [Theory]
    [InlineData("compétitions", "com-pé-ti-tions")]
    [InlineData("malheureusement", "mal-heu-reu-se-ment")]
    [InlineData("rentrée", "ren-trée")]
    [InlineData("chat", "chat")]                // mot court : pas de cesure
    public void Cesure_francaise(string word, string expected) =>
        Assert.Equal(expected, Hyphenator.French.Insert(word));
}

public class LayoutTests
{
    static readonly FontSpec F = new() { Family = "Mono", Size = 10, Color = "#000000" };

    static Paragraph P(string text, string align = "left") => new() { Align = align, Runs = { new Run { Text = text, Font = F } } };

    static FrameGeometry Frame(double x, double w, double h, params Exclusion[] ex) =>
        new() { Shape = new Shape { Kind = ShapeKind.Text, X = x, W = w, H = h }, X = x, Y = 0, Width = w, Height = h, Exclusions = ex.ToList() };

    static TextLayoutEngine Engine(bool hyphenate = true, bool shrink = true) =>
        new(new MonoMeasurer(), hyphenate ? Hyphenator.French : null) { ShrinkOnOverflow = shrink };

    /// <summary>Texte pose, tirets de cesure de fin de ligne retires (les mots coupes sont recolles).</summary>
    static string LaidText(ChainLayout r)
    {
        var words = new List<string>();
        string carry = "";
        foreach (var line in r.Frames.SelectMany(f => f.Lines))
        {
            var lineText = string.Join(" ", GroupWords(line));
            var parts = lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0) continue;
            parts[0] = carry + parts[0];
            carry = "";
            if (parts[^1].EndsWith('-') && parts[^1].Length > 1 && char.IsLetter(parts[^1][^2]))
            {
                carry = parts[^1][..^1];
                parts.RemoveAt(parts.Count - 1);
            }
            words.AddRange(parts);
        }
        if (carry.Length > 0) words.Add(carry);
        return string.Join(" ", words);
    }

    /// <summary>Recolle les pieces contigues d'une ligne en mots.</summary>
    static IEnumerable<string> GroupWords(LaidLine line)
    {
        var cur = "";
        double end = double.NaN;
        foreach (var p in line.Pieces)
        {
            if (!double.IsNaN(end) && p.X > end + 0.01) { yield return cur; cur = ""; }
            cur += p.Text;
            end = p.X + p.Width;
        }
        if (cur.Length > 0) yield return cur;
    }

    const string Text = "Alors que la rentrée bat son plein dans les différents clubs, certains membres ne se sont pas arrêtés de tout l’été! Plusieurs bénévoles ont participé aux stages régionaux, d’autres se sont envolés pour le Kazakhstan où ils ont rejoint les équipes locales.";

    [Fact]
    public void Le_texte_coule_dans_les_zones_suivantes_sans_perte_ni_doublon()
    {
        var frames = new[] { Frame(0, 100, 60), Frame(120, 100, 60), Frame(240, 100, 200) };
        var r = Engine(shrink: false).Layout(frames, new[] { P(Text) });
        Assert.False(r.Overflow);
        Assert.True(r.Frames[0].Lines.Count > 0 && r.Frames[1].Lines.Count > 0 && r.Frames[2].Lines.Count > 0);
        Assert.Equal(Text, LaidText(r));
    }

    [Fact]
    public void Aucune_ligne_ne_deborde_de_sa_zone()
    {
        var frames = new[] { Frame(0, 100, 60), Frame(120, 100, 200) };
        var r = Engine(shrink: false).Layout(frames, new[] { P(Text, "justify") });
        foreach (var (fl, geo) in r.Frames.Zip(frames))
            foreach (var line in fl.Lines)
            {
                Assert.True(line.Top + line.Height <= geo.Y + geo.Height + 0.01);
                Assert.All(line.Pieces, p => Assert.True(p.X + p.Width <= geo.X + geo.Width + 0.5, $"'{p.Text}' depasse"));
            }
    }

    [Fact]
    public void Une_cesure_ne_duplique_pas_le_mot()
    {
        // largeur choisie pour forcer la coupure de "malheureusement"
        var r = Engine(shrink: false).Layout(new[] { Frame(0, 70, 200) }, new[] { P("est malheureusement défaite deux fois.") });
        Assert.Equal("est malheureusement défaite deux fois.", LaidText(r));
        Assert.Contains(r.Frames[0].Lines.SelectMany(l => l.Pieces), p => p.Text.EndsWith('-'));
    }

    [Fact]
    public void Pas_de_coupure_apres_un_tiret_initial()
    {
        var r = Engine(shrink: false).Layout(new[] { Frame(0, 75, 200) }, new[] { P("Junior B (U18, -48Kg) décroche une médaille") });
        Assert.DoesNotContain(r.Frames[0].Lines.SelectMany(l => l.Pieces), p => p.Text == "-");
    }

    [Fact]
    public void Un_obstacle_pleine_largeur_est_saute()
    {
        var frame = Frame(0, 100, 200, new Exclusion(24, 80, 0, 100, true));
        var r = Engine(shrink: false).Layout(new[] { frame }, new[] { P(Text) });
        // seules les lettres (ascendante 0,8 em, descendante 0,25 em autour de la ligne de base) evitent l'obstacle
        Assert.DoesNotContain(r.Frames[0].Lines, l => l.Pieces.Count > 0 && l.Baseline - 8 < 80 && Math.Min(l.Top + l.Height, l.Baseline + 2.5) > 24);
        Assert.Contains(r.Frames[0].Lines, l => l.Baseline - 8 >= 80);
    }

    [Fact]
    public void Une_ligne_vide_se_loge_a_cote_d_un_obstacle()
    {
        // obstacle qui laisse une bande libre de 10 pt : trop etroite pour du texte, suffisante pour une ligne vide
        var ex = new Exclusion(20, 80, 0, 100, true) { RawLeft = 0, RawRight = 90 };
        var paras = new[] { P("Avant"), P(""), P(""), P("Apres") };
        var r = Engine(shrink: false).Layout(new[] { Frame(0, 100, 300, ex) }, paras);
        var lines = r.Frames[0].Lines;
        Assert.True(lines[1].Top < 80 && lines[2].Top < 80, "les lignes vides restent a cote de l'obstacle");
        Assert.True(lines[3].Baseline - 8 >= 80, "le texte passe sous l'obstacle");
    }

    [Fact]
    public void Un_obstacle_lateral_reduit_la_largeur()
    {
        var frame = Frame(0, 200, 300, new Exclusion(0, 60, 0, 90, false));
        var r = Engine(shrink: false).Layout(new[] { frame }, new[] { P(Text) });
        var first = r.Frames[0].Lines[0];
        Assert.True(first.Pieces[0].X >= 90 - 0.01, "le texte commence a droite de l'obstacle");
    }

    [Fact]
    public void Reduction_automatique_quand_le_texte_deborde()
    {
        var r = Engine().Layout(new[] { Frame(0, 100, 70) }, new[] { P(Text) });
        Assert.False(r.Overflow);
        Assert.True(r.Scale < 1);
        Assert.Equal(Text, LaidText(r));
    }

    [Fact]
    public void Justification_aligne_les_deux_bords()
    {
        var r = Engine(shrink: false).Layout(new[] { Frame(0, 100, 400) }, new[] { P(Text, "justify") });
        var line = r.Frames[0].Lines[0];
        var last = line.Pieces[^1];
        Assert.Equal(100, last.X + last.Width, 1);
    }
}
