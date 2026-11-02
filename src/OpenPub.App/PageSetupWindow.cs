using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenPub.App;

/// <summary>
/// Boite « Mise en page » (ou « Nouveau document ») : format et orientation des pages, marges, grille de disposition,
/// adaptation des objets a la nouvelle taille, nombre de pages d'un nouveau document. Unites : centimetres a l'ecran,
/// points dans le resultat.
/// </summary>
public sealed class PageSetupWindow : Window
{
    const double CmPerPt = 2.54 / 72;
    static readonly CultureInfo Cu = CultureInfo.CurrentCulture;

    /// <summary>Formats courants (largeur x hauteur en points, portrait).</summary>
    public static readonly (string Name, double W, double H)[] Sizes =
    {
        ("A4 (21 × 29,7 cm)", 595.276, 841.89), ("A5 (14,8 × 21 cm)", 419.528, 595.276), ("A3 (29,7 × 42 cm)", 841.89, 1190.551),
        ("A6 / carte postale (10,5 × 14,8 cm)", 297.638, 419.528), ("Lettre US (21,6 × 27,9 cm)", 612, 792), ("Légal US (21,6 × 35,6 cm)", 612, 1008),
        ("Carte de visite (8,5 × 5,5 cm)", 240.945, 155.906), ("Affiche A2 (42 × 59,4 cm)", 1190.551, 1683.78),
    };

    readonly ComboBox _size = new() { Width = 260 };
    readonly TextBox _w = Box(), _h = Box(), _mTop = Box(), _mRight = Box(), _mBottom = Box(), _mLeft = Box();
    readonly RadioButton _portrait = new() { Content = "Portrait", Margin = new Thickness(0, 0, 14, 0) }, _landscape = new() { Content = "Paysage" };
    readonly TextBox _cols = Box(), _rows = Box(), _gutter = Box(), _pages = Box();
    readonly CheckBox _scale = new() { Content = "Adapter les objets à la nouvelle taille", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    bool _sync;

    public double PageWidth { get; private set; }
    public double PageHeight { get; private set; }
    public double[] Margins { get; private set; } = { 36, 36, 36, 36 };
    public int GridColumns { get; private set; } = 1;
    public int GridRows { get; private set; } = 1;
    public double GridGutter { get; private set; } = 14.17;
    public bool ScaleObjects => _scale.IsChecked == true;
    public int PageCount { get; private set; } = 1;

    public PageSetupWindow(bool newDocument, double width, double height, double[]? margins, int cols = 1, int rows = 1, double gutter = 14.17)
    {
        Title = newDocument ? "Nouveau document" : "Mise en page";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var root = new StackPanel { Margin = new Thickness(18), Width = 440 };
        root.Children.Add(Section("Format de page"));
        foreach (var (name, _, _) in Sizes) _size.Items.Add(name);
        _size.Items.Add("Personnalisé");
        root.Children.Add(Row("Format", _size));
        var wh = new StackPanel { Orientation = Orientation.Horizontal };
        wh.Children.Add(_w); wh.Children.Add(new TextBlock { Text = "  ×  ", VerticalAlignment = VerticalAlignment.Center }); wh.Children.Add(_h);
        wh.Children.Add(new TextBlock { Text = "  cm", VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(Row("Largeur × hauteur", wh));
        var orient = new StackPanel { Orientation = Orientation.Horizontal };
        orient.Children.Add(_portrait); orient.Children.Add(_landscape);
        root.Children.Add(Row("Orientation", orient));
        if (newDocument) root.Children.Add(Row("Nombre de pages", _pages));
        else root.Children.Add(_scale);

        root.Children.Add(Section("Marges (cm)"));
        var m1 = new StackPanel { Orientation = Orientation.Horizontal };
        m1.Children.Add(Small("Haut", _mTop)); m1.Children.Add(Small("Bas", _mBottom)); m1.Children.Add(Small("Gauche", _mLeft)); m1.Children.Add(Small("Droite", _mRight));
        root.Children.Add(m1);

        root.Children.Add(Section("Grille de disposition"));
        var g = new StackPanel { Orientation = Orientation.Horizontal };
        g.Children.Add(Small("Colonnes", _cols)); g.Children.Add(Small("Lignes", _rows)); g.Children.Add(Small("Espacement (cm)", _gutter));
        root.Children.Add(g);
        root.Children.Add(new TextBlock
        {
            Text = "La grille (pointillés bleus) aide à aligner les zones ; le magnétisme s'y accroche.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = newDocument ? "Créer" : "OK", IsDefault = true, MinWidth = 90, Style = (Style)Application.Current.FindResource("PillButton") };
        ok.Click += (_, _) => { if (Build()) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;

        // valeurs initiales
        _sync = true;
        _w.Text = Cm(width); _h.Text = Cm(height);
        var m = margins is { Length: 4 } ? margins : new[] { 36.0, 36.0, 36.0, 36.0 };
        _mTop.Text = Cm(m[0]); _mRight.Text = Cm(m[1]); _mBottom.Text = Cm(m[2]); _mLeft.Text = Cm(m[3]);
        _cols.Text = cols.ToString(Cu); _rows.Text = rows.ToString(Cu); _gutter.Text = Cm(gutter); _pages.Text = "1";
        (width > height ? _landscape : _portrait).IsChecked = true;
        SelectSize(width, height);
        _sync = false;

        _size.SelectionChanged += (_, _) =>
        {
            if (_sync || _size.SelectedIndex < 0 || _size.SelectedIndex >= Sizes.Length) return;
            var (_, sw, sh) = Sizes[_size.SelectedIndex];
            bool land = _landscape.IsChecked == true;
            _sync = true; _w.Text = Cm(land ? sh : sw); _h.Text = Cm(land ? sw : sh); _sync = false;
        };
        void Orient(bool land)
        {
            if (_sync || Parse(_w.Text) is not { } w0 || Parse(_h.Text) is not { } h0) return;
            if ((w0 > h0) != land) { _sync = true; _w.Text = Cm(h0 / CmPerPt); _h.Text = Cm(w0 / CmPerPt); _sync = false; }
        }
        _portrait.Checked += (_, _) => Orient(false);
        _landscape.Checked += (_, _) => Orient(true);
        _w.TextChanged += (_, _) => OnDims();
        _h.TextChanged += (_, _) => OnDims();
    }

    void OnDims()
    {
        if (_sync || Parse(_w.Text) is not { } w || Parse(_h.Text) is not { } h) return;
        _sync = true;
        SelectSize(w / CmPerPt, h / CmPerPt);
        (w > h ? _landscape : _portrait).IsChecked = true;
        _sync = false;
    }

    void SelectSize(double w, double h)
    {
        int i = Array.FindIndex(Sizes, z => (Math.Abs(z.W - w) < 1 && Math.Abs(z.H - h) < 1) || (Math.Abs(z.W - h) < 1 && Math.Abs(z.H - w) < 1));
        _size.SelectedIndex = i >= 0 ? i : Sizes.Length;
    }

    bool Build()
    {
        if (Parse(_w.Text) is not { } w || Parse(_h.Text) is not { } h || w < 2 || h < 2 || w > 200 || h > 200
            || Parse(_mTop.Text) is not { } mt || Parse(_mRight.Text) is not { } mr || Parse(_mBottom.Text) is not { } mb || Parse(_mLeft.Text) is not { } ml
            || Parse(_gutter.Text) is not { } gu || !int.TryParse(_cols.Text, NumberStyles.Integer, Cu, out var c) || !int.TryParse(_rows.Text, NumberStyles.Integer, Cu, out var r)
            || !int.TryParse(_pages.Text, NumberStyles.Integer, Cu, out var n) || c < 1 || c > 24 || r < 1 || r > 24 || n < 1 || n > 200
            || mt < 0 || mr < 0 || mb < 0 || ml < 0 || ml + mr >= w || mt + mb >= h)
        {
            MessageBox.Show(this, "Vérifiez les valeurs : dimensions de 2 à 200 cm, marges plus petites que la page, 1 à 24 colonnes ou lignes, 1 à 200 pages.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        PageWidth = w / CmPerPt; PageHeight = h / CmPerPt;
        Margins = new[] { mt / CmPerPt, mr / CmPerPt, mb / CmPerPt, ml / CmPerPt };
        GridColumns = c; GridRows = r; GridGutter = gu / CmPerPt; PageCount = n;
        return true;
    }

    static double? Parse(string text)
    {
        var sep = Cu.NumberFormat.NumberDecimalSeparator;
        return double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, Cu, out var v) ? v : null;
    }

    static string Cm(double pt) => (pt * CmPerPt).ToString("0.##", Cu);
    static TextBox Box() => new() { Width = 64, VerticalContentAlignment = VerticalAlignment.Center };

    static TextBlock Section(string text) => new()
    {
        Text = text, FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 12, 0, 6),
        Foreground = (Brush)Application.Current.FindResource("SelectedText"),
    };

    static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(control);
        return g;
    }

    static StackPanel Small(string label, Control box)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = (Brush)Application.Current.FindResource("Muted") });
        sp.Children.Add(box);
        return sp;
    }
}
