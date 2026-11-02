using OpenPub.Core.Model;
using OpenPub.Core.Output;

namespace OpenPub.Tests;

public class OutputTests
{
    [Fact]
    public void Livret_de_8_pages_dans_l_ordre_de_pliage()
    {
        var sides = Booklet.Sides(8);
        Assert.Equal(new[] { (8, 1), (2, 7), (6, 3), (4, 5) }, sides.Select(s => (s.Left, s.Right)));
        Assert.Equal(new[] { true, false, true, false }, sides.Select(s => s.Front));
        Assert.Equal(new[] { 1, 1, 2, 2 }, sides.Select(s => s.Sheet));
    }

    [Fact]
    public void Livret_complete_par_des_pages_blanches()
    {
        Assert.Equal(4, Booklet.PaddedCount(1));
        Assert.Equal(8, Booklet.PaddedCount(5));
        Assert.Equal(12, Booklet.PaddedCount(12));
        // 6 pages -> 8 : les pages 7 et 8 sont blanches (0)
        var sides = Booklet.Sides(6);
        Assert.Equal(new[] { (0, 1), (2, 0), (6, 3), (4, 5) }, sides.Select(s => (s.Left, s.Right)));
        // chaque page reelle apparait une fois exactement
        var pages = Booklet.Sides(10).SelectMany(s => new[] { s.Left, s.Right }).Where(p => p > 0).OrderBy(p => p);
        Assert.Equal(Enumerable.Range(1, 10), pages);
    }

    [Fact]
    public void Recto_verso_manuel_separe_rectos_et_versos()
    {
        var (fronts, backs) = ManualDuplex.Split(new[] { 1, 2, 3, 4, 5 }, 0);
        Assert.Equal(new[] { 1, 3, 5 }, fronts);
        Assert.Equal(new[] { 2, 4, 0 }, backs);   // verso blanc pour la derniere feuille
        var (_, reversed) = ManualDuplex.Split(new[] { 1, 2, 3, 4, 5 }, 0, reverseBacks: true);
        Assert.Equal(new[] { 0, 4, 2 }, reversed);
    }

    [Fact]
    public void Choix_des_pages_plages_et_erreurs()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, PageRange.Parse("", 4, out _));
        Assert.Equal(new[] { 1, 2, 3, 4 }, PageRange.Parse("Toutes", 4, out _));
        Assert.Equal(new[] { 1, 2, 3, 5 }, PageRange.Parse("1-3; 5", 6, out _));
        Assert.Equal(new[] { 4, 5, 6, 2 }, PageRange.Parse("4-, 2, 5", 6, out _));
        Assert.Equal(new[] { 2, 3 }, PageRange.Parse("3-2", 6, out _));
        Assert.Null(PageRange.Parse("1-9", 6, out var err));
        Assert.Contains("6 page", err);
        Assert.Null(PageRange.Parse("deux", 6, out err));
        Assert.Contains("deux", err);
    }

    [Fact]
    public void Fond_perdu_et_traits_de_coupe_agrandissent_la_feuille()
    {
        var a4 = new PrintSheet(595.276, 841.89, PrintSheet.DefaultBleed, CropMarks: false);
        Assert.Equal(8.504, a4.Bleed, 3);
        Assert.Equal(595.276 + 2 * 8.504, a4.MediaWidth, 2);
        Assert.Equal((8.504, 8.504), (Math.Round(a4.Trim.X, 3), Math.Round(a4.Trim.Y, 3)));
        Assert.Equal(0, a4.BleedBox.X, 6);
        Assert.Empty(a4.MarkLines());

        var marks = a4 with { CropMarks = true };
        Assert.True(marks.Margin > marks.Bleed + PrintSheet.MarkLength);
        var lines = marks.MarkLines();
        Assert.Equal(8, lines.Count);
        // aucun trait n'entre dans la zone de fond perdu
        var (bx, by, bw, bh) = marks.BleedBox;
        Assert.All(lines, l =>
        {
            bool inside(double x, double y) => x > bx + 0.01 && x < bx + bw - 0.01 && y > by + 0.01 && y < by + bh - 0.01;
            Assert.False(inside(l.X1, l.Y1) || inside(l.X2, l.Y2));
        });
        // le premier trait prolonge le bord haut du format fini
        Assert.Equal(marks.Trim.Y, lines[0].Y1, 6);
        Assert.Equal(marks.Trim.Y, lines[0].Y2, 6);
    }

    [Fact]
    public void Fond_perdu_prolonge_un_rectangle_qui_touche_le_bord()
    {
        var page = new Page { Width = 200, Height = 300 };
        var fond = new Shape { X = 0, Y = 0, W = 200, H = 100, Fill = new Fill { Color = "#ff0000" } };
        var milieu = new Shape { X = 50, Y = 150, W = 20, H = 20, Fill = new Fill { Color = "#00ff00" } };
        var cercle = new Shape { Kind = ShapeKind.Ellipse, X = 0, Y = 200, W = 50, H = 50, Fill = new Fill { Color = "#0000ff" } };
        var texte = new Shape { Kind = ShapeKind.Text, X = 0, Y = 0, W = 200, H = 20 };
        page.Shapes.AddRange(new[] { fond, milieu, cercle, texte });
        var restore = BleedExtender.Apply(page, 9);
        Assert.Equal((-9.0, -9.0, 218.0, 109.0), (fond.X, fond.Y, fond.W, fond.H));
        Assert.Equal((50.0, 150.0), (milieu.X, milieu.Y));
        Assert.Equal(0, cercle.X);
        Assert.Equal(0, texte.X);
        restore();
        Assert.Equal((0.0, 0.0, 200.0, 100.0), (fond.X, fond.Y, fond.W, fond.H));
    }

    [Fact]
    public void Fond_perdu_d_une_image_garde_ses_proportions()
    {
        // image sans recadrage collee en haut a gauche : elle est tres legerement agrandie et recadree a droite et en bas
        var img = new Shape { Kind = ShapeKind.Image, X = 0, Y = 0, W = 100, H = 50, Src = "a.jpg" };
        var a = BleedExtender.Compute(img, 200, 300, 10)!;
        Assert.Equal((-10.0, -10.0, 110.0, 60.0), (a.X, a.Y, a.W, a.H));
        double sx = (1 - a.CropLeft - a.CropRight) / a.W, sy = (1 - a.CropTop - a.CropBottom) / a.H;
        // meme rapport d'echelle qu'avant (1/100 en largeur, 1/50 en hauteur) : l'image n'est pas deformee
        Assert.Equal(0.5, sx / sy, 6);
        // le bord oppose (droite, bas) ne bouge pas ; la hauteur, plus contrainte, est decouverte entierement
        Assert.Equal(0, a.CropRight, 6);
        Assert.Equal(0, a.CropBottom, 6);
        Assert.Equal(0, a.CropTop, 6);
        Assert.InRange(a.CropLeft, 0, 0.1);

        // image deja recadree : on decouvre la partie cachee, sans agrandir
        var cropped = new Shape { Kind = ShapeKind.Image, X = 0, Y = 100, W = 100, H = 50, Src = "a.jpg", ImageFx = new ImageFx { CropLeft = 0.2 } };
        var b = BleedExtender.Compute(cropped, 200, 300, 10)!;
        Assert.Equal(-10, b.X);
        Assert.Equal(0.2 - 10 * 0.8 / 100, b.CropLeft, 6);
        Assert.Equal(0, b.CropRight, 6);
        Assert.Equal((0.8 / 100), (1 - b.CropLeft - b.CropRight) / b.W, 6);
    }
}
