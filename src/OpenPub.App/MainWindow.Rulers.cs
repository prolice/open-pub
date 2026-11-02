using System.Windows;
using System.Windows.Input;
using OpenPub.App.Controls;
using OpenPub.Core.Model;

namespace OpenPub.App;

// Regles graduees et reperes de regle (glisser depuis une regle vers la page).
public partial class MainWindow
{
    bool _rulersHooked;

    void HookRulers()
    {
        if (_rulersHooked) return;
        _rulersHooked = true;
        RulerTop.GuideDragMove += (r, e) => GuideDrag(r, e, end: false);
        RulerTop.GuideDragEnd += (r, e) => GuideDrag(r, e, end: true);
        RulerLeft.GuideDragMove += (r, e) => GuideDrag(r, e, end: false);
        RulerLeft.GuideDragEnd += (r, e) => GuideDrag(r, e, end: true);
        Scroller.SizeChanged += (_, _) => UpdateRulers();
    }

    PageView? ActiveView() => ActivePage() is { } p ? _views.FirstOrDefault(v => v.Page == p) : _views.FirstOrDefault();

    /// <summary>Recale les regles sur la page active (origine, echelle, selection).</summary>
    void UpdateRulers()
    {
        if (RulerTop is null || RulerTop.Visibility != Visibility.Visible) return;
        HookRulers();
        if (_session is null || ActiveView() is not { } v || !v.IsLoaded)
        {
            RulerTop.Update(0, 0, 0, null); RulerLeft.Update(0, 0, 0, null);
            return;
        }
        var k = PageView.DipPerPoint * _zoom;
        var o = v.TranslatePoint(new Point(0, 0), Scroller);
        (double, double)? hx = null, hy = null;
        if (_session.Selected is not null && SelPage == v.Page)
        {
            var b = Core.Editing.ShapeOps.Bounds(_session.Selection);
            hx = (b.X, b.X + b.W); hy = (b.Y, b.Y + b.H);
        }
        RulerTop.Update(o.X, k, v.Page.Width, hx);
        RulerLeft.Update(o.Y, k, v.Page.Height, hy);
    }

    /// <summary>Glisser depuis la regle du haut : repere horizontal ; depuis la regle de gauche : repere vertical.</summary>
    void GuideDrag(RulerBar ruler, MouseEventArgs e, bool end)
    {
        if (_session is null) return;
        // page sous la souris (sinon la page active)
        var view = _views.FirstOrDefault(v =>
        {
            var q = e.GetPosition(v);
            return q.X >= 0 && q.Y >= 0 && q.X <= v.ActualWidth && q.Y <= v.ActualHeight;
        }) ?? ActiveView();
        if (view is null) return;
        var k = PageView.DipPerPoint * _zoom;
        var p = e.GetPosition(view);
        double x = p.X / k, y = p.Y / k;
        bool horizontal = ruler == RulerTop;
        bool inside = x >= 0 && y >= 0 && x <= view.Page.Width && y <= view.Page.Height;
        _session.PreviewGuideX = !horizontal && inside ? x : null;
        _session.PreviewGuideY = horizontal && inside ? y : null;
        if (end)
        {
            _session.PreviewGuideX = _session.PreviewGuideY = null;
            if (inside)
            {
                _session.BeginChange();
                var g = _session.Doc.Guides ??= new RulerGuides();
                if (horizontal) g.Y.Add(Math.Round(y, 2)); else g.X.Add(Math.Round(x, 2));
                _session.EndChange();
                StatusText.Text = "Repère ajouté : glissez-le pour le déplacer, hors de la page pour le supprimer.";
            }
        }
        foreach (var v in _views) v.InvalidateVisual();
    }
}
