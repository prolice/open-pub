using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenPub.App.Controls;

/// <summary>
/// Regle graduee en centimetres (horizontale ou verticale), alignee sur la page active. Un glisser depuis la regle
/// vers la page cree un repere (evenements GuideDrag*).
/// </summary>
public sealed class RulerBar : FrameworkElement
{
    public bool Vertical { get; init; }
    /// <summary>Position du bord de la page dans la regle (pixels) et echelle (pixels par point).</summary>
    public double Origin { get; private set; }
    public double Scale { get; private set; } = 1;
    /// <summary>Etendue de la page (points) : les graduations hors de la page sont estompees.</summary>
    public double Extent { get; private set; }
    /// <summary>Selection a mettre en evidence (debut, fin, en points), ou null.</summary>
    public (double From, double To)? Highlight { get; private set; }

    public event Action<RulerBar, MouseEventArgs>? GuideDragMove;
    public event Action<RulerBar, MouseEventArgs>? GuideDragEnd;
    bool _dragging;

    static readonly Brush Back = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0xFB, 0xFF)));
    static readonly Brush PageBack = Brushes.White;
    static readonly Brush Tick = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xAD)));
    static readonly Brush Label = Freeze(new SolidColorBrush(Color.FromRgb(0x52, 0x61, 0x7E)));
    static readonly Brush Sel = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x99, 0x35, 0xF4)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    public RulerBar()
    {
        Cursor = Cursors.Hand;
        ToolTip = "Glissez vers la page pour créer un repère";
    }

    public void Update(double origin, double scale, double extent, (double, double)? highlight)
    {
        if (Math.Abs(origin - Origin) < 0.01 && Math.Abs(scale - Scale) < 1e-6 && Math.Abs(extent - Extent) < 0.01 && highlight == Highlight) return;
        Origin = origin; Scale = scale; Extent = extent; Highlight = highlight;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double len = Vertical ? ActualHeight : ActualWidth, thick = Vertical ? ActualWidth : ActualHeight;
        Rect R(double a, double b, double c, double d) => Vertical ? new Rect(c, a, d, b) : new Rect(a, c, b, d);
        dc.DrawRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Scale <= 0) return;
        double p0 = Origin, p1 = Origin + Extent * Scale;
        dc.DrawRectangle(PageBack, null, R(Math.Max(0, p0), Math.Max(0, Math.Min(len, p1) - Math.Max(0, p0)), 0, thick));
        if (Highlight is { } h) dc.DrawRectangle(Sel, null, R(Origin + h.From * Scale, Math.Max(1, (h.To - h.From) * Scale), 0, thick));
        double pxPerCm = Scale * 72 / 2.54;
        double step = pxPerCm >= 40 ? 0.1 : pxPerCm >= 16 ? 0.5 : 1;          // graduation la plus fine (cm)
        int labelEvery = pxPerCm >= 25 ? 1 : pxPerCm >= 10 ? 2 : 5;
        var pen = new Pen(Tick, 1);
        double startCm = Math.Floor(-Origin / pxPerCm) - 1, endCm = Math.Ceiling((len - Origin) / pxPerCm) + 1;
        var tf = new Typeface("Segoe UI");
        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (double cm = startCm; cm <= endCm; cm += step)
        {
            double pos = Math.Round(Origin + cm * pxPerCm) + 0.5;
            if (pos < 0 || pos > len) continue;
            double rc = Math.Round(cm, 2);
            bool whole = Math.Abs(rc - Math.Round(rc)) < 1e-6, half = !whole && Math.Abs(rc * 2 - Math.Round(rc * 2)) < 1e-6;
            double t = whole ? thick * 0.55 : half ? thick * 0.35 : thick * 0.2;
            if (Vertical) dc.DrawLine(pen, new Point(thick - t, pos), new Point(thick, pos));
            else dc.DrawLine(pen, new Point(pos, thick - t), new Point(pos, thick));
            int n = (int)Math.Round(rc);
            if (whole && n % labelEvery == 0)
            {
                var ft = new FormattedText(n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 9, Label, ppd);
                if (Vertical)
                {
                    dc.PushTransform(new RotateTransform(-90, 2, pos - 2));
                    dc.DrawText(ft, new Point(2, pos - 2));
                    dc.Pop();
                }
                else dc.DrawText(ft, new Point(pos + 2, 1));
            }
        }
        var border = new Pen(Tick, 0.5);
        if (Vertical) dc.DrawLine(border, new Point(thick - 0.5, 0), new Point(thick - 0.5, len));
        else dc.DrawLine(border, new Point(0, thick - 0.5), new Point(len, thick - 0.5));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) GuideDragMove?.Invoke(this, e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        GuideDragEnd?.Invoke(this, e);
    }
}
