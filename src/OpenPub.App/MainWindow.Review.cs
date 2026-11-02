using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenPub.App.Controls;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;
using TextSearch = OpenPub.Core.Editing.TextSearch;

namespace OpenPub.App;

// Onglet Revision : orthographe, rechercher / remplacer, statistiques ; Insertion ▸ Symbole et Date ;
// menu contextuel de la page (suggestions d'orthographe, presse-papiers, organisation).
public partial class MainWindow
{
    /// <summary>Histoires du document dans l'ordre de lecture (pages, puis ordre d'empilement).</summary>
    List<(Page Page, Shape Head)> Stories()
    {
        var res = new List<(Page, Shape)>();
        if (_session is null) return res;
        foreach (var p in _session.Doc.Pages)
        {
            _session.Layout(p);              // zones fantomes des cellules a jour
            foreach (var s in p.Shapes)
            {
                if (s.IsStoryHead && s.Paragraphs is { Count: > 0 }) res.Add((p, s));
                // cellules des tableaux, dans l'ordre de lecture
                if (s.IsTable)
                    foreach (var cell in s.Table!.Cells.SelectMany(r => r).Where(c => !c.Hidden && c.Paragraphs is { Count: > 0 }))
                        res.Add((p, Layout.PageLayouter.CellFrame(cell)));
            }
        }
        return res;
    }

    /// <summary>Affiche une plage de texte : ecriture dans la zone, selection, defilement jusqu'a elle.</summary>
    void ShowRange(Page page, Shape head, TextPos a, TextPos b)
    {
        if (_session is null) return;
        if (_session.TextEdit is not { } cur || cur.Head != head)
        {
            if (Layout.PageLayouter.CellOf(head) is { } cell) _session.BeginCellEdit(page, cell.Table, cell.Row, cell.Col);
            else _session.BeginTextEdit(page, head);
        }
        _session.TextEdit?.Select(a, b);
        _views.FirstOrDefault(v => v.Page == page)?.RevealCaret();
    }

    /// <summary>Position de depart d'une recherche : apres la selection de texte en cours, sinon le debut du document.</summary>
    (int Story, TextPos Pos) SearchStart(List<(Page Page, Shape Head)> stories)
    {
        if (_session?.TextEdit is { } ed)
        {
            int i = stories.FindIndex(t => t.Head == ed.Head);
            if (i >= 0) return (i, ed.HasSelection ? TextPos.Max(ed.Caret, ed.Anchor) : ed.Caret);
        }
        return (0, new TextPos(0, 0));
    }

    // ---------------------------------------------------------------- rechercher / remplacer

    FindReplaceWindow? _find;

    void Find_Click(object sender, RoutedEventArgs e) => OpenFind(replace: false);
    void Replace_Click(object sender, RoutedEventArgs e) => OpenFind(replace: true);

    void OpenFind(bool replace)
    {
        if (_session is null) return;
        var initial = _session.TextEdit is { HasSelection: true } ed ? StoryEditor.GetText(ed.Paras, ed.SelStart, ed.SelEnd) : null;
        if (initial is not null && (initial.Contains('\n') || initial.Length > 60)) initial = null;
        if (_find is null || !_find.IsLoaded)
        {
            _find = new FindReplaceWindow(this) { Owner = this };
            _find.Show();
        }
        _find.Prepare(replace, initial);
        _find.Activate();
    }

    /// <summary>Occurrence suivante (avec retour au debut du document) ; faux si le texte est introuvable.</summary>
    internal bool FindNext(string query, bool matchCase, bool wholeWord)
    {
        var stories = Stories();
        if (stories.Count == 0 || query.Length == 0) return false;
        var (si, pos) = SearchStart(stories);
        for (int k = 0; k <= stories.Count; k++)
        {
            int idx = (si + k) % stories.Count;
            var (page, head) = stories[idx];
            foreach (var m in TextSearch.Find(head.Paragraphs!, query, matchCase, wholeWord))
            {
                if (k == 0 && m.From < pos) continue;
                if (k == stories.Count && m.From >= pos) break;          // tour complet
                ShowRange(page, head, m.From, m.To);
                StatusText.Text = $"« {query} » trouvé page {page.Index}.";
                return true;
            }
        }
        StatusText.Text = $"« {query} » est introuvable.";
        return false;
    }

    /// <summary>Remplace l'occurrence selectionnee (si c'en est une), puis passe a la suivante.</summary>
    internal void ReplaceCurrent(string query, string replacement, bool matchCase, bool wholeWord)
    {
        if (_session?.TextEdit is { HasSelection: true } ed)
        {
            var a = ed.SelStart; var b = ed.SelEnd;
            var hit = TextSearch.Find(ed.Paras, query, matchCase, wholeWord).FirstOrDefault(m => m.From == a && m.To == b);
            if (hit.Length > 0)
            {
                var page = ed.Page;
                _session.Change(page, () =>
                {
                    var end = TextSearch.Replace(ed.Paras, hit, replacement);
                    ed.SetCaret(end, false);
                });
            }
        }
        FindNext(query, matchCase, wholeWord);
    }

    internal int ReplaceAll(string query, string replacement, bool matchCase, bool wholeWord)
    {
        if (_session is null || query.Length == 0) return 0;
        _session.EndTextEdit();
        int n = 0;
        _session.BeginChange();
        foreach (var (_, head) in Stories()) n += TextSearch.ReplaceAll(head.Paragraphs!, query, replacement, matchCase, wholeWord);
        _session.EndChange();
        foreach (var p in _session.Doc.Pages) _session.Invalidate(p);
        StatusText.Text = n == 0 ? $"« {query} » est introuvable." : $"{n} remplacement(s) effectué(s).";
        return n;
    }

    // ---------------------------------------------------------------- orthographe

    SpellingWindow? _spelling;

    void SpellingDialog_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (EditorSession.SharedSpell.Value is null)
        {
            MessageBox.Show(this, "Le correcteur orthographique de Windows n'est pas disponible sur ce PC (Paramètres ▸ Heure et langue ▸ Langue : ajoutez le français).",
                "Orthographe", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_spelling is null || !_spelling.IsLoaded)
        {
            _spelling = new SpellingWindow(this) { Owner = this };
            _spelling.Show();
        }
        _spelling.Activate();
        _spelling.Next();
    }

    /// <summary>Faute suivante : selectionnee dans le texte ; renvoie le mot et sa langue, ou null a la fin du document.</summary>
    internal (string Word, string? Lang)? NextSpellingError()
    {
        var spell = EditorSession.SharedSpell.Value;
        var stories = Stories();
        if (spell is null || stories.Count == 0) return null;
        var (si, pos) = SearchStart(stories);
        for (int idx = si; idx < stories.Count; idx++)
        {
            var (page, head) = stories[idx];
            var paras = head.Paragraphs!;
            for (int p = 0; p < paras.Count; p++)
            {
                var lang = paras[p].Runs.FirstOrDefault(r => r.Text.Length > 0)?.Font?.Lang;
                foreach (var (st, len) in spell.Check(paras[p].PlainText, lang))
                {
                    var from = new TextPos(p, st);
                    if (idx == si && from < pos) continue;
                    ShowRange(page, head, from, new TextPos(p, st + len));
                    return (paras[p].PlainText.Substring(st, len), lang);
                }
            }
        }
        _session?.EndTextEdit();
        return null;
    }

    /// <summary>Remplace le mot selectionne (correction d'orthographe).</summary>
    internal void ReplaceSelection(string text)
    {
        if (_session?.TextEdit is not { HasSelection: true } ed) return;
        var a = ed.SelStart; var b = ed.SelEnd;
        _session.Change(ed.Page, () => ed.SetCaret(TextSearch.Replace(ed.Paras, new TextMatch(a.Para, a.Offset, b.Offset - a.Offset), text), false));
    }

    internal void RefreshSpelling()
    {
        if (_session is null) return;
        foreach (var v in _views) v.InvalidateVisual();
    }

    void SpellToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = SpellToggle.IsChecked == true;
        AppSettings.Update(s => s.SpellCheck = on);
        if (_session is not null) { _session.SpellCheck = on; RefreshSpelling(); }
    }

    void AutoCorrectToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = AutoCorrectToggle.IsChecked == true;
        AppSettings.Update(s => s.AutoCorrect = on);
        if (_session is not null) _session.AutoCorrect = on;
    }

    // ---------------------------------------------------------------- statistiques

    void Statistics_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var stories = Stories();
        var paras = stories.SelectMany(t => t.Head.Paragraphs!).ToList();
        int words = TextSearch.CountWords(paras);
        int chars = paras.Sum(p => p.PlainText.Length);
        int noSpace = paras.Sum(p => p.PlainText.Count(c => !char.IsWhiteSpace(c)));
        int frames = _session.Doc.AllShapes().Count(s => s.IsText);
        int images = _session.Doc.AllShapes().Count(s => s.IsImage && !s.Background);
        var msg = $"Pages : {_session.Doc.Pages.Count}\nZones de texte : {frames} ({stories.Count} textes)\nImages : {images}\n\n" +
                  $"Mots : {words.ToString("N0", CultureInfo.CurrentCulture)}\nCaractères (espaces compris) : {chars.ToString("N0", CultureInfo.CurrentCulture)}\n" +
                  $"Caractères (sans les espaces) : {noSpace.ToString("N0", CultureInfo.CurrentCulture)}\nParagraphes : {paras.Count(p => p.PlainText.Trim().Length > 0)}";
        if (Sel is { IsText: true } s && StoryHead(s)?.Paragraphs is { } sel)
            msg += $"\n\nZone sélectionnée : {TextSearch.CountWords(sel)} mots, {sel.Sum(p => p.PlainText.Length)} caractères";
        MessageBox.Show(this, msg, "Statistiques du document", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- symboles et date

    static readonly (string Text, string Label)[] Symbols =
    {
        ("\u00A0", "Espace insécable"), ("\u202F", "Espace fine insécable"), ("—", "Tiret cadratin —"), ("–", "Tiret demi-cadratin –"),
        ("«\u00A0", "Guillemet ouvrant «"), ("\u00A0»", "Guillemet fermant »"), ("’", "Apostrophe ’"), ("…", "Points de suspension …"),
        ("€", "Euro €"), ("°", "Degré °"), ("×", "Multiplié ×"), ("±", "Plus ou moins ±"), ("½", "Un demi ½"), ("¼", "Un quart ¼"),
        ("œ", "Œ minuscule œ"), ("Œ", "Œ majuscule"), ("É", "É majuscule"), ("À", "À majuscule"), ("Ç", "Ç majuscule"),
        ("©", "Copyright ©"), ("®", "Marque déposée ®"), ("™", "Marque ™"), ("•", "Puce •"), ("→", "Flèche →"), ("✓", "Coche ✓"),
        ("★", "Étoile ★"), ("♥", "Cœur ♥"), ("☺", "Sourire ☺"), ("☀", "Soleil ☀"), ("✿", "Fleur ✿"), ("♪", "Note ♪"),
    };

    bool RequireTextEdit()
    {
        if (_session?.TextEdit is not null) return true;
        StatusText.Text = "Double-cliquez d'abord dans un texte pour y placer le curseur.";
        return false;
    }

    void SymbolButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu(sender);
        foreach (var (text, label) in Symbols)
        {
            var t = text;
            Item(menu, label, () => { if (RequireTextEdit()) { _session!.TextEdit!.Type(t); RefocusPage(); } });
        }
        menu.IsOpen = true;
    }

    void DateButton_Click(object sender, RoutedEventArgs e)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var now = DateTime.Now;
        var menu = NewMenu(sender);
        foreach (var fmt in new[] { "d MMMM yyyy", "dddd d MMMM yyyy", "dd/MM/yyyy", "MMMM yyyy", "HH:mm" })
        {
            var text = now.ToString(fmt, fr);
            if (fmt.StartsWith("MMMM") || fmt.StartsWith("dddd")) text = char.ToUpper(text[0], fr) + text[1..];
            Item(menu, text, () => { if (RequireTextEdit()) { _session!.TextEdit!.Type(text); RefocusPage(); } });
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- menu contextuel de la page

    void OnPageContextMenu(PageView view, Point at)
    {
        if (_session is null) return;
        var menu = new ContextMenu { PlacementTarget = view, Placement = PlacementMode.MousePoint };
        LastMenu = menu;
        if (_session.TextEdit is { } ed && ed.Page == view.Page)
        {
            // suggestions d'orthographe pour le mot sous la souris
            var pos = ed.HitTest(at);
            var para = ed.Paras[pos.Para];
            var lang = para.Runs.FirstOrDefault(r => r.Text.Length > 0)?.Font?.Lang;
            if (_session.Spell is { } spell)
                foreach (var (st, len) in spell.Check(para.PlainText, lang))
                {
                    if (pos.Offset < st || pos.Offset > st + len) continue;
                    var word = para.PlainText.Substring(st, len);
                    ed.Select(new TextPos(pos.Para, st), new TextPos(pos.Para, st + len));
                    var sugg = spell.Suggest(word, lang);
                    if (sugg.Count == 0) menu.Items.Add(new MenuItem { Header = "(aucune suggestion)", IsEnabled = false });
                    foreach (var sg in sugg.Take(6))
                    {
                        var s2 = sg;
                        var it = Item(menu, s2, () => ReplaceSelection(s2));
                        it.FontWeight = FontWeights.Bold;
                    }
                    menu.Items.Add(new Separator());
                    Item(menu, "Ignorer tout", () => { spell.Ignore(word); RefreshSpelling(); });
                    Item(menu, "Ajouter au dictionnaire", () => { spell.Add(word, lang); RefreshSpelling(); });
                    menu.Items.Add(new Separator());
                    break;
                }
            Item(menu, "Couper", () => ed.Cut());
            Item(menu, "Copier", () => ed.Copy());
            Item(menu, "Coller", () => ed.Paste());
            menu.Items.Add(new Separator());
            Item(menu, "Tout sélectionner", () => ed.SelectAll());
            Item(menu, "Paragraphe…", () => ParagraphDialog_Click(this, new RoutedEventArgs()));
        }
        else
        {
            var target = view.HitTest(at);
            if (target is not null && !_session.IsSelected(target)) _session.SelectMany(Core.Editing.ShapeOps.GroupOf(view.Page, target), target, view.Page);
            bool has = _session.Selected is not null;
            void Cmd(string header, System.Windows.Input.ICommand cmd) =>
                menu.Items.Add(new MenuItem { Header = header, Command = cmd, CommandTarget = this });
            Cmd("Couper", System.Windows.Input.ApplicationCommands.Cut);
            Cmd("Copier", System.Windows.Input.ApplicationCommands.Copy);
            Cmd("Coller", System.Windows.Input.ApplicationCommands.Paste);
            if (has)
            {
                Item(menu, "Dupliquer", () => Duplicate_Click(this, new RoutedEventArgs()));
                Cmd("Supprimer", System.Windows.Input.ApplicationCommands.Delete);
                menu.Items.Add(new Separator());
                Cmd("Grouper", GroupCommand);
                Cmd("Dissocier", UngroupCommand);
                Item(menu, "Premier plan", () => BringFront_Click(this, new RoutedEventArgs()));
                Item(menu, "Arrière-plan", () => SendBack_Click(this, new RoutedEventArgs()));
                Item(menu, _session.Selection.All(x => x.Locked) ? "Déverrouiller la position" : "Verrouiller la position", () => Lock_Click(this, new RoutedEventArgs()));
                if (_session.Selected is { IsText: true } t)
                {
                    menu.Items.Add(new Separator());
                    Item(menu, "Modifier le texte", () => { _session.BeginTextEdit(view.Page, t); view.Focus(); });
                    Item(menu, "Créer un lien vers une autre zone…", () => StartLink(view.Page, t));
                }
                if (_session.Selected is { IsImage: true }) { menu.Items.Add(new Separator()); Item(menu, "Modifier l'image…", () => EditImage(false)); }
            }
            menu.Items.Add(new Separator());
            Cmd("Tout sélectionner", System.Windows.Input.ApplicationCommands.SelectAll);
        }
        menu.IsOpen = true;
    }
}
