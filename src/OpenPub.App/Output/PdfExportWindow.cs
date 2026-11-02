using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenPub.Core.Output;

namespace OpenPub.App.Output;

/// <summary>Boite « Exporter en PDF » : pages, puis options pour l'imprimeur (fond perdu, traits de coupe, PDF/X-4).</summary>
public sealed class PdfExportWindow : Window
{
    readonly PageChooser _pages;
    readonly CheckBox _bleed = new() { Content = "Fond perdu", VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _bleedMm = OutputUi.Box("3", 50);
    readonly CheckBox _marks = new() { Content = "Traits de coupe", Margin = new Thickness(0, 4, 0, 0) };
    readonly CheckBox _pdfx = new() { Content = "PDF/X-4 (profil d'impression)", Margin = new Thickness(0, 4, 0, 0) };
    readonly ComboBox _profile = new() { MinWidth = 260, Margin = new Thickness(22, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    readonly CheckBox _open = new() { Content = "Ouvrir le PDF une fois créé", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };

    public PdfExportOptions? Result { get; private set; }
    public bool OpenAfter => _open.IsChecked == true;

    public PdfExportWindow(int pageCount, int activePage)
    {
        OutputUi.Style(this, "Exporter en PDF", Help.HelpService.Pdf);
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        _pages = new PageChooser(pageCount, activePage);

        var root = new StackPanel { Margin = new Thickness(18), Width = 440 };
        root.Children.Add(OutputUi.Section("Pages"));
        root.Children.Add(_pages);
        root.Children.Add(OutputUi.Section("Pour l'imprimeur"));
        var bleedRow = new StackPanel { Orientation = Orientation.Horizontal };
        bleedRow.Children.Add(_bleed);
        _bleedMm.Margin = new Thickness(10, 0, 4, 0);
        bleedRow.Children.Add(_bleedMm);
        bleedRow.Children.Add(new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(bleedRow);
        root.Children.Add(OutputUi.Note("La page est agrandie de chaque côté ; les fonds et les images qui touchent le bord débordent dans cette marge, qui sera coupée."));
        root.Children.Add(_marks);
        root.Children.Add(_pdfx);
        root.Children.Add(_profile);
        var browse = new Button { Content = "Autre profil…", Margin = new Thickness(22, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
        root.Children.Add(browse);
        root.Children.Add(OutputUi.Note("PDF/X-4 : profil CMJN de l'imprimeur incorporé (FOGRA39 par défaut en Europe), couleurs RVB étiquetées sRGB ; " +
                                        "la conversion en CMJN est faite par l'imprimeur."));
        root.Children.Add(_open);
        root.Children.Add(OutputUi.Buttons(this, "Exporter…", Build));
        Content = root;

        foreach (var p in PdfExporter.CmykProfiles())
            _profile.Items.Add(new ComboBoxItem { Content = PdfExporter.IccInfo(p)?.Description ?? Path.GetFileName(p), Tag = p, ToolTip = p });
        if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = "Profil ICC CMJN de l'imprimeur", Filter = "Profils ICC (*.icc;*.icm)|*.icc;*.icm" };
            if (dlg.ShowDialog(this) != true) return;
            if (PdfExporter.IccInfo(dlg.FileName) is not { ColorSpace: "CMYK" } info)
            {
                MessageBox.Show(this, "Ce fichier n'est pas un profil CMJN.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var it = new ComboBoxItem { Content = info.Description, Tag = dlg.FileName, ToolTip = dlg.FileName };
            _profile.Items.Insert(0, it);
            _profile.SelectedItem = it;
            _pdfx.IsChecked = true;
        };
        void Sync()
        {
            _bleedMm.IsEnabled = _bleed.IsChecked == true;
            _profile.IsEnabled = browse.IsEnabled = _pdfx.IsChecked == true;
        }
        _bleed.Checked += (_, _) => Sync(); _bleed.Unchecked += (_, _) => Sync();
        _pdfx.Checked += (_, _) => { if (_bleed.IsChecked != true) _bleed.IsChecked = true; Sync(); };
        _pdfx.Unchecked += (_, _) => Sync();
        Sync();
    }

    bool Build()
    {
        var pages = _pages.Pages(out var err);
        if (pages is null) { MessageBox.Show(this, err, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        double bleed = 0;
        if (_bleed.IsChecked == true)
        {
            if (OutputUi.Number(_bleedMm.Text) is not { } mm || mm < 0 || mm > 20)
            {
                MessageBox.Show(this, "Le fond perdu doit être compris entre 0 et 20 mm.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            bleed = mm * PrintSheet.PtPerMm;
        }
        string? profile = null;
        if (_pdfx.IsChecked == true)
        {
            profile = OutputUi.Tag(_profile) as string;
            if (profile is null)
            {
                MessageBox.Show(this, "Aucun profil CMJN n'est installé : choisissez le profil fourni par l'imprimeur (« Autre profil… »).", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        Result = new PdfExportOptions { Pages = pages, Bleed = bleed, CropMarks = _marks.IsChecked == true, PdfX4 = _pdfx.IsChecked == true, OutputProfile = profile };
        return true;
    }
}
