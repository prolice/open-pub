using System.Globalization;
using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>Occurrence trouvee dans une histoire : paragraphe, position, longueur.</summary>
public readonly record struct TextMatch(int Para, int Start, int Length)
{
    public TextPos From => new(Para, Start);
    public TextPos To => new(Para, Start + Length);
}

/// <summary>Recherche et remplacement dans le texte des paragraphes (sans traverser les paragraphes).</summary>
public static class TextSearch
{
    public static IEnumerable<TextMatch> Find(IReadOnlyList<Paragraph> paras, string query, bool matchCase, bool wholeWord)
    {
        if (string.IsNullOrEmpty(query)) yield break;
        var cmp = matchCase ? CompareOptions.Ordinal : CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var ci = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;
        for (int p = 0; p < paras.Count; p++)
        {
            var text = paras[p].PlainText;
            int from = 0;
            while (from <= text.Length - query.Length)
            {
                int i = ci.IndexOf(text.AsSpan(from), query.AsSpan(), cmp, out int len);
                if (i < 0) break;
                i += from;
                if (len == 0) len = query.Length;
                if (!wholeWord || (IsBoundary(text, i - 1) && IsBoundary(text, i + len))) yield return new TextMatch(p, i, len);
                from = i + Math.Max(1, len);
            }
        }
    }

    static bool IsBoundary(string text, int i) => i < 0 || i >= text.Length || !char.IsLetterOrDigit(text[i]);

    /// <summary>Remplace une occurrence ; le texte de remplacement prend la mise en forme du debut de l'occurrence.</summary>
    public static TextPos Replace(List<Paragraph> paras, TextMatch m, string replacement)
    {
        var font = StoryEditor.FontAt(paras, new TextPos(m.Para, m.Start + 1));
        var at = StoryEditor.Delete(paras, m.From, m.To);
        return replacement.Length == 0 ? at : StoryEditor.Insert(paras, at, replacement, font);
    }

    /// <summary>Remplace toutes les occurrences ; renvoie leur nombre.</summary>
    public static int ReplaceAll(List<Paragraph> paras, string query, string replacement, bool matchCase, bool wholeWord)
    {
        // de la fin vers le debut : les positions restantes ne bougent pas
        var all = Find(paras, query, matchCase, wholeWord).ToList();
        for (int k = all.Count - 1; k >= 0; k--) Replace(paras, all[k], replacement);
        return all.Count;
    }

    /// <summary>Nombre de mots d'un texte (suites de lettres ou chiffres, apostrophes et traits d'union compris).</summary>
    public static int CountWords(IEnumerable<Paragraph> paras)
    {
        int n = 0;
        foreach (var p in paras)
        {
            bool inWord = false;
            foreach (var c in p.PlainText)
            {
                bool w = char.IsLetterOrDigit(c) || (inWord && c is '\'' or '’' or '-');
                if (w && !inWord) n++;
                inWord = w;
            }
        }
        return n;
    }
}
