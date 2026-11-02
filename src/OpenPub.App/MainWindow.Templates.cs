using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

// Modeles (Fichier ▸ Nouveau, Enregistrer comme modele) et blocs de construction (Insertion ▸ Blocs).
public partial class MainWindow
{
    /// <summary>Dossier des modeles et blocs de l'utilisateur (modifiable pour les tests).</summary>
    internal static string UserLibraryRoot { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenPub");
    static string TemplatesDir => Path.Combine(UserLibraryRoot, "Modèles");
    static string BlocksDir => Path.Combine(UserLibraryRoot, "Blocs");

    static string SafeName(string name) => string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Petite boite de saisie d'un nom.</summary>
    string? AskText(string title, string label, string initial)
    {
        var dlg = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("UiFont"), Background = (Brush)FindResource("Chrome"), Foreground = (Brush)FindResource("Ink"),
        };
        var box = new TextBox { Width = 300, Text = initial, Margin = new Thickness(0, 4, 0, 0) };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = label });
        root.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Style = (Style)FindResource("PillButton") };
        ok.Click += (_, _) => dlg.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dlg.Content = root;
        dlg.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dlg.ShowDialog() == true && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }

    // ---------------------------------------------------------------- nouveau document : galerie

    void NewDocument_Click()
    {
        if (!ConfirmDiscard()) return;
        var items = new List<(string Name, string Description, Action Create)>
        {
            ("Document vierge…", "Choisissez le format, l'orientation, les marges et le nombre de pages.", NewBlankDocument),
        };
        foreach (var (id, name, desc, build) in Templates.BuiltIn)
        {
            var b = build; var n = name;
            items.Add((name, desc, () => { var doc = b(); LoadPackage(OpubPackage.CreateFromDocument(doc)); StatusText.Text = $"Nouveau document d'après le modèle « {n} »."; }));
        }
        if (Directory.Exists(TemplatesDir))
            foreach (var f in Directory.GetFiles(TemplatesDir, "*.opubt").OrderBy(x => x))
            {
                var path = f;
                items.Add((Path.GetFileNameWithoutExtension(f), "Votre modèle · " + File.GetLastWriteTime(f).ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), () => OpenTemplateFile(path)));
            }

        var dlg = new Window
        {
            Title = "Nouveau document", Width = 560, Height = 520, ResizeMode = ResizeMode.CanResize, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("UiFont"), Background = (Brush)FindResource("Chrome"), Foreground = (Brush)FindResource("Ink"),
        };
        var list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        foreach (var (name, desc, _) in items)
        {
            var card = new StackPanel { Margin = new Thickness(6, 6, 6, 6) };
            card.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 14 });
            card.Children.Add(new TextBlock { Text = desc, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), MaxWidth = 480 });
            list.Items.Add(card);
        }
        list.SelectedIndex = 0;
        var root = new DockPanel { Margin = new Thickness(16) };
        var title = new TextBlock { Text = "Choisissez un modèle", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), FontFamily = (FontFamily)FindResource("TitleFont") };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var folder = new Button { Content = "Dossier de mes modèles", Margin = new Thickness(0, 0, 8, 0) };
        folder.Click += (_, _) => OpenFolder(TemplatesDir);
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Créer", IsDefault = true, MinWidth = 90, Style = (Style)FindResource("PillButton") };
        ok.Click += (_, _) => dlg.DialogResult = true;
        list.MouseDoubleClick += (_, _) => dlg.DialogResult = true;
        buttons.Children.Add(folder); buttons.Children.Add(cancel); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        root.Children.Add(list);
        dlg.Content = root;
        if (dlg.ShowDialog() != true || list.SelectedIndex < 0) return;
        items[list.SelectedIndex].Create();
    }

    void NewBlankDocument()
    {
        var dlg = new PageSetupWindow(true, 595.276, 841.89, null) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        var pkg = OpubPackage.CreateBlank(dlg.PageWidth, dlg.PageHeight, dlg.PageCount, dlg.Margins);
        if (dlg.GridColumns > 1 || dlg.GridRows > 1) pkg.Document.Grid = new LayoutGrid { Columns = dlg.GridColumns, Rows = dlg.GridRows, Gutter = dlg.GridGutter };
        LoadPackage(pkg);
        StatusText.Text = "Nouveau document : tracez une zone de texte (Insertion) ou insérez une image pour commencer.";
    }

    /// <summary>Nouveau document d'apres un modele de l'utilisateur (le modele lui-meme n'est pas modifie).</summary>
    internal void OpenTemplateFile(string path)
    {
        OpubPackage pkg;
        try { pkg = OpubPackage.Open(path, FontEmbedder.DefaultSearchDirs); }
        catch (Exception ex) { MessageBox.Show(this, $"Modèle illisible :\n{ex.Message}", "OpenPub", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        pkg.Retarget(null);
        pkg.Document.Source = Path.GetFileNameWithoutExtension(path);
        LoadPackage(pkg);
        StatusText.Text = $"Nouveau document d'après le modèle « {pkg.Document.Source} ».";
    }

    void SaveAsTemplate_Click()
    {
        if (_session is null) return;
        var name = AskText("Enregistrer comme modèle", "Nom du modèle :", Path.GetFileNameWithoutExtension(_session.Package.FilePath ?? _session.Doc.Source ?? "Mon modèle"));
        if (name is null) return;
        Directory.CreateDirectory(TemplatesDir);
        var path = Path.Combine(TemplatesDir, SafeName(name) + ".opubt");
        if (File.Exists(path) && MessageBox.Show(this, $"Le modèle « {name} » existe déjà. Le remplacer ?", "OpenPub", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _session.EndTextEdit();
        _session.Doc.Format = PubFormat.Required(_session.Doc);
        _session.Package.SaveCopy(path);
        StatusText.Text = $"Modèle « {name} » enregistré : Fichier ▸ Nouveau le propose.";
    }

    static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch { /* explorateur indisponible */ }
    }

    // ---------------------------------------------------------------- blocs de construction

    void BlocksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var menu = NewMenu(sender);
        foreach (var (id, name, _) in Templates.Blocks)
        {
            var bid = id;
            Item(menu, name, () => InsertBlock(Templates.BlockClip(bid), name));
        }
        if (Directory.Exists(BlocksDir) && Directory.GetFiles(BlocksDir, "*.json") is { Length: > 0 } files)
        {
            menu.Items.Add(new Separator());
            foreach (var f in files.OrderBy(x => x))
            {
                var path = f;
                var name = Path.GetFileNameWithoutExtension(f);
                Item(menu, name, () =>
                {
                    if (ShapeClip.FromJson(File.ReadAllText(path)) is { } clip) InsertBlock(clip, name);
                    else StatusText.Text = $"Bloc « {name} » illisible.";
                });
            }
        }
        menu.Items.Add(new Separator());
        Item(menu, "Enregistrer la sélection comme bloc…", SaveSelectionAsBlock);
        Item(menu, "Dossier de mes blocs", () => OpenFolder(BlocksDir));
        menu.IsOpen = true;
    }

    /// <summary>Insere un bloc au centre de la page active.</summary>
    void InsertBlock(ShapeClip clip, string name)
    {
        if (_session is null || ActivePage() is not { } page || clip.Shapes.Count == 0) return;
        _session.EndTextEdit();
        var b = ShapeOps.Bounds(clip.Shapes);
        foreach (var s in clip.Shapes) ShapeOps.Translate(s, page.Width / 2 - (b.X + b.W / 2), page.Height / 2 - (b.Y + b.H / 2));
        PasteShapes(page, clip);
        StatusText.Text = $"Bloc « {name} » inséré.";
    }

    void SaveSelectionAsBlock()
    {
        if (_session is null || SelPage is not { } page || SelectedShapes() is not { Count: > 0 } sel)
        {
            StatusText.Text = "Sélectionnez d'abord les objets à enregistrer comme bloc.";
            return;
        }
        var name = AskText("Enregistrer comme bloc", "Nom du bloc :", "Mon bloc");
        if (name is null) return;
        var clip = ShapeOps.Copy(page, sel);
        foreach (var src in clip.Shapes.Where(s => s.Src is not null).Select(s => s.Src!).Distinct())
        {
            var file = _session.Package.ResolveAsset(src);
            if (File.Exists(file)) clip.Assets[src] = Convert.ToBase64String(File.ReadAllBytes(file));
        }
        Directory.CreateDirectory(BlocksDir);
        File.WriteAllText(Path.Combine(BlocksDir, SafeName(name) + ".json"), clip.ToJson());
        StatusText.Text = $"Bloc « {name} » enregistré : Insertion ▸ Blocs le propose dans tous vos documents.";
    }
}
