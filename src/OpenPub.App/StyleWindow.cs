using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>Creer ou modifier un style de texte : nom, police, taille, gras, italique, souligne, couleur, alignement, espacements.</summary>
public sealed class StyleWindow : Window
{
    static readonly CultureInfo Cu = CultureInfo.CurrentCulture;
    readonly TextBox _name = new() { Width = 240 };
    readonly ComboBox _kind = new() { Width = 240 };
    readonly ComboBox _family = new() { Width = 240, IsEditable = true };
    readonly TextBox _size = new() { Width = 60 }, _before = new() { Width = 60 }, _after = new() { Width = 60 }, _line = new() { Width = 60 };
    readonly CheckBox _bold = new() { Content = "Gras", Margin = new Thickness(0, 0, 10, 0) }, _italic = new() { Content = "Italique", Margin = new Thickness(0, 0, 10, 0) }, _underline = new() { Content = "Souligné" };
    readonly ComboBox _align = new() { Width = 140 };
    readonly Border _swatch = new() { Width = 40, Height = 20, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) };
    readonly StackPanel _paraPart = new();
    string _color;
    readonly Func<string?, string?> _pickColor;

    public TextStyle Result { get; }

    public StyleWindow(TextStyle style, bool isNew, IEnumerable<string> families, Func<string?, string?> pickColor)
    {
        Result = style;
        _pickColor = pickColor;
        _color = style.Font?.Color ?? "#000000";
        Title = isNew ? "Nouveau style" : $"Modifier le style « {style.Name} »";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var root = new StackPanel { Margin = new Thickness(18), Width = 400 };
        root.Children.Add(Row("Nom", _name));
        _kind.Items.Add(new ComboBoxItem { Content = "Paragraphe (police et réglages du paragraphe)", Tag = "paragraph" });
        _kind.Items.Add(new ComboBoxItem { Content = "Caractère (police seulement, sur une sélection)", Tag = "character" });
        _kind.SelectedIndex = style.Kind == "character" ? 1 : 0;
        _kind.IsEnabled = isNew;
        root.Children.Add(Row("Type", _kind));
        foreach (var f in families) _family.Items.Add(f);
        _family.Text = style.Font?.Family ?? "";
        root.Children.Add(Row("Police", _family));
        _size.Text = style.Font?.Size?.ToString("0.#", Cu) ?? "";
        root.Children.Add(Row("Taille (pt)", _size));
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal };
        styleRow.Children.Add(_bold); styleRow.Children.Add(_italic); styleRow.Children.Add(_underline);
        _bold.IsChecked = style.Font?.Bold; _italic.IsChecked = style.Font?.Italic; _underline.IsChecked = style.Font?.Underline;
        root.Children.Add(Row("Style", styleRow));
        var colorBtn = new Button { Content = _swatch, Padding = new Thickness(4) };
        _swatch.Background = Rendering.PageRenderer.Brush(_color);
        colorBtn.Click += (_, _) => { if (_pickColor(_color) is { } c) { _color = c; _swatch.Background = Rendering.PageRenderer.Brush(c); } };
        root.Children.Add(Row("Couleur", colorBtn));
        foreach (var (l, t) in new[] { ("(inchangé)", ""), ("Gauche", "left"), ("Centré", "center"), ("Droite", "right"), ("Justifié", "justify") })
            _align.Items.Add(new ComboBoxItem { Content = l, Tag = t });
        _align.SelectedItem = _align.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (style.Align ?? "")) ?? _align.Items[0];
        _paraPart.Children.Add(Row("Alignement", _align));
        _before.Text = (style.SpaceBefore ?? 0).ToString("0.#", Cu);
        _after.Text = (style.SpaceAfter ?? 0).ToString("0.#", Cu);
        _line.Text = (new Paragraph { LineHeight = style.LineHeight }.LineSpacingFactor()).ToString("0.##", Cu);
        _paraPart.Children.Add(Row("Espace avant (pt)", _before));
        _paraPart.Children.Add(Row("Espace après (pt)", _after));
        _paraPart.Children.Add(Row("Interligne", _line));
        root.Children.Add(_paraPart);
        _kind.SelectionChanged += (_, _) => _paraPart.Visibility = Kind == "paragraph" ? Visibility.Visible : Visibility.Collapsed;
        _paraPart.Visibility = Kind == "paragraph" ? Visibility.Visible : Visibility.Collapsed;
        _name.Text = style.Name;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Style = (Style)Application.Current.FindResource("PillButton") };
        ok.Click += (_, _) => { if (Build()) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    string Kind => (string)((ComboBoxItem)_kind.SelectedItem).Tag;

    static double? Num(string text)
    {
        var sep = Cu.NumberFormat.NumberDecimalSeparator;
        if (text.Trim().Length == 0) return null;
        return double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, Cu, out var v) ? v : double.NaN;
    }

    bool Build()
    {
        var size = Num(_size.Text); var before = Num(_before.Text); var after = Num(_after.Text); var line = Num(_line.Text);
        if (_name.Text.Trim().Length == 0 || size is double.NaN || size is < 1 or > 500 || before is double.NaN || after is double.NaN || line is double.NaN or <= 0 or > 10)
        {
            MessageBox.Show(this, "Donnez un nom au style et vérifiez les valeurs numériques.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var s = Result;
        s.Name = _name.Text.Trim();
        s.Kind = Kind;
        s.Font ??= new FontSpec();
        s.Font.Family = _family.Text.Trim().Length > 0 ? _family.Text.Trim() : null;
        s.Font.Size = size;
        s.Font.Bold = _bold.IsChecked == true; s.Font.Italic = _italic.IsChecked == true; s.Font.Underline = _underline.IsChecked == true;
        s.Font.Color = _color;
        if (s.Kind == "paragraph")
        {
            var a = (string)((ComboBoxItem)_align.SelectedItem).Tag;
            s.Align = a.Length == 0 ? null : a;
            s.SpaceBefore = before is > 0 ? before : null;
            s.SpaceAfter = after is > 0 ? after : null;
            s.LineHeight = line is { } l && Math.Abs(l - 1) > 0.001 ? (l * 100).ToString("0.####", CultureInfo.InvariantCulture) + "%" : null;
        }
        return true;
    }

    static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(control);
        return g;
    }
}
