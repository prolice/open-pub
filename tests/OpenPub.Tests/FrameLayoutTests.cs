using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 3 : colonnes dans une zone, habillage reglable, ajustement automatique par zone.</summary>
public class FrameLayoutTests
{
    static readonly FontSpec F = new() { Family = "Mono", Size = 10, Color = "#000000" };

    static Shape TextBox(double x, double y, double w, double h, string text, int z = 1) => new()
    {
        Kind = ShapeKind.Text, X = x, Y = y, W = w, H = h, Z = z,
        Paragraphs = new List<Paragraph> { new() { Align = "left", Runs = { new Run { Text = text, Font = F } } } },
    };

    static string Words(int n) => string.Join(" ", Enumerable.Repeat("mot", n));

    static PageLayouter Layouter(bool shrink = true) => new(new TextLayoutEngine(new MonoMeasurer(), null) { ShrinkOnOverflow = shrink });

    [Fact]
    public void Deux_colonnes_dans_une_zone()
    {
        var t = TextBox(0, 0, 210, 60, Words(60));
        t.Columns = 2; t.ColumnGap = 10;
        var page = new Page { Width = 600, Height = 800, Shapes = { t } };
        var map = Layouter(shrink: false).LayoutPage(page, out var chains);
        var lines = map[t].Lines;
        Assert.Contains(lines, l => l.Left < 1);
        Assert.Contains(lines, l => Math.Abs(l.Left - 110) < 0.01);       // 2e colonne : (210 - 10) / 2 + 10
        Assert.All(lines, l => Assert.True(l.Right - l.Left <= 100.01));
        Assert.Single(chains);
    }

    [Fact]
    public void Habillage_aucun_carre_haut_et_bas()
    {
        Shape Img(string? wrap) => new() { Kind = ShapeKind.Image, X = 0, Y = 40, W = 60, H = 40, Z = 5, Wrap = wrap };
        foreach (var (wrap, check) in new (string?, Action<FrameGeometry>)[]
        {
            ("none", g => Assert.Empty(g.Exclusions)),
            ("square", g => { var e = Assert.Single(g.Exclusions); Assert.False(e.FullWidth); Assert.True(e.Right > 60); }),
            ("topbottom", g => Assert.True(Assert.Single(g.Exclusions).FullWidth)),
        })
        {
            var t = TextBox(0, 0, 300, 200, Words(10));
            var img = Img(wrap);
            var page = new Page { Width = 600, Height = 800, Shapes = { t, img } };
            check(PageLayouter.Geometry(t, PageLayouter.Obstacles(page)));
        }
    }

    [Fact]
    public void Habillage_serre_suit_une_ellipse()
    {
        var t = TextBox(0, 0, 300, 300, Words(10));
        var disk = new Shape { Kind = ShapeKind.Ellipse, X = 0, Y = 100, W = 100, H = 100, Z = 5, Wrap = "tight", Fill = new Fill { Color = "#ff0000" } };
        var page = new Page { Width = 600, Height = 800, Shapes = { t, disk } };
        var ex = PageLayouter.Geometry(t, PageLayouter.Obstacles(page)).Exclusions;
        Assert.True(ex.Count > 10);
        var top = ex.OrderBy(e => e.Top).First();
        var mid = ex.OrderBy(e => Math.Abs((e.Top + e.Bottom) / 2 - 150)).First();
        Assert.True(top.Right < mid.Right - 20);           // plus etroit en haut qu'au milieu
        Assert.InRange(mid.Right, 100, 105);
    }

    [Fact]
    public void Habillage_explicite_meme_si_l_objet_deborde_de_la_zone()
    {
        var t = TextBox(0, 0, 300, 200, Words(10));
        var img = new Shape { Kind = ShapeKind.Image, X = 250, Y = 150, W = 200, H = 200, Z = 5 };
        var page = new Page { Width = 600, Height = 800, Shapes = { t, img } };
        Assert.Empty(PageLayouter.Geometry(t, PageLayouter.Obstacles(page)).Exclusions);     // automatique : effleure seulement
        img.Wrap = "square";
        Assert.Single(PageLayouter.Geometry(t, PageLayouter.Obstacles(page)).Exclusions);
    }

    [Fact]
    public void Ajustement_none_ne_reduit_pas()
    {
        var t = TextBox(0, 0, 100, 24, Words(80));
        var page = new Page { Width = 600, Height = 800, Shapes = { t } };
        Layouter().LayoutPage(page, out var chains);
        Assert.True(chains[0].Scale < 1);
        t.AutoFit = "none";
        Layouter().LayoutPage(page, out chains);
        Assert.Equal(1, chains[0].Scale);
        Assert.True(chains[0].Overflow);
    }
}
