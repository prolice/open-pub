using System.Text;
using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>
/// Publipostage : lecture d'une liste (CSV, point-virgule, tabulations), champs « merge:Colonne » dans le texte,
/// document fusionne (les pages repetees pour chaque destinataire, champs remplaces par leurs valeurs).
/// </summary>
public static class MergeOps
{
    public const string Prefix = "merge:";

    /// <summary>Lit une liste : premiere ligne = noms des colonnes ; separateur detecte (tabulation, point-virgule ou virgule).</summary>
    public static MergeSource Parse(string text, string? name = null)
    {
        var lines = SplitRecords(text.TrimStart('﻿'));
        lines.RemoveAll(l => l.Trim().Length == 0);
        if (lines.Count == 0) return new MergeSource { Name = name };
        char sep = new[] { '\t', ';', ',' }.OrderByDescending(c => lines[0].Count(ch => ch == c)).First();
        var rows = lines.Select(l => SplitFields(l, sep)).ToList();
        var cols = rows[0].Select((c, i) => c.Trim().Length > 0 ? c.Trim() : $"Colonne {i + 1}").ToList();
        // noms de colonnes uniques
        for (int i = 0; i < cols.Count; i++)
            for (int j = 0; j < i; j++)
                if (cols[j] == cols[i]) { cols[i] += $" ({i + 1})"; break; }
        return new MergeSource
        {
            Name = name, Columns = cols,
            Rows = rows.Skip(1).Select(r => Enumerable.Range(0, cols.Count).Select(i => i < r.Count ? r[i].Trim() : "").ToList()).ToList(),
        };
    }

    /// <summary>Lignes du fichier (un saut de ligne entre guillemets reste dans le champ).</summary>
    static List<string> SplitRecords(string text)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (var c in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '"') quoted = !quoted;
            if (c == '\n' && !quoted) { res.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) res.Add(sb.ToString());
        return res;
    }

    static List<string> SplitFields(string line, char sep)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
                continue;
            }
            if (c == sep && !quoted) { res.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        res.Add(sb.ToString());
        return res;
    }

    /// <summary>Valeur d'un champ de publipostage pour une ligne (null si le champ n'est pas un champ de publipostage).</summary>
    public static string? Value(MergeSource src, string field, int row)
    {
        if (!field.StartsWith(Prefix)) return null;
        var col = field[Prefix.Length..];
        int c = src.Columns.IndexOf(col);
        if (c < 0) return "«" + col + "»";
        return row >= 0 && row < src.Rows.Count ? src.Rows[row][c] : "«" + col + "»";
    }

    /// <summary>Le document utilise-t-il des champs de publipostage ?</summary>
    public static bool HasFields(PubDocument doc) =>
        StyleOps.AllParagraphs(doc).Any(p => p.Runs.Any(r => r.Field?.StartsWith(Prefix) == true));

    /// <summary>
    /// Document fusionne : les pages du document sont repetees pour chaque ligne de la source, les champs de
    /// publipostage remplaces par leurs valeurs (texte ordinaire). Les autres champs (numero de page) restent.
    /// </summary>
    public static PubDocument Merged(PubDocument doc, IEnumerable<int>? rows = null)
    {
        var src = doc.Merge ?? new MergeSource();
        var result = ShapeOps.Clone(doc);
        result.Pages.Clear();
        result.Merge = null;
        result.Source = (doc.Source ?? "Document") + " (fusionné)";
        foreach (var r in rows ?? Enumerable.Range(0, src.Rows.Count))
            foreach (var page in doc.Pages)
            {
                var copy = ShapeOps.Clone(page);
                foreach (var p in ParagraphsOf(copy))
                    foreach (var run in p.Runs.Where(x => x.Field?.StartsWith(Prefix) == true))
                    {
                        run.Text = Value(src, run.Field!, r) ?? run.Text;
                        run.Field = null;
                    }
                result.Pages.Add(copy);
            }
        for (int i = 0; i < result.Pages.Count; i++) result.Pages[i].Index = i + 1;
        foreach (var p in result.Pages) foreach (var para in ParagraphsOf(p)) StoryEditor.Normalize(para);
        return result;
    }

    static IEnumerable<Paragraph> ParagraphsOf(Page page)
    {
        foreach (var s in page.Shapes)
        {
            if (s.Paragraphs is { } ps) foreach (var p in ps) yield return p;
            if (s.Table is { } t) foreach (var c in t.Cells.SelectMany(x => x)) if (c.Paragraphs is { } cps) foreach (var p in cps) yield return p;
        }
    }
}
