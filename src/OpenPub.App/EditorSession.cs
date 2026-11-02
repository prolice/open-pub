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

    public Shape? Selected { get; private set; }
    public Page? SelectedPage { get; private set; }

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

    public void Select(Shape? s, Page? page = null)
    {
        if (TextEdit is { } ed && (s is null || !ed.Contains(s))) EndTextEdit();
        var oldPage = SelectedPage;
        Selected = s;
        SelectedPage = s is null ? page ?? SelectedPage : page ?? PageOf(s);
        if (oldPage is not null && oldPage != SelectedPage) PageChanged?.Invoke(oldPage);
        if (SelectedPage is not null) PageChanged?.Invoke(SelectedPage);
        SelectionChanged?.Invoke();
    }

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
        Package.Document = doc;
        Measurer.LineFactorOverrides = doc.FontLineFactors;
        Measurer.AscentOverrides = doc.FontAscents;
        _layouts.Clear();
        Selected = null;
        SelectedPage = pageIdx >= 0 && pageIdx < doc.Pages.Count ? doc.Pages[pageIdx] : null;
        if (SelectedPage is not null && shapeIdx >= 0 && shapeIdx < SelectedPage.Shapes.Count)
            Selected = SelectedPage.Shapes[shapeIdx];
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
