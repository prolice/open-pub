using OpenPub.Core.Model;

namespace OpenPub.Layout;

/// <summary>
/// Moteur de mise en page du texte : decoupe les paragraphes d'une histoire en lignes et les
/// fait couler dans les zones d'une chaine, en contournant les obstacles (habillage), avec
/// cesure, justification et reduction automatique en cas de debordement (comme Publisher).
/// </summary>
public sealed class TextLayoutEngine
{
    readonly ITextMeasurer _m;
    readonly Hyphenator? _hyph;

    public bool ShrinkOnOverflow { get; set; } = true;
    public double MinScale { get; set; } = 0.5;
    /// <summary>Largeur minimale d'une bande de texte a cote d'un obstacle (points).</summary>
    public double MinBandWidth { get; set; } = 12;
    /// <summary>
    /// Depassement tolere en fin de ligne (points, ou 1 % de la largeur si c'est plus) : Publisher
    /// accepte qu'une ligne empiete legerement sur la marge interieure (mesure : 1,7 pt sur la newsletter).
    /// </summary>
    public double FitTolerance { get; set; } = 1.8;

    public TextLayoutEngine(ITextMeasurer measurer, Hyphenator? hyphenator = null)
    {
        _m = measurer;
        _hyph = hyphenator;
    }

    public ChainLayout Layout(IReadOnlyList<FrameGeometry> frames, IReadOnlyList<Paragraph> paragraphs)
    {
        var full = Run(frames, paragraphs, 1);
        if (!full.Overflow || !ShrinkOnOverflow) return Finish(full, frames);
        double lo = MinScale, hi = 1;
        ChainLayout? best = null;
        for (int i = 0; i < 7; i++)
        {
            var mid = (lo + hi) / 2;
            var r = Run(frames, paragraphs, mid);
            if (r.Overflow) hi = mid; else { lo = mid; best = r; }
        }
        return Finish(best ?? Run(frames, paragraphs, MinScale), frames);
    }

    // ------------------------------------------------------------------ jetons

    sealed class Word
    {
        /// <summary>Morceaux du mot ; Start = position dans le texte du paragraphe ; Hyphen = tiret de cesure ajoute.</summary>
        public List<(string Text, FontSpec Font, int Start, bool Hyphen)> Parts { get; } = new();
        public string Space = "";
        public FontSpec? SpaceFont;
        public bool BreakAfter;
        /// <summary>Position source du debut du mot (ou de ses espaces s'il n'a pas de lettres).</summary>
        public int Start;
        public string Text => string.Concat(Parts.Select(p => p.Text));
    }

    static readonly FontSpec DefaultFont = new() { Family = "Arial", Size = 10, Color = "#000000" };

    static List<Word> Tokenize(Paragraph p)
    {
        var words = new List<Word>();
        int pos = 0;                         // position dans le texte du paragraphe
        var cur = new Word();
        void Flush(bool force = false)
        {
            if (cur.Parts.Count > 0 || cur.Space.Length > 0 || force) { words.Add(cur); cur = new Word { Start = pos }; }
        }
        static bool Invisible(char c) => c is '\u00ad' or '\r';
        static bool Breaking(char c) => c is ' ' or '\t' or '\n' or '\v' or '\u2028';
        foreach (var run in p.Runs)
        {
            var font = run.Font ?? DefaultFont;
            var text = run.Text;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (Invisible(c)) { i++; pos++; continue; }
                if (c == '\n' || c == '\v' || c == '\u2028')
                {
                    cur.BreakAfter = true;
                    i++; pos++;
                    Flush(force: true);
                }
                else if (c == ' ' || c == '\t')
                {
                    int j = i;
                    while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
                    if (cur.Parts.Count == 0 && cur.Space.Length == 0) cur.Start = pos;
                    cur.Space += text[i..j].Replace('\t', ' ');
                    cur.SpaceFont = font;
                    pos += j - i;
                    i = j;
                }
                else
                {
                    if (cur.Space.Length > 0) Flush();
                    if (cur.Parts.Count == 0) cur.Start = pos;
                    int j = i;
                    var sb = new System.Text.StringBuilder();
                    int start = pos;
                    while (j < text.Length && !Breaking(text[j]))
                    {
                        if (!Invisible(text[j])) sb.Append(text[j]);
                        j++;
                    }
                    cur.Parts.Add((sb.ToString(), font, start, false));
                    pos += j - i;
                    i = j;
                }
            }
        }
        Flush();
        return words;
    }

    // ------------------------------------------------------------------ mesures

    double Size(FontSpec f, double scale) => (f.Size ?? 10) * scale;

    double WordWidth(Word w, double scale) => w.Parts.Sum(p => _m.Width(p.Text, p.Font, Size(p.Font, scale)));

    double SpaceWidth(Word w, double scale) =>
        w.Space.Length == 0 ? 0 : _m.Width(w.Space, w.SpaceFont ?? DefaultFont, Size(w.SpaceFont ?? DefaultFont, scale));

    (double LineHeight, double Single, double Ascent, double Size) Metrics(Paragraph p, double scale)
    {
        double single = 0, asc = 0.8, size = 0;
        var runs = p.Runs.Count > 0 ? p.Runs : new List<Run> { new() { Font = DefaultFont } };
        foreach (var r in runs)
        {
            var f = r.Font ?? DefaultFont;
            var h = Size(f, scale) * _m.LineFactor(f);
            if (h > single) { single = h; asc = _m.AscentRatio(f); }
            size = Math.Max(size, Size(f, scale));
        }
        return (single * p.LineSpacingFactor(), single, asc, size);
    }

    /// <summary>
    /// Boite des lettres d'une ligne, relative a son haut : seule cette boite doit eviter les obstacles
    /// (l'interligne peut les chevaucher). Approximation : ascendante 0,8 em, descendante 0,25 em.
    /// </summary>
    static (double Top, double Bottom) GlyphBand(double lineH, double single, double asc, double size)
    {
        double baseline = lineH - single * (1 - asc);
        return (Math.Max(0, baseline - 0.8 * size), Math.Min(lineH, baseline + 0.25 * size));
    }

    // ------------------------------------------------------------------ mise en page

    sealed class State
    {
        public int Frame;
        public double Y;
    }

    ChainLayout Run(IReadOnlyList<FrameGeometry> frames, IReadOnlyList<Paragraph> paragraphs, double scale)
    {
        var result = new ChainLayout { Scale = scale };
        foreach (var f in frames) result.Frames.Add(new FrameLayout { Shape = f.Shape });
        if (frames.Count == 0) return result;
        var st = new State();

        for (int pi = 0; pi < paragraphs.Count; pi++)
        {
            var para = paragraphs[pi];
            var (lineH, single, asc, fsize) = Metrics(para, scale);
            var glyph = GlyphBand(lineH, single, asc, fsize);
            st.Y += para.SpaceBefore ?? 0;
            var words = Tokenize(para);
            int paraLen = para.Runs.Sum(r => r.Text.Length);
            bool first = true;
            int idx = 0;
            Word? remainder = null;

            // un paragraphe fait seulement d'espaces se comporte comme une ligne vide
            if (words.All(w => w.Parts.Count == 0 && !w.BreakAfter)) words.Clear();
            if (words.Count == 0)
            {
                if (!FindBand(frames, st, lineH, glyph, para, true, out _, out _, emptyLine: true)) { result.Overflow = true; return result; }
                AddLine(result, frames, st, pi, lineH, single, asc, new List<LaidPiece>(), 0, 0, 0, para.Align, false, 0, 0, paraLen);
                st.Y += lineH;
            }

            while (idx < words.Count)
            {
                if (!FindBand(frames, st, lineH, glyph, para, first, out var start, out var end)) { result.Overflow = true; return result; }
                double avail = end - start, x = 0, lastSpace = 0;
                int lineStart = remainder is not null ? remainder.Start : words[idx].Start;
                var pieces = new List<LaidPiece>();
                int gaps = 0;
                bool hardBreak = false;

                while (idx < words.Count)
                {
                    var w = remainder ?? words[idx];
                    double ww = WordWidth(w, scale);
                    double tol = Math.Max(FitTolerance, avail * 0.01);
                    if (x + ww <= avail + tol || (pieces.Count == 0 && !CanSplit(w)))
                    {
                        if (pieces.Count > 0 && lastSpace > 0) gaps++;
                        Place(pieces, w, x, scale);
                        x += ww;
                        lastSpace = SpaceWidth(w, scale);
                        x += lastSpace;
                        remainder = null;
                        idx++;
                        if (w.BreakAfter) { hardBreak = true; break; }
                        continue;
                    }
                    // le mot ne tient pas : cesure si possible
                    var split = TrySplit(w, avail + tol - x, scale);
                    if (split is { } s)
                    {
                        if (pieces.Count > 0 && lastSpace > 0) gaps++;
                        Place(pieces, s.Head, x, scale);
                        x += WordWidth(s.Head, scale);
                        lastSpace = 0;
                        remainder = s.Tail;
                    }
                    else if (pieces.Count == 0)
                    {
                        // mot plus large que la ligne et insecable : on le pose quand meme
                        Place(pieces, w, x, scale);
                        x += ww;
                        remainder = null;
                        idx++;
                    }
                    break;
                }

                bool lastOfPara = idx >= words.Count && remainder is null;
                double lineWidth = x - lastSpace;
                int lineEnd = lastOfPara ? paraLen : (remainder is not null ? remainder.Start : words[idx].Start);
                AddLine(result, frames, st, pi, lineH, single, asc, pieces, start, avail, lineWidth, para.Align,
                    justify: !lastOfPara && !hardBreak, gaps, lineStart, lineEnd);
                st.Y += lineH;
                first = false;
            }
            st.Y += para.SpaceAfter ?? 0;
        }
        return result;
    }

    bool CanSplit(Word w) => w.Text.Contains('-') || (_hyph is not null && _hyph.Points(Core(w.Text, out _)).Length > 0);

    /// <summary>Partie lettres du mot (sans ponctuation de debut/fin), et son decalage.</summary>
    static string Core(string text, out int offset)
    {
        int a = 0, b = text.Length;
        while (a < b && !char.IsLetter(text[a])) a++;
        while (b > a && !char.IsLetter(text[b - 1])) b--;
        offset = a;
        return text[a..b];
    }

    (Word Head, Word Tail)? TrySplit(Word w, double room, double scale)
    {
        var text = w.Text;
        var candidates = new List<(int Pos, bool AddHyphen)>();
        for (int i = 2; i < text.Length - 1; i++)
            if (text[i - 1] == '-' && char.IsLetter(text[i - 2]) && char.IsLetter(text[i]))
                candidates.Add((i, false));       // trait d'union existant entre deux lettres ("Mi-septembre")
        if (_hyph is not null)
        {
            var core = Core(text, out var off);
            foreach (var p in _hyph.Points(core)) candidates.Add((p + off, true));
        }
        foreach (var (pos, addHyphen) in candidates.OrderByDescending(c => c.Pos))
        {
            var (head, tail) = SplitAt(w, pos, addHyphen);
            if (WordWidth(head, scale) <= room + 0.01) return (head, tail);
        }
        return null;
    }

    static (Word Head, Word Tail) SplitAt(Word w, int pos, bool addHyphen)
    {
        var head = new Word { Start = w.Start };
        var tail = new Word { Space = w.Space, SpaceFont = w.SpaceFont, BreakAfter = w.BreakAfter };
        int count = 0;
        foreach (var (t, f, st0, hy) in w.Parts)
        {
            if (count + t.Length <= pos) head.Parts.Add((t, f, st0, hy));
            else if (count >= pos) tail.Parts.Add((t, f, st0, hy));
            else
            {
                int k = pos - count;
                head.Parts.Add((t[..k], f, st0, false));
                tail.Parts.Add((t[k..], f, st0 + k, hy));
            }
            count += t.Length;
        }
        tail.Start = tail.Parts.Count > 0 ? tail.Parts[0].Start : w.Start;
        if (addHyphen && head.Parts.Count > 0)
        {
            var (lt, lf, ls, _) = head.Parts[^1];
            head.Parts[^1] = (lt + "-", lf, ls, true);
        }
        return (head, tail);
    }

    void Place(List<LaidPiece> pieces, Word w, double x, double scale)
    {
        foreach (var (t, f, start, hyphen) in w.Parts)
        {
            var size = Size(f, scale);
            var width = _m.Width(t, f, size);
            pieces.Add(new LaidPiece { Text = t, Font = f, SizePt = size, X = x, Width = width, Start = start,
                Length = hyphen ? t.Length - 1 : t.Length, Hyphen = hyphen });
            x += width;
        }
        // l'espace suivant est implicite : on memorise sa largeur dans la piece suivante via X
    }

    /// <summary>Trouve la prochaine bande horizontale libre de hauteur lineH (change de zone si besoin).</summary>
    bool FindBand(IReadOnlyList<FrameGeometry> frames, State st, double lineH, (double Top, double Bottom) glyph, Paragraph para, bool firstLine,
        out double start, out double end, bool emptyLine = false)
    {
        start = end = 0;
        for (int guard = 0; guard < 10000; guard++)
        {
            if (st.Frame >= frames.Count) return false;
            var f = frames[st.Frame];
            if (st.Y + lineH > f.Height + 0.01 && !(st.Y == 0 && lineH > f.Height))
            {
                st.Frame++;
                st.Y = 0;
                continue;
            }
            // seules les lettres [Y + gTop, Y + gBottom] evitent les obstacles ; l'interligne peut les chevaucher
            double lead = glyph.Top, gBottom = glyph.Bottom;
            start = (para.IndentLeft ?? 0) + (firstLine ? para.FirstIndent ?? 0 : 0);
            end = f.Width - (para.IndentRight ?? 0);
            double? jump = null;
            if (emptyLine)
            {
                // Une ligne vide n'a besoin d'aucune largeur : comme Publisher, elle se loge a cote
                // d'un objet tant qu'il reste une bande libre, meme etroite.
                foreach (var e in f.Exclusions)
                    if (e.Top < st.Y + gBottom && e.Bottom > st.Y + lead && e.RawLeft <= 0.01 && e.RawRight >= f.Width - 0.01)
                        jump = Math.Max(jump ?? 0, e.Bottom - lead);
                if (jump is { } je) { st.Y = Math.Max(je, st.Y + 0.5); continue; }
                return true;
            }
            foreach (var e in f.Exclusions)
            {
                if (e.Top >= st.Y + gBottom || e.Bottom <= st.Y + lead) continue;
                if (e.FullWidth) { jump = Math.Max(jump ?? 0, e.Bottom - lead); continue; }
                if (e.Left <= 0.01) start = Math.Max(start, e.Right);
                else end = Math.Min(end, e.Left);
            }
            if (jump is null && end - start < MinBandWidth)
                jump = f.Exclusions.Where(e => e.Top < st.Y + gBottom && e.Bottom > st.Y + lead).Select(e => e.Bottom - lead).DefaultIfEmpty(st.Y + 1).Min();
            if (jump is { } j)
            {
                st.Y = Math.Max(j, st.Y + 0.5);
                continue;
            }
            return true;
        }
        return false;
    }

    static void AddLine(ChainLayout result, IReadOnlyList<FrameGeometry> frames, State st, int pi, double lineH, double single,
        double asc, List<LaidPiece> pieces, double start, double avail, double lineWidth, string? align, bool justify, int gaps,
        int startOffset, int endOffset)
    {
        var f = frames[st.Frame];
        var line = new LaidLine
        {
            Top = f.Y + st.Y,
            Height = lineH,
            Baseline = f.Y + st.Y + lineH - single * (1 - asc),
            ParagraphIndex = pi,
            StartOffset = startOffset,
            EndOffset = endOffset,
            Left = f.X + start,
            Right = f.X + start + avail,
        };
        double offset = 0, extraPerGap = 0;
        switch (align)
        {
            case "center": offset = (avail - lineWidth) / 2; break;
            case "right" or "end": offset = avail - lineWidth; break;
            case "justify" when justify && gaps > 0: extraPerGap = (avail - lineWidth) / gaps; break;
        }
        // Les pieces d'un meme mot se suivent sans espace : on n'ajoute l'espacement de
        // justification qu'entre les mots (quand la piece precedente se termine avant un blanc).
        double shift = 0, prevEnd = double.NaN;
        foreach (var p in pieces)
        {
            double localX = p.X;
            if (!double.IsNaN(prevEnd) && localX > prevEnd + 0.01) shift += extraPerGap;
            prevEnd = localX + p.Width;
            p.X = f.X + start + offset + localX + shift;
        }
        line.Pieces.AddRange(pieces);
        result.Frames[st.Frame].Lines.Add(line);
    }

    static ChainLayout Finish(ChainLayout r, IReadOnlyList<FrameGeometry> frames)
    {
        // alignement vertical (zone isolee uniquement, comme Publisher)
        if (frames.Count == 1 && r.Frames[0].Lines.Count > 0 && frames[0].Shape.VAlign is "middle" or "bottom")
        {
            var f = frames[0];
            var lines = r.Frames[0].Lines;
            double used = lines[^1].Top + lines[^1].Height - lines[0].Top;
            double free = f.Height - used;
            if (free > 0)
            {
                double dy = f.Shape.VAlign == "middle" ? free / 2 : free;
                foreach (var l in lines) { l.Top += dy; l.Baseline += dy; }
            }
        }
        return r;
    }
}
