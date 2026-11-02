using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenPub.App.Output;

namespace OpenPub.App;

// Sorties : impression (apercu, livret, recto verso manuel), export PDF direct, export d'images.
public partial class MainWindow
{
    /// <summary>Entrees du menu Fichier consacrees aux sorties.</summary>
    void AddOutputItems(ContextMenu menu, bool has)
    {
        void Add(string header, string gesture, Action act, string glyph)
        {
            var it = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = has };
            it.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Foreground = (Brush)FindResource("IconBrush") };
            it.Click += (_, _) => act();
            menu.Items.Add(it);
        }
        Add("Imprimer…", "Ctrl+P", () => ApplicationCommands.Print.Execute(null, this), "");
        Add("Exporter en PDF…", "", ExportPdf, "");
        Add("Exporter en images (PNG, JPEG)…", "", ExportImages, "");
    }

    string DocumentTitle => Path.GetFileNameWithoutExtension(_session?.Package.FilePath ?? _session?.Doc.Source ?? "OpenPub");

    int ActivePageNumber => _session is not null && ActivePage() is { } p ? _session.Doc.Pages.IndexOf(p) + 1 : 1;

    /// <summary>Derniere boite de sortie ouverte (auto-test).</summary>
    internal Window? LastOutputWindow { get; private set; }

    void ShowPrintWindow()
    {
        if (_session is null || _session.Doc.Pages.Count == 0) return;
        _session.EndTextEdit();
        _session.Select(null);
        var win = new PrintWindow(_session, ActivePageNumber) { Owner = this };
        LastOutputWindow = win;
        if (win.ShowDialog() != true || win.Result is not { } o) return;
        bool done = PrintService.Print(_session, o, DocumentTitle, () =>
            MessageBox.Show(this,
                "Les rectos sont imprimés.\n\nRetournez la pile de feuilles imprimées et remettez-la dans le bac d'alimentation " +
                "(face imprimée selon votre imprimante, haut de la page vers l'intérieur), puis cliquez sur OK pour imprimer les versos.",
                "Recto verso manuel", MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK);
        if (done) StatusText.Text = "Impression envoyée à l'imprimante.";
    }

    void ExportPdf()
    {
        if (_session is null || _session.Doc.Pages.Count == 0) return;
        _session.EndTextEdit();
        var win = new PdfExportWindow(_session.Doc.Pages.Count, ActivePageNumber) { Owner = this };
        LastOutputWindow = win;
        if (win.ShowDialog() != true || win.Result is not { } o) return;
        var dlg = new SaveFileDialog
        {
            Title = "Exporter en PDF", Filter = "Document PDF (*.pdf)|*.pdf", DefaultExt = ".pdf",
            FileName = DocumentTitle + (o.Bleed > 0 || o.PdfX4 ? " - imprimeur" : "") + ".pdf",
            InitialDirectory = Path.GetDirectoryName(_session.Package.FilePath ?? "") is { Length: > 0 } d ? d : null,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            StatusText.Text = "Création du PDF…";
            var sw = Stopwatch.StartNew();
            PdfExporter.Export(_session, dlg.FileName, o with { Title = DocumentTitle });
            StatusText.Text = $"PDF créé ({o.Pages?.Count ?? _session.Doc.Pages.Count} page(s), {new FileInfo(dlg.FileName).Length / 1048576.0:0.0} Mo, {sw.Elapsed.TotalSeconds:0.0} s) : {dlg.FileName}";
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, "Le PDF n'a pas pu être créé :\n" + ex.Message, "Exporter en PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally { Mouse.OverrideCursor = null; }
        if (win.OpenAfter)
            try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); }
            catch (Exception) { /* pas de lecteur PDF : le fichier est cree quand meme */ }
    }

    void ExportImages()
    {
        if (_session is null || _session.Doc.Pages.Count == 0) return;
        _session.EndTextEdit();
        _session.Select(null);
        var win = new ImageExportWindow(_session.Doc.Pages.Count, ActivePageNumber) { Owner = this };
        LastOutputWindow = win;
        if (win.ShowDialog() != true || win.Pages is not { } pages) return;
        string ext = win.Format == ImageFormat.Jpeg ? "jpg" : "png";
        string dir;
        string? single = null;
        if (pages.Count == 1)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Exporter la page en image", DefaultExt = "." + ext, FileName = $"{DocumentTitle} - page {pages[0]}.{ext}",
                Filter = win.Format == ImageFormat.Jpeg ? "Image JPEG (*.jpg)|*.jpg" : "Image PNG (*.png)|*.png",
            };
            if (dlg.ShowDialog(this) != true) return;
            single = dlg.FileName;
            dir = Path.GetDirectoryName(single)!;
        }
        else
        {
            var dlg = new OpenFolderDialog { Title = "Dossier de destination des images" };
            if (dlg.ShowDialog(this) != true) return;
            dir = dlg.FolderName;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var files = Exporter.ExportImages(_session, dir, win.Format, win.Dpi, pages, single);
            StatusText.Text = files.Count == 1 ? $"Image exportée : {files[0]}" : $"{files.Count} pages exportées dans {dir}";
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, "L'export a échoué :\n" + ex.Message, "Exporter en images", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }
}
