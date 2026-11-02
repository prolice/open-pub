using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OpenPub.App.Controls;
using OpenPub.Core.Output;

namespace OpenPub.App.Output;

/// <summary>Disposition de l'impression.</summary>
public enum PrintMode
{
    /// <summary>Une page par feuille.</summary>
    Normal,
    /// <summary>Livret : 2 pages par face d'une feuille paysage, dans l'ordre de pliage.</summary>
    Booklet,
    /// <summary>Recto verso manuel : rectos, puis la pile retournee, versos.</summary>
    ManualDuplex,
    /// <summary>Livret sur une imprimante sans recto verso automatique : faces avant, puis faces arriere.</summary>
    BookletManualDuplex,
}

public sealed record PrintOptions
{
    /// <summary>Pages choisies (numeros a partir de 1).</summary>
    public required IReadOnlyList<int> Pages { get; init; }
    public int Copies { get; init; } = 1;
    public PrintMode Mode { get; init; } = PrintMode.Normal;
    /// <summary>Recto verso manuel : imprimer les versos dans l'ordre inverse (imprimantes qui empilent face dessus).</summary>
    public bool ReverseBacks { get; init; }
}

/// <summary>Une face imprimee : une page (Right = -1) ou deux pages cote a cote (0 = page blanche).</summary>
public readonly record struct PrintFace(int Left, int Right)
{
    public static readonly PrintFace Blank = new(0, -1);
    public bool TwoUp => Right >= 0;
    public override string ToString() => TwoUp ? $"{(Left == 0 ? "blanc" : Left)}-{(Right == 0 ? "blanc" : Right)}" : Left == 0 ? "blanc" : Left.ToString();
}

/// <summary>
/// Impression : choix des pages, exemplaires (assembles), livret (imposition 2 pages par face), recto verso manuel guide.
/// Les faces sont des FixedPage WPF (memes vues que l'ecran, sans les reperes), envoyees a l'imprimante choisie.
/// </summary>
public static class PrintService
{
    /// <summary>
    /// Passages d'impression : un seul (normal, livret sur imprimante recto verso) ou deux (rectos puis versos).
    /// Les exemplaires sont assembles : le document entier est repete.
    /// </summary>
    public static List<List<PrintFace>> Passes(PrintOptions o)
    {
        var pages = o.Pages;
        int copies = Math.Max(1, o.Copies);
        List<PrintFace> OneCopy()
        {
            if (o.Mode is PrintMode.Booklet or PrintMode.BookletManualDuplex)
            {
                // livret des pages choisies, numerotees dans leur ordre : la page k du livret est pages[k-1]
                return Booklet.Sides(pages.Count).Select(s => new PrintFace(s.Left == 0 ? 0 : pages[s.Left - 1], s.Right == 0 ? 0 : pages[s.Right - 1])).ToList();
            }
            return pages.Select(p => new PrintFace(p, -1)).ToList();
        }
        var faces = new List<PrintFace>();
        for (int c = 0; c < copies; c++)
        {
            var one = OneCopy();
            // recto verso : chaque exemplaire commence sur une nouvelle feuille
            if (o.Mode == PrintMode.ManualDuplex && one.Count % 2 == 1 && c < copies - 1) one.Add(PrintFace.Blank);
            faces.AddRange(one);
        }
        if (o.Mode is PrintMode.ManualDuplex or PrintMode.BookletManualDuplex)
        {
            var blank = o.Mode == PrintMode.BookletManualDuplex ? new PrintFace(0, 0) : PrintFace.Blank;
            var (fronts, backs) = ManualDuplex.Split(faces, blank, o.ReverseBacks);
            return new List<List<PrintFace>> { fronts, backs };
        }
        return new List<List<PrintFace>> { faces };
    }

    /// <summary>Format de feuille : celui de la page (normal), ou feuille paysage (livret).</summary>
    public static Size SheetSize(EditorSession session, PrintMode mode, Size? paper)
    {
        var first = session.Doc.Pages[0];
        if (mode is PrintMode.Booklet or PrintMode.BookletManualDuplex)
        {
            // papier de l'imprimante (A4 a defaut), tourne en paysage
            var p = paper ?? new Size(595.276 * PageView.DipPerPoint, 841.89 * PageView.DipPerPoint);
            return new Size(Math.Max(p.Width, p.Height), Math.Min(p.Width, p.Height));
        }
        return new Size(first.Width * PageView.DipPerPoint, first.Height * PageView.DipPerPoint);
    }

    /// <summary>Document WPF d'un passage : une FixedPage par face (unites : 1/96 de pouce).</summary>
    public static FixedDocument BuildDocument(EditorSession session, IEnumerable<PrintFace> faces, PrintMode mode, Size? paper = null)
    {
        var doc = new FixedDocument();
        var sheet = SheetSize(session, mode, paper);
        doc.DocumentPaginator.PageSize = sheet;
        foreach (var face in faces)
        {
            FixedPage fp;
            if (face.TwoUp)
            {
                fp = new FixedPage { Width = sheet.Width, Height = sheet.Height, Background = Brushes.White };
                // deux pages cote a cote, mises a l'echelle de la demi-feuille, centrees
                var ref0 = session.Doc.Pages[0];
                double pw = ref0.Width * PageView.DipPerPoint, ph = ref0.Height * PageView.DipPerPoint;
                double scale = Math.Min(sheet.Width / 2 / pw, sheet.Height / ph);
                double ox = (sheet.Width - 2 * pw * scale) / 2, oy = (sheet.Height - ph * scale) / 2;
                int slot = 0;
                foreach (var n in new[] { face.Left, face.Right })
                {
                    if (n > 0)
                    {
                        var view = Exporter.MakeView(session, session.Doc.Pages[n - 1], scale);
                        view.Tag = n;
                        FixedPage.SetLeft(view, ox + slot * pw * scale);
                        FixedPage.SetTop(view, oy);
                        fp.Children.Add(view);
                    }
                    slot++;
                }
            }
            else
            {
                var page = face.Left > 0 ? session.Doc.Pages[face.Left - 1] : session.Doc.Pages[0];
                fp = new FixedPage { Width = page.Width * PageView.DipPerPoint, Height = page.Height * PageView.DipPerPoint, Background = Brushes.White };
                if (face.Left > 0)
                {
                    var view = Exporter.MakeView(session, page);
                    view.Tag = face.Left;
                    fp.Children.Add(view);
                }
            }
            fp.Tag = face;
            var pc = new PageContent();
            ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
            doc.Pages.Add(pc);
        }
        return doc;
    }

    /// <summary>Faces d'un document construit (dans l'ordre), pour l'auto-test et l'apercu.</summary>
    public static List<PrintFace> FacesOf(FixedDocument doc) => doc.Pages.Select(p => (PrintFace)p.Child.Tag).ToList();

    /// <summary>
    /// Imprime : boite d'impression de Windows (choix de l'imprimante), puis un ou deux passages.
    /// <paramref name="confirmSecondPass"/> affiche la consigne « retournez la pile » et renvoie faux pour abandonner.
    /// </summary>
    public static bool Print(EditorSession session, PrintOptions o, string title, Func<bool> confirmSecondPass)
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = false };
        bool booklet = o.Mode is PrintMode.Booklet or PrintMode.BookletManualDuplex;
        var first = session.Doc.Pages[0];
        try
        {
            if (booklet) dlg.PrintTicket.PageOrientation = PageOrientation.Landscape;
            else
            {
                dlg.PrintTicket.PageMediaSize = new PageMediaSize(first.Width * PageView.DipPerPoint, first.Height * PageView.DipPerPoint);
                dlg.PrintTicket.PageOrientation = first.Width > first.Height ? PageOrientation.Landscape : PageOrientation.Portrait;
            }
            if (o.Mode == PrintMode.Booklet) dlg.PrintTicket.Duplexing = Duplexing.TwoSidedShortEdge;
            dlg.PrintTicket.CopyCount = 1;   // les exemplaires sont dans le document (assembles)
        }
        catch (Exception) { /* imprimante sans ces reglages : valeurs par defaut */ }
        if (dlg.ShowDialog() != true) return false;
        Size? paper = null;
        if (dlg.PrintTicket.PageMediaSize is { Width: { } mw, Height: { } mh }) paper = new Size(mw, mh);
        var passes = Passes(o);
        for (int i = 0; i < passes.Count; i++)
        {
            if (i > 0 && !confirmSecondPass()) return false;
            if (passes[i].Count == 0) continue;
            var doc = BuildDocument(session, passes[i], o.Mode, paper);
            dlg.PrintDocument(doc.DocumentPaginator, passes.Count > 1 ? $"{title} ({(i == 0 ? "rectos" : "versos")})" : title);
        }
        return true;
    }
}
