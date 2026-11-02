using System.Diagnostics;
using System.Reflection;

namespace OpenPub.App.Help;

/// <summary>
/// Liens vers la documentation en ligne (site de mises a jour, dossier /aide/). Les cibles correspondent
/// aux ancres de tools/build-docs.py : ne pas les renommer sans mettre la documentation a jour.
/// </summary>
public static class HelpService
{
    // pages et sections de la documentation
    public const string Home = "index.html";
    public const string Interface = "interface.html#ruban";
    public const string FileMenu = "interface.html#fichier";
    public const string HomeTab = "interface.html#accueil";
    public const string InsertTab = "interface.html#insertion";
    public const string ViewTab = "interface.html#affichage";
    public const string Panels = "interface.html#panneaux";
    public const string TextCreate = "texte.html#creer";
    public const string TextWrite = "texte.html#ecrire";
    public const string TextFormat = "texte.html#mise-en-forme";
    public const string TextLinked = "texte.html#zones-liees";
    public const string TextFit = "texte.html#habillage";
    public const string TextFrame = "texte.html#cadre";
    public const string TextEditor = "texte.html#editeur";
    public const string Images = "images.html#inserer";
    public const string PictureTools = "images.html#outils";
    public const string ImageEditor = "images.html#editeur";
    public const string ImageCrop = "images.html#recadrer";
    public const string Shapes = "formes.html#formes";
    public const string FreeDrawing = "formes.html#dessin-libre";
    public const string ShapeFormat = "formes.html#format";
    public const string Colors = "formes.html#couleurs";
    public const string Arrange = "disposition.html#selection";
    public const string Position = "disposition.html#position";
    public const string Rotation = "disposition.html#rotation";
    public const string Snapping = "disposition.html#magnetisme";
    public const string Pages = "pages.html#gerer";
    public const string NextIssue = "pages.html#numero-suivant";
    public const string Print = "impression.html#imprimer";
    public const string Pdf = "impression.html#pdf";
    public const string Updates = "mises-a-jour.html#automatiques";
    public const string Shortcuts = "raccourcis.html#general";
    public const string Faq = "faq.html#polices";
    // version 2.0
    public const string ParagraphFormat = "texte.html#paragraphe";
    public const string Lists = "texte.html#listes";
    public const string Tabs = "texte.html#tabulations";
    public const string TextBoxOptions = "texte.html#zone-options";
    public const string MultiSelect = "disposition.html#plusieurs";
    public const string Groups = "disposition.html#grouper";
    public const string Review = "revision.html#orthographe";
    public const string FindReplace = "revision.html#rechercher";
    public const string Recovery = "interface.html#recuperation";
    // version 2.1 : sorties (lot 10)
    public const string PrintPreview = "impression.html#apercu";
    public const string Booklet = "impression.html#livret";
    public const string ManualDuplex = "impression.html#recto-verso";
    public const string PdfForPrinter = "impression.html#imprimeur";
    public const string ImageExport = "impression.html#png";

    public static IReadOnlyList<string> AllTopics => typeof(HelpService).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToList();

    /// <summary>Adresse de base de l'aide (definie a la compilation, voir OpenPub.App.csproj).</summary>
    public static string? BaseUrl => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "HelpBase")?.Value;

    public static string UrlOf(string topic) => (BaseUrl ?? "https://calm-pebble-0080d6003.5.azurestaticapps.net/aide/").TrimEnd('/') + "/" + topic;

    /// <summary>Ouvre une page d'aide dans le navigateur par defaut.</summary>
    public static void Open(string topic)
    {
        try { Process.Start(new ProcessStartInfo(UrlOf(topic)) { UseShellExecute = true }); }
        catch (Exception) { /* pas de navigateur : rien a faire */ }
    }
}
