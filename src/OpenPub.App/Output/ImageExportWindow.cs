using System.Windows;
using System.Windows.Controls;

namespace OpenPub.App.Output;

/// <summary>Boite « Exporter en images » : format (PNG, JPEG), resolution, pages.</summary>
public sealed class ImageExportWindow : Window
{
    readonly ComboBox _format = OutputUi.Combo(("PNG (sans perte)", ImageFormat.Png), ("JPEG (photos, fichiers légers)", ImageFormat.Jpeg));
    readonly ComboBox _dpi = new() { IsEditable = true, Width = 90, Text = "200" };
    readonly PageChooser _pages;

    public ImageFormat Format { get; private set; }
    public double Dpi { get; private set; }
    public List<int>? Pages { get; private set; }
    public bool ActiveOnly => _pages.ActiveOnly;

    public ImageExportWindow(int pageCount, int activePage)
    {
        OutputUi.Style(this, "Exporter en images", Help.HelpService.ImageExport);
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        _pages = new PageChooser(pageCount, activePage);
        foreach (var v in new[] { "96", "150", "200", "300", "600" }) _dpi.Items.Add(v);
        _dpi.Text = "200";

        var root = new StackPanel { Margin = new Thickness(18), Width = 400 };
        root.Children.Add(OutputUi.Section("Image"));
        root.Children.Add(OutputUi.Row("Format", _format, 100));
        var dpiRow = new StackPanel { Orientation = Orientation.Horizontal };
        dpiRow.Children.Add(_dpi);
        dpiRow.Children.Add(new TextBlock { Text = "ppp (points par pouce)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        root.Children.Add(OutputUi.Row("Résolution", dpiRow, 100));
        root.Children.Add(OutputUi.Note("96 ppp : écran ; 150 à 200 ppp : courriel, site ; 300 ppp : impression."));
        root.Children.Add(OutputUi.Section("Pages"));
        root.Children.Add(_pages);
        root.Children.Add(OutputUi.Buttons(this, "Exporter…", Build));
        Content = root;
        _pages.SelectActive();
    }

    bool Build()
    {
        if (OutputUi.Number(_dpi.Text) is not { } dpi || dpi < 24 || dpi > 1200)
        {
            MessageBox.Show(this, "La résolution doit être comprise entre 24 et 1200 ppp.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var pages = _pages.Pages(out var err);
        if (pages is null) { MessageBox.Show(this, err, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        Format = (ImageFormat)OutputUi.Tag(_format)!;
        Dpi = dpi;
        Pages = pages;
        return true;
    }
}
