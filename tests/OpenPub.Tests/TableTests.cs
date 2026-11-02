using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 7 : tableaux.</summary>
public class TableTests
{
    static string Text(TableCell c) => string.Join("\n", c.Paragraphs!.Select(p => p.PlainText));

    [Fact]
    public void Creer_et_trouver_une_cellule()
    {
        var s = TableOps.Create(3, 4, 10, 20, 400, 60);
        var t = s.Table!;
        Assert.Equal(3, t.RowCount); Assert.Equal(4, t.ColCount);
        Assert.Equal(100, t.Cols[0]);
        Assert.Equal((1, 2), TableOps.CellAt(s, 10 + 250, 20 + 25));
        Assert.Null(TableOps.CellAt(s, 0, 0));
        Assert.Equal((1, 0), TableOps.Next(t, 0, 3, +1));
        Assert.Null(TableOps.Next(t, 2, 3, +1));
    }

    [Fact]
    public void Fusionner_et_fractionner()
    {
        var s = TableOps.Create(3, 3, 0, 0, 300, 60);
        var t = s.Table!;
        t.Cells[0][0].Paragraphs![0].Runs[0].Text = "A";
        t.Cells[0][1].Paragraphs![0].Runs[0].Text = "B";
        Assert.True(TableOps.Merge(t, 0, 0, 1, 1));
        Assert.Equal(2, t.Cells[0][0].RowSpan); Assert.Equal(2, t.Cells[0][0].ColSpan);
        Assert.True(t.Cells[1][1].Hidden);
        Assert.Equal("A\nB", Text(t.Cells[0][0]));                     // les textes sont reunis
        Assert.Equal((0, 0), TableOps.CellAt(s, 150, 30));             // la cellule cachee renvoie a la cellule fusionnee
        Assert.Equal((200.0, 40.0), (TableOps.CellRect(s, 0, 0).W, TableOps.CellRect(s, 0, 0).H));
        Assert.Equal((0, 2), TableOps.Next(t, 0, 0, +1));              // Tab saute les cellules cachees
        TableOps.Split(t, 0, 0);
        Assert.False(t.Cells[1][1].Hidden);
        Assert.Equal(1, t.Cells[0][0].RowSpan);
    }

    [Fact]
    public void Lignes_et_colonnes()
    {
        var s = TableOps.Create(2, 2, 0, 0, 200, 40);
        var t = s.Table!;
        TableOps.InsertRow(s, 1);
        Assert.Equal(3, t.RowCount); Assert.Equal(60, s.H);
        TableOps.InsertCol(s, 2);
        Assert.Equal(3, t.ColCount);
        Assert.Equal(200, s.W, 3);                                      // la largeur totale ne change pas
        TableOps.Merge(t, 0, 0, 0, 2);
        TableOps.InsertCol(s, 1);                                       // une colonne dans une fusion l'elargit
        Assert.Equal(4, t.Cells[0][0].ColSpan);
        Assert.True(t.Cells[0][1].Hidden);
        TableOps.DeleteCol(s, 1);
        Assert.Equal(3, t.ColCount);
        TableOps.DeleteRow(s, 0);
        Assert.Equal(2, t.RowCount);
        Assert.All(t.Cells.SelectMany(r => r), c => Assert.False(c.Hidden));
    }

    [Fact]
    public void Coller_un_texte_tabule()
    {
        var s = TableOps.FromTabbedText("Nom\tÂge\r\nLéa\t7\r\nTom\t9\r\n", 0, 0, 200);
        Assert.NotNull(s);
        Assert.Equal(3, s!.Table!.RowCount); Assert.Equal(2, s.Table.ColCount);
        Assert.Equal("9", Text(s.Table.Cells[2][1]));
        Assert.Null(TableOps.FromTabbedText("pas de tabulation", 0, 0, 200));
    }

    [Fact]
    public void Style_et_bordures()
    {
        var s = TableOps.Create(3, 2, 0, 0, 200, 60);
        TableOps.ApplyStyle(s.Table!, "bleu");
        Assert.Equal("#087aff", s.Table!.Cells[0][0].Fill?.Color);
        Assert.True(s.Table.Cells[0][1].Paragraphs![0].Runs[0].Font!.Bold);
        Assert.Equal("#eef5ff", s.Table.Cells[2][0].Fill?.Color);       // lignes alternees
        TableOps.SetBorders(s.Table, "none", null);
        Assert.All(s.Table.Cells.SelectMany(r => r), c => Assert.Equal(0, c.Top!.Weight));
    }

    [Fact]
    public void Les_lignes_s_agrandissent_pour_le_texte()
    {
        var s = TableOps.Create(2, 2, 0, 0, 100, 50);                   // cellules de 50 x 25 pt
        s.Table!.Cells[0][1].Paragraphs![0].Runs[0].Text = string.Join(" ", Enumerable.Repeat("mot", 20));
        s.Table.Cells[0][1].Paragraphs![0].Runs[0].Font = new FontSpec { Family = "Mono", Size = 10 };
        var page = new Page { Width = 600, Height = 800, Shapes = { s } };
        var map = new PageLayouter(new TextLayoutEngine(new MonoMeasurer(), null)).LayoutPage(page, out _);
        Assert.True(s.Table.ActualRows![0] > 25);                      // ligne agrandie
        Assert.Equal(25, s.Table.ActualRows[1], 3);                     // ligne vide : hauteur minimale
        Assert.Equal(s.Table.ActualRows.Sum(), s.H, 3);
        var frame = PageLayouter.CellFrame(s.Table.Cells[0][1]);
        Assert.True(map.ContainsKey(frame));
        Assert.Same(frame, PageLayouter.CellFrame(s.Table.Cells[0][1]));     // zone fantome stable
        Assert.Equal((0, 1), (PageLayouter.CellOf(frame)!.Row, PageLayouter.CellOf(frame)!.Col));
        Assert.Equal(50, frame.X, 3);
    }
}
