using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 2 : listes, tabulations, lettrines, styles de caractere (mesure monospace : 5 pt par caractere en 10 pt).</summary>
public class ParagraphLayoutTests
{
    static FontSpec F(Action<FontSpec>? change = null)
    {
        var f = new FontSpec { Family = "Mono", Size = 10, Color = "#000000" };
        change?.Invoke(f);
        return f;
    }

    static Paragraph P(string text, FontSpec? font = null) => new() { Align = "left", Runs = { new Run { Text = text, Font = font ?? F() } } };

    static FrameGeometry Frame(double w = 300, double h = 400) =>
        new() { Shape = new Shape { Kind = ShapeKind.Text, W = w, H = h }, X = 0, Y = 0, Width = w, Height = h };

    static ChainLayout Lay(params Paragraph[] paras) =>
        new TextLayoutEngine(new MonoMeasurer(), null) { ShrinkOnOverflow = false }.Layout(new[] { Frame() }, paras);

    static List<LaidLine> Lines(ChainLayout r) => r.Frames.SelectMany(f => f.Lines).ToList();

    [Fact]
    public void Numerotation_continue_et_niveaux()
    {
        Paragraph Item(string t, int level = 0, string fmt = "1.") =>
            new() { List = new ListStyle { Kind = "number", Format = fmt, Level = level }, Runs = { new Run { Text = t, Font = F() } } };
        var paras = new[] { Item("un"), Item("deux"), Item("a", 1, "a)"), Item("b", 1, "a)"), Item("trois"), P("fin"), Item("re") };
        var m = TextLayoutEngine.ListMarkers(paras);
        Assert.Equal(new[] { "1.", "2.", "a)", "b)", "3.", null, "1." }, m);
        Assert.Equal("iv.", TextLayoutEngine.FormatNumber(4, "i."));
        Assert.Equal("AB)", TextLayoutEngine.FormatNumber(28, "A)"));
    }

    [Fact]
    public void Puce_dans_le_retrait_suspendu()
    {
        var p = P("Bonjour");
        p.List = new ListStyle { Kind = "bullet", Symbol = "•" };
        p.IndentLeft = 18; p.FirstIndent = -18;
        var line = Lines(Lay(p))[0];
        var mark = Assert.Single(line.Marks);
        Assert.Equal("•", mark.Text);
        Assert.Equal(0, mark.X, 3);                    // a la position du retrait de premiere ligne
        Assert.Equal(18, line.Pieces[0].X, 3);         // texte au retrait gauche
        Assert.Equal(0, line.Pieces[0].Start);         // le curseur ne s'arrete pas sur la puce
    }

    [Fact]
    public void Tabulation_par_defaut_et_taquets()
    {
        // par defaut : tous les 36 pt
        var line = Lines(Lay(P("ab\tcd")))[0];
        Assert.Equal(36, line.Pieces[1].X, 3);
        Assert.Equal(3, line.Pieces[1].Start);
        // taquet droit a 200 pt avec points de suite : « 12,50 » se termine a 200
        var p = P("Prix\t12,50");
        p.Tabs = new List<TabStop> { new() { Pos = 200, Align = "right", Leader = "dot" } };
        var l2 = Lines(Lay(p))[0];
        var price = l2.Pieces[^1];
        Assert.Equal(200, price.X + price.Width, 3);
        Assert.Contains(l2.Marks, m => m.Text.StartsWith("."));
        // taquet decimal : la virgule tombe sur le taquet
        p.Tabs = new List<TabStop> { new() { Pos = 100, Align = "decimal" } };
        var l3 = Lines(Lay(p))[0];
        Assert.Equal(100, l3.Pieces[^1].X + 2 * 5, 3);   // « 12 » avant la virgule
    }

    [Fact]
    public void Lettrine_sur_trois_lignes()
    {
        var p = P(string.Join(" ", Enumerable.Repeat("mot", 60)));
        p.DropCap = 3;
        var lines = Lines(Lay(p));
        var cap = Assert.Single(lines[0].Marks);
        Assert.Equal("m", cap.Text);
        Assert.True(cap.SizePt > 25);
        Assert.Equal(1, lines[0].Pieces[0].Start);                       // le texte reprend apres la lettre
        Assert.True(lines[0].Pieces[0].X > cap.Width);                   // 3 premieres lignes decalees
        Assert.True(lines[2].Pieces[0].X > cap.Width);
        Assert.Equal(0, lines[3].Pieces[0].X, 3);                        // puis retour a la marge
        Assert.Equal(2 * lines[0].Height, cap.Dy, 3);                    // pied de la lettrine sur la 3e ligne
    }

    [Fact]
    public void Majuscules_exposant_et_espacement()
    {
        var caps = Lines(Lay(P("Bonjour", F(f => f.Caps = "small"))))[0];
        Assert.Equal("B", caps.Pieces[0].Text);
        Assert.Equal("ONJOUR", caps.Pieces[1].Text);
        Assert.True(caps.Pieces[1].SizePt < caps.Pieces[0].SizePt);
        Assert.Equal(1, caps.Pieces[1].Start);                             // positions source conservees
        var sup = Lines(Lay(P("x", F(f => f.Script = "super"))))[0].Pieces[0];
        Assert.True(sup.Dy < 0 && sup.SizePt < 10);
        var wide = Lines(Lay(P("abcd", F(f => f.Tracking = 100))))[0].Pieces[0];
        Assert.Equal(4 * 5 + 4 * 1, wide.Width, 3);                        // +1 pt par caractere (100/1000 de 10 pt)
        Assert.Equal(2 * 5 + 2, wide.Advance(new MonoMeasurer(), 2), 3);
        Assert.Equal("ABC", Lines(Lay(P("abc", F(f => f.Caps = "all"))))[0].Pieces[0].Text);
    }

    [Fact]
    public void Couper_un_paragraphe_garde_la_liste()
    {
        var p = P("un deux");
        p.List = new ListStyle { Kind = "bullet" };
        var paras = new List<Paragraph> { p };
        Core.Editing.StoryEditor.SplitParagraph(paras, new Core.Editing.TextPos(0, 2));
        Assert.Equal(2, paras.Count);
        Assert.NotNull(paras[1].List);
        Assert.NotSame(paras[0].List, paras[1].List);
    }
}
