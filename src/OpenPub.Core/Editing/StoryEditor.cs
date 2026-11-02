using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>Position dans une histoire : paragraphe et decalage (en caracteres) dans son texte.</summary>
public readonly record struct TextPos(int Para, int Offset) : IComparable<TextPos>
{
    public int CompareTo(TextPos o) => Para != o.Para ? Para.CompareTo(o.Para) : Offset.CompareTo(o.Offset);
    public static bool operator <(TextPos a, TextPos b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPos a, TextPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPos a, TextPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPos a, TextPos b) => a.CompareTo(b) >= 0;
    public static TextPos Min(TextPos a, TextPos b) => a <= b ? a : b;
    public static TextPos Max(TextPos a, TextPos b) => a >= b ? a : b;
}

/// <summary>
/// Operations d'edition sur les paragraphes d'une histoire (liste de paragraphes / segments).
/// Garantit qu'un paragraphe garde toujours au moins un segment (qui porte la mise en forme,
/// meme vide) et fusionne les segments voisins de meme mise en forme.
/// </summary>
public static class StoryEditor
{
    static readonly FontSpec Default = new() { Family = "Arial", Size = 10, Color = "#000000", Lang = "fr" };

    public static int Length(Paragraph p) => p.Runs.Sum(r => r.Text.Length);

    public static TextPos End(List<Paragraph> paras) =>
        paras.Count == 0 ? new TextPos(0, 0) : new TextPos(paras.Count - 1, Length(paras[^1]));

    public static TextPos Clamp(List<Paragraph> paras, TextPos p)
    {
        if (paras.Count == 0) return new TextPos(0, 0);
        int pi = Math.Clamp(p.Para, 0, paras.Count - 1);
        return new TextPos(pi, Math.Clamp(p.Offset, 0, Length(paras[pi])));
    }

    /// <summary>Mise en forme a utiliser pour du texte saisi a cette position (caractere precedent).</summary>
    public static FontSpec FontAt(List<Paragraph> paras, TextPos pos)
    {
        if (paras.Count == 0) return Default.Clone();
        var p = paras[Math.Clamp(pos.Para, 0, paras.Count - 1)];
        if (p.Runs.Count == 0) return Default.Clone();
        if (pos.Offset <= 0) return (p.Runs[0].Font ?? Default).Clone();
        int acc = 0;
        foreach (var r in p.Runs)
        {
            // segment contenant le caractere qui precede la position
            if (pos.Offset - 1 < acc + r.Text.Length) return (r.Font ?? Default).Clone();
            acc += r.Text.Length;
        }
        return (p.Runs[^1].Font ?? Default).Clone();
    }

    /// <summary>Coupe le segment a la position, pour que celle-ci tombe entre deux segments ; renvoie l'indice du segment suivant.</summary>
    static int SplitRunAt(Paragraph p, int offset)
    {
        int acc = 0;
        for (int i = 0; i < p.Runs.Count; i++)
        {
            var r = p.Runs[i];
            if (offset == acc) return i;
            if (offset < acc + r.Text.Length)
            {
                int k = offset - acc;
                var tail = new Run { Text = r.Text[k..], Font = r.Font?.Clone() };
                r.Text = r.Text[..k];
                p.Runs.Insert(i + 1, tail);
                return i + 1;
            }
            acc += r.Text.Length;
        }
        return p.Runs.Count;
    }

    public static void Normalize(Paragraph p, FontSpec? fallback = null)
    {
        var merged = new List<Run>();
        foreach (var r in p.Runs)
        {
            if (merged.Count > 0 && merged[^1].Font?.SameAs(r.Font) == true) merged[^1].Text += r.Text;
            else merged.Add(r);
        }
        var nonEmpty = merged.Where(r => r.Text.Length > 0).ToList();
        p.Runs = nonEmpty.Count > 0 ? nonEmpty
            : new List<Run> { new() { Text = "", Font = (merged.FirstOrDefault()?.Font ?? fallback ?? Default).Clone() } };
    }

    static Paragraph CloneProps(Paragraph p) => new()
    {
        Align = p.Align, LineHeight = p.LineHeight, SpaceBefore = p.SpaceBefore, SpaceAfter = p.SpaceAfter,
        IndentLeft = p.IndentLeft, IndentRight = p.IndentRight, FirstIndent = p.FirstIndent,
        List = p.List?.Clone(), Tabs = p.Tabs?.Select(t => t.Clone()).ToList(),
        // la lettrine reste au premier paragraphe
    };

    /// <summary>Insere du texte (les sauts de ligne creent des paragraphes) ; renvoie la position apres l'insertion.</summary>
    public static TextPos Insert(List<Paragraph> paras, TextPos pos, string text, FontSpec? font = null)
    {
        if (paras.Count == 0) paras.Add(new Paragraph { Runs = { new Run { Text = "", Font = (font ?? Default).Clone() } } });
        pos = Clamp(paras, pos);
        font ??= FontAt(paras, pos);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) pos = SplitParagraph(paras, pos);
            if (lines[i].Length == 0) continue;
            var p = paras[pos.Para];
            int idx = SplitRunAt(p, pos.Offset);
            p.Runs.Insert(idx, new Run { Text = lines[i], Font = font.Clone() });
            Normalize(p, font);
            pos = new TextPos(pos.Para, pos.Offset + lines[i].Length);
        }
        return pos;
    }

    /// <summary>Coupe le paragraphe a la position (touche Entree) ; le nouveau paragraphe reprend ses reglages.</summary>
    public static TextPos SplitParagraph(List<Paragraph> paras, TextPos pos)
    {
        pos = Clamp(paras, pos);
        var p = paras[pos.Para];
        var font = FontAt(paras, pos);
        int idx = SplitRunAt(p, pos.Offset);
        var next = CloneProps(p);
        next.Runs = p.Runs.Skip(idx).ToList();
        p.Runs = p.Runs.Take(idx).ToList();
        if (next.Runs.Count == 0) next.Runs.Add(new Run { Text = "", Font = font.Clone() });
        if (p.Runs.Count == 0) p.Runs.Add(new Run { Text = "", Font = font.Clone() });
        Normalize(p, font);
        Normalize(next, font);
        paras.Insert(pos.Para + 1, next);
        return new TextPos(pos.Para + 1, 0);
    }

    /// <summary>Supprime le texte entre deux positions (fusionne les paragraphes) ; renvoie la position du debut.</summary>
    public static TextPos Delete(List<Paragraph> paras, TextPos a, TextPos b)
    {
        if (paras.Count == 0) return new TextPos(0, 0);
        var from = Clamp(paras, TextPos.Min(a, b));
        var to = Clamp(paras, TextPos.Max(a, b));
        if (from == to) return from;
        var first = paras[from.Para];
        var keepFont = FontAt(paras, new TextPos(from.Para, from.Offset + 1 <= Length(first) ? from.Offset + 1 : from.Offset));
        int i1 = SplitRunAt(first, from.Offset);
        if (from.Para == to.Para)
        {
            int i2 = SplitRunAt(first, to.Offset);
            first.Runs.RemoveRange(i1, i2 - i1);
        }
        else
        {
            var last = paras[to.Para];
            int j = SplitRunAt(last, to.Offset);
            first.Runs = first.Runs.Take(i1).Concat(last.Runs.Skip(j)).ToList();
            paras.RemoveRange(from.Para + 1, to.Para - from.Para);
        }
        Normalize(first, keepFont);
        return from;
    }

    /// <summary>Applique une modification de mise en forme au texte entre deux positions.</summary>
    public static void ApplyFont(List<Paragraph> paras, TextPos a, TextPos b, Action<FontSpec> change)
    {
        var from = Clamp(paras, TextPos.Min(a, b));
        var to = Clamp(paras, TextPos.Max(a, b));
        for (int pi = from.Para; pi <= to.Para; pi++)
        {
            var p = paras[pi];
            int s = pi == from.Para ? from.Offset : 0;
            int e = pi == to.Para ? to.Offset : Length(p);
            if (Length(p) == 0)
            {
                // paragraphe vide : sa mise en forme (taille de la ligne) change aussi
                foreach (var r in p.Runs) { r.Font = (r.Font ?? Default).Clone(); change(r.Font); }
                continue;
            }
            if (s >= e) continue;
            int i1 = SplitRunAt(p, s), i2 = SplitRunAt(p, e);
            for (int i = i1; i < i2; i++)
            {
                var f = (p.Runs[i].Font ?? Default).Clone();
                change(f);
                p.Runs[i].Font = f;
            }
            Normalize(p);
        }
    }

    /// <summary>Applique un reglage de paragraphe a tous les paragraphes touches par la selection.</summary>
    public static void ApplyParagraph(List<Paragraph> paras, TextPos a, TextPos b, Action<Paragraph> change)
    {
        var from = Clamp(paras, TextPos.Min(a, b));
        var to = Clamp(paras, TextPos.Max(a, b));
        for (int pi = from.Para; pi <= to.Para; pi++) change(paras[pi]);
    }

    /// <summary>Texte brut entre deux positions (paragraphes separes par des sauts de ligne).</summary>
    public static string GetText(List<Paragraph> paras, TextPos a, TextPos b)
    {
        var from = Clamp(paras, TextPos.Min(a, b));
        var to = Clamp(paras, TextPos.Max(a, b));
        var sb = new System.Text.StringBuilder();
        for (int pi = from.Para; pi <= to.Para; pi++)
        {
            var t = paras[pi].PlainText;
            int s = pi == from.Para ? from.Offset : 0;
            int e = pi == to.Para ? to.Offset : t.Length;
            if (pi > from.Para) sb.Append('\n');
            sb.Append(t, s, Math.Max(0, e - s));
        }
        return sb.ToString();
    }

    /// <summary>Limites du mot autour de la position (double-clic).</summary>
    public static (TextPos From, TextPos To) WordAt(List<Paragraph> paras, TextPos pos)
    {
        pos = Clamp(paras, pos);
        var t = paras.Count == 0 ? "" : paras[pos.Para].PlainText;
        int s = pos.Offset, e = pos.Offset;
        while (s > 0 && char.IsLetterOrDigit(t[s - 1])) s--;
        while (e < t.Length && char.IsLetterOrDigit(t[e])) e++;
        return (new TextPos(pos.Para, s), new TextPos(pos.Para, e));
    }

    /// <summary>Position precedente / suivante (un caractere), en passant d'un paragraphe a l'autre.</summary>
    public static TextPos Step(List<Paragraph> paras, TextPos pos, int dir)
    {
        pos = Clamp(paras, pos);
        if (dir < 0)
        {
            if (pos.Offset > 0) return pos with { Offset = pos.Offset - 1 };
            return pos.Para > 0 ? new TextPos(pos.Para - 1, Length(paras[pos.Para - 1])) : pos;
        }
        if (pos.Offset < Length(paras[pos.Para])) return pos with { Offset = pos.Offset + 1 };
        return pos.Para < paras.Count - 1 ? new TextPos(pos.Para + 1, 0) : pos;
    }

    /// <summary>Debut du mot precedent / suivant (Ctrl + fleches).</summary>
    public static TextPos StepWord(List<Paragraph> paras, TextPos pos, int dir)
    {
        pos = Clamp(paras, pos);
        var t = paras[pos.Para].PlainText;
        int i = pos.Offset;
        if (dir < 0)
        {
            if (i == 0) return Step(paras, pos, -1);
            while (i > 0 && char.IsWhiteSpace(t[i - 1])) i--;
            while (i > 0 && !char.IsWhiteSpace(t[i - 1])) i--;
        }
        else
        {
            if (i >= t.Length) return Step(paras, pos, 1);
            while (i < t.Length && !char.IsWhiteSpace(t[i])) i++;
            while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        }
        return pos with { Offset = i };
    }
}
