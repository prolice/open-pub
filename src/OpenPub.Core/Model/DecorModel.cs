using System.Text.Json.Serialization;

namespace OpenPub.Core.Model;

// Lot 9 (formes, effets et texte decoratif) : champs ajoutes au modele. Tous sont facultatifs dans document.json :
// un ancien fichier s'ouvre a l'identique, et une ancienne version d'OpenPub ignore ces champs (elle garde alors la
// couleur unie de Fill.Color, toujours renseignee).

public static partial class ShapeKind
{
    /// <summary>Texte decoratif (WordArt) : texte de <see cref="Shape.WordArt"/>, rempli et contoure comme une forme.</summary>
    public const string WordArt = "wordart";
}

public sealed partial class Shape
{
    /// <summary>
    /// Reglages de la forme automatique (poignees jaunes) : epaisseur d'une fleche, profondeur d'une etoile, pointe
    /// d'une bulle... En parts du cadre (elles suivent le redimensionnement). Null = valeurs par defaut de la forme.
    /// </summary>
    public List<double>? Adjust { get; set; }
    /// <summary>Lueur, bords adoucis, ombre interieure (formes, zones de texte, images).</summary>
    public EffectsFx? Effects { get; set; }
    /// <summary>Texte decoratif (objet de type <see cref="ShapeKind.WordArt"/>).</summary>
    public WordArtFx? WordArt { get; set; }
    /// <summary>Bordure decorative : motif repete le long du cadre.</summary>
    public BorderArtFx? BorderArt { get; set; }

    [JsonIgnore] public bool IsWordArt => Kind == ShapeKind.WordArt;
    /// <summary>Espace reserve pour une image : cadre d'image encore vide.</summary>
    [JsonIgnore] public bool IsPlaceholder => IsImage && string.IsNullOrEmpty(Src);
}

public sealed partial class Fill
{
    /// <summary>Arrets du degrade (2 au moins) ; null = pas de degrade. <see cref="Color"/> garde la 1re couleur.</summary>
    public List<GradientStopFx>? Stops { get; set; }
    /// <summary>"linear" (par defaut) ou "radial".</summary>
    public string? GradientType { get; set; }
    /// <summary>Direction du degrade lineaire en degres (0 = de gauche a droite, 90 = de haut en bas).</summary>
    public double? Angle { get; set; }
    /// <summary>Motif (voir <see cref="FillPatterns"/>) dessine en <see cref="PatternColor"/> sur le fond <see cref="Color"/>.</summary>
    public string? Pattern { get; set; }
    public string? PatternColor { get; set; }
    /// <summary>Image en remplissage (chemin "assets/..." du paquet).</summary>
    public string? Image { get; set; }
    /// <summary>Image repetee en mosaique (sinon etiree pour couvrir la forme, sans deformation).</summary>
    public bool? ImageTile { get; set; }

    [JsonIgnore] public bool HasGradient => Stops is { Count: >= 2 };
    [JsonIgnore] public bool IsRadial => GradientType == "radial";

    public Fill Clone()
    {
        var f = (Fill)MemberwiseClone();
        f.Stops = Stops?.Select(s => s.Clone()).ToList();
        return f;
    }
}

public sealed partial class LineStyle
{
    /// <summary>Opacite du contour (null = opaque).</summary>
    public double? Opacity { get; set; }
}

/// <summary>Arret d'un degrade.</summary>
public sealed class GradientStopFx
{
    /// <summary>Position de 0 (debut) a 1 (fin).</summary>
    public double Pos { get; set; }
    public string Color { get; set; } = "#000000";
    /// <summary>Opacite de l'arret (null = opaque).</summary>
    public double? Opacity { get; set; }

    public GradientStopFx Clone() => (GradientStopFx)MemberwiseClone();
}

/// <summary>Motifs de remplissage dessines en code.</summary>
public static class FillPatterns
{
    public static readonly (string Id, string Label)[] All =
    {
        ("dots", "Pois"), ("smallDots", "Petits points"), ("horizontal", "Lignes horizontales"), ("vertical", "Lignes verticales"),
        ("diagUp", "Diagonales montantes"), ("diagDown", "Diagonales descendantes"), ("grid", "Quadrillage"),
        ("diagGrid", "Treillis"), ("checker", "Damier"), ("zigzag", "Zigzag"), ("bricks", "Briques"), ("waves", "Vagues"),
    };
}

/// <summary>Effets de forme et d'image (en plus de l'ombre portee <see cref="ShadowFx"/>).</summary>
public sealed class EffectsFx
{
    /// <summary>Lueur autour de l'objet : couleur (null = aucune), taille (points), opacite.</summary>
    public string? GlowColor { get; set; }
    public double GlowSize { get; set; } = 8;
    public double GlowOpacity { get; set; } = 0.6;
    /// <summary>Bords adoucis : largeur du fondu vers la transparence (points, 0 = aucun).</summary>
    public double SoftEdge { get; set; }
    /// <summary>Ombre interieure (null = aucune).</summary>
    public ShadowFx? InnerShadow { get; set; }

    [JsonIgnore] public bool HasGlow => GlowColor is not null && GlowSize > 0 && GlowOpacity > 0;
    [JsonIgnore] public bool IsEmpty => !HasGlow && SoftEdge <= 0 && InnerShadow is null;

    public EffectsFx Clone()
    {
        var e = (EffectsFx)MemberwiseClone();
        if (InnerShadow is { } i) e.InnerShadow = new ShadowFx { Color = i.Color, Opacity = i.Opacity, Distance = i.Distance, Angle = i.Angle, Blur = i.Blur };
        return e;
    }
}

/// <summary>Texte decoratif : le texte remplit le cadre, eventuellement deforme le long d'un trace.</summary>
public sealed class WordArtFx
{
    public string Text { get; set; } = "";
    public string Family { get; set; } = "Arial Black";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    /// <summary>Deformation : voir <see cref="WordArtWarps"/>.</summary>
    public string Warp { get; set; } = WordArtWarps.None;
    /// <summary>Deformation « le long d'un trace » : points de la ligne de base, en parts du cadre (0 a 1).</summary>
    public List<double[]>? PathPoints { get; set; }

    public WordArtFx Clone()
    {
        var c = (WordArtFx)MemberwiseClone();
        c.PathPoints = PathPoints?.Select(p => (double[])p.Clone()).ToList();
        return c;
    }
}

public static class WordArtWarps
{
    public const string None = "none";
    public const string ArcUp = "arcUp";
    public const string ArcDown = "arcDown";
    public const string Circle = "circle";
    public const string Wave = "wave";
    /// <summary>Le long d'un trace libre (Dessin ▸ Texte sur le tracé) ; ne figure pas dans la liste des deformations.</summary>
    public const string Path = "path";

    public static readonly (string Id, string Label)[] All =
    {
        (None, "Aucune (texte droit)"), (ArcUp, "Arc vers le haut"), (ArcDown, "Arc vers le bas"), (Circle, "Cercle"), (Wave, "Vague"),
    };
}

/// <summary>Bordure decorative : motif (voir BorderArtLayout) repete le long du cadre.</summary>
public sealed class BorderArtFx
{
    public string Pattern { get; set; } = "hearts";
    /// <summary>Taille d'un motif (points).</summary>
    public double Size { get; set; } = 14;
    public string Color { get; set; } = "#d13438";
    /// <summary>Seconde couleur, alternee avec la premiere (null = une seule couleur).</summary>
    public string? Color2 { get; set; }

    public BorderArtFx Clone() => (BorderArtFx)MemberwiseClone();
}
