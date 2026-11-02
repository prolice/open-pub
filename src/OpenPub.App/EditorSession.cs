using OpenPub.App.Editing;
using OpenPub.App.Rendering;
using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.App;

/// <summary>Etat d'edition d'un document ouvert : paquet, historique, mise en page, selection.</summary>
public sealed class EditorSession : IDisposable
{
    public OpubPackage Package { get; }
    public PubDocument Doc => Package.Document;
    public UndoStack History { get; } = new();
    public FontResolver Fonts { get; }
    public WpfTextMeasurer Measurer { get; }
    public PageLayouter Layouter { get; }
    public TextLayoutEngine Engine { get; }
    public ImageCache Images { get; }
    public bool Modified { get; private set; }

    readonly Dictionary<Page, PageLayoutResult> _layouts = new();

    readonly List<Shape> _selection = new();

    /// <summary>Objet principal de la selection (dernier clique) ; ses proprietes sont affichees.</summary>
    public Shape? Selected { get; private set; }
    public Page? SelectedPage { get; private set; }
    /// <summary>Tous les objets selectionnes (une seule page) ; contient <see cref="Selected"/>.</summary>
    public IReadOnlyList<Shape> Selection => _selection;
    public bool MultiSelection => _selection.Count > 1;

    /// <summary>Edition de texte en cours sur la page (null sinon).</summary>
    public InPlaceTextEditor? TextEdit { get; private set; }
    public event Action? TextEditChanged;

    /// <summary>Outil de dessin actif ("text" ou une forme de <see cref="Core.Model.ShapeGeometry"/>, "line") ; null = selection.</summary>
    public string? DrawTool { get; set; }

    /// <summary>Couleur et epaisseur (points) du crayon pour les prochains dessins (main levee, forme libre, ligne, fleche).</summary>
    public string PenColor { get; set; } = "#1f2a44";
    public double PenWidth { get; set; } = 2;

    /// <summary>Magnetisme (alignement automatique sur la page, les marges et les autres objets).</summary>
    public bool SnapEnabled { get; set; } = true;
    /// <summary>Afficher les reperes de marge et les contours des zones de texte.</summary>
    public bool ShowGuides { get; set; } = true;
    /// <summary>Reperes affiches pendant un glissement (coordonnees de page) : verticaux (x) et horizontaux (y).</summary>
    public List<double> ActiveGuidesX { get; } = new();
    public List<double> ActiveGuidesY { get; } = new();

    public void BeginTextEdit(Page page, Shape frame, System.Windows.Point? at = null)
    {
        EndTextEdit();
        var head = frame.Chain is null ? frame : page.ChainFrames(frame.Chain).First();
        var ed = new InPlaceTextEditor(this, page, head);
        ed.Changed += () => { PageChanged?.Invoke(page); TextEditChanged?.Invoke(); };
        TextEdit = ed;
        Select(frame, page);
        if (at is { } p) ed.SetCaret(ed.HitTest(p), false);
        else ed.SetCaret(Core.Editing.StoryEditor.End(ed.Paras), false);
        TextEditChanged?.Invoke();
    }

    public void EndTextEdit()
    {
        if (TextEdit is not { } ed) return;
        ed.Stop();
        TextEdit = null;
        PageChanged?.Invoke(ed.Page);
        TextEditChanged?.Invoke();
    }

    /// <summary>Le document a ete remplace (annuler/retablir) ou sa structure a change : reconstruire les vues.</summary>
    public event Action? DocumentReplaced;
    /// <summary>Une page doit etre redessinee.</summary>
    public event Action<Page>? PageChanged;
    public event Action? SelectionChanged;
    public event Action? ModifiedChanged;

    public EditorSession(OpubPackage package)
    {
        Package = package;
        Fonts = new FontResolver(package.FontsDir);
        Measurer = new WpfTextMeasurer(Fonts)
        {
            LineFactorOverrides = package.Document.FontLineFactors,
            AscentOverrides = package.Document.FontAscents,
        };
        Engine = new TextLayoutEngine(Measurer, Hyphenator.French);
        Layouter = new PageLayouter(Engine);
        Images = new ImageCache(package);
    }

    public PageLayoutResult Layout(Page page)
    {
        if (!_layouts.TryGetValue(page, out var r))
        {
            var map = Layouter.LayoutPage(page, out var chains);
            r = new PageLayoutResult(map, chains);
            _layouts[page] = r;
        }
        return r;
    }

    public Page? PageOf(Shape s) => Doc.Pages.FirstOrDefault(p => p.Shapes.Contains(s));

    public void Select(Shape? s, Page? page = null) =>
        SelectMany(s is null ? Array.Empty<Shape>() : new[] { s }, s, page);

    /// <summary>Selectionne plusieurs objets d'une meme page ; primary = objet principal (sinon le dernier).</summary>
    public void SelectMany(IEnumerable<Shape> shapes, Shape? primary = null, Page? page = null)
    {
        var list = shapes.Distinct().ToList();
        primary ??= list.LastOrDefault();
        if (primary is not null && !list.Contains(primary)) list.Add(primary);
        if (TextEdit is { } ed && (primary is null || list.Count > 1 || !ed.Contains(primary))) EndTextEdit();
        var oldPage = SelectedPage;
        _selection.Clear();
        _selection.AddRange(list);
        Selected = primary;
        SelectedPage = primary is null ? page ?? SelectedPage : page ?? PageOf(primary);
        if (oldPage is not null && oldPage != SelectedPage) PageChanged?.Invoke(oldPage);
        if (SelectedPage is not null) PageChanged?.Invoke(SelectedPage);
        SelectionChanged?.Invoke();
    }

    /// <summary>Ajoute ou retire des objets de la selection (Ctrl / Maj + clic).</summary>
    public void ToggleSelect(IReadOnlyList<Shape> shapes, Page page)
    {
        if (SelectedPage != page) { SelectMany(shapes, shapes.LastOrDefault(), page); return; }
        var list = _selection.ToList();
        if (shapes.All(list.Contains)) list.RemoveAll(shapes.Contains);
        else list.AddRange(shapes.Where(x => !list.Contains(x)));
        var primary = list.Contains(Selected!) ? Selected : list.LastOrDefault();
        SelectMany(list, primary, page);
    }

    public bool IsSelected(Shape s) => _selection.Contains(s);

    // ---------------------------------------------------------------- modifications

    /// <summary>A appeler avant une modification (enregistre l'etat pour Annuler).</summary>
    public void BeginChange() => History.Push(Doc);

    /// <summary>A appeler apres la modification ; supprime l'entree d'historique si rien n'a change.</summary>
    public void EndChange()
    {
        History.DropLastIfUnchanged(Doc);
        MarkModified();
    }

    /// <summary>Recalcule la mise en page de la page et la redessine.</summary>
    public void Invalidate(Page page)
    {
        _layouts.Remove(page);
        PageChanged?.Invoke(page);
    }

    public void Change(Page page, Action action)
    {
        BeginChange();
        action();
        EndChange();
        Invalidate(page);
    }

    public void StructureChanged()
    {
        _layouts.Clear();
        MarkModified();
        DocumentReplaced?.Invoke();
    }

    public void MarkModified()
    {
        if (!Modified) { Modified = true; ModifiedChanged?.Invoke(); }
    }

    public void Undo() => Replace(History.Undo(Doc));
    public void Redo() => Replace(History.Redo(Doc));

    void Replace(PubDocument? doc)
    {
        if (doc is null) return;
        EndTextEdit();
        // conserve la selection par position (les objets sont recrees)
        int pageIdx = SelectedPage is null ? -1 : Doc.Pages.IndexOf(SelectedPage);
        int shapeIdx = Selected is null || SelectedPage is null ? -1 : SelectedPage.Shapes.IndexOf(Selected);
        var others = SelectedPage is null ? new List<int>() : _selection.Select(x => SelectedPage.Shapes.IndexOf(x)).Where(i => i >= 0).ToList();
        Package.Document = doc;
        Measurer.LineFactorOverrides = doc.FontLineFactors;
        Measurer.AscentOverrides = doc.FontAscents;
        _layouts.Clear();
        Selected = null;
        _selection.Clear();
        SelectedPage = pageIdx >= 0 && pageIdx < doc.Pages.Count ? doc.Pages[pageIdx] : null;
        if (SelectedPage is not null && shapeIdx >= 0 && shapeIdx < SelectedPage.Shapes.Count)
        {
            Selected = SelectedPage.Shapes[shapeIdx];
            _selection.AddRange(others.Where(i => i < SelectedPage.Shapes.Count).Select(i => SelectedPage.Shapes[i]));
            if (!_selection.Contains(Selected)) _selection.Add(Selected);
        }
        MarkModified();
        DocumentReplaced?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void Save(string? path = null)
    {
        Package.Save(path);
        Modified = false;
        ModifiedChanged?.Invoke();
    }

    public void Dispose()
    {
        EndTextEdit();
        Package.Dispose();
    }
}

public sealed record PageLayoutResult(Dictionary<Shape, FrameLayout> Frames, List<ChainLayout> Chains)
{
    public bool Overflows(Shape lastFrame) => Chains.Any(c => c.Overflow && c.Frames.Count > 0 && c.Frames[^1].Shape == lastFrame);
}
