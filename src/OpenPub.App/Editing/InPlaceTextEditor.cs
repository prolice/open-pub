using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenPub.App.Controls;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.App.Editing;

/// <summary>
/// Edition du texte directement sur la page (comme dans Publisher) : curseur, selection, saisie,
/// suppression, coupure de paragraphe, presse-papiers, mise en forme. Le texte edite est celui
/// de l'histoire entiere (toutes les zones liees) ; la mise en page est recalculee a chaque frappe.
/// </summary>
public sealed class InPlaceTextEditor
{
    readonly EditorSession _session;
    public Page Page { get; }
    public Shape Head { get; }
    public TextPos Caret { get; private set; }
    public TextPos Anchor { get; private set; }
    public bool HasSelection => Caret != Anchor;
    public TextPos SelStart => TextPos.Min(Caret, Anchor);
    public TextPos SelEnd => TextPos.Max(Caret, Anchor);
    public bool CaretVisible { get; private set; } = true;

    readonly DispatcherTimer _blink;
    double? _desiredX;              // colonne visee pour Haut / Bas
    bool _typing;                   // frappe continue : une seule entree d'historique

    public event Action? Changed;

    public List<Paragraph> Paras => Head.Paragraphs ??= new List<Paragraph>();
    public List<Shape> Frames => Head.Chain is null ? new List<Shape> { Head } : Page.ChainFrames(Head.Chain);

    public InPlaceTextEditor(EditorSession session, Page page, Shape head)
    {
        _session = session;
        Page = page;
        Head = head;
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blink.Tick += (_, _) => { CaretVisible = !CaretVisible; Changed?.Invoke(); };
        _blink.Start();
    }

    public void Stop() => _blink.Stop();

    public bool Contains(Shape s) => s == Head || (Head.Chain is not null && s.Chain == Head.Chain);

    void Touch(bool keepDesiredX = false)
    {
        CaretVisible = true;
        _blink.Stop(); _blink.Start();
        if (!keepDesiredX) _desiredX = null;
        Changed?.Invoke();
    }

    public void SetCaret(TextPos pos, bool extend)
    {
        Caret = StoryEditor.Clamp(Paras, pos);
        if (!extend) Anchor = Caret;
        _typing = false;
        Touch();
    }

    public void Select(TextPos from, TextPos to)
    {
        Anchor = StoryEditor.Clamp(Paras, from);
        Caret = StoryEditor.Clamp(Paras, to);
        _typing = false;
        Touch();
    }

    public void SelectAll() => Select(new TextPos(0, 0), StoryEditor.End(Paras));

    // ---------------------------------------------------------------- correspondance ecran <-> texte

    IEnumerable<(Shape Frame, LaidLine Line)> Lines()
    {
        var layout = _session.Layout(Page);
        foreach (var f in Frames)
            if (layout.Frames.TryGetValue(f, out var fl))
                foreach (var l in fl.Lines) yield return (f, l);
    }

    /// <summary>Position du texte la plus proche d'un point de la page.</summary>
    public TextPos HitTest(Point p)
    {
        (Shape Frame, LaidLine Line)? best = null;
        double bestD = double.MaxValue;
        Point bestLocal = p;
        foreach (var (f, l) in Lines())
        {
            var lp = Geo.ToLocal(f, p);       // zone eventuellement tournee
            // distance : d'abord la zone, puis la ligne
            double dx = lp.X < f.X ? f.X - lp.X : lp.X > f.X + f.W ? lp.X - f.X - f.W : 0;
            double dy = lp.Y < l.Top ? l.Top - lp.Y : lp.Y > l.Top + l.Height ? lp.Y - l.Top - l.Height : 0;
            double d = dx * 1000 + dy;
            if (d < bestD) { bestD = d; best = (f, l); bestLocal = lp; }
        }
        if (best is not { } b) return new TextPos(0, 0);
        return new TextPos(b.Line.ParagraphIndex, OffsetAtX(b.Line, bestLocal.X));
    }

    int OffsetAtX(LaidLine line, double x)
    {
        if (line.Pieces.Count == 0) return line.StartOffset;
        var first = line.Pieces[0];
        if (x <= first.X) return first.Start;
        for (int i = 0; i < line.Pieces.Count; i++)
        {
            var pc = line.Pieces[i];
            if (x <= pc.X + pc.Width)
            {
                // caractere le plus proche dans le morceau
                int best = pc.Start;
                double bestD = Math.Abs(x - pc.X);
                for (int k = 1; k <= pc.Length; k++)
                {
                    double px = pc.X + pc.Advance(_session.Measurer, k);
                    double d = Math.Abs(x - px);
                    if (d < bestD) { bestD = d; best = pc.Start + k; }
                }
                return best;
            }
            if (i + 1 < line.Pieces.Count && x < line.Pieces[i + 1].X)
            {
                var next = line.Pieces[i + 1];
                double mid = (pc.X + pc.Width + next.X) / 2;
                return x < mid ? pc.Start + pc.Length : next.Start;
            }
        }
        var last = line.Pieces[^1];
        int end = last.Start + last.Length;
        // fin de la derniere ligne du paragraphe : tout le texte, espaces compris
        return line.EndOffset >= StoryEditor.Length(Paras[line.ParagraphIndex]) ? line.EndOffset : end;
    }

    /// <summary>Ligne affichant une position (null si hors des zones visibles, ex. texte en debordement).</summary>
    (Shape Frame, LaidLine Line)? LineOf(TextPos pos)
    {
        (Shape, LaidLine)? candidate = null;
        foreach (var (f, l) in Lines())
        {
            if (l.ParagraphIndex != pos.Para) continue;
            if (pos.Offset >= l.StartOffset && pos.Offset < l.EndOffset) return (f, l);
            if (pos.Offset == l.EndOffset) candidate = (f, l);     // fin de paragraphe
            if (pos.Offset >= l.StartOffset && candidate is null) candidate = (f, l);
        }
        return candidate;
    }

    double XAt(LaidLine line, int offset)
    {
        if (line.Pieces.Count == 0)
            return line.Left;
        LaidPiece? prev = null;
        foreach (var pc in line.Pieces)
        {
            if (offset < pc.Start)
            {
                if (prev is null) return pc.X;
                double a = prev.X + prev.Width, b = pc.X;
                int ao = prev.Start + prev.Length, bo = pc.Start;
                return bo > ao ? a + (b - a) * (offset - ao) / (bo - ao) : a;
            }
            if (offset <= pc.Start + pc.Length)
                return pc.X + pc.Advance(_session.Measurer, offset - pc.Start);
            prev = pc;
        }
        var last = line.Pieces[^1];
        double space = _session.Measurer.Width(" ", last.Font, last.SizePt);
        return Math.Min(line.Right, last.X + last.Width + space * (offset - last.Start - last.Length));
    }

    /// <summary>Curseur : rectangle dans le repere de sa zone (a dessiner avec la rotation de la zone).</summary>
    public (Rect Rect, Shape Frame)? CaretInfo()
    {
        if (LineOf(Caret) is not { } fl) return null;
        var x = XAt(fl.Line, Caret.Offset);
        double h = fl.Line.Pieces.Count > 0 ? fl.Line.Pieces.Max(p => p.SizePt) * 1.2 : fl.Line.Height;
        return (new Rect(x, fl.Line.Baseline - h * 0.8, 0, h), fl.Frame);
    }

    /// <summary>Rectangle du curseur (coordonnees de page, approximatif pour une zone tournee).</summary>
    public Rect? CaretRect()
    {
        if (CaretInfo() is not { } ci) return null;
        var p = Geo.ToPage(ci.Frame, ci.Rect.TopLeft);
        return new Rect(p.X, p.Y, 0, ci.Rect.Height);
    }

    /// <summary>Rectangles de surbrillance de la selection.</summary>
    public IEnumerable<Rect> SelectionRects() => SelectionRectsWithFrame().Select(x => x.Rect);

    /// <summary>Surbrillance de la selection, dans le repere de chaque zone.</summary>
    public IEnumerable<(Rect Rect, Shape Frame)> SelectionRectsWithFrame()
    {
        if (!HasSelection) yield break;
        var a = SelStart; var b = SelEnd;
        foreach (var (frame, l) in Lines())
        {
            var ls = new TextPos(l.ParagraphIndex, l.StartOffset);
            var le = new TextPos(l.ParagraphIndex, l.EndOffset);
            if (le < a || ls > b || (le == a && l.EndOffset > l.StartOffset)) continue;
            double x1 = a > ls ? XAt(l, a.Offset) : (l.Pieces.Count > 0 ? Math.Min(l.Pieces[0].X, l.Left) : l.Left);
            double x2 = b < le ? XAt(l, b.Offset) : (l.Pieces.Count > 0 ? Math.Max(l.Pieces[^1].X + l.Pieces[^1].Width, x1 + 3) : l.Left + 4);
            if (x2 > x1) yield return (new Rect(x1, l.Top, x2 - x1, l.Height), frame);
        }
    }

    // ---------------------------------------------------------------- modifications

    void Modify(Func<TextPos> op, bool typing = false)
    {
        if (!(typing && _typing)) _session.BeginChange();
        var pos = op();
        _typing = typing;
        Caret = Anchor = StoryEditor.Clamp(Paras, pos);
        if (!typing) _session.EndChange(); else _session.MarkModified();
        _session.Invalidate(Page);
        Touch();
    }

    public void Type(string text)
    {
        if (text.Length == 0) return;
        Modify(() =>
        {
            var font = StoryEditor.FontAt(Paras, SelStart);
            var at = HasSelection ? StoryEditor.Delete(Paras, Caret, Anchor) : Caret;
            return StoryEditor.Insert(Paras, at, text, font);
        }, typing: !HasSelection && !text.Contains('\n'));
    }

    /// <summary>Saisie au clavier : avec la correction typographique francaise si elle est active.</summary>
    public void TypeInput(string text)
    {
        if (_session.AutoCorrect && text.Length == 1 && !HasSelection)
        {
            var font = StoryEditor.FontAt(Paras, Caret);
            var plain = Paras[Caret.Para].PlainText;
            var before = plain[..Math.Min(Caret.Offset, plain.Length)];
            if ((font.Lang is null || font.Lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
                && FrenchTypography.Correct(before, text[0]) is { } fix && fix.DeleteBefore <= Caret.Offset)
            {
                Modify(() =>
                {
                    var from = new TextPos(Caret.Para, Caret.Offset - fix.DeleteBefore);
                    var at = fix.DeleteBefore > 0 ? StoryEditor.Delete(Paras, from, Caret) : Caret;
                    return StoryEditor.Insert(Paras, at, fix.Insert, font);
                }, typing: true);
                return;
            }
        }
        Type(text);
    }

    public void Backspace(bool word)
    {
        if (HasSelection) { Modify(() => StoryEditor.Delete(Paras, Caret, Anchor)); return; }
        var from = word ? StoryEditor.StepWord(Paras, Caret, -1) : StoryEditor.Step(Paras, Caret, -1);
        if (from == Caret) return;
        Modify(() => StoryEditor.Delete(Paras, from, Caret));
    }

    public void DeleteForward(bool word)
    {
        if (HasSelection) { Modify(() => StoryEditor.Delete(Paras, Caret, Anchor)); return; }
        var to = word ? StoryEditor.StepWord(Paras, Caret, 1) : StoryEditor.Step(Paras, Caret, 1);
        if (to == Caret) return;
        Modify(() => StoryEditor.Delete(Paras, Caret, to));
    }

    public void NewParagraph() => Modify(() =>
    {
        var at = HasSelection ? StoryEditor.Delete(Paras, Caret, Anchor) : Caret;
        return StoryEditor.SplitParagraph(Paras, at);
    });

    public void Copy()
    {
        if (HasSelection) Clipboard.SetText(StoryEditor.GetText(Paras, Caret, Anchor));
    }

    public void Cut()
    {
        if (!HasSelection) return;
        Copy();
        Modify(() => StoryEditor.Delete(Paras, Caret, Anchor));
    }

    public void Paste()
    {
        if (!Clipboard.ContainsText()) return;
        var text = Clipboard.GetText().Replace("\r\n", "\n");
        Modify(() =>
        {
            var font = StoryEditor.FontAt(Paras, SelStart);
            var at = HasSelection ? StoryEditor.Delete(Paras, Caret, Anchor) : Caret;
            return StoryEditor.Insert(Paras, at, text, font);
        });
    }

    /// <summary>Plage concernee par une mise en forme : la selection, sinon le mot sous le curseur.</summary>
    public (TextPos, TextPos) FormatRange() => HasSelection ? (SelStart, SelEnd) : StoryEditor.WordAt(Paras, Caret);

    public void ApplyFont(Action<FontSpec> change)
    {
        var (a, b) = FormatRange();
        _session.BeginChange();
        StoryEditor.ApplyFont(Paras, a, b, change);
        _session.EndChange();
        _typing = false;
        _session.Invalidate(Page);
        Touch();
    }

    public void ApplyParagraph(Action<Paragraph> change)
    {
        _session.BeginChange();
        StoryEditor.ApplyParagraph(Paras, SelStart, SelEnd, change);
        _session.EndChange();
        _typing = false;
        _session.Invalidate(Page);
        Touch();
    }

    public void ToggleFont(Func<FontSpec, bool> get, Action<FontSpec, bool> set)
    {
        var (a, _) = FormatRange();
        bool on = get(StoryEditor.FontAt(Paras, StoryEditor.Step(Paras, a, 1)));
        ApplyFont(f => set(f, !on));
    }

    /// <summary>Change le niveau des elements de liste selectionnes (retrait de 18 pt par niveau).</summary>
    public void ShiftListLevel(int delta) => ApplyParagraph(p =>
    {
        if (p.List is null) return;
        int lv = Math.Clamp(p.List.Level + delta, 0, 8);
        p.List.Level = lv;
        p.IndentLeft = 18 * (lv + 1);
        p.FirstIndent = -18;
    });

    /// <summary>Mise en forme au curseur (pour la barre de mise en forme).</summary>
    public FontSpec CurrentFont() => StoryEditor.FontAt(Paras, HasSelection ? StoryEditor.Step(Paras, SelStart, 1) : Caret);
    public Paragraph CurrentParagraph() => Paras[StoryEditor.Clamp(Paras, Caret).Para];

    // ---------------------------------------------------------------- clavier

    /// <summary>Traite une touche ; renvoie vrai si elle a ete consommee.</summary>
    public bool HandleKey(Key key, ModifierKeys mods)
    {
        bool shift = mods.HasFlag(ModifierKeys.Shift), ctrl = mods.HasFlag(ModifierKeys.Control);
        switch (key)
        {
            case Key.Left: Move(ctrl ? StoryEditor.StepWord(Paras, Caret, -1) : (HasSelection && !shift ? SelStart : StoryEditor.Step(Paras, Caret, -1)), shift); return true;
            case Key.Right: Move(ctrl ? StoryEditor.StepWord(Paras, Caret, 1) : (HasSelection && !shift ? SelEnd : StoryEditor.Step(Paras, Caret, 1)), shift); return true;
            case Key.Up: MoveVertical(-1, shift); return true;
            case Key.Down: MoveVertical(1, shift); return true;
            case Key.Home:
                if (ctrl) Move(new TextPos(0, 0), shift);
                else if (LineOf(Caret) is { } h) Move(new TextPos(Caret.Para, h.Line.StartOffset), shift);
                return true;
            case Key.End:
                if (ctrl) Move(StoryEditor.End(Paras), shift);
                else if (LineOf(Caret) is { } e)
                {
                    var paraLen = StoryEditor.Length(Paras[Caret.Para]);
                    int end = e.Line.EndOffset >= paraLen ? paraLen : TrimEnd(Caret.Para, e.Line.EndOffset);
                    Move(new TextPos(Caret.Para, end), shift);
                }
                return true;
            case Key.Back: Backspace(ctrl); return true;
            case Key.Delete: DeleteForward(ctrl); return true;
            case Key.Enter:
                if (shift) { Type("\v"); return true; }          // saut de ligne dans le paragraphe
                // Entree sur un element de liste vide : fin de la liste (comme Word et Publisher)
                if (!HasSelection && CurrentParagraph() is { List: not null } lp && lp.PlainText.Length == 0)
                {
                    ApplyParagraph(p => { p.List = null; p.IndentLeft = null; p.FirstIndent = null; });
                    return true;
                }
                NewParagraph();
                return true;
            case Key.Tab:
                // dans une liste, en debut d'element (ou sur une selection) : change le niveau
                if (CurrentParagraph() is { List: not null } && (HasSelection || Caret.Offset == 0))
                {
                    ShiftListLevel(shift ? -1 : 1);
                    return true;
                }
                if (shift) return true;
                Type("\t");
                return true;
        }
        if (ctrl)
        {
            switch (key)
            {
                case Key.A: SelectAll(); return true;
                case Key.C: Copy(); return true;
                case Key.X: Cut(); return true;
                case Key.V: Paste(); return true;
                case Key.B: ToggleFont(f => f.Bold, (f, v) => f.Bold = v); return true;
                case Key.I: ToggleFont(f => f.Italic, (f, v) => f.Italic = v); return true;
                case Key.U: ToggleFont(f => f.Underline, (f, v) => f.Underline = v); return true;
            }
        }
        return false;
    }

    int TrimEnd(int para, int offset)
    {
        var t = Paras[para].PlainText;
        while (offset > 0 && offset <= t.Length && char.IsWhiteSpace(t[offset - 1])) offset--;
        return offset;
    }

    void Move(TextPos to, bool extend)
    {
        Caret = StoryEditor.Clamp(Paras, to);
        if (!extend) Anchor = Caret;
        _typing = false;
        Touch();
    }

    void MoveVertical(int dir, bool extend)
    {
        var lines = Lines().ToList();
        var cur = LineOf(Caret);
        if (cur is not { } c) return;
        int i = lines.FindIndex(l => l.Line == c.Line);
        int j = i + dir;
        _desiredX ??= XAt(c.Line, Caret.Offset);
        if (j < 0) { Move(new TextPos(0, 0), extend); return; }
        if (j >= lines.Count) { Move(StoryEditor.End(Paras), extend); return; }
        var target = lines[j].Line;
        var x = _desiredX.Value;
        Caret = new TextPos(target.ParagraphIndex, OffsetAtX(target, x));
        if (!extend) Anchor = Caret;
        _typing = false;
        Touch(keepDesiredX: true);
    }
}
