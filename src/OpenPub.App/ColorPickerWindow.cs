using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenPub.App.Rendering;

namespace OpenPub.App;

/// <summary>
/// Choix d'une couleur : palette facon Office (teintes et nuances), couleurs du document, saisie hexadecimale.
/// Resultat : "#rrggbb", ou null si "Aucune" a ete choisi (Picked indique si un choix a ete fait).
/// </summary>
public sealed class ColorPickerWindow : Window
{
    public string? Result { get; private set; }
    public bool Picked { get; private set; }
    readonly TextBox _hex = new() { Width = 90, Margin = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
    readonly Border _preview = new() { Width = 40, Height = 24, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };

    static readonly string[] Base = { "#ffffff", "#000000", "#e7e6e6", "#44546a", "#4472c4", "#ed7d31", "#a5a5a5", "#ffc000", "#5b9bd5", "#70ad47" };
    static readonly string[] Standard = { "#c00000", "#ff0000", "#ffc000", "#ffff00", "#92d050", "#00b050", "#00b0f0", "#0070c0", "#002060", "#7030a0" };

    public ColorPickerWindow(string? current, IEnumerable<string> documentColors, bool allowNone, string title = "Couleur")
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(Title2("Couleurs du thème"));
        var grid = new UniformGrid { Columns = 10 };
        // ligne de base, puis 4 nuances (eclaircies / assombries) par colonne
        foreach (var c in Base) grid.Children.Add(Swatch(c));
        foreach (var k in new[] { 0.8, 0.6, 0.4, -0.25, -0.5 })
            foreach (var c in Base) grid.Children.Add(Swatch(Shade(c, k)));
        root.Children.Add(grid);

        root.Children.Add(Title2("Couleurs standard"));
        var std = new UniformGrid { Columns = 10 };
        foreach (var c in Standard) std.Children.Add(Swatch(c));
        root.Children.Add(std);

        var docColors = documentColors.Where(c => c.StartsWith('#') && c.Length == 7).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        if (docColors.Count > 0)
        {
            root.Children.Add(Title2("Couleurs du document"));
            var doc = new UniformGrid { Columns = 10 };
            foreach (var c in docColors) doc.Children.Add(Swatch(c));
            root.Children.Add(doc);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Code :", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_hex);
        row.Children.Add(_preview);
        root.Children.Add(row);
        _hex.TextChanged += (_, _) => { if (Parse(_hex.Text) is { } c) _preview.Background = PageRenderer.Brush(c); };
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ok(); };
        _hex.Text = current ?? "#000000";

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (allowNone)
        {
            var none = new Button { Content = "Aucune", Width = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0, 4, 0, 4) };
            none.Click += (_, _) => { Result = null; Picked = true; DialogResult = true; };
            buttons.Children.Add(none);
        }
        var cancel = new Button { Content = "Annuler", Width = 80, IsCancel = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0, 4, 0, 4) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Padding = new Thickness(0, 4, 0, 4) };
        ok.Click += (_, _) => Ok();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F1) { Help.HelpService.Open(Help.HelpService.Colors); e.Handled = true; } };
    }

    void Ok()
    {
        if (Parse(_hex.Text) is not { } c) { _hex.Focus(); _hex.SelectAll(); return; }
        Result = c; Picked = true; DialogResult = true;
    }

    static TextBlock Title2(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 4), FontSize = 12 };

    Button Swatch(string hex)
    {
        var b = new Button
        {
            Width = 22, Height = 22, Margin = new Thickness(1.5), Background = PageRenderer.Brush(hex), ToolTip = hex,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xc8, 0xc8, 0xc8)), Cursor = Cursors.Hand,
        };
        b.Click += (_, _) => { Result = hex; Picked = true; DialogResult = true; };
        return b;
    }

    public static string? Parse(string text)
    {
        var t = text.Trim().TrimStart('#');
        if (t.Length == 3) t = string.Concat(t.Select(c => $"{c}{c}"));
        return t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? "#" + t.ToLowerInvariant() : null;
    }

    /// <summary>Eclaircit (k &gt; 0) ou assombrit (k &lt; 0) une couleur.</summary>
    static string Shade(string hex, double k)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        byte F(byte v) => (byte)Math.Clamp(k > 0 ? v + (255 - v) * k : v * (1 + k), 0, 255);
        return $"#{F(c.R):x2}{F(c.G):x2}{F(c.B):x2}";
    }
}
