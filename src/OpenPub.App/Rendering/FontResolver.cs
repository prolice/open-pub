using System.IO;
using System.Windows;
using System.Windows.Media;
using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.App.Rendering;

/// <summary>
/// Trouve la police WPF d'une famille : d'abord les polices embarquees dans le paquet (fonts/),
/// puis les polices installees ; a defaut, une police de substitution (signalee).
/// </summary>
public sealed class FontResolver
{
    readonly Dictionary<string, FontFamily> _families = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, FontFileInfo> _metrics = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _packageFamilies = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _system;
    readonly Uri? _packageFonts;
    public HashSet<string> Substituted { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static readonly FontFamily Fallback = new("Arial");

    public FontResolver(string? packageFontsDir)
    {
        _system = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packageFontsDir is not null && Directory.Exists(packageFontsDir))
        {
            _packageFonts = new Uri(Path.GetFullPath(packageFontsDir).TrimEnd('\\') + "\\");
            foreach (var f in Directory.GetFiles(packageFontsDir))
                if (FontFileInfo.TryRead(f, out var info))
                    _packageFamilies.Add(info.Family);
        }
    }

    public IEnumerable<string> AvailableFamilies => _packageFamilies.Concat(_system).Distinct().OrderBy(n => n);

    public FontFamily Family(string? name)
    {
        name ??= "Arial";
        if (_families.TryGetValue(name, out var ff)) return ff;
        if (_packageFonts is not null && _packageFamilies.Contains(name))
            ff = new FontFamily(_packageFonts, "./#" + name);
        else if (_system.Contains(name))
            ff = new FontFamily(name);
        else if (Legacy(name) is { } leg)
            ff = leg.Family;
        else
            ff = Fallback;
        // verifie que la police se charge vraiment (WPF bascule sinon en silence sur une police par defaut)
        if (ff != Fallback && !new System.Windows.Media.Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out _)
                           && !new System.Windows.Media.Typeface(ff, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal).TryGetGlyphTypeface(out _))
            ff = Fallback;
        if (ff == Fallback && !name.Equals("Arial", StringComparison.OrdinalIgnoreCase)) Substituted.Add(name);
        _families[name] = ff;
        return ff;
    }

    /// <summary>Nom de famille d'une FontFamily WPF (y compris les polices du paquet "file:///...#Nom").</summary>
    public static string NameOf(FontFamily ff)
    {
        var src = ff.Source ?? "Arial";
        var hash = src.LastIndexOf('#');
        var name = hash >= 0 ? src[(hash + 1)..] : src;
        var comma = name.IndexOf(',');
        return (comma >= 0 ? name[..comma] : name).Trim();
    }

    public Typeface Typeface(FontSpec f)
    {
        var family = Family(f.Family);
        var weight = f.Bold ? FontWeights.Bold : FontWeights.Normal;
        var style = f.Italic ? FontStyles.Italic : FontStyles.Normal;
        // nom Windows "historique" (ex. "Eras Bold ITC" = famille "Eras ITC" en gras) : la graisse fait partie du nom
        if (f.Family is not null && _legacy.TryGetValue(f.Family, out var leg) && leg is not null)
        {
            if (leg.Weight.ToOpenTypeWeight() > weight.ToOpenTypeWeight()) weight = leg.Weight;
            if (leg.Style != FontStyles.Normal) style = leg.Style;
        }
        return new Typeface(family, style, weight, FontStretches.Normal);
    }

    sealed record LegacyFont(FontFamily Family, FontWeight Weight, FontStyle Style);
    readonly Dictionary<string, LegacyFont?> _legacy = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, string>? _legacyIndex;

    /// <summary>
    /// Polices installees connues de Windows sous un nom de famille "historique" (table name, ID 1)
    /// different de la famille typographique utilisee par WPF.
    /// </summary>
    LegacyFont? Legacy(string name)
    {
        if (_legacy.TryGetValue(name, out var cached)) return cached;
        if (_legacyIndex is null)
        {
            _legacyIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[] { FontEmbedder.SystemFontsDir, FontEmbedder.UserFontsDir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!(file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))) continue;
                    if (FontFileInfo.TryRead(file, out var info)) _legacyIndex.TryAdd(info.Family, file);
                }
            }
        }
        LegacyFont? result = null;
        if (_legacyIndex.TryGetValue(name, out var path))
        {
            try
            {
                // WPF regroupe les fichiers par famille typographique : on cherche la famille systeme dont une
                // variante est exactement ce fichier (ex. ERASBD.TTF "Eras Bold ITC" = "Eras ITC" en gras)
                var full = Path.GetFullPath(path);
                var firstWord = name.Split(' ')[0];
                var candidates = Fonts.SystemFontFamilies.OrderByDescending(f => f.Source.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase));
                foreach (var fam in candidates)
                {
                    foreach (var tf in fam.GetTypefaces())
                    {
                        if (tf.TryGetGlyphTypeface(out var g) && g.FontUri.IsFile &&
                            string.Equals(Path.GetFullPath(g.FontUri.LocalPath), full, StringComparison.OrdinalIgnoreCase))
                        {
                            result = new LegacyFont(fam, tf.Weight, tf.Style);
                            break;
                        }
                    }
                    if (result is not null || !fam.Source.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (Exception) { result = null; }
        }
        _legacy[name] = result;
        return result;
    }

    /// <summary>Metriques verticales (win) lues dans le fichier de police reel.</summary>
    public FontFileInfo Metrics(FontSpec f)
    {
        var key = $"{f.Family}|{f.Bold}|{f.Italic}";
        if (_metrics.TryGetValue(key, out var m)) return m;
        m = new FontFileInfo(f.Family ?? "", "", 1.17, 0.8);
        if (Typeface(f).TryGetGlyphTypeface(out var gt))
        {
            try
            {
                using var s = gt.GetFontStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                if (FontFileInfo.TryRead(ms.ToArray(), out var info)) m = info;
                else m = new FontFileInfo(f.Family ?? "", "", gt.Height, gt.Baseline / Math.Max(gt.Height, 1e-6));
            }
            catch (Exception)
            {
                m = new FontFileInfo(f.Family ?? "", "", gt.Height, gt.Baseline / Math.Max(gt.Height, 1e-6));
            }
        }
        _metrics[key] = m;
        return m;
    }
}
