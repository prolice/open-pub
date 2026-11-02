using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;
using PathSegment = OpenPub.Core.Model.PathSegment;

namespace OpenPub.App.Controls;

/// <summary>
/// Affiche une page et gere la souris et le clavier : selection, deplacement, redimensionnement
/// (8 poignees) avec magnetisme, edition du texte directement sur la page (double-clic).
/// </summary>
public sealed class PageView : FrameworkElement
{
    public const double DipPerPoint = 96.0 / 72.0;
    readonly EditorSession _session;
    public Page Page { get; }
    public bool Interactive { get; init; } = true;

    double _zoom = 1;
    public double Zoom
    {
        get => _zoom;
        set { _zoom = value; InvalidateMeasure(); InvalidateVisual(); }
    }
    double K => DipPerPoint * _zoom;

    public event Action<Shape>? ReplaceImageRequested;
    /// <summary>Mode « créer un lien » : clic sur la zone cible (null : clic hors d'une zone, le mode est annule).</summary>
    public event Action<PageView, Shape?>? LinkTargetPicked;
    /// <summary>Ctrl + clic : ouvrir le lien sous le point (vrai si un lien a ete ouvert).</summary>
    public Func<PageView, Point, bool>? FollowLink { get; set; }
    /// <summary>Un repere de regle a ete deplace ou supprime (toutes les pages doivent etre redessinees).</summary>
    public event Action? GuidesChanged;
    /// <summary>Clic sur l'indicateur de debordement d'une zone (proposer de lier une nouvelle zone).</summary>
    public event Action<PageView, Shape>? OverflowClicked;
    /// <summary>Fin d'une selection de texte a la souris (reproduction de la mise en forme).</summary>
    public event Action? TextSelectionFinished;
    /// <summary>Le curseur de texte a bouge (pour le faire defiler dans la vue) : rectangle en pixels de la vue.</summary>
    public event Action<PageView, Rect>? CaretMoved;

    enum DragMode { None, Move, Resize, TextSelect, Rotate, Draw, Freehand, Marquee, Guide, TableCol, TableRow }
    int _tableEdge;               // bordure de colonne ou de ligne tiree (indice de la colonne / ligne a gauche / au-dessus)
    double _tableEdgeStart;
    bool _guideVertical;          // repere deplace : vertical (X) ou horizontal (Y)
    int _guideIndex;
    DragMode _mode;
    int _handle;                 // 0..7 : NO, N, NE, E, SE, S, SO, O
    Point _start;
    Rect _orig;
    double _origRotation;
    bool _moved;
    Rect? _drawRect;              // apercu du trace en mode dessin
    Rect? _marquee;               // rectangle de selection en cours
    bool _marqueeAdd;             // Ctrl/Maj : le rectangle ajoute a la selection
    /// <summary>Etat de depart des objets deplaces ou redimensionnes ensemble (selection multiple, groupe).</summary>
    readonly Dictionary<Shape, Rect> _origs = new();

    /// <summary>Points de depart et d'arrivee du dernier trace (pour le sens d'une ligne).</summary>
    public Point DrawStart { get; private set; }
    public Point DrawEnd { get; private set; }

    List<Point>? _stroke;          // trace a main levee en cours
    List<Point>? _poly;            // sommets de la forme libre en cours
    Point _hover;                  // position de la souris (elastique de la forme libre)

    /// <summary>Un dessin libre (main levee, forme libre) est termine ; keepTool : l'outil reste actif.</summary>
    public event Action<PageView, Shape, bool>? ShapeCreated;

    /// <summary>Abandonne un dessin en cours (changement d'outil, Echap).</summary>
    public void CancelDrawing()
    {
        _stroke = null; _poly = null;
        if (_mode == DragMode.Freehand) { _mode = DragMode.None; ReleaseMouseCapture(); }
        InvalidateVisual();
    }

    void FinishPolygon(bool closed)
    {
        if (_poly is { Count: >= 2 } pts &&
            OpenPub.App.Editing.DrawingFactory.Polygon(pts, closed, _session.PenColor, _session.PenWidth) is { } shape)
        {
            _poly = null;
            InvalidateVisual();
            ShapeCreated?.Invoke(this, shape, false);
            return;
        }
        CancelDrawing();
    }

    /// <summary>Un objet a ete trace a la souris en mode dessin (outil, rectangle en points).</summary>
    public event Action<PageView, string, Rect>? ShapeDrawn;

    static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x1f, 0x6f, 0xd1)));
    static readonly Brush GuideBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xe0, 0x2d, 0x9c)));
    static readonly Brush MarginBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xa0, 0x4f, 0x8f, 0xe8)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public PageView(EditorSession session, Page page)
    {
        _session = session;
        Page = page;
        Focusable = true;
        FocusVisualStyle = null;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SnapsToDevicePixels = false;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Page.Width * K, Page.Height * K);

    bool Editing => _session.TextEdit is { } ed && ed.Page == Page;

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(K, K));
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        bool marks = Interactive && _session.ShowGuides;
        if (Interactive && _session.TextEdit is { } ed0 && ed0.Page == Page)
        {
            // surbrillance semi-transparente de la selection de texte
            PageRenderer.DrawPage(dc, Page, _session, ppd, marks, overlay: dc2 =>
            {
                foreach (var (r, f) in ed0.SelectionRectsWithFrame())
                {
                    bool rot = PageRenderer.PushRotation(dc2, f);
                    dc2.DrawRectangle(SelectionBrush, null, r);
                    if (rot) dc2.Pop();
                }
            });
        }
        else PageRenderer.DrawPage(dc, Page, _session, ppd, marks);

        if (marks) { DrawMargins(dc); DrawGuides(dc); }
        if (Interactive && _session.Selected is { } sel && Page.Shapes.Contains(sel))
        {
            if (_session.MultiSelection) DrawMultiSelection(dc);
            else DrawSelection(dc, sel);
        }
        if (_marquee is { } mq)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x18, 0x1f, 0x6f, 0xd1)), new Pen(new SolidColorBrush(Color.FromRgb(0x1f, 0x6f, 0xd1)), 1 / K) { DashStyle = DashStyles.Dash }, mq);
        if (Interactive && _session.TextEdit is { } ed && ed.Page == Page && ed.CaretVisible && ed.CaretInfo() is { } ci)
        {
            bool rot = PageRenderer.PushRotation(dc, ci.Frame);
            dc.DrawLine(new Pen(Brushes.Black, 1.2 / K), ci.Rect.TopLeft, ci.Rect.BottomLeft);
            if (rot) dc.Pop();
        }
        if (_stroke is { Count: >= 2 } st)
        {
            var pen = new Pen(PageRenderer.Brush(_session.PenColor), Math.Max(_session.PenWidth, 0.5)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(st[0], false, false); c.PolyLineTo(st.Skip(1).ToList(), true, true); }
            dc.DrawGeometry(null, pen, g);
        }
        if (_poly is { Count: >= 1 } poly)
        {
            var pen = new Pen(PageRenderer.Brush(_session.PenColor), Math.Max(_session.PenWidth, 0.5)) { LineJoin = PenLineJoin.Round };
            for (int i = 1; i < poly.Count; i++) dc.DrawLine(pen, poly[i - 1], poly[i]);
            dc.DrawLine(new Pen(GuideBrush, 1 / K) { DashStyle = DashStyles.Dash }, poly[^1], _hover);
            bool nearFirst = poly.Count >= 3 && (_hover - poly[0]).Length <= 7 / K;
            for (int i = 0; i < poly.Count; i++)
                dc.DrawEllipse(i == 0 && nearFirst ? GuideBrush : Brushes.White, new Pen(GuideBrush, 1 / K), poly[i], (i == 0 && nearFirst ? 5 : 3) / K, (i == 0 && nearFirst ? 5 : 3) / K);
        }
        if (_drawRect is { } dr)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0x1f, 0x6f, 0xd1)), new Pen(GuideBrush, 1 / K) { DashStyle = DashStyles.Dash }, dr);
        if (Interactive && _mode is DragMode.Move or DragMode.Resize)
        {
            var gp = new Pen(GuideBrush, 1 / K);
            foreach (var x in _session.ActiveGuidesX) dc.DrawLine(gp, new Point(x, 0), new Point(x, Page.Height));
            foreach (var y in _session.ActiveGuidesY) dc.DrawLine(gp, new Point(0, y), new Point(Page.Width, y));
        }
        dc.Pop();
    }

    void DrawMargins(DrawingContext dc)
    {
        var m = Snapper.Margins(_session.Doc);
        var pen = new Pen(MarginBrush, 0.8 / K) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, pen, new Rect(m[3], m[0], Math.Max(0, Page.Width - m[1] - m[3]), Math.Max(0, Page.Height - m[0] - m[2])));
    }

    double HandleSize => 7 / K;

    static readonly Brush RulerGuideBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xA8, 0x8F)));
    static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0x5B, 0x9B, 0xD5)));

    /// <summary>Reperes de regle (verts), grille de disposition (bleue), apercu du repere en cours de creation.</summary>
    void DrawGuides(DrawingContext dc)
    {
        var doc = _session.Doc;
        if (doc.Grid is { } grid)
        {
            var m = Snapper.Margins(doc);
            var (xs, ys) = grid.Lines(m[3], m[0], Page.Width - m[1] - m[3], Page.Height - m[0] - m[2]);
            var gp = new Pen(GridBrush, 0.7 / K) { DashStyle = DashStyles.Dot };
            foreach (var x in xs) dc.DrawLine(gp, new Point(x, m[0]), new Point(x, Page.Height - m[2]));
            foreach (var y in ys) dc.DrawLine(gp, new Point(m[3], y), new Point(Page.Width - m[1], y));
        }
        var pen = new Pen(RulerGuideBrush, 1 / K);
        if (doc.Guides is { } g)
        {
            foreach (var x in g.X) dc.DrawLine(pen, new Point(x, 0), new Point(x, Page.Height));
            foreach (var y in g.Y) dc.DrawLine(pen, new Point(0, y), new Point(Page.Width, y));
        }
        var prev = new Pen(RulerGuideBrush, 1 / K) { DashStyle = DashStyles.Dash };
        if (_session.PreviewGuideX is { } px) dc.DrawLine(prev, new Point(px, 0), new Point(px, Page.Height));
        if (_session.PreviewGuideY is { } py) dc.DrawLine(prev, new Point(0, py), new Point(Page.Width, py));
    }

    /// <summary>Bordure interieure d'un tableau sous le point : colonne (vrai) ou ligne, indice de la colonne / ligne avant elle.</summary>
    (bool Col, int Index)? TableEdgeAt(Shape table, Point p)
    {
        var t = table.Table!;
        double tol = 3 / K;
        if (p.Y < table.Y - tol || p.Y > table.Y + table.H + tol || p.X < table.X - tol || p.X > table.X + table.W + tol) return null;
        double x = table.X;
        for (int c = 0; c < t.ColCount - 1; c++) { x += t.Cols[c]; if (Math.Abs(p.X - x) <= tol) return (true, c); }
        double y = table.Y;
        var hs = OpenPub.Core.Editing.TableOps.RowHeights(t);
        for (int r = 0; r < t.RowCount; r++) { y += hs[r]; if (Math.Abs(p.Y - y) <= tol && r < t.RowCount - 1) return (false, r); }
        return null;
    }

    /// <summary>Repere de regle sous le point (a 4 pixels pres) : (vertical ?, indice), ou null.</summary>
    (bool Vertical, int Index)? GuideAt(Point p)
    {
        if (_session.Doc.Guides is not { } g || !_session.ShowGuides) return null;
        double tol = 4 / K;
        for (int i = 0; i < g.X.Count; i++) if (Math.Abs(g.X[i] - p.X) <= tol) return (true, i);
        for (int i = 0; i < g.Y.Count; i++) if (Math.Abs(g.Y[i] - p.Y) <= tol) return (false, i);
        return null;
    }

    static readonly Brush SelBlue = Freeze(new SolidColorBrush(Color.FromRgb(0x1f, 0x6f, 0xd1)));
    static readonly Brush LockedGray = Freeze(new SolidColorBrush(Color.FromRgb(0x9a, 0xa3, 0xb5)));

    /// <summary>Boite englobante de la selection multiple (coordonnees de page).</summary>
    Rect SelectionBox()
    {
        var b = OpenPub.Core.Editing.ShapeOps.Bounds(_session.Selection);
        return new Rect(b.X, b.Y, b.W, b.H);
    }

    bool AnyLocked => _session.Selection.Any(x => x.Locked);

    /// <summary>Selection multiple ou groupe : contour de chaque objet et poignees sur la boite commune (sans rotation).</summary>
    void DrawMultiSelection(DrawingContext dc)
    {
        var thin = new Pen(new SolidColorBrush(Color.FromArgb(0xa0, 0x1f, 0x6f, 0xd1)), 1 / K) { DashStyle = DashStyles.Dash };
        foreach (var s in _session.Selection.Where(Page.Shapes.Contains))
        {
            bool rot = PageRenderer.PushRotation(dc, s);
            dc.DrawRectangle(null, thin, new Rect(s.X, s.Y, s.W, s.H));
            if (rot) dc.Pop();
        }
        var box = SelectionBox();
        var pen = new Pen(AnyLocked ? LockedGray : SelBlue, 1.2 / K);
        dc.DrawRectangle(null, pen, box);
        foreach (var h in Handles(box)) dc.DrawRectangle(Brushes.White, pen, h);
    }

    void DrawSelection(DrawingContext dc, Shape s)
    {
        bool rot = PageRenderer.PushRotation(dc, s);
        DrawSelectionLocal(dc, s);
        if (rot) dc.Pop();
    }

    /// <summary>Position de la poignee de rotation (repere de l'objet).</summary>
    Point RotationHandle(Shape s) => new(s.X + s.W / 2, s.Y - 22 / K);

    void DrawSelectionLocal(DrawingContext dc, Shape s)
    {
        var pen = new Pen(s.Locked ? LockedGray : SelBlue, 1.2 / K);
        if (Editing) pen.DashStyle = DashStyles.Dash;
        dc.DrawRectangle(null, pen, new Rect(s.X, s.Y, s.W, s.H));
        // zones liees : montrer toute la chaine
        if (s.Chain is not null)
        {
            var chainPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x1f, 0x6f, 0xd1)), 1 / K) { DashStyle = DashStyles.Dash };
            foreach (var f in Page.ChainFrames(s.Chain).Where(f => f != s))
                dc.DrawRectangle(null, chainPen, new Rect(f.X, f.Y, f.W, f.H));
        }
        if (Editing) return;          // pas de poignees pendant l'edition du texte
        if (s.Locked)
        {
            // objet verrouille : petit cadenas au lieu des poignees
            var ft = new FormattedText("\uE72E", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe Fluent Icons, Segoe MDL2 Assets"), 11 / K, LockedGray, 1);
            dc.DrawText(ft, new Point(s.X + s.W - 12 / K, s.Y - 14 / K));
            return;
        }
        foreach (var h in Handles(s))
            dc.DrawRectangle(Brushes.White, pen, h);
        // poignee de rotation (cercle vert au-dessus de l'objet)
        var rh = RotationHandle(s);
        dc.DrawLine(pen, new Point(s.X + s.W / 2, s.Y), rh);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2e, 0xa0, 0x43)), new Pen(Brushes.White, 1 / K), rh, 4.5 / K, 4.5 / K);
    }

    IEnumerable<Rect> Handles(Shape s) => Handles(new Rect(s.X, s.Y, s.W, s.H));

    IEnumerable<Rect> Handles(Rect r)
    {
        double hs = HandleSize, x0 = r.X, y0 = r.Y, x1 = r.Right, y1 = r.Bottom, xm = (x0 + x1) / 2, ym = (y0 + y1) / 2;
        foreach (var (x, y) in new[] { (x0, y0), (xm, y0), (x1, y0), (x1, ym), (x1, y1), (xm, y1), (x0, y1), (x0, ym) })
            yield return new Rect(x - hs / 2, y - hs / 2, hs, hs);
    }

    Point ToPage(MouseEventArgs e) { var p = e.GetPosition(this); return new Point(p.X / K, p.Y / K); }

    /// <summary>Objet le plus haut sous le point (les fonds de page ne sont pas selectionnables par clic).</summary>
    public Shape? HitTest(Point p)
    {
        for (int i = Page.Shapes.Count - 1; i >= 0; i--)
        {
            var s = Page.Shapes[i];
            if (!Selectable(s)) continue;
            var tol = 3 / K;
            var lp = Geo.ToLocal(s, p);
            if (lp.X >= s.X - tol && lp.X <= s.X + s.W + tol && lp.Y >= s.Y - tol && lp.Y <= s.Y + s.H + tol)
                return s;
        }
        return null;
    }

    /// <summary>Les fonds de page (image de fond, forme couvrant toute la page) ne se selectionnent pas a la souris.</summary>
    bool Selectable(Shape s) =>
        !s.Background && !(s.Kind is not (ShapeKind.Text or ShapeKind.Image) && s.W >= Page.Width * 0.95 && s.H >= Page.Height * 0.95);

    /// <summary>Zone de la chaine en cours d'edition sous le point (les zones de texte passent avant les autres objets).</summary>
    Shape? EditingFrameAt(Point p)
    {
        if (_session.TextEdit is not { } ed || ed.Page != Page) return null;
        return ed.Frames.FirstOrDefault(f =>
        {
            var lp = Geo.ToLocal(f, p);
            return lp.X >= f.X - 2 && lp.X <= f.X + f.W + 2 && lp.Y >= f.Y - 2 && lp.Y <= f.Y + f.H + 2;
        });
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Interactive) return;
        Focus();
        var p = ToPage(e);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // Ctrl + clic sur un lien : l'ouvrir (sinon Ctrl + clic ajoute a la selection)
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _session.DrawTool is null && FollowLink?.Invoke(this, p) == true)
        {
            e.Handled = true;
            return;
        }

        // ecriture dans une cellule : un clic dans une autre cellule du meme tableau y place le curseur
        if (_session.TextEdit is { Owner: { } owner } ced && ced.Page == Page && EditingFrameAt(p) is null && HitTest(p) == owner
            && OpenPub.Core.Editing.TableOps.CellAt(owner, p.X, p.Y) is { } other)
        {
            _session.BeginCellEdit(Page, owner, other.Row, other.Col, p);
            _mode = DragMode.TextSelect;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        // edition de texte en cours : clic dans la chaine = curseur / selection
        if (_session.TextEdit is { } ed && ed.Page == Page && EditingFrameAt(p) is not null)
        {
            var pos = ed.HitTest(p);
            if (e.ClickCount == 2) { var (a, b) = StoryEditor.WordAt(ed.Paras, pos); ed.Select(a, b); }
            else if (e.ClickCount >= 3)
                ed.Select(new TextPos(pos.Para, 0), new TextPos(pos.Para, StoryEditor.Length(ed.Paras[pos.Para])));
            else
            {
                ed.SetCaret(pos, shift);
                _mode = DragMode.TextSelect;
                CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        // mode « créer un lien » : le clic designe la zone qui recevra la suite du texte
        if (_session.LinkSource is not null)
        {
            LinkTargetPicked?.Invoke(this, HitTest(p));
            e.Handled = true;
            return;
        }
        // indicateur de debordement « A… » en bas a droite d'une zone : lier une zone pour la suite du texte
        if (_session.DrawTool is null && !Editing && _session.ShowGuides)
        {
            var layout = _session.Layout(Page);
            foreach (var s0 in Page.Shapes.Where(x => x.IsText && Math.Abs(x.Rotation % 360) < 0.01))
                if (new Rect(s0.X + s0.W - 16, s0.Y + s0.H - 12, 18, 14).Contains(p) && layout.Overflows(s0))
                {
                    OverflowClicked?.Invoke(this, s0);
                    e.Handled = true;
                    return;
                }
        }
        // crayon : trace a main levee
        if (_session.DrawTool == "freehand")
        {
            _session.EndTextEdit();
            _stroke = new List<Point> { p };
            _mode = DragMode.Freehand;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        // forme libre : un clic par sommet, double-clic ou Entree pour terminer, clic sur le 1er point pour fermer
        if (_session.DrawTool == "polygon")
        {
            _session.EndTextEdit();
            if (_poly is null) _poly = new List<Point> { p };
            else if (e.ClickCount >= 2) FinishPolygon(closed: false);
            else if (_poly.Count >= 3 && (p - _poly[0]).Length <= 7 / K) FinishPolygon(closed: true);
            else _poly.Add(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Constrain45(_poly[^1], p) : p);
            _hover = p;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        // mode dessin : tracer une forme ou une zone de texte
        if (_session.DrawTool is not null)
        {
            _session.EndTextEdit();
            _mode = DragMode.Draw;
            _start = p;
            _drawRect = new Rect(p, p);
            DrawStart = DrawEnd = p;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        var sel = _session.Selected;
        bool add = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || shift;
        if (sel is not null && Page.Shapes.Contains(sel) && !Editing && !add && !AnyLocked)
        {
            if (_session.MultiSelection)
            {
                int i = 0;
                foreach (var h in Handles(SelectionBox()))
                {
                    var hit = h; hit.Inflate(HandleSize * 0.6, HandleSize * 0.6);
                    if (hit.Contains(p)) { StartDrag(DragMode.Resize, p, sel, i); e.Handled = true; return; }
                    i++;
                }
            }
            else
            {
                // tableau selectionne : tirer une bordure interieure regle la largeur d'une colonne ou la hauteur d'une ligne
                if (sel.IsTable && TableEdgeAt(sel, p) is { } edge)
                {
                    _mode = edge.Col ? DragMode.TableCol : DragMode.TableRow;
                    _tableEdge = edge.Index;
                    _start = p;
                    var tb = sel.Table!;
                    _tableEdgeStart = edge.Col ? tb.Cols[edge.Index] : OpenPub.Core.Editing.TableOps.RowHeights(tb)[edge.Index];
                    _session.BeginChange();
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
                var lp = Geo.ToLocal(sel, p);
                if ((lp - RotationHandle(sel)).Length <= 7 / K) { StartDrag(DragMode.Rotate, p, sel, -1); e.Handled = true; return; }
                int i = 0;
                foreach (var h in Handles(sel))
                {
                    var hit = h; hit.Inflate(HandleSize * 0.6, HandleSize * 0.6);
                    if (hit.Contains(lp)) { StartDrag(DragMode.Resize, p, sel, i); e.Handled = true; return; }
                    i++;
                }
            }
        }
        var target = HitTest(p);
        // repere de regle : glisser pour le deplacer (sur un objet : Maj + glisser)
        if (!Editing && GuideAt(p) is { } guide && (target is null || shift))
        {
            _mode = DragMode.Guide;
            (_guideVertical, _guideIndex) = guide;
            _session.BeginChange();
            CaptureMouse();
            e.Handled = true;
            return;
        }
        // dans un groupe deja « ouvert » (un seul de ses membres selectionne), le clic choisit un membre
        bool inGroup = target?.Group is not null && _session.Selection.Count == 1 && sel?.Group == target.Group && Page.Shapes.Contains(sel);
        if (e.ClickCount == 2 && target is not null)
        {
            if (target.IsTable && OpenPub.Core.Editing.TableOps.CellAt(target, p.X, p.Y) is { } cellRc)
                _session.BeginCellEdit(Page, target, cellRc.Row, cellRc.Col, p);
            else if (target.IsText) _session.BeginTextEdit(Page, target, p);
            else if (target.Group is not null && !inGroup) _session.Select(target, Page);      // entrer dans le groupe
            else if (target.IsImage) ReplaceImageRequested?.Invoke(target);
            e.Handled = true;
            return;
        }
        if (target is null)
        {
            // clic dans le vide : rectangle de selection
            if (!add) _session.Select(null, Page);
            _mode = DragMode.Marquee;
            _marqueeAdd = add;
            _start = p;
            _marquee = new Rect(p, p);
            CaptureMouse();
            e.Handled = true;
            return;
        }
        var unit = inGroup ? new List<Shape> { target } : OpenPub.Core.Editing.ShapeOps.GroupOf(Page, target);
        if (add)
        {
            _session.ToggleSelect(unit, Page);
            if (!_session.IsSelected(target)) { e.Handled = true; return; }
        }
        else if (!(_session.IsSelected(target) && _session.MultiSelection && _session.SelectedPage == Page))
            _session.SelectMany(unit, target, Page);
        if (!_session.Selection.All(x => x.Locked)) StartDrag(DragMode.Move, p, target, -1);
        e.Handled = true;
    }

    void StartDrag(DragMode mode, Point p, Shape s, int handle)
    {
        _mode = mode;
        _handle = handle;
        _start = p;
        _origs.Clear();
        if (_session.MultiSelection && mode != DragMode.Rotate)
        {
            foreach (var x in _session.Selection) _origs[x] = new Rect(x.X, x.Y, x.W, x.H);
            _orig = SelectionBox();
            _origRotation = 0;
        }
        else
        {
            _orig = new Rect(s.X, s.Y, s.W, s.H);
            _origRotation = s.Rotation;
        }
        _moved = false;
        _session.BeginChange();
        CaptureMouse();
    }

    /// <summary>Place des objets d'une boite dans une autre (deplacement et mise a l'echelle, centres conserves en proportion).</summary>
    public static void FitShapes(IEnumerable<Shape> shapes, Rect from, Rect to)
    {
        double sx = from.Width > 0 ? to.Width / from.Width : 1, sy = from.Height > 0 ? to.Height / from.Height : 1;
        foreach (var x in shapes.ToList())
        {
            double cx = to.X + (x.X + x.W / 2 - from.X) * sx, cy = to.Y + (x.Y + x.H / 2 - from.Y) * sy;
            double w = x.W * sx, h = x.H * sy;
            MoveShape(x, new Rect(cx - w / 2, cy - h / 2, w, h));
        }
    }

    /// <summary>Selection multiple : place chaque objet en suivant la boite commune (deplacement ou mise a l'echelle).</summary>
    void ApplyMulti(Rect box)
    {
        double sx = _orig.Width > 0 ? box.Width / _orig.Width : 1, sy = _orig.Height > 0 ? box.Height / _orig.Height : 1;
        foreach (var (x, o) in _origs)
        {
            if (x.Locked) continue;
            // centre de l'objet ramene dans la nouvelle boite ; taille a l'echelle
            double cx = box.X + (o.X + o.Width / 2 - _orig.X) * sx, cy = box.Y + (o.Y + o.Height / 2 - _orig.Y) * sy;
            double w = o.Width * sx, h = o.Height * sy;
            MoveShape(x, new Rect(cx - w / 2, cy - h / 2, w, h));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Interactive) return;
        var p = ToPage(e);
        if (_mode == DragMode.TextSelect)
        {
            if (_session.TextEdit is { } ed && e.LeftButton == MouseButtonState.Pressed) ed.SetCaret(ed.HitTest(p), extend: true);
            return;
        }
        if (_mode == DragMode.Freehand && _stroke is not null)
        {
            if ((p - _stroke[^1]).Length >= 0.7 / K) { _stroke.Add(p); InvalidateVisual(); }
            return;
        }
        if (_poly is not null)
        {
            _hover = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Constrain45(_poly[^1], p) : p;
            InvalidateVisual();
        }
        if (_mode == DragMode.None)
        {
            Cursor = _session.LinkSource is not null ? (HitTest(p) is { IsText: true } ? Cursors.Hand : Cursors.No)
                   : _session.DrawTool is "freehand" ? Cursors.Pen : _session.DrawTool is not null ? Cursors.Cross : CursorAt(p);
            return;
        }
        if (_mode == DragMode.Marquee)
        {
            _marquee = new Rect(_start, p);
            InvalidateVisual();
            return;
        }
        if (_mode is DragMode.TableCol or DragMode.TableRow && _session.Selected is { Table: { } tt } tsh)
        {
            if (_mode == DragMode.TableCol && _tableEdge + 1 < tt.ColCount)
            {
                // la colonne de droite cede ce que prend celle de gauche : la largeur du tableau ne change pas
                double total = tt.Cols[_tableEdge] + tt.Cols[_tableEdge + 1];
                double nw = Math.Clamp(_tableEdgeStart + (p.X - _start.X), 8, total - 8);
                tt.Cols[_tableEdge] = nw; tt.Cols[_tableEdge + 1] = total - nw;
            }
            else if (_mode == DragMode.TableRow)
                tt.Rows[_tableEdge] = Math.Max(8, _tableEdgeStart + (p.Y - _start.Y));
            OpenPub.Core.Editing.TableOps.SyncSize(tsh);
            _session.Invalidate(Page);
            return;
        }
        if (_mode == DragMode.Guide && _session.Doc.Guides is { } gd)
        {
            var list = _guideVertical ? gd.X : gd.Y;
            if (_guideIndex < list.Count) list[_guideIndex] = Math.Round(_guideVertical ? p.X : p.Y, 2);
            InvalidateVisual();
            return;
        }
        if (_mode == DragMode.Draw)
        {
            var r0 = new Rect(_start, p);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Maj : carre / cercle
                double side = Math.Max(r0.Width, r0.Height);
                r0 = new Rect(p.X < _start.X ? _start.X - side : _start.X, p.Y < _start.Y ? _start.Y - side : _start.Y, side, side);
            }
            _drawRect = r0;
            DrawStart = _start; DrawEnd = p;
            InvalidateVisual();
            return;
        }
        var s = _session.Selected;
        if (s is null) return;
        if (_mode == DragMode.Rotate)
        {
            var c = Geo.Center(s);
            double ang = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 90;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ang = Math.Round(ang / 15) * 15;
            else if (_session.SnapEnabled)
                foreach (var snapAng in new[] { 0.0, 90, 180, -90, 270, -180 })
                    if (Math.Abs(Geo.Normalize(ang - snapAng)) < 3) ang = snapAng;
            s.Rotation = Geo.Normalize(ang);
            _moved = true;
            _session.Invalidate(Page);
            return;
        }
        double dx = p.X - _start.X, dy = p.Y - _start.Y;
        if (!_moved && Math.Abs(dx) * K < 2 && Math.Abs(dy) * K < 2) return;
        _moved = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _mode == DragMode.Move)
        {
            if (Math.Abs(dx) > Math.Abs(dy)) dy = 0; else dx = 0;       // deplacement contraint
        }
        var r = _orig;
        bool snap = _session.SnapEnabled && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        double tol = 6 / K;                                              // 6 pixels a l'ecran
        _session.ActiveGuidesX.Clear(); _session.ActiveGuidesY.Clear();
        bool multi = _origs.Count > 0;
        if (_mode == DragMode.Move)
        {
            r.X += dx; r.Y += dy;
            if (snap) r = Snapper.SnapMove(r, Page, _session.Doc, s, tol, _session.ActiveGuidesX, _session.ActiveGuidesY, multi ? _session.Selection : null);
            if (multi) { ApplyMulti(r); _session.Invalidate(Page); return; }
        }
        else if (Math.Abs(_origRotation % 360) > 0.01)
        {
            // objet tourne : redimensionnement dans son repere, le bord oppose restant fixe a l'ecran
            var c0 = new Point(_orig.X + _orig.Width / 2, _orig.Y + _orig.Height / 2);
            var a = Geo.Rotate(_start, c0, -_origRotation);
            var b = Geo.Rotate(p, c0, -_origRotation);
            double ldx = b.X - a.X, ldy = b.Y - a.Y;
            double x0 = _orig.Left, y0 = _orig.Top, x1 = _orig.Right, y1 = _orig.Bottom;
            if (_handle is 0 or 6 or 7) x0 = Math.Min(x0 + ldx, x1 - 4);
            if (_handle is 2 or 3 or 4) x1 = Math.Max(x1 + ldx, x0 + 4);
            if (_handle is 0 or 1 or 2) y0 = Math.Min(y0 + ldy, y1 - 4);
            if (_handle is 4 or 5 or 6) y1 = Math.Max(y1 + ldy, y0 + 4);
            var newCenterLocal = new Point((x0 + x1) / 2, (y0 + y1) / 2);
            var newCenter = Geo.Rotate(newCenterLocal, c0, _origRotation);
            r = new Rect(newCenter.X - (x1 - x0) / 2, newCenter.Y - (y1 - y0) / 2, x1 - x0, y1 - y0);
        }
        else
        {
            double x0 = r.Left, y0 = r.Top, x1 = r.Right, y1 = r.Bottom;
            if (_handle is 0 or 6 or 7) x0 += dx;
            if (_handle is 2 or 3 or 4) x1 += dx;
            if (_handle is 0 or 1 or 2) y0 += dy;
            if (_handle is 4 or 5 or 6) y1 += dy;
            // Maj sur une poignee d'angle : conserve les proportions (utile pour les photos)
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _handle is 0 or 2 or 4 or 6 && _orig.Height > 0)
            {
                double ratio = _orig.Width / _orig.Height, w = x1 - x0;
                double h = w / ratio;
                if (_handle is 0 or 2) y0 = y1 - h; else y1 = y0 + h;
            }
            const double min = 4;
            if (x1 - x0 < min) { if (_handle is 0 or 6 or 7) x0 = x1 - min; else x1 = x0 + min; }
            if (y1 - y0 < min) { if (_handle is 0 or 1 or 2) y0 = y1 - min; else y1 = y0 + min; }
            r = new Rect(x0, y0, x1 - x0, y1 - y0);
            if (snap && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                r = Snapper.SnapResize(r, _handle, Page, _session.Doc, s, tol, _session.ActiveGuidesX, _session.ActiveGuidesY, multi ? _session.Selection : null);
            if (multi) { ApplyMulti(r); _session.Invalidate(Page); return; }
        }
        MoveShape(s, r);
        _session.Invalidate(Page);
    }

    /// <summary>Deplace un objet ; les traces (chemins, detourages) suivent.</summary>
    public static void MoveShape(Shape s, Rect r)
    {
        double sx = s.W > 0 ? r.Width / s.W : 1, sy = s.H > 0 ? r.Height / s.H : 1;
        double ox = s.X, oy = s.Y;
        void Map(List<PathSegment>? path)
        {
            if (path is null) return;
            foreach (var seg in path)
            {
                if (seg.X is { } x) seg.X = r.X + (x - ox) * sx;
                if (seg.Y is { } y) seg.Y = r.Y + (y - oy) * sy;
                if (seg.X1 is { } x1) seg.X1 = r.X + (x1 - ox) * sx;
                if (seg.Y1 is { } y1) seg.Y1 = r.Y + (y1 - oy) * sy;
                if (seg.X2 is { } x2) seg.X2 = r.X + (x2 - ox) * sx;
                if (seg.Y2 is { } y2) seg.Y2 = r.Y + (y2 - oy) * sy;
                if (seg.Rx is { } rx) seg.Rx = rx * sx;
                if (seg.Ry is { } ry) seg.Ry = ry * sy;
            }
        }
        Map(s.Path);
        Map(s.Clip);
        if (s.Table is { } table && (Math.Abs(sx - 1) > 1e-9 || Math.Abs(sy - 1) > 1e-9))
        {
            // tableau redimensionne : colonnes et lignes a l'echelle
            for (int i = 0; i < table.Cols.Count; i++) table.Cols[i] *= sx;
            for (int i = 0; i < table.Rows.Count; i++) table.Rows[i] *= sy;
            table.ActualRows = null;
        }
        s.X = r.X; s.Y = r.Y; s.W = r.Width; s.H = r.Height;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;
        var mode = _mode;
        _mode = DragMode.None;
        ReleaseMouseCapture();
        if (mode == DragMode.TextSelect) { TextSelectionFinished?.Invoke(); return; }
        if (mode is DragMode.TableCol or DragMode.TableRow) { _session.EndChange(); _session.Invalidate(Page); return; }
        if (mode == DragMode.Guide)
        {
            // repere tire hors de la page : supprime
            var q = ToPage(e);
            if (_session.Doc.Guides is { } gd && (q.X < 0 || q.Y < 0 || q.X > Page.Width || q.Y > Page.Height))
            {
                var list = _guideVertical ? gd.X : gd.Y;
                if (_guideIndex < list.Count) list.RemoveAt(_guideIndex);
                if (gd.X.Count == 0 && gd.Y.Count == 0) _session.Doc.Guides = null;
            }
            _session.EndChange();
            GuidesChanged?.Invoke();
            return;
        }
        if (mode == DragMode.Marquee)
        {
            var mq = _marquee ?? Rect.Empty;
            _marquee = null;
            InvalidateVisual();
            if (mq.Width * K < 3 && mq.Height * K < 3) return;
            // objets entierement dans le rectangle ; un groupe n'est pris que s'il y est en entier
            bool Inside(Shape x) { var b = x.Bounds(); return mq.Contains(new Rect(b.X, b.Y, b.W, b.H)); }
            var picked = Page.Shapes.Where(x => Selectable(x) && Inside(x)).ToList();
            picked.RemoveAll(x => x.Group is not null && !OpenPub.Core.Editing.ShapeOps.GroupOf(Page, x).All(Inside));
            var all = (_marqueeAdd && _session.SelectedPage == Page ? _session.Selection.Concat(picked) : picked).Distinct().ToList();
            _session.SelectMany(all, all.LastOrDefault(), Page);
            return;
        }
        if (mode == DragMode.Freehand)
        {
            var pts = _stroke;
            _stroke = null;
            InvalidateVisual();
            // tolerance de lissage : environ 1 pixel a l'ecran
            if (pts is { Count: >= 2 } && OpenPub.App.Editing.DrawingFactory.Freehand(pts, _session.PenColor, _session.PenWidth, 1.0 / K) is { } shape)
                ShapeCreated?.Invoke(this, shape, true);
            return;
        }
        if (mode == DragMode.Draw)
        {
            var r = _drawRect ?? Rect.Empty;
            _drawRect = null;
            var tool = _session.DrawTool;
            if (tool is not null) ShapeDrawn?.Invoke(this, tool, r);
            InvalidateVisual();
            return;
        }
        _session.ActiveGuidesX.Clear(); _session.ActiveGuidesY.Clear();
        _session.EndChange();
        _session.Invalidate(Page);
    }

    // ---------------------------------------------------------------- clavier (edition de texte)

    static Point Constrain45(Point from, Point to)
    {
        var d = to - from;
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4), len = d.Length;
        return new Point(from.X + Math.Cos(ang) * len, from.Y + Math.Sin(ang) * len);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_poly is not null)
        {
            switch (e.Key)
            {
                case Key.Enter: FinishPolygon(closed: false); e.Handled = true; return;
                case Key.Escape: CancelDrawing(); e.Handled = true; return;
                case Key.Back:
                    if (_poly.Count > 1) _poly.RemoveAt(_poly.Count - 1); else _poly = null;
                    InvalidateVisual(); e.Handled = true; return;
            }
        }
        if (_session.TextEdit is { } ed && ed.Page == Page)
        {
            if (e.Key == Key.Escape) { _session.EndTextEdit(); e.Handled = true; return; }
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (ed.HandleKey(key, Keyboard.Modifiers)) { e.Handled = true; NotifyCaret(); return; }
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_session.TextEdit is { } ed && ed.Page == Page && !string.IsNullOrEmpty(e.Text))
        {
            // caracteres de controle (Ctrl+lettre, Entree, Retour arriere) traites dans OnKeyDown
            var text = new string(e.Text.Where(c => !char.IsControl(c) || c == '\t').ToArray());
            if (text.Length > 0) { ed.TypeInput(text); NotifyCaret(); }
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    /// <summary>Fait defiler la vue jusqu'au curseur de texte (recherche, orthographe).</summary>
    public void RevealCaret() => NotifyCaret();

    /// <summary>Clic droit sur la page : menu contextuel (position en points).</summary>
    public event Action<PageView, Point>? ContextMenuRequested;

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (!Interactive) return;
        ContextMenuRequested?.Invoke(this, ToPage(e));
        e.Handled = true;
    }

    void NotifyCaret()
    {
        if (_session.TextEdit?.CaretRect() is { } c)
            CaretMoved?.Invoke(this, new Rect(c.X * K - 20, c.Y * K - 20, 40, c.Height * K + 40));
    }

    Cursor CursorAt(Point p)
    {
        if (EditingFrameAt(p) is not null) return Cursors.IBeam;
        var sel = _session.Selected;
        if (sel is not null && Page.Shapes.Contains(sel) && !Editing && _session.MultiSelection && !AnyLocked)
        {
            int k = 0;
            foreach (var h in Handles(SelectionBox()))
            {
                var hit = h; hit.Inflate(HandleSize * 0.6, HandleSize * 0.6);
                if (hit.Contains(p))
                    return k switch { 0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, _ => Cursors.SizeWE };
                k++;
            }
        }
        else if (sel is not null && Page.Shapes.Contains(sel) && !Editing && !sel.Locked)
        {
            if (sel.IsTable && TableEdgeAt(sel, p) is { } te) return te.Col ? Cursors.SizeWE : Cursors.SizeNS;
            var lp = Geo.ToLocal(sel, p);
            if ((lp - RotationHandle(sel)).Length <= 7 / K) return Cursors.Hand;
            int i = 0;
            foreach (var h in Handles(sel))
            {
                var hit = h; hit.Inflate(HandleSize * 0.6, HandleSize * 0.6);
                if (hit.Contains(lp))
                    return i switch { 0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, _ => Cursors.SizeWE };
                i++;
            }
        }
        if (HitTest(p) is { Locked: false }) return Cursors.SizeAll;
        if (GuideAt(p) is { } g) return g.Vertical ? Cursors.SizeWE : Cursors.SizeNS;
        return Cursors.Arrow;
    }
}
