namespace OpenPub.Core.Editing;

/// <summary>
/// Correction typographique a la frappe, selon l'usage francais : guillemets « », apostrophe courbe,
/// espace insecable avant : ; ! ? et apres « / avant ».
/// </summary>
public static class FrenchTypography
{
    public const char Nbsp = '\u00A0';
    public const char NarrowNbsp = '\u202F';

    /// <summary>
    /// Texte a inserer a la place du caractere tape, et nombre de caracteres a effacer avant le curseur
    /// (une espace ordinaire remplacee par une insecable). Null : rien a corriger.
    /// </summary>
    public static (string Insert, int DeleteBefore)? Correct(string before, char typed)
    {
        char prev = before.Length > 0 ? before[^1] : '\0';
        bool afterSpace = prev is '\0' or ' ' or '\t' or '\n' or '\v' or '(' or '[' or Nbsp or NarrowNbsp or '\u2028';
        switch (typed)
        {
            case '"':
                if (afterSpace) return ("«" + Nbsp, 0);
                return (Nbsp + "»", 0);
            case '\'':
                return ("’", 0);
            case ':':
                if (prev == ' ') return (Nbsp + ":", 1);
                return null;
            case ';' or '!' or '?':
                if (prev == ' ') return (NarrowNbsp + typed.ToString(), 1);
                return null;
            default:
                return null;
        }
    }
}
