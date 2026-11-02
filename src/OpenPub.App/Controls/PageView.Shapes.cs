using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.App.Controls;

/// <summary>
/// Lot 9 (formes) dans la vue de page : poignees jaunes de reglage des formes automatiques, mode « modifier les
/// points » d'un trace, bouton « insérer une image » des espaces reserves, double-clic sur un WordArt.
/// Branche sur PageView par quelques appels (ShapeEdit...) en tete des gestionnaires de souris et de clavier.
/// </summary>
public sealed partial class PageView
{
    /// <summary>Clic sur le bouton d'un espace reserve pour image (ou double-clic sur le cadre vide).</summary>
    public event Action<PageView, Shape>? PlaceholderClicked;
    /// <summary>Double-clic sur un texte WordArt.</summary>
    public event Action<PageView, Shape>? WordArtEditRequested;
    /// <summary>Le mode « modifier les points » commence ou se termine.</summary>
    public event Action<PageView>? PointEditChanged;

    /// <summary>Trace dont on modifie les points (null = mode inactif).</summary>
    public Shape? PointEditShape { get; private set; }
    /// <summary>Sommet selectionne (indice de segment du trace), -1 sinon.</summary>
    public int SelectedNode { get; private set; } = -1;

    enum EditDrag { None, Adjust, Node, Control }
    EditDrag _edrag;
    int _eIndex, _eSeg, _eWhich;
    bool _emoved;

    static readonly Brush AdjustYellow = Freeze(new SolidColorBrush(Color.FromRgb(0xff, 0xd2, 0x3f)));
    static readonly Brush AdjustBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x8a, 0x62, 0x00)));
    static readonly Brush NodeInk = Freeze(new SolidColorBrush(Color.FromRgb(0x1f, 0x2a, 0x44)));

    double EditTol => 6 / K;

    /// <summary>
    /// Commence la modification des points (la forme automatique est d'abord convertie en trace, ce qui s'annule).
    /// Renvoie faux pour une zone de texte, une image ou un WordArt.
    /// </summary>
    public bool BeginPointEdit(Shape s)
    {
        if (s.IsText || s.IsImage || s.IsWordArt || s.Locked || !Page.Shapes.Contains(s)) return false;
        if (s.Geometry is not null || s.Path is not { Count: >= 2 } || s.Kind != ShapeKind.Path)
            _session.Change(Page, () => PathEditor.ToEditable(s));
        else PathEditor.Normalize(s.Path);
        _session.Select(s, Page);
        PointEditShape = s;
        SelectedNode = -1;
        InvalidateVisual();
        PointEditChanged?.Invoke(this);
        return true;
    }

    public void EndPointEdit()
    {
        if (PointEditShape is null) return;
        PointEditShape = null;
        SelectedNode = -1;
        _edrag = EditDrag.None;
        InvalidateVisual();
        PointEditChanged?.Invoke(this);
    }

    /// <summary>Le mode s'arrete si l'objet n'est plus selectionne (annuler, autre selection).</summary>
    bool PointEditAlive()
    {
        if (PointEditShape is { } s && (!Page.Shapes.Contains(s) || _session.Selected != s || s.Path is null)) EndPointEdit();
        return PointEditShape is not null;
    }

    /// <summary>Forme automatique selectionnee seule, avec des poignees de reglage (repere local).</summary>
    (Shape S, ShapeDef Def)? AdjustTarget()
    {
        if (_session.Selected is not { Geometry: { } g } s || _session.MultiSelection || s.Locked || Editing || PointEditShape is not null
            || !Page.Shapes.Contains(s) || _session.DrawTool is not null) return null;
        return ShapeLibrary.Find(g) is { Handles.Length: > 0 } d ? (s, d) : null;
    }

    int AdjustHit(Point p)
    {
        if (AdjustTarget() is not var (s, d)) return -1;
        var lp = Geo.ToLocal(s, p);
        for (int i = 0; i < d.Handles.Length; i++)
        {
            var (hx, hy) = d.Handles[i].Position(s);
            if ((lp - new Point(hx, hy)).Length <= EditTol) return i;
        }
        return -1;
    }

    /// <summary>Clic gauche (apres les outils de dessin) : renvoie vrai si le clic est pris en charge ici.</summary>
    bool ShapeEditMouseDown(Point p, MouseButtonEventArgs e)
    {
        if (PointEditAlive()) return PointEditDown(p, e);

        // poignee jaune de reglage
        if (AdjustHit(p) is var hi and >= 0)
        {
            _edrag = EditDrag.Adjust;
            _eIndex = hi;
            _emoved = false;
            _session.BeginChange();
            CaptureMouse();
            return true;
        }

        var target = HitTest(p);
        if (target is null) return false;
        bool alone = target.Group is null || _session.Selected == target;
        // espace reserve : bouton central, ou double-clic sur le cadre
        if (target.IsPlaceholder && (DecorRenderer.PlaceholderButton(target).Contains(Geo.ToLocal(target, p)) || (e.ClickCount == 2 && alone)))
        {
            _session.Select(target, Page);
            PlaceholderClicked?.Invoke(this, target);
            return true;
        }
        if (target.IsWordArt && e.ClickCount == 2 && alone)
        {
            _session.Select(target, Page);
            WordArtEditRequested?.Invoke(this, target);
            return true;
        }
        return false;
    }

    bool PointEditDown(Point p, MouseButtonEventArgs e)
    {
        var s = PointEditShape!;
        var path = s.Path!;
        var lp = Geo.ToLocal(s, p);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        // poignees de courbe du sommet selectionne
        if (SelectedNode >= 0 && SelectedNode < path.Count)
            foreach (var (seg, which) in PathEditor.Controls(path, SelectedNode))
            {
                var (cx, cy) = PathEditor.ControlPoint(path, seg, which);
                if ((lp - new Point(cx, cy)).Length <= EditTol) { StartEditDrag(EditDrag.Control, SelectedNode, seg, which); return true; }
            }
        foreach (var i in PathEditor.Nodes(path))
        {
            var (nx, ny) = PathEditor.NodePoint(path, i);
            if ((lp - new Point(nx, ny)).Length > EditTol) continue;
            if (ctrl)
            {
                // Ctrl + clic sur un sommet : le supprimer
                DeleteSelectedNode(i);
                return true;
            }
            SelectedNode = i;
            if (e.ClickCount == 2) { ToggleSmooth(); return true; }
            StartEditDrag(EditDrag.Node, i, -1, 0);
            return true;
        }
        var near = PathEditor.Nearest(path, lp.X, lp.Y);
        if (near.Seg >= 0 && near.Dist <= EditTol)
        {
            if (ctrl || e.ClickCount == 2)
            {
                // Ctrl + clic (ou double-clic) sur le contour : ajouter un sommet
                int idx = -1;
                _session.Change(Page, () => idx = PathEditor.InsertNode(path, near.Seg, near.T));
                SelectedNode = idx;
                InvalidateVisual();
            }
            return true;
        }
        if (lp.X >= s.X - EditTol && lp.X <= s.X + s.W + EditTol && lp.Y >= s.Y - EditTol && lp.Y <= s.Y + s.H + EditTol)
        {
            SelectedNode = -1;
            InvalidateVisual();
            return true;
        }
        // clic hors de l'objet : fin du mode, le clic est traite normalement
        EndPointEdit();
        return false;
    }

    void StartEditDrag(EditDrag mode, int node, int seg, int which)
    {
        _edrag = mode; _eIndex = node; _eSeg = seg; _eWhich = which; _emoved = false;
        _session.BeginChange();
        CaptureMouse();
        InvalidateVisual();
    }

    /// <summary>Supprime le sommet (Ctrl + clic, Suppr) ; un trace garde au moins 2 sommets (3 s'il est ferme).</summary>
    public bool DeleteSelectedNode(int? node = null)
    {
        if (PointEditShape is not { Path: { } path } s) return false;
        int i = node ?? SelectedNode;
        if (i < 0 || i >= path.Count) return false;
        bool ok = false;
        _session.Change(Page, () => { ok = PathEditor.DeleteNode(path, i); if (ok) PathEditor.FitFrame(s); });
        SelectedNode = -1;
        InvalidateVisual();
        return ok;
    }

    /// <summary>Point lisse ou angle vif pour le sommet selectionne.</summary>
    public void ToggleSmooth()
    {
        if (PointEditShape is not { Path: { } path } s || SelectedNode < 0) return;
        bool smooth = !PathEditor.IsSmooth(path, SelectedNode);
        _session.Change(Page, () => { PathEditor.SetSmooth(path, SelectedNode, smooth); PathEditor.FitFrame(s); });
        InvalidateVisual();
    }

    bool ShapeEditMouseMove(Point p, MouseEventArgs e)
    {
        if (_edrag == EditDrag.None)
        {
            if (_mode != DragMode.None || e.LeftButton == MouseButtonState.Pressed) return false;
            if (PointEditAlive())
            {
                var s = PointEditShape!;
                var lp = Geo.ToLocal(s, p);
                bool overNode = PathEditor.Nodes(s.Path!).Any(i => (lp - Pt(PathEditor.NodePoint(s.Path!, i))).Length <= EditTol);
                bool overLine = !overNode && PathEditor.Nearest(s.Path!, lp.X, lp.Y).Dist <= EditTol;
                Cursor = overNode ? Cursors.SizeAll : overLine ? Cursors.Cross : Cursors.Arrow;
                return true;
            }
            if (AdjustHit(p) >= 0) { Cursor = Cursors.Hand; return true; }
            if (HitTest(p) is { IsPlaceholder: true } ph && DecorRenderer.PlaceholderButton(ph).Contains(Geo.ToLocal(ph, p))) { Cursor = Cursors.Hand; return true; }
            return false;
        }
        var t = _edrag == EditDrag.Adjust ? _session.Selected : PointEditShape;
        if (t is null) return true;
        var q = Geo.ToLocal(t, p);
        _emoved = true;
        switch (_edrag)
        {
            case EditDrag.Adjust:
                if (ShapeLibrary.Find(t.Geometry) is { } d && _eIndex < d.Handles.Length) d.Handles[_eIndex].Apply(t, q.X, q.Y);
                break;
            case EditDrag.Node:
                PathEditor.MoveNode(t.Path!, _eIndex, q.X, q.Y);
                break;
            case EditDrag.Control:
                PathEditor.MoveControl(t.Path!, _eIndex, _eSeg, _eWhich, q.X, q.Y);
                break;
        }
        _session.Invalidate(Page);
        return true;
    }

    bool ShapeEditMouseUp()
    {
        if (_edrag == EditDrag.None) return false;
        var mode = _edrag;
        _edrag = EditDrag.None;
        ReleaseMouseCapture();
        if (mode is EditDrag.Node or EditDrag.Control && PointEditShape is { } s && _emoved) PathEditor.FitFrame(s);
        _session.EndChange();
        _session.Invalidate(Page);
        return true;
    }

    bool ShapeEditKeyDown(KeyEventArgs e)
    {
        if (!PointEditAlive()) return false;
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                EndPointEdit();
                e.Handled = true;
                return true;
            case Key.Delete:
            case Key.Back:
                DeleteSelectedNode();
                e.Handled = true;
                return true;
        }
        return false;
    }

    /// <summary>
    /// Suppr pendant la modification des points : supprimer le sommet (et non l'objet). Traite avant la commande
    /// Supprimer de la fenetre, qui reagit a la touche pendant l'evenement KeyDown.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && PointEditAlive()) { DeleteSelectedNode(); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    static Point Pt((double X, double Y) p) => new(p.X, p.Y);

    /// <summary>Poignees jaunes et sommets du mode « modifier les points » (dessines par-dessus la selection).</summary>
    void DrawShapeEditOverlay(DrawingContext dc)
    {
        if (!Interactive) return;
        if (AdjustTarget() is var (s, d))
        {
            bool rot = PageRenderer.PushRotation(dc, s);
            var pen = new Pen(AdjustBorder, 0.8 / K);
            double r = 4.2 / K;
            foreach (var h in d.Handles)
            {
                var (hx, hy) = h.Position(s);
                // losange jaune, comme les poignees de reglage d'Office
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(hx, hy - r), true, true);
                    c.PolyLineTo(new[] { new Point(hx + r, hy), new Point(hx, hy + r), new Point(hx - r, hy) }, true, true);
                }
                dc.DrawGeometry(AdjustYellow, pen, g);
            }
            if (rot) dc.Pop();
        }
        if (!PointEditAlive()) return;
        var ps = PointEditShape!;
        var path = ps.Path!;
        bool rot2 = PageRenderer.PushRotation(dc, ps);
        dc.DrawGeometry(null, new Pen(SelBlue, 1 / K) { DashStyle = DashStyles.Dash }, PageRenderer.PathGeometry(path));
        double hs = 3.5 / K;
        if (SelectedNode >= 0 && SelectedNode < path.Count)
        {
            var n = Pt(PathEditor.NodePoint(path, SelectedNode));
            var linePen = new Pen(SelBlue, 0.8 / K);
            foreach (var (seg, which) in PathEditor.Controls(path, SelectedNode))
            {
                var c = Pt(PathEditor.ControlPoint(path, seg, which));
                dc.DrawLine(linePen, n, c);
                dc.DrawEllipse(Brushes.White, linePen, c, hs, hs);
            }
        }
        var nodePen = new Pen(NodeInk, 0.9 / K);
        foreach (var i in PathEditor.Nodes(path))
        {
            var n = Pt(PathEditor.NodePoint(path, i));
            var rect = new Rect(n.X - hs, n.Y - hs, 2 * hs, 2 * hs);
            dc.DrawRectangle(i == SelectedNode ? NodeInk : Brushes.White, nodePen, rect);
        }
        if (rot2) dc.Pop();
    }
}
