using System.Text.Json.Serialization;

namespace OpenPub.Core.Model;

// Modele de document OpenPub ("openpub/1"). Unites : points (1/72 pouce), origine en haut a gauche de la page.
// Les noms JSON correspondent a document.json produit par tools/pub2json.py.

public sealed class PubDocument
{
    public string Format { get; set; } = "openpub/1";
    public string? Source { get; set; }
    public string Unit { get; set; } = "pt";
    public string? Reader { get; set; }
    public Dictionary<string, string>? Meta { get; set; }
    public double PageWidth { get; set; } = 595.276;
    public double PageHeight { get; set; } = 841.89;
    public List<Page> Pages { get; set; } = new();
    /// <summary>Reperes de marge (haut, droite, bas, gauche) en points ; null = 0,5 pouce.</summary>
    public double[]? Margins { get; set; }
    /// <summary>
    /// Interligne simple calibre par police (multiple de la taille), quand Publisher n'utilise pas
    /// la valeur standard de la police (ex. polices cloud d'Office). Mesure sur les apercus Publisher.
    /// </summary>
    public Dictionary<string, double>? FontLineFactors { get; set; }
    /// <summary>Part de l'interligne simple situee au-dessus de la ligne de base, calibree par police.</summary>
    public Dictionary<string, double>? FontAscents { get; set; }

    public IEnumerable<Shape> AllShapes() => Pages.SelectMany(p => p.Shapes);
}

public sealed class Page
{
    public int Index { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public List<Shape> Shapes { get; set; } = new();

    /// <summary>Zones d'une chaine, dans l'ordre de lecture.</summary>
    public List<Shape> ChainFrames(string chain) =>
        Shapes.Where(s => s.Chain == chain).OrderBy(s => s.ChainIndex ?? 0).ToList();
}

public static class ShapeKind
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Rect = "rect";
    public const string Ellipse = "ellipse";
    public const string Path = "path";
}

public sealed class Shape
{
    public string Kind { get; set; } = ShapeKind.Rect;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public int Z { get; set; }
    public string? Group { get; set; }
    /// <summary>Rotation en degres, sens horaire, autour du centre de l'objet.</summary>
    public double Rotation { get; set; }
    /// <summary>
    /// Forme automatique dessinee dans le cadre de l'objet (voir <see cref="ShapeGeometry"/>) ; pour une zone de
    /// texte, c'est la forme de son cadre (fond et contour). Null = trace importe (Path) ou rectangle.
    /// </summary>
    public string? Geometry { get; set; }
    /// <summary>Rayon des coins (points) pour la forme "roundRect".</summary>
    public double? CornerRadius { get; set; }
    /// <summary>Ombre portee (formes, zones de texte, images).</summary>
    public ShadowFx? Shadow { get; set; }
    /// <summary>Retouches d'image : recadrage, couleur, luminosite, miroir, reflet...</summary>
    public ImageFx? ImageFx { get; set; }

    public Fill? Fill { get; set; }
    public LineStyle? Line { get; set; }
    public List<PathSegment>? Path { get; set; }

    // Image
    public string? Src { get; set; }
    public string? Mime { get; set; }
    public bool Background { get; set; }
    public bool Tile { get; set; }
    public List<PathSegment>? Clip { get; set; }

    // Zone de texte
    public double[]? Padding { get; set; }          // haut, droite, bas, gauche
    public int Columns { get; set; } = 1;
    public double ColumnGap { get; set; }
    public string? VAlign { get; set; }             // top | middle | bottom
    public List<Paragraph>? Paragraphs { get; set; }
    public string? Chain { get; set; }
    public int? ChainIndex { get; set; }
    public int? FlowFrom { get; set; }

    [JsonIgnore] public bool IsText => Kind == ShapeKind.Text;
    [JsonIgnore] public bool IsImage => Kind == ShapeKind.Image;
    /// <summary>Premiere zone d'une chaine (ou zone isolee) : c'est elle qui porte le texte.</summary>
    [JsonIgnore] public bool IsStoryHead => IsText && (Chain is null || (ChainIndex ?? 0) == 0);

    /// <summary>Boite englobante de l'objet tourne (coordonnees de page).</summary>
    public (double X, double Y, double W, double H) Bounds()
    {
        if (Math.Abs(Rotation % 360) < 0.01) return (X, Y, W, H);
        double a = Rotation * Math.PI / 180, c = Math.Abs(Math.Cos(a)), sn = Math.Abs(Math.Sin(a));
        double bw = W * c + H * sn, bh = W * sn + H * c;
        double cx = X + W / 2, cy = Y + H / 2;
        return (cx - bw / 2, cy - bh / 2, bw, bh);
    }

    public double PadTop => Padding is { Length: 4 } ? Padding[0] : 0;
    public double PadRight => Padding is { Length: 4 } ? Padding[1] : 0;
    public double PadBottom => Padding is { Length: 4 } ? Padding[2] : 0;
    public double PadLeft => Padding is { Length: 4 } ? Padding[3] : 0;
}

/// <summary>Formes automatiques disponibles (valeurs de <see cref="Shape.Geometry"/>).</summary>
public static class ShapeGeometry
{
    public const string Rect = "rect";
    public const string RoundRect = "roundRect";
    public const string Ellipse = "ellipse";
    public const string Triangle = "triangle";
    public const string Diamond = "diamond";
    public const string Hexagon = "hexagon";
    public const string Star = "star5";
    public const string Arrow = "arrowRight";
    public const string Bubble = "bubble";
    public const string Heart = "heart";
}

public sealed class ShadowFx
{
    public string Color { get; set; } = "#000000";
    public double Opacity { get; set; } = 0.35;
    /// <summary>Distance (points) et direction (degres, 45 = vers le bas a droite).</summary>
    public double Distance { get; set; } = 4;
    public double Angle { get; set; } = 45;
    /// <summary>Flou (points).</summary>
    public double Blur { get; set; } = 5;
}

public sealed class ImageFx
{
    /// <summary>Recadrage : part de l'image source retiree de chaque cote (0 a 1).</summary>
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    /// <summary>null (couleurs d'origine), "gray", "bw" (noir et blanc), "sepia", "washout" (filigrane).</summary>
    public string? Recolor { get; set; }
    /// <summary>-1 a 1.</summary>
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    /// <summary>Transparence de l'image (0 = opaque, 1 = invisible).</summary>
    public double Transparency { get; set; }
    /// <summary>Reflet sous l'image : taille (part de la hauteur), opacite de depart, ecart (points). Taille 0 = aucun.</summary>
    public double ReflectionSize { get; set; }
    public double ReflectionOpacity { get; set; } = 0.4;
    public double ReflectionGap { get; set; } = 1;

    public bool HasCrop => CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0;
    public bool HasPixelEffect => Recolor is not null || Math.Abs(Brightness) > 1e-6 || Math.Abs(Contrast) > 1e-6;
    public bool IsEmpty => !HasCrop && !HasPixelEffect && !FlipH && !FlipV && Transparency <= 0 && ReflectionSize <= 0;

    public ImageFx Clone() => (ImageFx)MemberwiseClone();

    /// <summary>Cle des retouches de pixels (cache des images traitees).</summary>
    public string PixelKey() =>
        FormattableString.Invariant($"{CropLeft:0.####},{CropTop:0.####},{CropRight:0.####},{CropBottom:0.####},{Recolor},{Brightness:0.###},{Contrast:0.###}");
}

public sealed class Fill
{
    public string? Color { get; set; }
    public double? Opacity { get; set; }
    public bool Gradient { get; set; }
}

public sealed class LineStyle
{
    public string Color { get; set; } = "#000000";
    public double Weight { get; set; } = 1;
    public bool Dash { get; set; }
    /// <summary>Pointes de fleche au debut / a la fin d'un trace ouvert (ligne, main levee).</summary>
    public bool ArrowStart { get; set; }
    public bool ArrowEnd { get; set; }
}

/// <summary>Segment de trace facon SVG : a = M, L, C, Q, A ou Z ; coordonnees absolues en points.</summary>
public sealed class PathSegment
{
    public string A { get; set; } = "L";
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? X1 { get; set; }
    public double? Y1 { get; set; }
    public double? X2 { get; set; }
    public double? Y2 { get; set; }
    public double? Rx { get; set; }
    public double? Ry { get; set; }
    public double? Rot { get; set; }
    public int? Large { get; set; }
    public int? Sweep { get; set; }
}

public sealed class Paragraph
{
    public string? Align { get; set; }              // left | center | right | end | justify
    public string? LineHeight { get; set; }         // ex. "105.0000%"
    public double? SpaceBefore { get; set; }
    public double? SpaceAfter { get; set; }
    public double? IndentLeft { get; set; }
    public double? IndentRight { get; set; }
    public double? FirstIndent { get; set; }
    public List<Run> Runs { get; set; } = new();

    [JsonIgnore] public string PlainText => string.Concat(Runs.Select(r => r.Text));

    /// <summary>Facteur d'interligne (1 = simple).</summary>
    public double LineSpacingFactor()
    {
        if (LineHeight is { } lh && lh.EndsWith('%') &&
            double.TryParse(lh.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v / 100.0;
        return 1.0;
    }
}

public sealed class Run
{
    public string Text { get; set; } = "";
    public FontSpec? Font { get; set; }
}

public sealed class FontSpec
{
    public string? Family { get; set; }
    public double? Size { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Color { get; set; }
    public string? Lang { get; set; }

    public FontSpec Clone() => (FontSpec)MemberwiseClone();

    public bool SameAs(FontSpec? o) => o is not null && Family == o.Family && Size == o.Size && Bold == o.Bold &&
                                       Italic == o.Italic && Underline == o.Underline && Color == o.Color;
}
