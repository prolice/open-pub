using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>Boite « WordArt » : texte, police, gras / italique, deformation, avec apercu dans le style de l'objet.</summary>
public sealed class WordArtWindow : Window
{
    readonly Shape _model;
    readonly EditorSession _session;
    readonly TextBox _text = new() { Width = 340, Height = 54, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
    readonly ComboBox _family = new() { Width = 220, IsEditable = true };
    readonly CheckBox _bold = new() { Content = "Gras", Margin = new Thickness(0, 0, 12, 0) }, _italic = new() { Content = "Italique" };
    readonly ComboBox _warp = FillWindow.Combo(WordArtWarps.All.Select(w => (w.Label, w.Id)).ToArray());
    readonly Image _preview = new() { Width = 340, Height = 120, Stretch = Stretch.Uniform };

    /// <summary>Texte decoratif choisi (null si annule).</summary>
    public WordArtFx? Result { get; private set; }

    public WordArtWindow(Shape model, EditorSession session)
    {
        _model = model;
        _session = session;
        var wa = model.WordArt ?? new WordArtFx();
        Title = "Texte WordArt";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        // polices du document d'abord, puis celles du systeme
        var families = FontEmbedder.DocumentFamilies(session.Doc).Concat(new[] { "Arial Black", "Impact", "Georgia", "Segoe Script", "Comic Sans MS" })
            .Concat(Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var f in families) _family.Items.Add(f);
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(FillWindow.Row("Texte", _text));
        root.Children.Add(FillWindow.Row("Police", _family));
        var style = new StackPanel { Orientation = Orientation.Horizontal };
        style.Children.Add(_bold); style.Children.Add(_italic);
        root.Children.Add(FillWindow.Row("Style", style));
        root.Children.Add(FillWindow.Row("Déformation", _warp));
        root.Children.Add(FillWindow.Section("Aperçu"));
        root.Children.Add(new Border { Child = _preview, Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left });
        root.Children.Add(new TextBlock
        {
            Text = "Remplissage, contour, ombre et effets : onglet Format de la forme. Le texte remplit le cadre : redimensionnez-le pour l'étirer.",
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 500, Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("PillButton"), Padding = new Thickness(22, 5, 22, 5), IsDefault = true };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        _text.Text = wa.Text;
        _family.Text = wa.Family;
        _bold.IsChecked = wa.Bold;
        _italic.IsChecked = wa.Italic;
        FillWindow.Select(_warp, wa.Warp);
        _text.TextChanged += (_, _) => Preview();
        _family.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Preview);
        _family.LostFocus += (_, _) => Preview();
        _bold.Click += (_, _) => Preview();
        _italic.Click += (_, _) => Preview();
        _warp.SelectionChanged += (_, _) => Preview();
        ok.Click += (_, _) => { Result = Build(); DialogResult = true; };
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); Preview(); };
    }

    WordArtFx Build() => new()
    {
        Text = _text.Text.Trim().Length == 0 ? "Texte" : _text.Text.Replace("\r", "").Replace('\n', ' '),
        Family = string.IsNullOrWhiteSpace(_family.Text) ? "Arial Black" : _family.Text.Trim(),
        Bold = _bold.IsChecked == true, Italic = _italic.IsChecked == true,
        Warp = _warp.SelectedItem is ComboBoxItem it ? (string)it.Tag : WordArtWarps.None,
    };

    void Preview()
    {
        var wa = Build();
        (double w, double h) = wa.Warp switch { WordArtWarps.Circle => (150.0, 150.0), WordArtWarps.ArcUp or WordArtWarps.ArcDown => (300, 140), _ => (300, 90) };
        var tmp = new Shape { Kind = ShapeKind.WordArt, W = w, H = h, WordArt = wa, Fill = _model.Fill, Line = _model.Line };
        var g = DecorRenderer.WordArtGeometry(tmp, _session);
        if (g is null) { _preview.Source = null; return; }
        var brush = DecorRenderer.FillBrush(_model.Fill, _session.Images);
        var pen = DecorRenderer.PenOf(_model.Line);
        if (brush is null && pen is null) brush = Brushes.Black;
        var drawing = new GeometryDrawing(brush, pen, g);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, w, h))));
        group.Children.Add(drawing);
        _preview.Source = new DrawingImage(group);
    }
}
