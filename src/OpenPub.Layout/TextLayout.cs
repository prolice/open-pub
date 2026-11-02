using OpenPub.Core.Model;

namespace OpenPub.Layout;

/// <summary>Mesure du texte, fournie par la couche d'affichage (WPF) ou par les tests.</summary>
public interface ITextMeasurer
{
    /// <summary>Largeur en points de <paramref name="text"/> dans la police donnee, a la taille <paramref name="sizePt"/>.</summary>
    double Width(string text, FontSpec font, double sizePt);

    /// <summary>Hauteur de ligne simple / taille ((winAscent + winDescent) / unitsPerEm).</summary>
    double LineFactor(FontSpec font);

    /// <summary>Part de la hauteur de ligne situee au-dessus de la ligne de base.</summary>
    double AscentRatio(FontSpec font);
}

/// <summary>Geometrie d'une zone de la chaine : rectangle de contenu (marges deduites) et obstacles.</summary>
public sealed class FrameGeometry
{
    public required Shape Shape { get; init; }
    /// <summary>Rectangle de contenu, coordonnees de page (points).</summary>
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Obstacles d'habillage, coordonnees locales au contenu.</summary>
    public List<Exclusion> Exclusions { get; init; } = new();
}

/// <summary>
/// Zone interdite au texte, sur une bande verticale [Top, Bottom] (coordonnees locales).
/// Left/Right/FullWidth : emprise appliquee au texte (une bande trop etroite est fermee) ;
/// RawLeft/RawRight : emprise reelle de l'objet, utilisee pour les lignes vides.
/// </summary>
public readonly record struct Exclusion(double Top, double Bottom, double Left, double Right, bool FullWidth)
{
    public double RawLeft { get; init; } = Left;
    public double RawRight { get; init; } = Right;
}

public sealed class LaidPiece
{
    public required string Text { get; init; }
    public required FontSpec Font { get; init; }
    public double SizePt { get; init; }
    /// <summary>Position du debut, coordonnees de page.</summary>
    public double X { get; set; }
    public double Width { get; init; }
    /// <summary>Position du morceau dans le texte du paragraphe, et nombre de caracteres source couverts.</summary>
    public int Start { get; init; }
    public int Length { get; init; }
    /// <summary>Le texte affiche se termine par un tiret de cesure absent du texte source.</summary>
    public bool Hyphen { get; init; }
}

public sealed class LaidLine
{
    /// <summary>Haut de la ligne et ligne de base, coordonnees de page.</summary>
    public double Top { get; set; }
    public double Height { get; init; }
    public double Baseline { get; set; }
    public List<LaidPiece> Pieces { get; } = new();
    public int ParagraphIndex { get; init; }
    /// <summary>Plage du texte source couverte par la ligne : [StartOffset, EndOffset[ (espaces de fin compris).</summary>
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    /// <summary>Bords gauche et droit de la bande de texte de la ligne (coordonnees de page).</summary>
    public double Left { get; init; }
    public double Right { get; init; }
}

public sealed class FrameLayout
{
    public required Shape Shape { get; init; }
    public List<LaidLine> Lines { get; } = new();
}

public sealed class ChainLayout
{
    public List<FrameLayout> Frames { get; } = new();
    /// <summary>Le texte ne tient pas, meme apres reduction maximale.</summary>
    public bool Overflow { get; set; }
    /// <summary>Facteur de reduction applique (1 = aucune).</summary>
    public double Scale { get; set; } = 1;
}
