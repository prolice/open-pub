using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OpenPub.App.Editing;

/// <summary>
/// Correcteur orthographique de Windows (API Spell Checking, spellcheck.h) : dictionnaires fournis par Windows
/// (aucun fichier a embarquer), dictionnaire personnel de l'utilisateur partage avec les autres applications.
/// Un verificateur par langue ; resultats mis en cache par texte de paragraphe.
/// </summary>
public sealed class SpellService
{
    readonly ISpellCheckerFactory _factory;
    readonly Dictionary<string, ISpellChecker?> _checkers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, string), List<(int Start, int Length)>> _cache = new();
    readonly HashSet<string> _ignored = new(StringComparer.Ordinal);

    SpellService(ISpellCheckerFactory f) => _factory = f;

    /// <summary>Service disponible (Windows 8 ou plus) ; null si l'API est absente.</summary>
    public static SpellService? TryCreate()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            if (type is null || Activator.CreateInstance(type) is not ISpellCheckerFactory f) return null;
            var svc = new SpellService(f);
            return svc.Checker("fr-FR") is null ? null : svc;
        }
        catch { return null; }
    }

    /// <summary>Etiquette de langue Windows pour la langue d'un segment de texte ("fr", "fr-BE", "en"...).</summary>
    public static string Tag(string? lang) => lang switch
    {
        null or "" => "fr-FR",
        _ when lang.Contains('-') => lang,
        "fr" => "fr-FR", "en" => "en-US", "nl" => "nl-NL", "de" => "de-DE", "es" => "es-ES", "it" => "it-IT",
        _ => lang,
    };

    ISpellChecker? Checker(string tag)
    {
        if (_checkers.TryGetValue(tag, out var c)) return c;
        c = null;
        try
        {
            _factory.IsSupported(tag, out int ok);
            if (ok != 0) { _factory.CreateSpellChecker(tag, out var sc); c = sc; }
        }
        catch { c = null; }
        // langue non installee : on se rabat sur le francais
        if (c is null && !tag.Equals("fr-FR", StringComparison.OrdinalIgnoreCase)) c = Checker("fr-FR");
        _checkers[tag] = c;
        return c;
    }

    /// <summary>Mots mal orthographies (debut, longueur) dans un texte.</summary>
    public List<(int Start, int Length)> Check(string text, string? lang)
    {
        var tag = Tag(lang);
        if (_cache.TryGetValue((tag, text), out var res)) return res;
        res = new List<(int, int)>();
        if (text.Trim().Length > 0 && Checker(tag) is { } c)
        {
            try
            {
                // les caracteres de controle (saut de ligne, tabulation) deviennent des espaces : positions inchangees
                var clean = new string(text.Select(ch => char.IsControl(ch) || ch == '\u00AD' ? ' ' : ch).ToArray());
                c.Check(clean, out var errors);
                while (errors.Next(out var err) == 0 && err is not null)
                {
                    err.get_StartIndex(out uint st);
                    err.get_Length(out uint len);
                    var word = clean.Substring((int)st, (int)len);
                    if (!_ignored.Contains(word)) res.Add(((int)st, (int)len));
                }
            }
            catch (COMException) { /* verificateur indisponible : pas de soulignement */ }
        }
        if (_cache.Count > 4000) _cache.Clear();
        _cache[(tag, text)] = res;
        return res;
    }

    public List<string> Suggest(string word, string? lang)
    {
        var list = new List<string>();
        if (Checker(Tag(lang)) is not { } c) return list;
        try
        {
            c.Suggest(word, out var e);
            var buf = new string[1];
            while (list.Count < 8 && e.Next(1, buf, IntPtr.Zero) == 0) list.Add(buf[0]);
        }
        catch (COMException) { }
        return list;
    }

    /// <summary>Ignorer ce mot pour toute la session.</summary>
    public void Ignore(string word)
    {
        _ignored.Add(word);
        _cache.Clear();
    }

    /// <summary>Ajouter au dictionnaire personnel de Windows.</summary>
    public void Add(string word, string? lang)
    {
        try { Checker(Tag(lang))?.Add(word); } catch (COMException) { }
        _cache.Clear();
    }

    // ------------------------------------------------------------------ interop (spellcheck.h)

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        void get_SupportedLanguages(out IEnumString value);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        void get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string value);
        void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
        void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString value);
        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
        void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
        void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);
        void get_OptionIds(out IEnumString value);
        void get_Id([MarshalAs(UnmanagedType.LPWStr)] out string value);
        void get_LocalizedName([MarshalAs(UnmanagedType.LPWStr)] out string value);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError
    {
        [PreserveSig] int Next(out ISpellingError? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError
    {
        void get_StartIndex(out uint value);
        void get_Length(out uint value);
        void get_CorrectiveAction(out int value);
        void get_Replacement([MarshalAs(UnmanagedType.LPWStr)] out string value);
    }
}
