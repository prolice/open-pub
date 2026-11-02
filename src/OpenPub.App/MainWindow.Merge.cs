using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Publipostage (Insertion ▸ Publipostage), liens hypertextes, texte de remplacement des images,
// export d'une page web autonome (e-mail, site).
public partial class MainWindow
{
    // ---------------------------------------------------------------- publipostage

    void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var doc = _session.Doc;
        var menu = NewMenu(sender);
        Item(menu, "Choisir la liste (fichier CSV)…", PickMergeFile);
        Item(menu, "Coller la liste (copiée depuis Excel)", PasteMergeList);
        if (doc.Merge is { } src)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = $"Liste : {src.Name ?? "sans nom"} · {src.Rows.Count} ligne(s) · {src.Columns.Count} colonne(s)", IsEnabled = false });
            var fields = new MenuItem { Header = "Insérer un champ" };
            foreach (var col in src.Columns)
            {
                var c = col;
                var it = new MenuItem { Header = $"«{c}»" };
                it.Click += (_, _) => InsertField(MergeOps.Prefix + c);
                fields.Items.Add(it);
            }
            menu.Items.Add(fields);
            menu.Items.Add(new Separator());
            Item(menu, _session.MergePreview < 0 ? "Aperçu avec la première ligne" : "Afficher les noms des champs", () => SetMergePreview(_session.MergePreview < 0 ? 0 : -1));
            if (_session.MergePreview >= 0)
            {
                Item(menu, $"Ligne suivante ({Math.Min(_session.MergePreview + 2, src.Rows.Count)} sur {src.Rows.Count})", () => SetMergePreview(Math.Min(_session.MergePreview + 1, src.Rows.Count - 1)));
                Item(menu, "Ligne précédente", () => SetMergePreview(Math.Max(0, _session.MergePreview - 1)));
            }
            menu.Items.Add(new Separator());
            Item(menu, $"Fusionner : nouveau document ({src.Rows.Count * doc.Pages.Count} pages)", MergeToNewDocument);
            Item(menu, "Retirer la liste", () => { _session.BeginChange(); doc.Merge = null; _session.EndChange(); SetMergePreview(-1); });
        }
        menu.IsOpen = true;
    }

    void SetMergePreview(int row)
    {
        if (_session is null) return;
        _session.MergePreview = row;
        _session.StructureChanged();
        if (row >= 0 && _session.Doc.Merge is { } src)
            StatusText.Text = $"Aperçu du publipostage : ligne {row + 1} sur {src.Rows.Count}.";
    }

    void SetMergeSource(MergeSource src)
    {
        if (_session is null) return;
        if (src.Columns.Count == 0 || src.Rows.Count == 0)
        {
            MessageBox.Show(this, "La liste est vide : la première ligne doit contenir les noms des colonnes, puis une ligne par destinataire.",
                "Publipostage", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _session.BeginChange();
        _session.Doc.Merge = src;
        _session.EndChange();
        StatusText.Text = $"Liste « {src.Name} » : {src.Rows.Count} ligne(s), colonnes {string.Join(", ", src.Columns)}. Insérez les champs dans le texte (Publipostage ▸ Insérer un champ).";
    }

    void PickMergeFile()
    {
        var dlg = new OpenFileDialog { Filter = "Listes (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|Tous les fichiers|*.*", Title = "Liste des destinataires" };
        if (dlg.ShowDialog(this) != true) return;
        string text;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            // UTF-8 si valide, sinon Windows-1252 (CSV enregistre par Excel)
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding(1252).GetString(bytes); }
        }
        catch (IOException ex) { MessageBox.Show(this, ex.Message, "Publipostage", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        SetMergeSource(MergeOps.Parse(text, Path.GetFileNameWithoutExtension(dlg.FileName)));
    }

    void PasteMergeList()
    {
        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch (System.Runtime.InteropServices.ExternalException) { }
        if (string.IsNullOrWhiteSpace(text)) { StatusText.Text = "Copiez d'abord la liste dans Excel (noms des colonnes compris)."; return; }
        SetMergeSource(MergeOps.Parse(text, "Liste collée"));
    }

    void MergeToNewDocument()
    {
        if (_session?.Doc.Merge is not { } src) return;
        if (!MergeOps.HasFields(_session.Doc) &&
            MessageBox.Show(this, "Le document ne contient aucun champ de publipostage : toutes les copies seront identiques. Continuer ?", "Publipostage",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (!ConfirmDiscard()) return;
        _session.EndTextEdit();
        var merged = MergeOps.Merged(_session.Doc);
        // les images du document d'origine sont recopiees dans le nouveau paquet
        var pkg = OpubPackage.CreateFromDocument(merged);
        var assets = Path.Combine(_session.Package.WorkDir, "assets");
        if (Directory.Exists(assets))
        {
            Directory.CreateDirectory(Path.Combine(pkg.WorkDir, "assets"));
            foreach (var f in Directory.GetFiles(assets)) File.Copy(f, Path.Combine(pkg.WorkDir, "assets", Path.GetFileName(f)), true);
        }
        var fonts = _session.Package.FontsDir;
        if (Directory.Exists(fonts))
        {
            Directory.CreateDirectory(pkg.FontsDir);
            foreach (var f in Directory.GetFiles(fonts)) File.Copy(f, Path.Combine(pkg.FontsDir, Path.GetFileName(f)), true);
        }
        LoadPackage(pkg);
        StatusText.Text = $"Document fusionné : {src.Rows.Count} destinataire(s), {merged.Pages.Count} pages. Imprimez-le ou exportez-le.";
    }

    // ---------------------------------------------------------------- liens hypertextes

    void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        Item(menu, "Ajouter un lien…", AddLink);
        Item(menu, "Retirer le lien", RemoveLink);
        menu.IsOpen = true;
    }

    static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Contains('@') && !url.Contains("://") && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return "mailto:" + url;
        if (!url.Contains(':')) return "https://" + url;
        return url;
    }

    void AddLink()
    {
        if (_session is null) return;
        var current = _session.TextEdit is { } ed0 ? StoryEditor.FontAt(ed0.Paras, ed0.SelStart) is not null ? RunAt(ed0.Paras, ed0.SelStart)?.Link : null : Sel?.Link;
        var url = AskText("Lien hypertexte", "Adresse (site web ou adresse e-mail) :", current ?? "https://");
        if (url is null || url == "https://") return;
        url = NormalizeUrl(url);
        if (_session.TextEdit is { HasSelection: true } ed)
        {
            var a = ed.SelStart; var b = ed.SelEnd;
            _session.Change(ed.Page, () =>
            {
                StoryEditor.ApplyFont(ed.Paras, a, b, f => { f.Underline = true; f.Color = "#0563c1"; });
                SetRunLinks(ed.Paras, a, b, url);
            });
            RefocusPage();
        }
        else if (_session.TextEdit is not null) { StatusText.Text = "Sélectionnez d'abord le texte du lien."; return; }
        else if (SelPage is { } page && SelectedShapes() is { Count: > 0 } sel)
            _session.Change(page, () => { foreach (var s in sel) s.Link = url; });
        else { StatusText.Text = "Sélectionnez un texte ou un objet."; return; }
        StatusText.Text = $"Lien vers {url} ajouté (Ctrl + clic pour l'ouvrir).";
    }

    void RemoveLink()
    {
        if (_session is null) return;
        if (_session.TextEdit is { } ed)
        {
            var (a, b) = ed.HasSelection ? (ed.SelStart, ed.SelEnd) : ed.FormatRange();
            _session.Change(ed.Page, () => SetRunLinks(ed.Paras, a, b, null));
        }
        else if (SelPage is { } page && SelectedShapes() is { Count: > 0 } sel)
            _session.Change(page, () => { foreach (var s in sel) s.Link = null; });
    }

    static Run? RunAt(List<Paragraph> paras, TextPos pos)
    {
        if (pos.Para >= paras.Count) return null;
        int acc = 0;
        foreach (var r in paras[pos.Para].Runs) { if (pos.Offset < acc + r.Text.Length) return r; acc += r.Text.Length; }
        return paras[pos.Para].Runs.LastOrDefault();
    }

    /// <summary>Lien sur les segments de la plage (decoupes a ses bornes).</summary>
    static void SetRunLinks(List<Paragraph> paras, TextPos a, TextPos b, string? url)
    {
        StoryEditor.ApplyRun(paras, a, b, r => r.Link = url);
    }

    /// <summary>Ctrl + clic sur un texte ou un objet lie : ouvre le lien.</summary>
    internal bool TryOpenLink(Page page, Point p)
    {
        if (_session is null) return false;
        string? url = null;
        var layout = _session.Layout(page);
        foreach (var (shape, fl) in layout.Frames)
        {
            var head = shape.Chain is null ? shape : page.ChainFrames(shape.Chain).FirstOrDefault() ?? shape;
            if (head.Paragraphs is not { } paras) continue;
            foreach (var line in fl.Lines)
                foreach (var pc in line.Pieces)
                    if (p.X >= pc.X && p.X <= pc.X + pc.Width && p.Y >= line.Top && p.Y <= line.Top + line.Height
                        && RunAt(paras, new TextPos(line.ParagraphIndex, pc.Start))?.Link is { } l) url = l;
        }
        url ??= _views.FirstOrDefault(v => v.Page == page)?.HitTest(p)?.Link;
        if (url is null) return false;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { StatusText.Text = "Impossible d'ouvrir le lien : " + url; }
        return true;
    }

    // ---------------------------------------------------------------- texte de remplacement

    void AltText_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { } s || SelPage is not { } page) return;
        var text = AskText("Texte de remplacement", "Décrivez l'image en quelques mots (lu par les lecteurs d'écran, affiché si l'image ne s'affiche pas) :", s.AltText ?? "");
        if (text is null) return;
        _session.Change(page, () => s.AltText = text);
        StatusText.Text = "Texte de remplacement enregistré.";
    }

    // ---------------------------------------------------------------- export web / e-mail

    void ExportHtml_Click()
    {
        if (_session is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "Page web (*.html)|*.html",
            FileName = Path.GetFileNameWithoutExtension(_session.Package.FilePath ?? _session.Doc.Source ?? "document") + ".html",
            Title = "Exporter pour le web ou un e-mail",
        };
        if (dlg.ShowDialog(this) != true) return;
        _session.Select(null);
        File.WriteAllText(dlg.FileName, BuildHtml(_session, 1.5), new UTF8Encoding(false));
        StatusText.Text = $"Page web enregistrée : {dlg.FileName} (images incluses, liens actifs).";
    }

    /// <summary>Zones cliquables d'une page (points) : morceaux de texte lies et objets lies.</summary>
    internal static List<(Rect Area, string Url)> LinkAreas(EditorSession session, Page page)
    {
        var res = new List<(Rect, string)>();
        foreach (var (shape, fl) in session.Layout(page).Frames)
        {
            var head = shape.Chain is null ? shape : page.ChainFrames(shape.Chain).FirstOrDefault() ?? shape;
            if (head.Paragraphs is not { } paras) continue;
            foreach (var line in fl.Lines)
                foreach (var pc in line.Pieces)
                    if (RunAt(paras, new TextPos(line.ParagraphIndex, pc.Start))?.Link is { } l) res.Add((new Rect(pc.X, line.Top, pc.Width, line.Height), l));
        }
        foreach (var s in page.Shapes.Where(s => s.Link is not null))
        {
            var bb = s.Bounds();
            res.Add((new Rect(bb.X, bb.Y, bb.W, bb.H), s.Link!));
        }
        return res;
    }

    /// <summary>Image PNG d'une page, sans reperes d'edition.</summary>
    static byte[] RenderPagePng(EditorSession session, Page page, double dpi)
    {
        var view = new Controls.PageView(session, page) { Interactive = false, Zoom = 1 };
        var size = new Size(page.Width * Controls.PageView.DipPerPoint, page.Height * Controls.PageView.DipPerPoint);
        view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        int w = (int)Math.Ceiling(size.Width * dpi / 96), h = (int)Math.Ceiling(size.Height * dpi / 96);
        var rtb = new RenderTargetBitmap(w, h, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(view);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Page web autonome : chaque page en image (incluse dans le fichier), zones cliquables pour les liens,
    /// textes de remplacement des images et texte des pages pour l'accessibilite et la recherche.
    /// </summary>
    internal static string BuildHtml(EditorSession session, double scale)
    {
        var doc = session.Doc;
        var sb = new StringBuilder();
        string title = WebUtility.HtmlEncode(doc.Source ?? "Document");
        sb.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append($"<title>{title}</title><style>body{{margin:0;background:#eef2f8;font-family:Arial,sans-serif}}.page{{max-width:{doc.PageWidth * scale:0}px;margin:16px auto;background:#fff;box-shadow:0 4px 16px rgba(17,28,67,.15)}}.page img{{display:block;width:100%;height:auto}}.sr{{position:absolute;left:-9999px}}</style></head><body>");
        int n = 0;
        foreach (var page in doc.Pages)
        {
            n++;
            var png = RenderPagePng(session, page, 72 * scale);
            var b64 = Convert.ToBase64String(png);
            int w = (int)Math.Round(page.Width * scale), h = (int)Math.Round(page.Height * scale);
            var alt = string.Join(" ; ", page.Shapes.Where(s => s.AltText is { Length: > 0 }).Select(s => s.AltText));
            sb.Append($"<div class=\"page\"><img src=\"data:image/png;base64,{b64}\" width=\"{w}\" height=\"{h}\" alt=\"{WebUtility.HtmlEncode($"Page {n}" + (alt.Length > 0 ? " : " + alt : ""))}\" usemap=\"#p{n}\">");
            // zones cliquables : objets lies, morceaux de texte lies
            sb.Append($"<map name=\"p{n}\">");
            string Area(double x, double y, double ww, double hh, string url) =>
                $"<area shape=\"rect\" coords=\"{(int)(x * scale)},{(int)(y * scale)},{(int)((x + ww) * scale)},{(int)((y + hh) * scale)}\" href=\"{WebUtility.HtmlEncode(url)}\" alt=\"{WebUtility.HtmlEncode(url)}\">";
            foreach (var (r, url) in LinkAreas(session, page)) sb.Append(Area(r.X, r.Y, r.Width, r.Height, url));
            sb.Append("</map>");
            // texte de la page (lecteurs d'ecran, moteurs de recherche)
            var text = string.Join("\n", page.Shapes.Where(s => s.IsStoryHead && s.Paragraphs is not null).SelectMany(s => s.Paragraphs!).Select(p => p.PlainText).Where(t => t.Trim().Length > 0));
            if (text.Length > 0) sb.Append("<div class=\"sr\">").Append(WebUtility.HtmlEncode(text).Replace("\n", "<br>")).Append("</div>");
            sb.Append("</div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
