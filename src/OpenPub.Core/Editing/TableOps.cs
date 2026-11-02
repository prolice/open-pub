using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>
/// Operations sur les tableaux : creation, lignes et colonnes, fusion et fractionnement de cellules,
/// geometrie des cellules, styles predefinis, conversion d'un texte tabule (copie depuis Excel ou Word).
/// </summary>
public static class TableOps
{
    public static readonly FontSpec DefaultFont = new() { Family = "Arial", Size = 10, Color = "#1f2a44", Lang = "fr" };

    static List<Paragraph> Text(string text, FontSpec? font = null) =>
        new() { new Paragraph { Align = "left", Runs = { new Run { Text = text, Font = (font ?? DefaultFont).Clone() } } } };

    static TableCell NewCell(FontSpec? font = null, string text = "") => new() { Paragraphs = Text(text, font) };

    /// <summary>Nouveau tableau (objet) de rows x cols cellules dans le rectangle donne.</summary>
    public static Shape Create(int rows, int cols, double x, double y, double w, double h, FontSpec? font = null)
    {
        rows = Math.Clamp(rows, 1, 200); cols = Math.Clamp(cols, 1, 50);
        var t = new TableData
        {
            Cols = Enumerable.Repeat(w / cols, cols).ToList(),
            Rows = Enumerable.Repeat(h / rows, rows).ToList(),
            Border = new LineStyle { Color = "#9aa3b5", Weight = 0.75 },
        };
        for (int r = 0; r < rows; r++) t.Cells.Add(Enumerable.Range(0, cols).Select(_ => NewCell(font)).ToList());
        return new Shape { Kind = ShapeKind.Table, X = x, Y = y, W = w, H = h, Table = t };
    }

    /// <summary>Tableau a partir d'un texte tabule (lignes separees par des retours, cellules par des tabulations).</summary>
    public static Shape? FromTabbedText(string text, double x, double y, double width, FontSpec? font = null)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (lines.Length == 0 || !lines.Any(l => l.Contains('\t'))) return null;
        var cells = lines.Select(l => l.Split('\t')).ToList();
        int cols = cells.Max(c => c.Length);
        double rowH = ((font ?? DefaultFont).Size ?? 10) * 1.9;
        var s = Create(cells.Count, cols, x, y, width, rowH * cells.Count, font);
        for (int r = 0; r < cells.Count; r++)
            for (int c = 0; c < cols; c++)
                s.Table!.Cells[r][c].Paragraphs = Text(c < cells[r].Length ? cells[r][c].Trim() : "", font);
        return s;
    }

    /// <summary>Recalcule la taille de l'objet d'apres ses colonnes et ses lignes (reelles si connues).</summary>
    public static void SyncSize(Shape s)
    {
        if (s.Table is not { } t) return;
        s.W = t.Cols.Sum();
        s.H = (t.ActualRows is { } a && a.Length == t.Rows.Count ? a : t.Rows.ToArray()).Sum();
    }

    /// <summary>Hauteurs utilisees : reelles (calculees par la mise en page) ou minimales.</summary>
    public static double[] RowHeights(TableData t) => t.ActualRows is { } a && a.Length == t.Rows.Count ? a : t.Rows.ToArray();

    /// <summary>Rectangle d'une cellule (fusions comprises), coordonnees de page.</summary>
    public static (double X, double Y, double W, double H) CellRect(Shape s, int row, int col, double[]? heights = null)
    {
        var t = s.Table!;
        var hs = heights ?? RowHeights(t);
        var cell = t.Cells[row][col];
        double x = s.X + t.Cols.Take(col).Sum(), y = s.Y + hs.Take(row).Sum();
        double w = t.Cols.Skip(col).Take(Math.Max(1, cell.ColSpan)).Sum();
        double h = hs.Skip(row).Take(Math.Max(1, cell.RowSpan)).Sum();
        return (x, y, w, h);
    }

    /// <summary>Cellule visible sous un point (coordonnees de page), ou null.</summary>
    public static (int Row, int Col)? CellAt(Shape s, double px, double py)
    {
        if (s.Table is not { } t) return null;
        var hs = RowHeights(t);
        double y = s.Y;
        for (int r = 0; r < t.RowCount; r++)
        {
            double x = s.X;
            for (int c = 0; c < t.ColCount; c++)
            {
                if (px >= x && px < x + t.Cols[c] && py >= y && py < y + hs[r]) return Owner(t, r, c);
                x += t.Cols[c];
            }
            y += hs[r];
        }
        return null;
    }

    /// <summary>Cellule visible qui recouvre (r, c) (elle-meme, ou la cellule fusionnee qui la cache).</summary>
    public static (int Row, int Col) Owner(TableData t, int r, int c)
    {
        for (int rr = r; rr >= 0; rr--)
            for (int cc = c; cc >= 0; cc--)
            {
                var cell = t.Cells[rr][cc];
                if (!cell.Hidden && rr + cell.RowSpan > r && cc + cell.ColSpan > c) return (rr, cc);
            }
        return (r, c);
    }

    /// <summary>Cellule visible suivante (Tab) ou precedente (Maj+Tab) ; null au bout du tableau.</summary>
    public static (int Row, int Col)? Next(TableData t, int r, int c, int dir)
    {
        int i = r * t.ColCount + c;
        for (i += dir; i >= 0 && i < t.RowCount * t.ColCount; i += dir)
        {
            int rr = i / t.ColCount, cc = i % t.ColCount;
            if (!t.Cells[rr][cc].Hidden) return (rr, cc);
        }
        return null;
    }

    // ---------------------------------------------------------------- lignes et colonnes

    public static void InsertRow(Shape s, int at)
    {
        var t = s.Table!;
        at = Math.Clamp(at, 0, t.RowCount);
        var model = t.Cells[Math.Clamp(at == 0 ? 0 : at - 1, 0, t.RowCount - 1)];
        var font = model.Select(c => c.Paragraphs?.FirstOrDefault()?.Runs.FirstOrDefault()?.Font).FirstOrDefault(f => f is not null);
        var row = Enumerable.Range(0, t.ColCount).Select(c => new TableCell { Paragraphs = Text("", font), Fill = model[c].Fill is { } f ? new Fill { Color = f.Color, Opacity = f.Opacity } : null }).ToList();
        // une fusion verticale qui traverse la nouvelle ligne s'agrandit
        for (int c = 0; c < t.ColCount; c++)
        {
            var (orow, ocol) = at < t.RowCount ? Owner(t, at, c) : (-1, -1);
            if (orow >= 0 && orow < at && ocol == c) { t.Cells[orow][ocol].RowSpan++; row[c].Hidden = true; }
        }
        t.Cells.Insert(at, row);
        t.Rows.Insert(at, t.Rows[Math.Clamp(at == 0 ? 0 : at - 1, 0, t.Rows.Count - 1)]);
        t.ActualRows = null;
        SyncSize(s);
    }

    public static void DeleteRow(Shape s, int row)
    {
        var t = s.Table!;
        if (t.RowCount <= 1) return;
        Unmerge(t, row, -1);
        t.Cells.RemoveAt(row);
        t.Rows.RemoveAt(row);
        t.ActualRows = null;
        SyncSize(s);
    }

    public static void InsertCol(Shape s, int at)
    {
        var t = s.Table!;
        at = Math.Clamp(at, 0, t.ColCount);
        int src = Math.Clamp(at == 0 ? 0 : at - 1, 0, t.ColCount - 1);
        for (int r = 0; r < t.RowCount; r++)
        {
            var model = t.Cells[r][src];
            var cell = new TableCell { Paragraphs = Text("", model.Paragraphs?.FirstOrDefault()?.Runs.FirstOrDefault()?.Font), Fill = model.Fill is { } f ? new Fill { Color = f.Color, Opacity = f.Opacity } : null };
            var (orow, ocol) = at < t.ColCount ? Owner(t, r, at) : (-1, -1);
            if (ocol >= 0 && ocol < at && orow == r) { t.Cells[orow][ocol].ColSpan++; cell.Hidden = true; }
            else if (ocol >= 0 && ocol < at && orow < r) cell.Hidden = true;
            t.Cells[r].Insert(at, cell);
        }
        // la nouvelle colonne prend la moitie de sa voisine : la largeur totale ne change pas
        double half = t.Cols[src] / 2;
        t.Cols[src] = half;
        t.Cols.Insert(at, half);
        SyncSize(s);
    }

    public static void DeleteCol(Shape s, int col)
    {
        var t = s.Table!;
        if (t.ColCount <= 1) return;
        Unmerge(t, -1, col);
        double w = t.Cols[col];
        foreach (var row in t.Cells) row.RemoveAt(col);
        t.Cols.RemoveAt(col);
        // la largeur liberee revient a la colonne voisine
        t.Cols[Math.Min(col, t.ColCount - 1)] += w;
        SyncSize(s);
    }

    /// <summary>Defait les fusions qui touchent la ligne (ou la colonne) qui va disparaitre.</summary>
    static void Unmerge(TableData t, int row, int col)
    {
        for (int r = 0; r < t.RowCount; r++)
            for (int c = 0; c < t.ColCount; c++)
            {
                var cell = t.Cells[r][c];
                if (cell.Hidden || (cell.RowSpan == 1 && cell.ColSpan == 1)) continue;
                bool touches = row >= 0 ? row >= r && row < r + cell.RowSpan : col >= c && col < c + cell.ColSpan;
                if (touches) Split(t, r, c);
            }
    }

    // ---------------------------------------------------------------- fusion

    /// <summary>Fusionne le rectangle de cellules [r0..r1] x [c0..c1] ; le texte des cellules est reuni dans la premiere.</summary>
    public static bool Merge(TableData t, int r0, int c0, int r1, int c1)
    {
        if (r1 < r0) (r0, r1) = (r1, r0);
        if (c1 < c0) (c0, c1) = (c1, c0);
        if (r0 == r1 && c0 == c1) return false;
        // les fusions existantes dans la zone sont d'abord defaites
        for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) if (!t.Cells[r][c].Hidden) Split(t, r, c);
        var first = t.Cells[r0][c0];
        first.Paragraphs ??= Text("");
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                if (r == r0 && c == c0) continue;
                var cell = t.Cells[r][c];
                if (cell.Paragraphs is { } ps && ps.Any(p => p.PlainText.Trim().Length > 0)) first.Paragraphs.AddRange(ps);
                cell.Paragraphs = Text("");
                cell.Hidden = true;
            }
        first.RowSpan = r1 - r0 + 1;
        first.ColSpan = c1 - c0 + 1;
        return true;
    }

    /// <summary>Fractionne une cellule fusionnee : les cellules recouvertes reapparaissent, vides.</summary>
    public static void Split(TableData t, int r, int c)
    {
        var cell = t.Cells[r][c];
        for (int rr = r; rr < Math.Min(t.RowCount, r + cell.RowSpan); rr++)
            for (int cc = c; cc < Math.Min(t.ColCount, c + cell.ColSpan); cc++)
                if (rr != r || cc != c) t.Cells[rr][cc].Hidden = false;
        cell.RowSpan = cell.ColSpan = 1;
    }

    // ---------------------------------------------------------------- bordures et styles

    /// <summary>Bordures : "all", "outside", "inside", "none", "horizontal" (lignes entre les rangees).</summary>
    public static void SetBorders(TableData t, string which, LineStyle? style)
    {
        LineStyle? None() => new LineStyle { Weight = 0, Color = "#000000" };
        LineStyle? S() => style is null ? None() : new LineStyle { Color = style.Color, Weight = style.Weight, Dash = style.Dash };
        for (int r = 0; r < t.RowCount; r++)
            for (int c = 0; c < t.ColCount; c++)
            {
                var cell = t.Cells[r][c];
                if (cell.Hidden) continue;
                bool top = r == 0, left = c == 0, bottom = r + cell.RowSpan >= t.RowCount, right = c + cell.ColSpan >= t.ColCount;
                switch (which)
                {
                    case "all": cell.Top = S(); cell.Bottom = S(); cell.Left = S(); cell.Right = S(); break;
                    case "none": cell.Top = cell.Bottom = cell.Left = cell.Right = None(); break;
                    case "outside":
                        cell.Top = top ? S() : cell.Top; cell.Bottom = bottom ? S() : cell.Bottom;
                        cell.Left = left ? S() : cell.Left; cell.Right = right ? S() : cell.Right; break;
                    case "inside":
                        if (!top) cell.Top = S(); if (!bottom) cell.Bottom = S(); if (!left) cell.Left = S(); if (!right) cell.Right = S(); break;
                    case "horizontal":
                        cell.Left = cell.Right = None(); cell.Top = top ? None() : S(); cell.Bottom = bottom ? None() : S(); break;
                }
            }
        if (which is "all" or "none") t.Border = which == "none" ? null : S();
    }

    /// <summary>Styles de tableau predefinis : (nom, couleur d'en-tete, couleur des lignes alternees, couleur des bordures).</summary>
    public static readonly (string Id, string Label, string? Header, string? Band, string Border)[] Styles =
    {
        ("simple", "Simple (bordures fines)", null, null, "#9aa3b5"),
        ("bleu", "En-tête bleu, lignes alternées", "#087aff", "#eef5ff", "#c9d8ef"),
        ("violet", "En-tête violet, lignes alternées", "#9935f4", "#f5edff", "#dccdf2"),
        ("turquoise", "En-tête turquoise", "#06a39e", "#e6faf9", "#bfe6e4"),
        ("encre", "En-tête encre, sobre", "#111c43", null, "#c4cad6"),
        ("aere", "Aéré (lignes horizontales seulement)", null, "#f6f8fc", "#d8dfee"),
    };

    public static void ApplyStyle(TableData t, string id)
    {
        var st = Styles.FirstOrDefault(x => x.Id == id);
        if (st.Id is null) return;
        t.Style = id;
        var border = new LineStyle { Color = st.Border, Weight = 0.75 };
        SetBorders(t, id == "aere" ? "horizontal" : "all", border);
        for (int r = 0; r < t.RowCount; r++)
            for (int c = 0; c < t.ColCount; c++)
            {
                var cell = t.Cells[r][c];
                string? fill = r == 0 && st.Header is not null ? st.Header : st.Band is not null && r % 2 == 0 && r > 0 ? st.Band : null;
                cell.Fill = fill is null ? null : new Fill { Color = fill };
                // en-tete : texte blanc et gras sur fond fonce
                if (cell.Paragraphs is { } ps)
                    foreach (var run in ps.SelectMany(p => p.Runs))
                    {
                        run.Font = (run.Font ?? DefaultFont).Clone();
                        run.Font.Bold = r == 0 && st.Header is not null || (r == 0 && id == "aere");
                        if (r == 0 && st.Header is not null) run.Font.Color = "#ffffff";
                        else if (run.Font.Color == "#ffffff") run.Font.Color = DefaultFont.Color;
                    }
            }
    }
}
