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

    /// <summary>
    /// Pages maitres (objets communs : bandeau, numero de page...) dessinees sous les pages qui les utilisent.
    /// Null : aucune page maitre.
    /// </summary>
    public List<Page>? Masters { get; set; }
    /// <summary>Numerotation des pages (premier numero, style).</summary>
    public PageNumbering? Numbering { get; set; }
    /// <summary>Reperes de regle (positions en points depuis le bord de la page), communs a toutes les pages.</summary>
    public RulerGuides? Guides { get; set; }
    /// <summary>Grille de disposition (colonnes et lignes a l'interieur des marges).</summary>
    public LayoutGrid? Grid { get; set; }
    /// <summary>Styles de paragraphe et de caractere du document.</summary>
    public List<TextStyle>? Styles { get; set; }
    /// <summary>Jeu de couleurs et jeu de polices du document.</summary>
    public DocTheme? Theme { get; set; }
    /// <summary>Source du publipostage (colonnes et lignes, copiees dans le document).</summary>
    public MergeSource? Merge { get; set; }

    /// <summary>Version de format la plus recente que cette application sait lire et ecrire.</summary>
    public const int SupportedFormat = 2;

    /// <summary>Numero de version du format ("openpub/2" -> 2).</summary>
    [JsonIgnore]
    public int FormatVersion => Format.StartsWith("openpub/") && int.TryParse(Format["openpub/".Length..], out var v) ? v : 1;

    /// <summary>Tous les objets du document, pages maitres comprises.</summary>
    public IEnumerable<Shape> AllShapes() => Pages.Concat(Masters ?? new List<Page>()).SelectMany(p => p.Shapes);

    /// <summary>Page maitre utilisee par une page (null : aucune).</summary>
    public Page? MasterOf(Page page)
    {
        if (Masters is not { Count: > 0 } ms || page.IgnoreMaster || ms.Contains(page)) return null;
        return page.Master is { } id ? ms.FirstOrDefault(m => m.Id == id) ?? ms[0] : ms[0];
    }

    /// <summary>Numero affiche d'une page (numerotation du document, redemarrages par page).</summary>
    public string PageNumber(Page page)
    {
        int n = Numbering?.Start ?? 1;
        foreach (var p in Pages)
        {
            if (p.NumberStart is { } restart) n = restart;
            if (p == page) return PageNumbering.Format(n, Numbering?.Style);
            n++;
        }
        return "#";
    }
}

/// <summary>Version de format necessaire pour enregistrer un document sans perte.</summary>
public static class PubFormat
{
    /// <summary>« openpub/2 » si le document utilise des elements de la version 2 (pages maitres, numerotation, reperes...).</summary>
    public static string Required(PubDocument doc)
    {
        bool v2 = doc.Masters is { Count: > 0 } || doc.Numbering is not null || doc.Guides is not null || doc.Grid is not null
                  || doc.Pages.Any(p => p.BackColor is not null || p.NumberStart is not null || p.Master is not null || p.IgnoreMaster)
                  || doc.AllShapes().Any(s => s.Kind == ShapeKind.Table) || doc.Styles is { Count: > 0 } || doc.Theme is not null
                  || doc.Merge is not null || doc.AllShapes().Any(s => s.Link is not null || s.AltText is not null)
                  || doc.AllShapes().Any(s => s.Adjust is not null || s.Effects is not null || s.WordArt is not null || s.BorderArt is not null
                                             || s.Fill?.Stops is not null || s.Fill?.Pattern is not null || s.Fill?.Image is not null)
                  || doc.AllShapes().Any(s => s.Paragraphs?.Any(p => p.Runs.Any(r => r.Field is not null)) == true);
        return v2 || doc.FormatVersion >= 2 ? $"openpub/{Math.Max(2, Math.Min(doc.FormatVersion, PubDocument.SupportedFormat))}" : "openpub/1";
    }
}

/// <summary>
/// Style de texte nomme. Un style de paragraphe porte police et reglages de paragraphe ; un style de caractere
/// seulement la police. Appliquer un style recopie ses valeurs dans le texte ; modifier un style met a jour tout
/// le texte qui l'utilise.
/// </summary>
public sealed class TextStyle
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"paragraph" ou "character".</summary>
    public string Kind { get; set; } = "paragraph";
    public FontSpec? Font { get; set; }
    public string? Align { get; set; }
    public string? LineHeight { get; set; }
    public double? SpaceBefore { get; set; }
    public double? SpaceAfter { get; set; }
    public double? IndentLeft { get; set; }
    public double? FirstIndent { get; set; }
    /// <summary>Role dans le jeu de polices : "heading" (police des titres) ou "body" (police du texte).</summary>
    public string? FontRole { get; set; }
}

/// <summary>Donnees du publipostage : une ligne par destinataire.</summary>
public sealed class MergeSource
{
    public string? Name { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

/// <summary>Jeu de couleurs (principale puis accents) et jeu de polices (titres, texte).</summary>
public sealed class DocTheme
{
    public string? Name { get; set; }
    public List<string> Colors { get; set; } = new();
    public string? HeadingFont { get; set; }
    public string? BodyFont { get; set; }
}

/// <summary>Numerotation des pages.</summary>
public sealed class PageNumbering
{
    public int Start { get; set; } = 1;
    /// <summary>"1" (1, 2, 3), "i", "I", "a" ou "A".</summary>
    public string? Style { get; set; }

    public static string Format(int n, string? style)
    {
        string s = style ?? "1";
        if (n <= 0) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return s switch
        {
            "a" or "A" => Alpha(n, s == "A"),
            "i" or "I" => s == "I" ? Roman(n) : Roman(n).ToLowerInvariant(),
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    static string Alpha(int n, bool upper)
    {
        var sb = new System.Text.StringBuilder();
        for (; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)((upper ? 'A' : 'a') + (n - 1) % 26));
        return sb.ToString();
    }

    static string Roman(int n)
    {
        var map = new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") };
        var sb = new System.Text.StringBuilder();
        foreach (var (v, r) in map) while (n >= v) { sb.Append(r); n -= v; }
        return sb.ToString();
    }
}

/// <summary>Reperes de regle : lignes verticales (X) et horizontales (Y), en points.</summary>
public sealed class RulerGuides
{
    public List<double> X { get; set; } = new();
    public List<double> Y { get; set; } = new();
}

/// <summary>Grille de disposition a l'interieur des marges.</summary>
public sealed class LayoutGrid
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    /// <summary>Espace entre colonnes et entre lignes (points).</summary>
    public double Gutter { get; set; } = 14.17;

    /// <summary>Bords des colonnes et des lignes dans le rectangle de contenu (x0, y0, largeur, hauteur).</summary>
    public (List<double> Xs, List<double> Ys) Lines(double x0, double y0, double w, double h)
    {
        var xs = new List<double>(); var ys = new List<double>();
        int c = Math.Max(1, Columns), r = Math.Max(1, Rows);
        double cw = (w - Gutter * (c - 1)) / c, rh = (h - Gutter * (r - 1)) / r;
        for (int i = 1; i < c; i++) { xs.Add(x0 + i * cw + (i - 1) * Gutter); xs.Add(x0 + i * (cw + Gutter)); }
        for (int i = 1; i < r; i++) { ys.Add(y0 + i * rh + (i - 1) * Gutter); ys.Add(y0 + i * (rh + Gutter)); }
        return (xs, ys);
    }
}

public sealed class Page
{
    public int Index { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public List<Shape> Shapes { get; set; } = new();

    // pages maitres
    /// <summary>Identifiant d'une page maitre ("A", "B"...).</summary>
    public string? Id { get; set; }
    /// <summary>Nom d'une page maitre.</summary>
    public string? Name { get; set; }
    /// <summary>Page maitre utilisee (identifiant) ; null : la premiere.</summary>
    public string? Master { get; set; }
    /// <summary>Ne pas afficher la page maitre sur cette page.</summary>
    public bool IgnoreMaster { get; set; }
    /// <summary>Couleur d'arriere-plan de la page (null : blanc ; sinon celle de la page maitre).</summary>
    public string? BackColor { get; set; }
    /// <summary>La numerotation recommence a ce numero sur cette page.</summary>
    public int? NumberStart { get; set; }

    /// <summary>Zones d'une chaine, dans l'ordre de lecture.</summary>
    public List<Shape> ChainFrames(string chain) =>
        Shapes.Where(s => s.Chain == chain).OrderBy(s => s.ChainIndex ?? 0).ToList();
}

public static partial class ShapeKind
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Rect = "rect";
    public const string Ellipse = "ellipse";
    public const string Path = "path";
    public const string Table = "table";
}

public sealed partial class Shape
{
    public string Kind { get; set; } = ShapeKind.Rect;
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public int Z { get; set; }
    public string? Group { get; set; }
    /// <summary>Position verrouillee : l'objet ne peut plus etre deplace, redimensionne ou tourne a la souris ni au clavier.</summary>
    public bool Locked { get; set; }
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
    /// <summary>Ajustement du texte de la zone (tete de chaine) : null ou "shrink" (reduire en cas de debordement), "none", "grow" (agrandir la zone).</summary>
    public string? AutoFit { get; set; }
    /// <summary>Habillage du texte autour de cet objet : null (automatique), "none", "square", "tight", "topbottom".</summary>
    public string? Wrap { get; set; }
    /// <summary>Distance entre l'objet et le texte qui l'habille (points) ; null = 0,1 cm.</summary>
    public double? WrapDistance { get; set; }
    public List<Paragraph>? Paragraphs { get; set; }
    public string? Chain { get; set; }
    public int? ChainIndex { get; set; }
    /// <summary>Lien hypertexte de l'objet (adresse web ou « mailto: »).</summary>
    public string? Link { get; set; }
    /// <summary>Texte de remplacement (accessibilite, export web) : ce que montre l'image.</summary>
    public string? AltText { get; set; }
    /// <summary>Tableau (objet de type "table") : colonnes, lignes et cellules.</summary>
    public TableData? Table { get; set; }
    public int? FlowFrom { get; set; }

    [JsonIgnore] public bool IsText => Kind == ShapeKind.Text;
    [JsonIgnore] public bool IsTable => Kind == ShapeKind.Table && Table is not null;
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

/// <summary>Tableau : largeurs des colonnes, hauteurs minimales des lignes (points), cellules [ligne][colonne].</summary>
public sealed class TableData
{
    public List<double> Cols { get; set; } = new();
    /// <summary>Hauteurs minimales ; une ligne s'agrandit pour contenir son texte.</summary>
    public List<double> Rows { get; set; } = new();
    public List<List<TableCell>> Cells { get; set; } = new();
    /// <summary>Bordure des cellules qui n'en definissent pas (null : aucune).</summary>
    public LineStyle? Border { get; set; }
    /// <summary>Style de tableau applique (pour memoire).</summary>
    public string? Style { get; set; }
    /// <summary>Hauteurs reelles des lignes, calculees par la mise en page.</summary>
    [JsonIgnore] public double[]? ActualRows { get; set; }

    public int RowCount => Cells.Count;
    public int ColCount => Cols.Count;
}

/// <summary>Cellule d'un tableau.</summary>
public sealed class TableCell
{
    public List<Paragraph>? Paragraphs { get; set; }
    public Fill? Fill { get; set; }
    /// <summary>top | middle | bottom.</summary>
    public string? VAlign { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColSpan { get; set; } = 1;
    /// <summary>Cellule recouverte par une cellule fusionnee (ni dessinee ni modifiable).</summary>
    public bool Hidden { get; set; }
    /// <summary>Bordures propres (null : bordure du tableau ; epaisseur 0 : aucune).</summary>
    public LineStyle? Top { get; set; }
    public LineStyle? Right { get; set; }
    public LineStyle? Bottom { get; set; }
    public LineStyle? Left { get; set; }
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

public sealed partial class Fill
{
    public string? Color { get; set; }
    public double? Opacity { get; set; }
    public bool Gradient { get; set; }
}

public sealed partial class LineStyle
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
    /// <summary>Puce ou numero du paragraphe (null = paragraphe normal).</summary>
    public ListStyle? List { get; set; }
    /// <summary>Taquets de tabulation (positions depuis le bord gauche du texte, en points) ; sinon tous les 1,27 cm.</summary>
    public List<TabStop>? Tabs { get; set; }
    /// <summary>Style de paragraphe applique (identifiant).</summary>
    public string? Style { get; set; }
    /// <summary>Lettrine : la premiere lettre occupe ce nombre de lignes (null ou 0 = aucune).</summary>
    public int? DropCap { get; set; }
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

/// <summary>Liste a puces ou numerotee.</summary>
public sealed class ListStyle
{
    /// <summary>"bullet" ou "number".</summary>
    public string Kind { get; set; } = "bullet";
    /// <summary>Symbole de la puce (ex. "•", "–", "✓").</summary>
    public string? Symbol { get; set; } = "•";
    /// <summary>Numerotation : "1." "1)" "a." "a)" "A." "i." "I." ; le premier caractere donne le style.</summary>
    public string? Format { get; set; }
    /// <summary>Niveau d'imbrication (0 = premier niveau).</summary>
    public int Level { get; set; }
    /// <summary>Numero de depart (numerotation), sinon suite de la liste precedente.</summary>
    public int? Start { get; set; }

    public ListStyle Clone() => (ListStyle)MemberwiseClone();
}

/// <summary>Taquet de tabulation.</summary>
public sealed class TabStop
{
    /// <summary>Position en points depuis le bord gauche du texte de la zone.</summary>
    public double Pos { get; set; }
    /// <summary>"left", "center", "right" ou "decimal".</summary>
    public string Align { get; set; } = "left";
    /// <summary>Points de suite : null, "dot", "dash" ou "line".</summary>
    public string? Leader { get; set; }

    public TabStop Clone() => (TabStop)MemberwiseClone();
}

public sealed class Run
{
    public string Text { get; set; } = "";
    public FontSpec? Font { get; set; }
    /// <summary>
    /// Champ dont la valeur est calculee a l'affichage : "page" (numero de page), "pages" (nombre de pages),
    /// "date" (date du jour). Le texte du segment ("#") sert de position et de repli.
    /// </summary>
    public string? Field { get; set; }
    /// <summary>Style de caractere applique (identifiant).</summary>
    public string? Style { get; set; }
    /// <summary>Lien hypertexte du texte (adresse web ou « mailto: »).</summary>
    public string? Link { get; set; }
}

public sealed class FontSpec
{
    public string? Family { get; set; }
    public double? Size { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    /// <summary>"super" (exposant), "sub" (indice) ou null.</summary>
    public string? Script { get; set; }
    /// <summary>"all" (tout en majuscules), "small" (petites majuscules) ou null.</summary>
    public string? Caps { get; set; }
    /// <summary>Couleur de surlignage du texte (null = aucune).</summary>
    public string? Highlight { get; set; }
    /// <summary>Espacement des caracteres, en millièmes de la taille (positif : plus large).</summary>
    public double? Tracking { get; set; }
    public string? Color { get; set; }
    public string? Lang { get; set; }

    public FontSpec Clone() => (FontSpec)MemberwiseClone();

    public bool SameAs(FontSpec? o) => o is not null && Family == o.Family && Size == o.Size && Bold == o.Bold &&
                                       Italic == o.Italic && Underline == o.Underline && Color == o.Color &&
                                       Strike == o.Strike && Script == o.Script && Caps == o.Caps && Highlight == o.Highlight &&
                                       Tracking == o.Tracking && Lang == o.Lang;
}
