using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenPub.App.Rendering;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using Shape = OpenPub.Core.Model.Shape;

namespace OpenPub.App;

/// <summary>
/// Editeur d'image (comme les outils image de Publisher) : recadrage, couleurs (gris, noir et blanc, sepia,
/// filigrane), luminosite, contraste, transparence, miroir, rotation, forme de decoupe, ombre, reflet.
/// Travaille sur une copie de l'objet ; <see cref="Apply"/> reporte les reglages sur l'original.
/// </summary>
public sealed class ImageEditorWindow : Window
{
    readonly EditorSession _session;
    readonly Shape _original;
    readonly Shape _work;
    readonly double _origFw, _origFh;               // part visible de l'image a l'ouverture (recadrage)
    readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
    readonly Grid _previewHost = new() { Background = new SolidColorBrush(Color.FromRgb(0xe9, 0xec, 0xf1)) };
    readonly Canvas _cropCanvas = new() { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Cursor = Cursors.Cross };
    readonly Rectangle _cropRect = new() { Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
    readonly Path _cropShade = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
    readonly ToggleButton _cropToggle = new() { Content = "✂ Recadrer (tracer le cadre à conserver)", Padding = new Thickness(8, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Left };
    readonly Dictionary<string, Slider> _sliders = new();
    readonly Dictionary<string, RadioButton> _colorButtons = new();
    readonly ComboBox _shapeBox = new() { Margin = new Thickness(0, 0, 0, 6) };
    readonly CheckBox _shadowOn = new() { Content = "Ombre portée", Margin = new Thickness(0, 0, 0, 4) };
    readonly CheckBox _reflOn = new() { Content = "Reflet sous l'image", Margin = new Thickness(0, 0, 0, 4) };
    readonly ToggleButton _flipH = new() { Content = "⇋ Miroir horizontal", Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 6, 0) };
    readonly ToggleButton _flipV = new() { Content = "⇵ Miroir vertical", Padding = new Thickness(6, 4, 6, 4) };
    readonly TextBlock _rotText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    bool _loading;
    Point? _cropStart;

    ImageFx Fx => _work.ImageFx ??= new ImageFx();

    public ImageEditorWindow(EditorSession session, Shape image)
    {
        _session = session;
        _original = image;
        _work = System.Text.Json.JsonSerializer.Deserialize<Shape>(System.Text.Json.JsonSerializer.Serialize(image, DocumentJson.Options), DocumentJson.Options)!;
        _origFw = 1 - (image.ImageFx?.CropLeft ?? 0) - (image.ImageFx?.CropRight ?? 0);
        _origFh = 1 - (image.ImageFx?.CropTop ?? 0) - (image.ImageFx?.CropBottom ?? 0);

        Title = "Modifier l'image";
        Width = 1060; Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 13;
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        _previewHost.Children.Add(_preview);
        _cropCanvas.Children.Add(_cropShade);
        _cropCanvas.Children.Add(_cropRect);
        _previewHost.Children.Add(_cropCanvas);
        root.Children.Add(_previewHost);

        var panel = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White };
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        Content = root;

        // --- recadrage
        panel.Children.Add(Section("RECADRER"));
        panel.Children.Add(_cropToggle);
        _cropToggle.Checked += (_, _) => { _cropCanvas.Visibility = Visibility.Visible; Refresh(); };
        _cropToggle.Unchecked += (_, _) => { _cropCanvas.Visibility = Visibility.Collapsed; Refresh(); };
        panel.Children.Add(SliderRow("cl", "Gauche", 0, 95, v => Fx.CropLeft = v / 100, () => Fx.CropLeft * 100, "%"));
        panel.Children.Add(SliderRow("ct", "Haut", 0, 95, v => Fx.CropTop = v / 100, () => Fx.CropTop * 100, "%"));
        panel.Children.Add(SliderRow("cr", "Droite", 0, 95, v => Fx.CropRight = v / 100, () => Fx.CropRight * 100, "%"));
        panel.Children.Add(SliderRow("cb", "Bas", 0, 95, v => Fx.CropBottom = v / 100, () => Fx.CropBottom * 100, "%"));
        panel.Children.Add(LinkButton("Annuler le recadrage", () => { Fx.CropLeft = Fx.CropTop = Fx.CropRight = Fx.CropBottom = 0; }));
        _cropCanvas.MouseLeftButtonDown += CropDown;
        _cropCanvas.MouseMove += CropMove;
        _cropCanvas.MouseLeftButtonUp += (_, _) => { _cropStart = null; _cropCanvas.ReleaseMouseCapture(); };

        // --- couleur
        panel.Children.Add(Section("COULEUR"));
        var colors = new WrapPanel();
        foreach (var (id, label) in new (string?, string)[] { (null, "Origine"), ("gray", "Gris"), ("bw", "Noir et blanc"), ("sepia", "Sépia"), ("washout", "Filigrane") })
        {
            var rb = new RadioButton { Content = label, GroupName = "recolor", Margin = new Thickness(0, 2, 12, 2), Tag = id };
            rb.Checked += (_, _) => { if (_loading) return; Fx.Recolor = (string?)rb.Tag; Refresh(); };
            _colorButtons[id ?? ""] = rb;
            colors.Children.Add(rb);
        }
        panel.Children.Add(colors);

        // --- reglages
        panel.Children.Add(Section("RÉGLAGES"));
        panel.Children.Add(SliderRow("br", "Luminosité", -100, 100, v => Fx.Brightness = v / 100, () => Fx.Brightness * 100, ""));
        panel.Children.Add(SliderRow("co", "Contraste", -100, 100, v => Fx.Contrast = v / 100, () => Fx.Contrast * 100, ""));
        panel.Children.Add(SliderRow("tr", "Transparence", 0, 95, v => Fx.Transparency = v / 100, () => Fx.Transparency * 100, "%"));

        // --- miroir et rotation
        panel.Children.Add(Section("MIROIR ET ROTATION"));
        var flips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        flips.Children.Add(_flipH); flips.Children.Add(_flipV);
        _flipH.Click += (_, _) => { Fx.FlipH = _flipH.IsChecked == true; Refresh(); };
        _flipV.Click += (_, _) => { Fx.FlipV = _flipV.IsChecked == true; Refresh(); };
        panel.Children.Add(flips);
        var rot = new StackPanel { Orientation = Orientation.Horizontal };
        rot.Children.Add(SmallButton("⟲ 90°", () => _work.Rotation = Controls.Geo.Normalize(_work.Rotation - 90)));
        rot.Children.Add(SmallButton("⟳ 90°", () => _work.Rotation = Controls.Geo.Normalize(_work.Rotation + 90)));
        rot.Children.Add(SmallButton("0°", () => _work.Rotation = 0));
        rot.Children.Add(_rotText);
        panel.Children.Add(rot);

        // --- forme
        panel.Children.Add(Section("FORME DE LA PHOTO"));
        _shapeBox.Items.Add(new ComboBoxItem { Content = "Rectangle (aucune découpe)", Tag = "" });
        if (image.Clip is { Count: > 6 }) _shapeBox.Items.Add(new ComboBoxItem { Content = "Détourage d'origine (Publisher)", Tag = "clip" });
        foreach (var (id, label) in ShapeGeometries.Catalog.Where(c => c.Id != ShapeGeometry.Rect))
            _shapeBox.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        _shapeBox.SelectionChanged += (_, _) =>
        {
            if (_loading || _shapeBox.SelectedItem is not ComboBoxItem it) return;
            var tag = (string)it.Tag;
            _work.Geometry = tag is "" or "clip" ? null : tag;
            _work.Clip = tag == "clip" ? _original.Clip : null;
            Refresh();
        };
        panel.Children.Add(_shapeBox);
        panel.Children.Add(SliderRow("rad", "Arrondi des coins", 0, 50, v => _work.CornerRadius = Math.Min(_work.W, _work.H) * v / 100,
            () => _work.CornerRadius is { } r ? r / Math.Max(1, Math.Min(_work.W, _work.H)) * 100 : 15, "%"));

        // --- ombre
        panel.Children.Add(Section("OMBRE"));
        panel.Children.Add(_shadowOn);
        _shadowOn.Click += (_, _) => { _work.Shadow = _shadowOn.IsChecked == true ? new ShadowFx() : null; Load(); Refresh(); };
        panel.Children.Add(SliderRow("sd", "Distance", 0, 20, v => { if (_work.Shadow is { } s) s.Distance = v; }, () => _work.Shadow?.Distance ?? 4, " pt"));
        panel.Children.Add(SliderRow("sb", "Flou", 0, 20, v => { if (_work.Shadow is { } s) s.Blur = v; }, () => _work.Shadow?.Blur ?? 5, " pt"));
        panel.Children.Add(SliderRow("so", "Opacité", 0, 100, v => { if (_work.Shadow is { } s) s.Opacity = v / 100; }, () => (_work.Shadow?.Opacity ?? 0.35) * 100, "%"));
        panel.Children.Add(SliderRow("sa", "Direction", 0, 359, v => { if (_work.Shadow is { } s) s.Angle = v; }, () => _work.Shadow?.Angle ?? 45, "°"));

        // --- reflet
        panel.Children.Add(Section("REFLET"));
        panel.Children.Add(_reflOn);
        _reflOn.Click += (_, _) => { Fx.ReflectionSize = _reflOn.IsChecked == true ? 0.35 : 0; Load(); Refresh(); };
        panel.Children.Add(SliderRow("rs", "Taille", 5, 100, v => { if (Fx.ReflectionSize > 0) Fx.ReflectionSize = v / 100; }, () => Fx.ReflectionSize > 0 ? Fx.ReflectionSize * 100 : 35, "%"));
        panel.Children.Add(SliderRow("ro", "Opacité", 5, 100, v => Fx.ReflectionOpacity = v / 100, () => Fx.ReflectionOpacity * 100, "%"));
        panel.Children.Add(SliderRow("rg", "Écart", 0, 20, v => Fx.ReflectionGap = v, () => Fx.ReflectionGap, " pt"));

        // --- boutons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var help = new Button { Content = "Aide", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Aide sur l'éditeur d'images (F1)" };
        help.Click += (_, _) => Help.HelpService.Open(_cropToggle.IsChecked == true ? Help.HelpService.ImageCrop : Help.HelpService.ImageEditor);
        buttons.Children.Add(help);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F1) return;
            Help.HelpService.Open(_cropToggle.IsChecked == true ? Help.HelpService.ImageCrop : Help.HelpService.ImageEditor);
            e.Handled = true;
        };
        var reset = new Button { Content = "Tout réinitialiser", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) =>
        {
            _work.ImageFx = null; _work.Shadow = null; _work.Geometry = null; _work.CornerRadius = null;
            _work.Clip = _original.Clip; _work.Rotation = 0;
            Load(); Refresh();
        };
        var cancel = new Button { Content = "Annuler", Width = 90, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Appliquer", Width = 90, IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(reset); buttons.Children.Add(cancel); buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        Loaded += (_, _) => { Load(); Refresh(); };
        SizeChanged += (_, _) => UpdateCropOverlay();
    }

    // ---------------------------------------------------------------- controles

    static TextBlock Section(string t) => new()
    {
        Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("Muted"),
        Margin = new Thickness(0, 14, 0, 6),
    };

    Button SmallButton(string text, Action act)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => { act(); Load(); Refresh(); };
        return b;
    }

    Button LinkButton(string text, Action act)
    {
        var b = new Button { Content = text, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = (Brush)Application.Current.FindResource("Accent"), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        b.Click += (_, _) => { act(); Load(); Refresh(); };
        return b;
    }

    readonly Dictionary<string, (Func<double> Get, TextBlock Value, string Unit)> _readers = new();

    FrameworkElement SliderRow(string id, string label, double min, double max, Action<double> set, Func<double> get, string unit)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var s = new Slider { Minimum = min, Maximum = max, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        var v = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Foreground = Brushes.DimGray };
        Grid.SetColumn(s, 1); Grid.SetColumn(v, 2);
        g.Children.Add(t); g.Children.Add(s); g.Children.Add(v);
        s.ValueChanged += (_, e) =>
        {
            v.Text = $"{e.NewValue:0}{unit}";
            if (_loading) return;
            set(e.NewValue);
            Refresh();
        };
        _sliders[id] = s;
        _readers[id] = (get, v, unit);
        return g;
    }

    /// <summary>Recopie l'etat de la copie de travail dans les controles.</summary>
    void Load()
    {
        _loading = true;
        foreach (var (id, s) in _sliders) s.Value = _readers[id].Get();
        foreach (var (id, s) in _sliders) _readers[id].Value.Text = $"{s.Value:0}{_readers[id].Unit}";
        var key = _work.ImageFx?.Recolor ?? "";
        if (_colorButtons.TryGetValue(key, out var rb)) rb.IsChecked = true;
        _flipH.IsChecked = _work.ImageFx?.FlipH == true;
        _flipV.IsChecked = _work.ImageFx?.FlipV == true;
        _shadowOn.IsChecked = _work.Shadow is not null;
        _reflOn.IsChecked = (_work.ImageFx?.ReflectionSize ?? 0) > 0;
        var tag = _work.Geometry ?? (_work.Clip is { Count: > 6 } ? "clip" : "");
        _shapeBox.SelectedItem = _shapeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? _shapeBox.Items[0];
        _rotText.Text = $"Rotation : {_work.Rotation:0}°";
        _loading = false;
    }

    // ---------------------------------------------------------------- apercu

    /// <summary>Taille d'affichage de la copie de travail apres recadrage (les proportions suivent la part visible).</summary>
    (double W, double H) WorkSize()
    {
        double fw = 1 - Fx.CropLeft - Fx.CropRight, fh = 1 - Fx.CropTop - Fx.CropBottom;
        return (_original.W * fw / Math.Max(0.01, _origFw), _original.H * fh / Math.Max(0.01, _origFh));
    }

    void Refresh()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            if (_cropToggle.IsChecked == true)
            {
                // recadrage : image entiere (sans recadrage ni effets de bord), le cadre conserve est superpose
                var full = Fx.Clone();
                full.CropLeft = full.CropTop = full.CropRight = full.CropBottom = 0;
                var bmp = _session.Images.Get(_work.Src, full);
                if (bmp is not null) dc.DrawImage(bmp, new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight));
            }
            else
            {
                var (w, h) = WorkSize();
                var temp = System.Text.Json.JsonSerializer.Deserialize<Shape>(System.Text.Json.JsonSerializer.Serialize(_work, DocumentJson.Options), DocumentJson.Options)!;
                Controls.PageView.MoveShape(temp, new Rect(0, 0, w, h));
                temp.CornerRadius = _work.CornerRadius;
                bool rot = PageRenderer.PushRotation(dc, temp);
                PageRenderer.DrawImage(dc, temp, _session);
                if (rot) dc.Pop();
            }
        }
        group.Freeze();
        _preview.Source = new DrawingImage(group);
        _rotText.Text = $"Rotation : {_work.Rotation:0}°";
        Dispatcher.BeginInvoke(UpdateCropOverlay, System.Windows.Threading.DispatcherPriority.Render);
    }

    // ---------------------------------------------------------------- recadrage a la souris

    /// <summary>Rectangle affiche de l'image dans la zone d'apercu (pixels de l'ecran).</summary>
    Rect ImageArea()
    {
        if (_preview.Source is null || _preview.ActualWidth <= 0) return Rect.Empty;
        var origin = _preview.TranslatePoint(new Point(0, 0), _cropCanvas);
        return new Rect(origin, new Size(_preview.ActualWidth, _preview.ActualHeight));
    }

    void UpdateCropOverlay()
    {
        if (_cropToggle.IsChecked != true) return;
        var a = ImageArea();
        if (a.IsEmpty) return;
        var r = new Rect(a.X + Fx.CropLeft * a.Width, a.Y + Fx.CropTop * a.Height,
            Math.Max(1, (1 - Fx.CropLeft - Fx.CropRight) * a.Width), Math.Max(1, (1 - Fx.CropTop - Fx.CropBottom) * a.Height));
        Canvas.SetLeft(_cropRect, r.X); Canvas.SetTop(_cropRect, r.Y);
        _cropRect.Width = r.Width; _cropRect.Height = r.Height;
        _cropShade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(a), new RectangleGeometry(r));
    }

    void CropDown(object sender, MouseButtonEventArgs e)
    {
        _cropStart = e.GetPosition(_cropCanvas);
        _cropCanvas.CaptureMouse();
    }

    void CropMove(object sender, MouseEventArgs e)
    {
        if (_cropStart is not { } s0 || e.LeftButton != MouseButtonState.Pressed) return;
        var a = ImageArea();
        if (a.IsEmpty) return;
        var p = e.GetPosition(_cropCanvas);
        double x0 = Math.Clamp((Math.Min(s0.X, p.X) - a.X) / a.Width, 0, 1), x1 = Math.Clamp((Math.Max(s0.X, p.X) - a.X) / a.Width, 0, 1);
        double y0 = Math.Clamp((Math.Min(s0.Y, p.Y) - a.Y) / a.Height, 0, 1), y1 = Math.Clamp((Math.Max(s0.Y, p.Y) - a.Y) / a.Height, 0, 1);
        if (x1 - x0 < 0.03 || y1 - y0 < 0.03) return;
        Fx.CropLeft = x0; Fx.CropRight = 1 - x1; Fx.CropTop = y0; Fx.CropBottom = 1 - y1;
        Load();
        UpdateCropOverlay();
    }

    public void StartCrop() => _cropToggle.IsChecked = true;

    // ---------------------------------------------------------------- application

    /// <summary>Reporte les reglages sur l'objet d'origine (a appeler entre BeginChange et EndChange).</summary>
    public void Apply()
    {
        var oldFx = _original.ImageFx;
        double ocl = oldFx?.CropLeft ?? 0, oct = oldFx?.CropTop ?? 0;
        // le contenu reste en place sur la page : le cadre suit le recadrage
        double fullW = _original.W / Math.Max(0.01, _origFw), fullH = _original.H / Math.Max(0.01, _origFh);
        var (w, h) = WorkSize();
        double x = _original.X + (Fx.CropLeft - ocl) * fullW;
        double y = _original.Y + (Fx.CropTop - oct) * fullH;
        var clip = _work.Clip;
        _original.Clip = null;           // le detourage est recalcule par MoveShape
        Controls.PageView.MoveShape(_original, new Rect(x, y, w, h));
        if (clip is not null && _work.Geometry is null)
        {
            // detourage d'origine conserve : on le remet a l'echelle du nouveau cadre
            var tmp = new Shape { X = _work.X, Y = _work.Y, W = _work.W, H = _work.H, Clip = clip };
            Controls.PageView.MoveShape(tmp, new Rect(x, y, w, h));
            _original.Clip = tmp.Clip;
        }
        _original.ImageFx = Fx.IsEmpty ? null : Fx;
        _original.Shadow = _work.Shadow;
        _original.Geometry = _work.Geometry;
        _original.CornerRadius = _work.CornerRadius;
        _original.Rotation = _work.Rotation;
    }
}
