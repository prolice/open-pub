using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.App.Rendering;

/// <summary>Mesure du texte avec WPF (mode Ideal, independant de la resolution). Largeurs mises en cache.</summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    const double RefSize = 100;
    readonly FontResolver _fonts;
    readonly Dictionary<(string, string?, bool, bool), double> _cache = new();
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public WpfTextMeasurer(FontResolver fonts) => _fonts = fonts;

    /// <summary>Interlignes calibres du document (famille -> multiple de la taille).</summary>
    public Dictionary<string, double>? LineFactorOverrides { get; set; }

    public double Width(string text, FontSpec font, double sizePt)
    {
        if (text.Length == 0) return 0;
        var key = (text, font.Family, font.Bold, font.Italic);
        if (!_cache.TryGetValue(key, out var w))
        {
            var ft = new FormattedText(text, Fr, FlowDirection.LeftToRight, _fonts.Typeface(font), RefSize,
                Brushes.Black, 1.0) { };
            w = ft.WidthIncludingTrailingWhitespace;
            _cache[key] = w;
        }
        return w * sizePt / RefSize;
    }

    public double LineFactor(FontSpec font) =>
        font.Family is not null && LineFactorOverrides is not null && LineFactorOverrides.TryGetValue(font.Family, out var v)
            ? v : _fonts.Metrics(font).WinLineFactor;

    public Dictionary<string, double>? AscentOverrides { get; set; }

    public double AscentRatio(FontSpec font) =>
        font.Family is not null && AscentOverrides is not null && AscentOverrides.TryGetValue(font.Family, out var v)
            ? v : _fonts.Metrics(font).WinAscentRatio;
}
