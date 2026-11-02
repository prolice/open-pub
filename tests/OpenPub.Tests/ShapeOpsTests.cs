using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

public class ShapeOpsTests
{
    static Shape Box(double x, double y, double w = 10, double h = 10) => new() { X = x, Y = y, W = w, H = h };

    static Shape TextFrame(string chain, int index, string? text = null) => new()
    {
        Kind = ShapeKind.Text, X = 10 * index, Y = 0, W = 50, H = 50, Chain = chain, ChainIndex = index,
        Paragraphs = text is null ? new List<Paragraph>() : new List<Paragraph> { new() { Runs = { new Run { Text = text } } } },
    };

    [Fact]
    public void Aligne_a_gauche_et_au_milieu()
    {
        var a = Box(10, 0); var b = Box(40, 30, 20, 20); var c = Box(25, 60);
        ShapeOps.Align(new[] { a, b, c }, AlignEdge.Left);
        Assert.All(new[] { a, b, c }, s => Assert.Equal(10, s.X));
        ShapeOps.Align(new[] { a, b, c }, AlignEdge.Middle);
        // boite commune : y de 0 a 70, milieu 35
        Assert.Equal(30, a.Y); Assert.Equal(25, b.Y); Assert.Equal(30, c.Y);
    }

    [Fact]
    public void Un_groupe_s_aligne_comme_un_seul_objet()
    {
        var a = Box(0, 0); var g1 = Box(50, 0) ; var g2 = Box(70, 0);
        g1.Group = g2.Group = "g";
        ShapeOps.Align(new[] { a, g1, g2 }, AlignEdge.Right);
        Assert.Equal(70, a.X);
        Assert.Equal(50, g1.X);          // le groupe garde sa forme
        Assert.Equal(70, g2.X);
    }

    [Fact]
    public void Repartit_les_espaces_egalement()
    {
        var a = Box(0, 0); var b = Box(15, 0, 30); var c = Box(90, 0);
        ShapeOps.Distribute(new[] { c, a, b }, horizontal: true);
        // largeur totale 100, objets 50 : deux espaces de 25
        Assert.Equal(0, a.X); Assert.Equal(35, b.X); Assert.Equal(90, c.X);
    }

    [Fact]
    public void Le_deplacement_entraine_le_trace()
    {
        var s = Box(0, 0);
        s.Path = new List<PathSegment> { new() { A = "M", X = 0, Y = 0 }, new() { A = "C", X1 = 2, Y1 = 3, X2 = 4, Y2 = 5, X = 10, Y = 10 } };
        ShapeOps.Translate(s, 5, -2);
        Assert.Equal(5, s.X); Assert.Equal(-2, s.Y);
        Assert.Equal(7, s.Path[1].X1); Assert.Equal(3, s.Path[1].Y2); Assert.Equal(15, s.Path[1].X);
    }

    [Fact]
    public void Avancer_d_un_plan_passe_au_dessus_de_l_objet_chevauche()
    {
        var page = new Page { Width = 100, Height = 100 };
        var bg = Box(0, 0); bg.Background = true;
        var a = Box(0, 0); var far = Box(80, 80); var b = Box(5, 5);
        page.Shapes.AddRange(new[] { bg, a, far, b });
        ShapeOps.StepOrder(page, new[] { a }, +1);
        Assert.Equal(new[] { bg, far, b, a }, page.Shapes);     // saute l'objet eloigne, passe au-dessus de b
        ShapeOps.StepOrder(page, new[] { a }, -1);
        Assert.Equal(new[] { bg, far, a, b }, page.Shapes);
        ShapeOps.StepOrder(page, new[] { a }, -1);
        ShapeOps.StepOrder(page, new[] { a }, -1);
        Assert.Equal(bg, page.Shapes[0]);                       // jamais sous le fond de page
        Assert.Equal(Enumerable.Range(1, 4), page.Shapes.Select(s => s.Z));
    }

    [Fact]
    public void Copier_une_zone_suivante_d_une_chaine_emporte_le_texte()
    {
        var page = new Page();
        var head = TextFrame("c1", 0, "Bonjour"); var next = TextFrame("c1", 1);
        page.Shapes.AddRange(new[] { head, next });
        var pasted = ShapeOps.Paste(ShapeOps.Copy(page, new[] { next }), 12);
        var only = Assert.Single(pasted);
        Assert.Null(only.Chain);
        Assert.Equal("Bonjour", only.Paragraphs![0].PlainText);
        Assert.Equal(22, only.X);
        Assert.Equal(10, next.X);                                // l'original n'est pas modifie
    }

    [Fact]
    public void Coller_une_chaine_entiere_cree_une_nouvelle_chaine_et_un_nouveau_groupe()
    {
        var page = new Page();
        var head = TextFrame("c1", 0, "Texte"); var next = TextFrame("c1", 1);
        var img = Box(0, 0); img.Kind = ShapeKind.Image; img.Group = "g1"; head.Group = "g1";
        page.Shapes.AddRange(new[] { head, next, img });
        var pasted = ShapeOps.Paste(ShapeOps.Copy(page, page.Shapes), 0);
        Assert.Equal(3, pasted.Count);
        var chain = pasted.Where(s => s.Chain is not null).ToList();
        Assert.Equal(2, chain.Count);
        Assert.NotEqual("c1", chain[0].Chain);
        Assert.Equal(new int?[] { 0, 1 }, chain.Select(s => s.ChainIndex));
        Assert.Equal("Texte", chain[0].Paragraphs![0].PlainText);
        Assert.Empty(chain[1].Paragraphs!);
        Assert.NotNull(pasted[2].Group);
        Assert.NotEqual("g1", pasted[2].Group);
        Assert.Equal(pasted[0].Group, pasted[2].Group);
    }

    [Fact]
    public void Le_presse_papiers_se_relit()
    {
        var page = new Page();
        page.Shapes.Add(Box(1, 2));
        var clip = ShapeOps.Copy(page, page.Shapes);
        clip.Assets["assets/a.png"] = "AAAA";
        var back = ShapeClip.FromJson(clip.ToJson());
        Assert.NotNull(back);
        Assert.Equal(2, back!.Shapes[0].Y);
        Assert.Equal("AAAA", back.Assets["assets/a.png"]);
        Assert.Null(ShapeClip.FromJson("pas du json"));
    }
}
