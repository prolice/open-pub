using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>
/// Boite « Remplissage » : aucun, couleur unie, degrade (lineaire ou radial, editeur d'arrets), motif, image ;
/// transparence du remplissage et du contour. Apercu en direct.
/// </summary>
public sealed class FillWindow : Window
{
    static readonly CultureInfo Cu = CultureInfo.CurrentCulture;
    readonly ImageCache _images;
    readonly Func<string?, string, (bool Picked, string? Color)> _pickColor;
    readonly Func<string?> _pickImage;
    Fill _f;

    readonly ComboBox _kind = Combo(("Aucun remplissage", "none"), ("Couleur unie", "solid"), ("Dégradé", "gradient"), ("Motif", "pattern"), ("Image", "image"));
    readonly StackPanel _solidPanel = new(), _gradPanel = new(), _patPanel = new(), _imgPanel = new();
    readonly Button _color = ColorButton(), _patFg = ColorButton(), _patBg = ColorButton(), _stopColor = ColorButton();
    readonly ComboBox _gradType = Combo(("Linéaire", "linear"), ("Radial", "radial"));
    readonly TextBox _angle = new() { Width = 60 }, _stopPos = new() { Width = 60 };
    readonly ListBox _stops = new() { Height = 92, Width = 220 };
    readonly Slider _stopOpacity = new() { Minimum = 0, Maximum = 100, Width = 150, IsMoveToPointEnabled = true };
    readonly ComboBox _pattern = new() { Width = 220 };
    readonly TextBlock _imgName = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _tile = new() { Content = "Répéter en mosaïque (sinon l'image couvre la forme)", Margin = new Thickness(0, 6, 0, 0) };
    readonly Slider _transp = new() { Minimum = 0, Maximum = 100, Width = 200, IsMoveToPointEnabled = true };
    readonly Slider _lineTransp = new() { Minimum = 0, Maximum = 100, Width = 200, IsMoveToPointEnabled = true };
    readonly System.Windows.Shapes.Rectangle _preview = new() { Width = 220, Height = 90, Stroke = Brushes.Gray, StrokeThickness = 1 };
    bool _updating;

    /// <summary>Remplissage choisi (null = aucun) et transparence du contour (opacite, null = opaque).</summary>
    public Fill? ResultFill { get; private set; }
    public double? ResultLineOpacity { get; private set; }

    public FillWindow(Fill? current, double? lineOpacity, ImageCache images, Func<string?, string, (bool, string?)> pickColor, Func<string?> pickImage, string? page = null)
    {
        _images = images;
        _pickColor = pickColor;
        _pickImage = pickImage;
        _f = current?.Clone() ?? new Fill { Color = "#5b9bd5" };
        Title = "Remplissage";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var root = new StackPanel { Margin = new Thickness(18), Width = 530 };
        root.Children.Add(Row("Type", _kind));

        _solidPanel.Children.Add(Row("Couleur", _color));

        _gradPanel.Children.Add(Row("Type de dégradé", _gradType));
        _gradPanel.Children.Add(Row("Direction (°)", _angle));
        var stopRow = new StackPanel { Orientation = Orientation.Horizontal };
        stopRow.Children.Add(_stops);
        var stopButtons = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var add = new Button { Content = "Ajouter un arrêt", Padding = new Thickness(10, 3, 10, 3) };
        var del = new Button { Content = "Supprimer", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0) };
        stopButtons.Children.Add(add); stopButtons.Children.Add(del);
        stopRow.Children.Add(stopButtons);
        _gradPanel.Children.Add(Row("Arrêts", stopRow));
        _gradPanel.Children.Add(Row("Couleur de l'arrêt", _stopColor));
        _gradPanel.Children.Add(Row("Position (%)", _stopPos));
        _gradPanel.Children.Add(Row("Opacité de l'arrêt", _stopOpacity));

        foreach (var (id, label) in FillPatterns.All) _pattern.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        _patPanel.Children.Add(Row("Motif", _pattern));
        _patPanel.Children.Add(Row("Couleur du motif", _patFg));
        _patPanel.Children.Add(Row("Couleur du fond", _patBg));

        var imgRow = new StackPanel { Orientation = Orientation.Horizontal };
        var pick = new Button { Content = "Choisir une image…", Padding = new Thickness(10, 3, 10, 3) };
        imgRow.Children.Add(pick); imgRow.Children.Add(_imgName);
        _imgPanel.Children.Add(Row("Image", imgRow));
        _imgPanel.Children.Add(_tile);

        foreach (var p in new[] { _solidPanel, _gradPanel, _patPanel, _imgPanel }) root.Children.Add(p);
        root.Children.Add(Section("Transparence"));
        root.Children.Add(Row("Remplissage (%)", _transp));
        root.Children.Add(Row("Contour (%)", _lineTransp));
        root.Children.Add(Section("Aperçu"));
        root.Children.Add(new Border
        {
            Child = _preview, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6),
            Background = new DrawingBrush(Checker()) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 12, 12), ViewportUnits = BrushMappingMode.Absolute },
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("PillButton"), Padding = new Thickness(22, 5, 22, 5), IsDefault = true };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        // etat initial
        _updating = true;
        var kind = current is null ? "none" : _f.Image is not null ? "image" : _f.HasGradient ? "gradient" : _f.Pattern is not null ? "pattern" : "solid";
        Select(_kind, page ?? kind);
        if (page == "gradient" && !_f.HasGradient) InitGradient();
        if (page == "pattern" && _f.Pattern is null) { _f.Pattern = "dots"; _f.PatternColor ??= "#1f2a44"; _f.Color ??= "#ffffff"; }
        _transp.Value = (1 - (_f.Opacity ?? 1)) * 100;
        _lineTransp.Value = (1 - (lineOpacity ?? 1)) * 100;
        _updating = false;
        Sync();

        _kind.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            var k = TagOf(_kind);
            if (k == "gradient" && !_f.HasGradient) InitGradient();
            if (k == "pattern" && _f.Pattern is null) { _f.Pattern = "dots"; _f.PatternColor ??= "#1f2a44"; }
            Sync();
        };
        _color.Click += (_, _) => PickInto(_f.Color, "Couleur de remplissage", c => _f.Color = c);
        _patFg.Click += (_, _) => PickInto(_f.PatternColor, "Couleur du motif", c => _f.PatternColor = c);
        _patBg.Click += (_, _) => PickInto(_f.Color, "Couleur du fond", c => _f.Color = c);
        _pattern.SelectionChanged += (_, _) => { if (!_updating && _pattern.SelectedItem is ComboBoxItem it) { _f.Pattern = (string)it.Tag; Sync(); } };
        _gradType.SelectionChanged += (_, _) => { if (!_updating) { _f.GradientType = TagOf(_gradType) == "radial" ? "radial" : null; Sync(); } };
        _angle.LostFocus += (_, _) => ApplyAngle();
        _angle.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyAngle(); };
        _stops.SelectionChanged += (_, _) => { if (!_updating) Sync(); };
        _stopColor.Click += (_, _) => { if (CurrentStop() is { } st) PickInto(st.Color, "Couleur de l'arrêt", c => st.Color = c); };
        _stopPos.LostFocus += (_, _) => ApplyStopPos();
        _stopPos.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyStopPos(); };
        _stopOpacity.ValueChanged += (_, _) => { if (!_updating && CurrentStop() is { } st) { st.Opacity = _stopOpacity.Value >= 99.5 ? null : _stopOpacity.Value / 100; Sync(); } };
        add.Click += (_, _) => AddStop();
        del.Click += (_, _) =>
        {
            if (_f.Stops is { Count: > 2 } && CurrentStop() is { } st) { _f.Stops.Remove(st); Sync(); }
        };
        pick.Click += (_, _) => { if (_pickImage() is { } src) { _f.Image = src; Sync(); } };
        _tile.Click += (_, _) => { _f.ImageTile = _tile.IsChecked == true ? true : null; Sync(); };
        _transp.ValueChanged += (_, _) => { if (!_updating) Sync(); };
        ok.Click += (_, _) => { Commit(); DialogResult = true; };
    }

    void InitGradient()
    {
        var c = _f.Color ?? "#5b9bd5";
        _f.Stops = new List<GradientStopFx> { new() { Pos = 0, Color = "#ffffff" }, new() { Pos = 1, Color = c } };
        _f.Angle ??= 90;
    }

    GradientStopFx? CurrentStop() => _f.Stops is { } st && _stops.SelectedIndex >= 0 && _stops.SelectedIndex < st.Count ? st.OrderBy(s => s.Pos).ElementAt(_stops.SelectedIndex) : null;

    void AddStop()
    {
        if (_f.Stops is null) InitGradient();
        var ordered = _f.Stops!.OrderBy(s => s.Pos).ToList();
        // nouvel arret au milieu du plus grand intervalle
        int best = 0;
        for (int i = 1; i < ordered.Count - 1; i++) if (ordered[i + 1].Pos - ordered[i].Pos > ordered[best + 1].Pos - ordered[best].Pos) best = i;
        var a = ordered[best]; var b = ordered[Math.Min(best + 1, ordered.Count - 1)];
        var n = new GradientStopFx { Pos = (a.Pos + b.Pos) / 2, Color = a.Color };
        _f.Stops!.Add(n);
        Sync();
        _stops.SelectedIndex = _f.Stops.OrderBy(s => s.Pos).ToList().IndexOf(n);
    }

    void ApplyAngle()
    {
        if (Parse(_angle.Text) is { } a) { _f.Angle = ((a % 360) + 360) % 360; Sync(); }
    }

    void ApplyStopPos()
    {
        if (CurrentStop() is { } st && Parse(_stopPos.Text) is { } v) { st.Pos = Math.Clamp(v / 100, 0, 1); Sync(); }
    }

    void PickInto(string? current, string title, Action<string> set)
    {
        var (ok, c) = _pickColor(current, title);
        if (ok && c is not null) { set(c); Sync(); }
    }

    /// <summary>Remplissage tel que choisi (sans les donnees des autres types).</summary>
    Fill? Build()
    {
        var k = TagOf(_kind);
        if (k == "none") return null;
        double t = _transp.Value / 100;
        var f = new Fill { Opacity = t <= 0.005 ? null : 1 - t };
        switch (k)
        {
            case "gradient":
                f.Stops = _f.Stops?.Select(s => s.Clone()).ToList();
                f.Color = _f.Stops?.OrderBy(s => s.Pos).FirstOrDefault()?.Color ?? _f.Color;
                f.GradientType = _f.GradientType;
                f.Angle = _f.IsRadial ? null : _f.Angle ?? 90;
                break;
            case "pattern":
                f.Pattern = _f.Pattern ?? "dots"; f.PatternColor = _f.PatternColor ?? "#000000"; f.Color = _f.Color ?? "#ffffff";
                break;
            case "image":
                if (_f.Image is null) return null;
                f.Image = _f.Image; f.ImageTile = _f.ImageTile; f.Color = "#ffffff";
                break;
            default:
                f.Color = _f.Color ?? "#5b9bd5";
                break;
        }
        return f;
    }

    void Commit()
    {
        ResultFill = Build();
        double lt = _lineTransp.Value / 100;
        ResultLineOpacity = lt <= 0.005 ? null : 1 - lt;
    }

    void Sync()
    {
        _updating = true;
        var k = TagOf(_kind);
        _solidPanel.Visibility = k == "solid" ? Visibility.Visible : Visibility.Collapsed;
        _gradPanel.Visibility = k == "gradient" ? Visibility.Visible : Visibility.Collapsed;
        _patPanel.Visibility = k == "pattern" ? Visibility.Visible : Visibility.Collapsed;
        _imgPanel.Visibility = k == "image" ? Visibility.Visible : Visibility.Collapsed;
        SetSwatch(_color, _f.Color);
        SetSwatch(_patFg, _f.PatternColor);
        SetSwatch(_patBg, _f.Color);
        Select(_gradType, _f.IsRadial ? "radial" : "linear");
        _angle.IsEnabled = !_f.IsRadial;
        if (!_angle.IsKeyboardFocused) _angle.Text = (_f.Angle ?? 90).ToString("0.#", Cu);
        int sel = _stops.SelectedIndex;
        _stops.Items.Clear();
        foreach (var st in _f.Stops?.OrderBy(s => s.Pos) ?? Enumerable.Empty<GradientStopFx>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(DecorRenderer.Swatch(PageRenderer.Brush(st.Color, st.Opacity ?? 1), 22, 14));
            row.Children.Add(new TextBlock { Text = $"  {st.Pos * 100:0} %  {st.Color}", VerticalAlignment = VerticalAlignment.Center });
            _stops.Items.Add(row);
        }
        _stops.SelectedIndex = Math.Clamp(sel < 0 ? 0 : sel, -1, _stops.Items.Count - 1);
        if (CurrentStop() is { } cur)
        {
            SetSwatch(_stopColor, cur.Color);
            if (!_stopPos.IsKeyboardFocused) _stopPos.Text = (cur.Pos * 100).ToString("0.#", Cu);
            _stopOpacity.Value = (cur.Opacity ?? 1) * 100;
        }
        Select(_pattern, _f.Pattern ?? "dots");
        _imgName.Text = _f.Image is null ? "(aucune)" : System.IO.Path.GetFileName(_f.Image);
        _tile.IsChecked = _f.ImageTile == true;
        _preview.Fill = DecorRenderer.FillBrush(Build(), _images) ?? Brushes.Transparent;
        _updating = false;
    }

    static DrawingGroup Checker()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 12, 12));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)), null, new Rect(0, 0, 6, 6));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xdd, 0xdd, 0xdd)), null, new Rect(6, 6, 6, 6));
        }
        return g;
    }

    static Button ColorButton() => new() { Width = 60, Height = 22, Padding = new Thickness(3) };

    static void SetSwatch(Button b, string? color) =>
        b.Content = new Border { Width = 44, Height = 12, Background = color is null ? Brushes.Transparent : PageRenderer.Brush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

    static string TagOf(ComboBox c) => c.SelectedItem is ComboBoxItem it ? (string)it.Tag : "";

    static double? Parse(string text)
    {
        var sep = Cu.NumberFormat.NumberDecimalSeparator;
        return double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, Cu, out var v) ? v : null;
    }

    internal static ComboBox Combo(params (string Label, string Tag)[] items)
    {
        var c = new ComboBox { Width = 180 };
        foreach (var (l, t) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = t });
        c.SelectedIndex = 0;
        return c;
    }

    internal static void Select(ComboBox c, string tag)
    {
        var it = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (it is not null) c.SelectedItem = it;
    }

    internal static TextBlock Section(string text) => new()
    {
        Text = text, FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 12, 0, 6),
        Foreground = (Brush)Application.Current.FindResource("SelectedText"),
    };

    internal static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(control);
        return g;
    }
}
