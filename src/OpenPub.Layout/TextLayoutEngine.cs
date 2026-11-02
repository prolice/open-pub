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

    /// <param name="shrink">Reduire le texte en cas de debordement (null : reglage du moteur).</param>
    public ChainLayout Layout(IReadOnlyList<FrameGeometry> frames, IReadOnlyList<Paragraph> paragraphs, bool? shrink = null)
    {
        var full = Run(frames, paragraphs, 1);
        if (!full.Overflow || !(shrink ?? ShrinkOnOverflow)) return Finish(full, frames);
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
        /// <summary>Jeton de tabulation (le caractere \t a la position Start).</summary>
        public bool IsTab;
        /// <summary>Position source du debut du mot (ou de ses espaces s'il n'a pas de lettres).</summary>
        public int Start;
        public string Text => string.Concat(Parts.Select(p => p.Text));
    }

    static readonly FontSpec DefaultFont = new() { Family = "Arial", Size = 10, Color = "#000000" };

    static List<Word> Tokenize(Paragraph p, int skip = 0)
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
                if (pos < skip) { i++; pos++; continue; }          // lettrine : premiere lettre posee a part
                if (Invisible(c)) { i++; pos++; continue; }
                if (c == '\n' || c == '\v' || c == '\u2028')
                {
                    cur.BreakAfter = true;
                    i++; pos++;
                    Flush(force: true);
                }
                else if (c == '\t')
                {
                    Flush();
                    words.Add(new Word { IsTab = true, Start = pos });
                    cur = new Word { Start = pos + 1 };
                    i++; pos++;
                }
                else if (c == ' ')
                {
                    int j = i;
                    while (j < text.Length && text[j] == ' ') j++;
                    if (cur.Parts.Count == 0 && cur.Space.Length == 0) cur.Start = pos;
                    cur.Space += text[i..j];
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

    /// <summary>
    /// Decoupe un morceau de texte selon son apparence : majuscules (toutes ou petites), exposant / indice.
    /// Chaque segment garde le meme nombre de caracteres que la source (la position du curseur reste exacte).
    /// </summary>
    static IEnumerable<(string Text, double Size, double Dy, int Offset)> Segments(string text, FontSpec f, double size)
    {
        double sz = f.Script is "super" or "sub" ? size * 0.62 : size;
        double dy = f.Script switch { "super" => -0.34 * size, "sub" => 0.14 * size, _ => 0 };
        if (f.Caps == "all") { yield return (Upper(text), sz, dy, 0); yield break; }
        if (f.Caps != "small") { yield return (text, sz, dy, 0); yield break; }
        // petites majuscules : les minuscules deviennent des capitales reduites
        int i = 0;
        while (i < text.Length)
        {
            bool lower = char.IsLower(text[i]);
            int j = i;
            while (j < text.Length && char.IsLower(text[j]) == lower) j++;
            yield return (lower ? Upper(text[i..j]) : text[i..j], lower ? sz * 0.78 : sz, dy, i);
            i = j;
        }
    }

    static string Upper(string t) => string.Create(t.Length, t, (span, src) => { for (int k = 0; k < src.Length; k++) span[k] = char.ToUpperInvariant(src[k]); });

    static double TrackingPt(FontSpec f, double size) => (f.Tracking ?? 0) / 1000 * size;

    double PartWidth(string text, FontSpec f, double scale)
    {
        double size = Size(f, scale), w = 0, tr = TrackingPt(f, size);
        foreach (var (t, sz, _, _) in Segments(text, f, size)) w += _m.Width(t, f, sz) + tr * t.Length;
        return w;
    }

    double WordWidth(Word w, double scale) => w.Parts.Sum(p => PartWidth(p.Text, p.Font, scale));

    double SpaceWidth(Word w, double scale) =>
        w.Space.Length == 0 ? 0 : PartWidth(w.Space, w.SpaceFont ?? DefaultFont, scale);

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
        var markers = ListMarkers(paragraphs);

        for (int pi = 0; pi < paragraphs.Count; pi++)
        {
            var para = paragraphs[pi];
            var (lineH, single, asc, fsize) = Metrics(para, scale);
            var glyph = GlyphBand(lineH, single, asc, fsize);
            st.Y += para.SpaceBefore ?? 0;
            var firstFont = para.Runs.FirstOrDefault(r => r.Text.Length > 0)?.Font ?? para.Runs.FirstOrDefault()?.Font ?? DefaultFont;

            // lettrine : premiere lettre agrandie sur plusieurs lignes, posee a part
            int dropLines = markers[pi] is null && para.DropCap is >= 2 and <= 10 ? para.DropCap.Value : 0;
            LaidPiece? cap = null;
            double capIndent = 0;
            var plain = para.PlainText;
            if (dropLines > 0 && plain.Length > 0 && char.IsLetterOrDigit(plain[0]))
            {
                double capSize = ((dropLines - 1) * lineH + 0.72 * fsize) / 0.72;
                var capText = plain[..1];
                double capW = _m.Width(capText, firstFont, capSize);
                cap = new LaidPiece { Text = capText, Font = firstFont, SizePt = capSize, Width = capW, Start = 0, Length = 1, Dy = (dropLines - 1) * lineH };
                capIndent = capW + 0.2 * fsize;
            }
            else dropLines = 0;
            int lineNo = 0;

            var words = Tokenize(para, cap is null ? 0 : 1);
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
                var marks = new List<LaidPiece>();
                double capX = start;
                if (lineNo < dropLines) start += capIndent;
                double avail = end - start, x = 0, lastSpace = 0;
                int lineStart = first ? 0 : remainder is not null ? remainder.Start : words[idx].Start;
                var pieces = new List<LaidPiece>();
                int gaps = 0;
                bool hardBreak = false, hasTab = false;
                if (first && cap is not null)
                    marks.Add(new LaidPiece { Text = cap.Text, Font = cap.Font, SizePt = cap.SizePt, Width = cap.Width, Start = 0, Length = 1, Dy = cap.Dy, X = capX - start });
                if (first && markers[pi] is { } mk)
                {
                    // puce ou numero dans le retrait negatif ; le texte commence au retrait gauche
                    // police du paragraphe, sans les effets de caractere (barre, surlignage, exposant...)
                    var mf = firstFont.Clone();
                    mf.Strike = mf.Underline = false; mf.Highlight = null; mf.Script = null; mf.Caps = null; mf.Tracking = null;
                    var size = Size(mf, scale);
                    double mw = _m.Width(mk, mf, size);
                    marks.Add(new LaidPiece { Text = mk, Font = mf, SizePt = size, Width = mw, X = 0, Start = 0, Length = 0 });
                    double hang = -(para.FirstIndent ?? 0);
                    x = Math.Max(hang, mw + _m.Width(" ", firstFont, size));
                }

                while (idx < words.Count)
                {
                    var w = remainder ?? words[idx];
                    if (w.IsTab)
                    {
                        // tabulation : saut au taquet suivant (le texte qui suit s'aligne selon le taquet)
                        var tab = NextTab(para, start + x);
                        double target = tab.Pos - start;
                        if (tab.Align is "right" or "center" or "decimal")
                        {
                            double seg = SegmentAfter(words, idx + 1, scale, tab.Align == "decimal");
                            target -= tab.Align == "center" ? seg / 2 : seg;
                        }
                        target = Math.Max(target, x);
                        if (target > avail + 0.01 && pieces.Count > 0) target = x;          // taquet hors de la ligne : on ne saute pas
                        var tabFont = w.SpaceFont ?? pieces.LastOrDefault()?.Font ?? firstFont;
                        if (Leader(tab.Leader, tabFont, Size(tabFont, scale), x, target) is { } ld) marks.Add(ld);
                        x = target;
                        lastSpace = 0;
                        hasTab = true;
                        idx++;
                        continue;
                    }
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
                // une ligne avec tabulations ou puce garde ses positions : ni centrage ni justification
                var align = hasTab || (first && markers[pi] is not null && para.Align is "center" or "right" or "end") ? "left" : para.Align;
                AddLine(result, frames, st, pi, lineH, single, asc, pieces, start, avail, lineWidth, align,
                    justify: !lastOfPara && !hardBreak && !hasTab, gaps, lineStart, lineEnd, marks);
                st.Y += lineH;
                first = false;
                lineNo++;
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
            double tr = TrackingPt(f, size);
            foreach (var (seg, sz, dy, off) in Segments(t, f, size))
            {
                var width = _m.Width(seg, f, sz) + tr * seg.Length;
                bool last = off + seg.Length == t.Length;
                pieces.Add(new LaidPiece { Text = seg, Font = f, SizePt = sz, X = x, Width = width, Start = start + off,
                    Length = hyphen && last ? seg.Length - 1 : seg.Length, Hyphen = hyphen && last, Dy = dy, Tracking = tr });
                x += width;
            }
        }
        // l'espace suivant est implicite : on memorise sa largeur dans la piece suivante via X
    }

    // ------------------------------------------------------------------ listes, tabulations, lettrines

    /// <summary>Texte de la puce ou du numero de chaque paragraphe (null si le paragraphe n'est pas dans une liste).</summary>
    public static string?[] ListMarkers(IReadOnlyList<Paragraph> paras)
    {
        var res = new string?[paras.Count];
        var counters = new int[10];
        for (int i = 0; i < paras.Count; i++)
        {
            var l = paras[i].List;
            if (l is null)
            {
                // un paragraphe ordinaire non vide interrompt la numerotation
                if (paras[i].PlainText.Trim().Length > 0) Array.Clear(counters);
                continue;
            }
            int lv = Math.Clamp(l.Level, 0, 9);
            for (int k = lv + 1; k < 10; k++) counters[k] = 0;
            if (l.Kind != "number") { res[i] = string.IsNullOrEmpty(l.Symbol) ? "•" : l.Symbol; continue; }
            // numero de depart : au debut de la liste a ce niveau
            counters[lv] = l.Start is { } st && counters[lv] == 0 ? st : counters[lv] + 1;
            res[i] = FormatNumber(counters[lv], l.Format ?? "1.");
        }
        return res;
    }

    public static string FormatNumber(int n, string format)
    {
        if (format.Length == 0) format = "1.";
        string body = format[0] switch
        {
            'a' => Alpha(n, false),
            'A' => Alpha(n, true),
            'i' => Roman(n).ToLowerInvariant(),
            'I' => Roman(n),
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return body + format[1..];
    }

    static string Alpha(int n, bool upper)
    {
        var sb = new System.Text.StringBuilder();
        for (; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)((upper ? 'A' : 'a') + (n - 1) % 26));
        return sb.ToString();
    }

    static string Roman(int n)
    {
        var map = new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, r) in map) while (n >= v) { sb.Append(r); n -= v; }
        return sb.ToString();
    }

    /// <summary>Intervalle des tabulations par defaut (1,27 cm, comme Publisher).</summary>
    public const double DefaultTabInterval = 36;

    /// <summary>Taquet suivant une position (points depuis le bord gauche du texte).</summary>
    public static TabStop NextTab(Paragraph p, double pos)
    {
        if (p.Tabs is { Count: > 0 } tabs)
            foreach (var t in tabs.OrderBy(t => t.Pos))
                if (t.Pos > pos + 0.01) return t;
        return new TabStop { Pos = (Math.Floor(pos / DefaultTabInterval + 1e-6) + 1) * DefaultTabInterval, Align = "left" };
    }

    /// <summary>Largeur du texte qui suit une tabulation, jusqu'a la tabulation suivante ou la fin de ligne (decimal : jusqu'au separateur).</summary>
    double SegmentAfter(List<Word> words, int from, double scale, bool decimalOnly)
    {
        double w = 0;
        for (int k = from; k < words.Count && !words[k].IsTab; k++)
        {
            var word = words[k];
            if (decimalOnly)
            {
                var txt = word.Text;
                int sep = txt.IndexOfAny(new[] { ',', '.' });
                if (sep >= 0 && word.Parts.Count > 0) return w + PartWidth(txt[..sep], word.Parts[0].Font, scale);
            }
            w += WordWidth(word, scale);
            if (word.BreakAfter) break;
            if (k + 1 < words.Count && !words[k + 1].IsTab) w += SpaceWidth(word, scale);
        }
        return w;
    }

    LaidPiece? Leader(string? kind, FontSpec font, double size, double from, double to)
    {
        if (kind is null || to - from < 2) return null;
        string ch = kind switch { "dash" => "-", "line" => "_", _ => "." };
        double cw = _m.Width(ch + " ", font, size);
        if (cw <= 0) return null;
        int n = (int)Math.Floor((to - from - 1) / cw);
        if (n <= 0) return null;
        var text = string.Concat(Enumerable.Repeat(ch + " ", n)).TrimEnd();
        return new LaidPiece { Text = text, Font = font, SizePt = size, X = to - _m.Width(text, font, size) - 1, Width = _m.Width(text, font, size), Start = 0, Length = 0 };
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
        int startOffset, int endOffset, List<LaidPiece>? marks = null)
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
        if (marks is not null)
            foreach (var m in marks) { m.X = f.X + start + offset + m.X; line.Marks.Add(m); }
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
