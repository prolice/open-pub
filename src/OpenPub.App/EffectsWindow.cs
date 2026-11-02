using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.App.Rendering;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>Boite « Effets » d'une forme ou d'une image : lueur, bords adoucis, ombre interieure.</summary>
public sealed class EffectsWindow : Window
{
    static readonly CultureInfo Cu = CultureInfo.CurrentCulture;
    readonly Func<string?, string, (bool Picked, string? Color)> _pickColor;
    readonly CheckBox _glowOn = new() { Content = "Lueur autour de l'objet" };
    readonly Button _glowColor = new() { Width = 60, Height = 22 };
    readonly TextBox _glowSize = new() { Width = 60 };
    readonly Slider _glowTransp = new() { Minimum = 0, Maximum = 100, Width = 180, IsMoveToPointEnabled = true };
    readonly TextBox _soft = new() { Width = 60 };
    readonly CheckBox _innerOn = new() { Content = "Ombre intérieure" };
    readonly Button _innerColor = new() { Width = 60, Height = 22 };
    readonly TextBox _innerDist = new() { Width = 60 }, _innerBlur = new() { Width = 60 }, _innerAngle = new() { Width = 60 };
    readonly Slider _innerTransp = new() { Minimum = 0, Maximum = 100, Width = 180, IsMoveToPointEnabled = true };
    string _glow, _inner;

    /// <summary>Effets choisis (null = aucun).</summary>
    public EffectsFx? Result { get; private set; }

    public EffectsWindow(EffectsFx? current, Func<string?, string, (bool, string?)> pickColor)
    {
        _pickColor = pickColor;
        var fx = current?.Clone() ?? new EffectsFx();
        Title = "Effets";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        _glow = fx.GlowColor ?? "#ffd23f";
        _inner = fx.InnerShadow?.Color ?? "#000000";
        var root = new StackPanel { Margin = new Thickness(18), Width = 400 };
        root.Children.Add(FillWindow.Section("Lueur"));
        root.Children.Add(_glowOn);
        root.Children.Add(FillWindow.Row("Couleur", _glowColor));
        root.Children.Add(FillWindow.Row("Taille (pt)", _glowSize));
        root.Children.Add(FillWindow.Row("Transparence (%)", _glowTransp));
        root.Children.Add(FillWindow.Section("Bords adoucis"));
        root.Children.Add(FillWindow.Row("Largeur du fondu (pt)", _soft));
        root.Children.Add(FillWindow.Section("Ombre intérieure"));
        root.Children.Add(_innerOn);
        root.Children.Add(FillWindow.Row("Couleur", _innerColor));
        root.Children.Add(FillWindow.Row("Distance (pt)", _innerDist));
        root.Children.Add(FillWindow.Row("Flou (pt)", _innerBlur));
        root.Children.Add(FillWindow.Row("Direction (°)", _innerAngle));
        root.Children.Add(FillWindow.Row("Transparence (%)", _innerTransp));
        root.Children.Add(new TextBlock
        {
            Text = "Direction : 45° = décalage vers le bas à droite ; l'ombre intérieure longe alors les bords haut et gauche.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("PillButton"), Padding = new Thickness(22, 5, 22, 5), IsDefault = true };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        _glowOn.IsChecked = fx.HasGlow;
        _glowSize.Text = fx.GlowSize.ToString("0.#", Cu);
        _glowTransp.Value = (1 - fx.GlowOpacity) * 100;
        _soft.Text = fx.SoftEdge.ToString("0.#", Cu);
        var sh = fx.InnerShadow ?? new ShadowFx { Distance = 4, Blur = 6, Opacity = 0.45 };
        _innerOn.IsChecked = fx.InnerShadow is not null;
        _innerDist.Text = sh.Distance.ToString("0.#", Cu);
        _innerBlur.Text = sh.Blur.ToString("0.#", Cu);
        _innerAngle.Text = sh.Angle.ToString("0.#", Cu);
        _innerTransp.Value = (1 - sh.Opacity) * 100;
        Swatch(_glowColor, _glow);
        Swatch(_innerColor, _inner);
        _glowColor.Click += (_, _) => { var (p, c) = _pickColor(_glow, "Couleur de la lueur"); if (p && c is not null) { _glow = c; Swatch(_glowColor, c); _glowOn.IsChecked = true; } };
        _innerColor.Click += (_, _) => { var (p, c) = _pickColor(_inner, "Couleur de l'ombre intérieure"); if (p && c is not null) { _inner = c; Swatch(_innerColor, c); _innerOn.IsChecked = true; } };
        ok.Click += (_, _) =>
        {
            var r = new EffectsFx
            {
                GlowColor = _glowOn.IsChecked == true ? _glow : null,
                GlowSize = Math.Clamp(Parse(_glowSize.Text) ?? 8, 0, 100),
                GlowOpacity = 1 - _glowTransp.Value / 100,
                SoftEdge = Math.Clamp(Parse(_soft.Text) ?? 0, 0, 200),
                InnerShadow = _innerOn.IsChecked == true
                    ? new ShadowFx
                    {
                        Color = _inner, Distance = Math.Clamp(Parse(_innerDist.Text) ?? 4, 0, 100), Blur = Math.Clamp(Parse(_innerBlur.Text) ?? 6, 0, 100),
                        Angle = Parse(_innerAngle.Text) ?? 45, Opacity = 1 - _innerTransp.Value / 100,
                    }
                    : null,
            };
            Result = r.IsEmpty ? null : r;
            DialogResult = true;
        };
    }

    static void Swatch(Button b, string color) =>
        b.Content = new Border { Width = 44, Height = 12, Background = PageRenderer.Brush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };

    static double? Parse(string text)
    {
        var sep = Cu.NumberFormat.NumberDecimalSeparator;
        return double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, Cu, out var v) ? v : null;
    }
}
