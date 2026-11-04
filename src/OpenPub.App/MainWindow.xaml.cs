using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using OpenPub.App.Controls;
using OpenPub.App.Rendering;
using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using Page = OpenPub.Core.Model.Page;

namespace OpenPub.App;

public partial class MainWindow : Window
{
    EditorSession? _session;
    readonly List<PageView> _views = new();
    readonly List<PageView> _thumbs = new();
    double _zoom = 1;
    const double CmPerPoint = 2.54 / 72;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Activated += OnActivatedCheckFile;
        UpdateUi();
        // groupe « Aide » en fin de chaque onglet du ruban (lien vers la section correspondante)
        AddHelpGroup(PanelHome, Help.HelpService.HomeTab, "Aide sur l'onglet Accueil");
        AddHelpGroup(PanelInsert, Help.HelpService.InsertTab, "Aide sur l'onglet Insertion");
        AddHelpGroup(PanelDraw, Help.HelpService.FreeDrawing, "Aide sur le dessin");
        AddHelpGroup(PanelTable, Help.HelpService.TableTools, "Aide sur les tableaux");
        AddHelpGroup(PanelDesign, Help.HelpService.PageDesign, "Aide sur la création de page : mise en page, pages maîtres, numéros");
        AddHelpGroup(PanelReview, Help.HelpService.Review, "Aide sur la révision : orthographe, rechercher, typographie");
        AddHelpGroup(PanelView, Help.HelpService.Snapping, "Aide sur l'affichage, les repères et le magnétisme");
        AddHelpGroup(PanelShape, Help.HelpService.ShapeFormat, "Aide sur le format des formes");
        AddHelpGroup(PanelPicture, Help.HelpService.PictureTools, "Aide sur les outils image");
        Loaded += async (_, _) =>
        {
            // verification discrete des mises a jour, quelques secondes apres le demarrage
            if (!Update.UpdateService.AutoCheck || Update.UpdateService.FeedUrl is null) return;
            await Task.Delay(3000);
            await CheckForUpdates(manual: false);
        };
    }

    // ================================================================ fichiers

    public void OpenFile(string path) => OpenFile(path, remember: true);

    /// <param name="remember">Ajouter aux documents recents (pas pour une copie de secours).</param>
    public void OpenFile(string path, bool remember)
    {
        if (Path.GetExtension(path).Equals(".pub", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Les fichiers .pub doivent d'abord être convertis (tools/preview.sh), puis ouverts via leur document.json.",
                "OpenPub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmDiscard()) return;
        OpubPackage pkg;
        try { pkg = OpubPackage.Open(path, FontEmbedder.DefaultSearchDirs); }
        catch (Exception ex)
        {
            Diagnostics.CrashReporter.Report(ex, "ouverture");
            MessageBox.Show(this, $"Impossible d'ouvrir le fichier :\n{ex.Message}", "OpenPub", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        LoadPackage(pkg);
        if (TrackRecent && remember) RecentFiles.Add(path);
        if (pkg.Document.FormatVersion > Core.Model.PubDocument.SupportedFormat)
            MessageBox.Show(this, "Ce document a été créé avec une version plus récente d'OpenPub. Il s'affiche, mais l'enregistrer avec cette " +
                "version pourrait perdre des éléments : l'enregistrement est désactivé. Mettez OpenPub à jour (Fichier ▸ Rechercher des mises à jour).",
                "OpenPub", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Affiche un paquet ouvert ou cree (remplace le document courant, deja confirme).</summary>
    void LoadPackage(OpubPackage pkg)
    {
        DiscardRecovery();
        _session?.Dispose();
        _editMasters = false;
        _session = new EditorSession(pkg);
        RecordFileStamp();
        _session.PageChanged += p => { foreach (var v in _views.Concat(_thumbs).Where(v => v.Page == p)) v.InvalidateVisual(); RefreshProperties(); };
        _session.DocumentReplaced += BuildPages;
        _session.SelectionChanged += RefreshProperties;
        _session.SelectionChanged += () =>
        {
            // pinceau : un clic sur une zone de texte (hors ecriture) lui applique la mise en forme
            if (_painterFont is not null && _session?.TextEdit is null && Sel is { IsText: true })
                Dispatcher.BeginInvoke(ApplyPainter);
        };
        _session.ModifiedChanged += UpdateTitle;
        _session.TextEditChanged += RefreshFormat;
        _session.SnapEnabled = SnapToggle.IsChecked == true;
        var prefs = AppSettings.Load();
        _session.SpellCheck = prefs.SpellCheck;
        _session.AutoCorrect = prefs.AutoCorrect;
        SpellToggle.IsChecked = prefs.SpellCheck;
        AutoCorrectToggle.IsChecked = prefs.AutoCorrect;
        _session.ShowGuides = GuidesToggle.IsChecked == true;
        FontBox.ItemsSource = _session.Fonts.AvailableFamilies.ToList();
        SizeBox.ItemsSource = new[] { 6, 7, 8, 9, 9.5, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 60, 72 };
        _session.History.Changed += (_, _) => CommandManager.InvalidateRequerySuggested();
        BuildPages();
        Dispatcher.BeginInvoke(() => ZoomFit_Click(this, new RoutedEventArgs()), System.Windows.Threading.DispatcherPriority.Loaded);
        UpdateUi();
    }

    /// <summary>Memorise les documents ouverts / enregistres dans la liste des recents (desactive pour les tests).</summary>
    public bool TrackRecent { get; set; }

    /// <summary>Liste des recents imposee (captures de la documentation : jamais les vrais fichiers de l'utilisateur).</summary>
    internal IReadOnlyList<string>? RecentOverride { get; set; }

    /// <summary>Fermeture sans confirmation (tests automatiques).</summary>
    public bool SkipConfirm { get; set; }

    bool ConfirmDiscard()
    {
        if (SkipConfirm || _session is not { Modified: true }) return true;
        var r = MessageBox.Show(this, "Enregistrer les modifications du document ?", "OpenPub",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveDocument(saveAs: _session.Package.FilePath is null);
        return true;
    }

    bool SaveDocument(bool saveAs)
    {
        if (_session is null) return false;
        if (_session.Doc.FormatVersion > Core.Model.PubDocument.SupportedFormat)
        {
            MessageBox.Show(this, "Ce document vient d'une version plus récente d'OpenPub : mettez OpenPub à jour pour l'enregistrer.",
                "OpenPub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        // format : « openpub/2 » des que le document utilise une nouveaute que la version 1 perdrait
        _session.Doc.Format = Core.Model.PubFormat.Required(_session.Doc);
        string? path = _session.Package.FilePath;
        if (saveAs || path is null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Document OpenPub (*.opub)|*.opub",
                FileName = Path.GetFileNameWithoutExtension(path ?? _session.Doc.Source ?? "document") + ".opub",
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        // fichier modifie par un autre programme (OneDrive, autre PC) depuis son ouverture
        if (!saveAs && path == _session.Package.FilePath && FileChangedOnDisk() &&
            MessageBox.Show(this, $"« {Path.GetFileName(path)} » a été modifié par un autre programme depuis son ouverture (synchronisation, autre PC…).\n\n" +
                "L'enregistrer quand même remplacera cette autre version. Continuer ?", "OpenPub", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;
        try
        {
            _session.Save(path, keepBackup: AppSettings.Load().KeepBackup);
            if (TrackRecent) RecentFiles.Add(path);
            DiscardRecovery();
            RecordFileStamp();
            StatusText.Text = $"Enregistré : {path}";
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.CrashReporter.Report(ex, "enregistrement");
            MessageBox.Show(this, $"Échec de l'enregistrement :\n{ex.Message}", "OpenPub", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        DiscardRecovery();          // fermeture normale : la copie de secours n'a plus lieu d'etre
        _session?.Dispose();
    }

    void New_Executed(object sender, ExecutedRoutedEventArgs e) => NewDocument_Click();

    void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Documents OpenPub (*.opub;document.json)|*.opub;document.json|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }
    void Save_Executed(object sender, ExecutedRoutedEventArgs e) => SaveDocument(saveAs: false);
    void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveDocument(saveAs: true);
    void HasDoc_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session is not null;
    // pendant l'edition du texte, Suppr efface du texte (gere par la page), pas l'objet
    void HasSel_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.Selected is not null && _session.TextEdit is null;
    void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.History.CanUndo == true;
    void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _session?.History.CanRedo == true;
    void Undo_Executed(object sender, ExecutedRoutedEventArgs e) => _session?.Undo();
    void Redo_Executed(object sender, ExecutedRoutedEventArgs e) => _session?.Redo();
    // impression, PDF et images : voir MainWindow.Output.cs
    void Print_Executed(object sender, ExecutedRoutedEventArgs e) => ShowPrintWindow();

    // ================================================================ pages et zoom

    void BuildPages()
    {
        PagesPanel.Children.Clear();
        Thumbnails.Children.Clear();
        _views.Clear();
        _thumbs.Clear();
        if (_session is null) return;
        // annuler a pu retirer les pages maitres : retour aux pages du document
        if (_editMasters && _session.Doc.Masters is not { Count: > 0 })
        {
            _editMasters = false;
            MastersToggle.IsChecked = false;
            MasterBanner.Visibility = Visibility.Collapsed;
        }
        // double page : grille de deux colonnes, la premiere page seule a droite (comme une planche imprimee)
        Panel host = PagesPanel;
        bool spread = SpreadView && !EditingMasters;
        if (spread)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            grid.Children.Add(new Border());
            PagesPanel.Children.Add(grid);
            host = grid;
        }
        foreach (var page in PageList)
        {
            var cell = new StackPanel { Margin = spread ? new Thickness(1, 0, 1, 0) : new Thickness(0), VerticalAlignment = VerticalAlignment.Top };
            host.Children.Add(cell);
            cell.Children.Add(new TextBlock
            {
                Text = EditingMasters ? $"Page maître {page.Id}{(page.Name is { Length: > 0 } nm ? " — " + nm : "")}" : $"Page {page.Index}",
                Foreground = (Brush)FindResource(EditingMasters ? "Violet" : "Muted"), FontSize = 12, Margin = new Thickness(0, 14, 0, 6),
                FontWeight = EditingMasters ? FontWeights.SemiBold : FontWeights.Normal,
            });
            var view = new PageView(_session, page) { Zoom = _zoom };
            view.ReplaceImageRequested += img => { _session?.Select(img, page); EditImage(false); };
            view.CaretMoved += (v, r) => v.BringIntoView(r);
            view.ShapeDrawn += OnShapeDrawn;
            view.ShapeCreated += OnShapeCreated;
            view.LinkTargetPicked += OnLinkTargetPicked;
            view.FollowLink = (v, pt) => TryOpenLink(v.Page, pt);
            view.GuidesChanged += () => { foreach (var v2 in _views) v2.InvalidateVisual(); };
            view.ContextMenuRequested += OnPageContextMenu;
            view.OverflowClicked += OnOverflowClicked;
            HookShapeEvents(view);         // lot 9 : espaces reserves, WordArt, modification des points
            view.TextSelectionFinished += () => { if (_painterFont is not null && _session?.TextEdit is { HasSelection: true }) ApplyPainter(); };
            _views.Add(view);
            cell.Children.Add(new Border
            {
                Child = view, Background = Brushes.White,
                // ombre douce et bleutee, comme les cartes de la charte
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 8, Direction = 270, Color = Color.FromRgb(0x2E, 0x52, 0x9A), Opacity = 0.16 },
            });

            var thumb = new PageView(_session, page) { Interactive = false, Zoom = 0.16 };
            _thumbs.Add(thumb);
            // miniature facon maquette : cadre arrondi (violet si active) et pastille numerotee
            var frame = new Border
            {
                Child = thumb, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Padding = new Thickness(2),
                BorderBrush = (Brush)FindResource("Line"), Background = Brushes.White, Tag = "frame",
            };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, Tag = "badge",
                Child = new TextBlock { Text = EditingMasters ? page.Id ?? "?" : page.Index.ToString(), FontWeight = FontWeights.Bold, FontSize = 12 },
            };
            var tb = new Button
            {
                Content = new StackPanel { Children = { frame, badge } },
                Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(4), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Tag = view,
            };
            tb.Click += (_, _) => { var v = (PageView)tb.Tag; _session?.Select(null, v.Page); v.BringIntoView(); HighlightActivePage(); };
            Thumbnails.Children.Add(tb);
        }
        var subst = _session.Fonts.Substituted;
        // force la resolution des polices pour le message d'avertissement
        foreach (var f in FontEmbedder.DocumentFamilies(_session.Doc)) _session.Fonts.Family(f);
        FontWarning.Text = subst.Count > 0 ? "Polices manquantes (remplacées par Arial) : " + string.Join(", ", subst) : "";
        UpdateTitle();
        RefreshProperties();
        HighlightActivePage();
        Dispatcher.BeginInvoke(UpdateRulers, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Met en evidence la miniature de la page active.</summary>
    void HighlightActivePage()
    {
        var active = ActivePage();
        foreach (var child in Thumbnails.Children.OfType<Button>())
        {
            bool on = child.Tag is PageView v && v.Page == active;
            if (child.Content is not StackPanel sp) continue;
            foreach (var b in sp.Children.OfType<Border>())
            {
                if ((string?)b.Tag == "frame")
                    b.BorderBrush = on ? (Brush)FindResource("Violet") : (Brush)FindResource("Line");
                else if ((string?)b.Tag == "badge" && b.Child is TextBlock t)
                {
                    b.Background = on ? (Brush)FindResource("Accent") : Brushes.Transparent;
                    t.Foreground = on ? Brushes.White : (Brush)FindResource("Muted");
                }
            }
        }
        if (_session is not null && active is not null)
            PageIndicator.Text = EditingMasters ? $"Page maître {active.Id}" : $"Page {_session.Doc.Pages.IndexOf(active) + 1} sur {_session.Doc.Pages.Count}";
    }

    void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateRulers();
        if (_session?.Selected is null) HighlightActivePage();
    }

    void SnapToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null) _session.SnapEnabled = SnapToggle.IsChecked == true;
    }

    void GuidesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.ShowGuides = GuidesToggle.IsChecked == true;
        foreach (var v in _views) v.InvalidateVisual();
    }

    // ================================================================ aide contextuelle

    /// <summary>Section de l'aide correspondant a ce que l'utilisateur est en train de faire.</summary>
    public string CurrentHelpTopic()
    {
        if (_session is null) return Help.HelpService.Home;
        if (_session.TextEdit is not null) return Help.HelpService.TextWrite;
        switch (_session.DrawTool)
        {
            case "text": return Help.HelpService.TextCreate;
            case "freehand" or "polygon" or "line" or "arrow": return Help.HelpService.FreeDrawing;
            case not null: return Help.HelpService.Shapes;
        }
        if (Sel is { IsTable: true }) return Help.HelpService.TableTools;
        if (Sel is { } s)
            return s.IsImage ? (s.IsPlaceholder ? Help.HelpService.Placeholders : Help.HelpService.PictureTools)
                 : s.IsText ? (s.Chain is not null ? Help.HelpService.TextLinked : Help.HelpService.TextFormat)
                 : ShapeHelpTopic(s);          // lot 9 : WordArt, modification des points (MainWindow.Shapes.cs)
        if (TabInsert.IsChecked == true) return Help.HelpService.InsertTab;
        if (TabDraw.IsChecked == true) return Help.HelpService.FreeDrawing;
        if (TabView.IsChecked == true) return Help.HelpService.ViewTab;
        if (TabReview.IsChecked == true) return Help.HelpService.Review;
        if (TabDesign.IsChecked == true) return _editMasters ? Help.HelpService.Masters : Help.HelpService.PageDesign;
        return Help.HelpService.Home;
    }

    void HelpContext_Click(object sender, RoutedEventArgs e) => Help.HelpService.Open(CurrentHelpTopic());
    void HelpLink_Click(object sender, RoutedEventArgs e) => Help.HelpService.Open(CurrentHelpTopic());
    void UpdateHelp_Click(object sender, RoutedEventArgs e) => Help.HelpService.Open(Help.HelpService.Updates);
    void PagesHelp_Click(object sender, RoutedEventArgs e) => Help.HelpService.Open(Help.HelpService.Pages);

    void AddHelpGroup(Panel panel, string topic, string tooltip)
    {
        var b = new Button { Style = (Style)FindResource("RBig"), Tag = "\uE897", Content = "Aide", ToolTip = tooltip + " (F1)" };
        b.Click += (_, _) => Help.HelpService.Open(topic);
        panel.Children.Add(new HeaderedContentControl { Style = (Style)FindResource("RGroup"), Header = "Aide", Content = b });
    }

    /// <summary>Texte du lien « En savoir plus » du panneau de droite, selon le contexte.</summary>
    void RefreshHelpLink()
    {
        var topic = CurrentHelpTopic();
        HelpLinkText.Text = topic switch
        {
            Help.HelpService.TextWrite => "Écrire sur la page : aide ›",
            Help.HelpService.TextLinked => "Zones de texte liées : aide ›",
            Help.HelpService.TextFormat => "Mettre en forme le texte : aide ›",
            Help.HelpService.PictureTools => "Retoucher les images : aide ›",
            Help.HelpService.ShapeFormat => "Formes, contour et rotation : aide ›",
            Help.HelpService.FreeDrawing => "Dessin libre : aide ›",
            _ => "Aide en ligne d'OpenPub ›",
        };
    }

    // ================================================================ mises a jour

    OpenPub.Core.Update.UpdateManifest? _pendingUpdate;

    async Task CheckForUpdates(bool manual)
    {
        if (manual) StatusText.Text = "Recherche des mises à jour…";
        var r = await Update.UpdateService.CheckAsync(honorSkip: !manual);
        if (r.IsNewer && r.Manifest is { } m)
        {
            _pendingUpdate = m;
            UpdateText.Text = $"OpenPub {m.Version} est disponible (vous avez la {Update.UpdateService.CurrentVersion}).";
            UpdateNotes.Text = m.Notes ?? "";
            UpdateNotes.ToolTip = m.Notes;
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = UpdateSkipButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
        if (manual)
        {
            StatusText.Text = r.Message;
            if (!r.IsNewer) MessageBox.Show(this, r.Message, "Mises à jour", MessageBoxButton.OK,
                r.Manifest is null ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not { } m) return;
        if (!ConfirmDiscard()) return;           // proposer d'enregistrer avant de quitter
        UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = UpdateSkipButton.IsEnabled = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateText.Text = $"Téléchargement d'OpenPub {m.Version}…";
        try
        {
            var file = await Update.UpdateService.DownloadAsync(m, new Progress<double>(v => UpdateProgress.Value = v));
            UpdateText.Text = "Installation… OpenPub va redémarrer.";
            Update.UpdateService.InstallAndRestart(file, _session?.Package.FilePath);
            SkipConfirm = true;
            await Task.Delay(500);
            Close();
        }
        catch (Exception ex)
        {
            UpdateText.Text = "La mise à jour a échoué.";
            UpdateNotes.Text = ex.Message;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = UpdateSkipButton.IsEnabled = true;
        }
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    void UpdateSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is { } m) Update.UpdateService.SkipVersion(m.Version);
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // ================================================================ ruban

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelHome is null) return;          // pendant InitializeComponent
        if (HelpLinkText is not null) RefreshHelpLink();
        PanelHome.Visibility = TabHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelInsert.Visibility = TabInsert.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelView.Visibility = TabView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelReview.Visibility = TabReview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelDesign.Visibility = TabDesign.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTable.Visibility = TabTable.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelDraw.Visibility = TabDraw.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelShape.Visibility = TabShape.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelPicture.Visibility = TabPicture.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RibbonScroller?.ScrollToHorizontalOffset(0);
        if (sender is FrameworkElement tab) tab.BringIntoView();
    }

    // Petits ecrans : le ruban et la ligne d'onglets defilent horizontalement.
    void RibbonScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || sv.ScrollableWidth <= 0) return;
        // ne pas voler la molette aux listes deroulantes ouvertes ou aux zones de saisie
        if (e.OriginalSource is DependencyObject d && FindParent<ComboBox>(d) is { IsDropDownOpen: true }) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    void RibbonScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var sv = RibbonScroller;
        RibbonLeft.Visibility = sv.HorizontalOffset > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        RibbonRight.Visibility = sv.HorizontalOffset < sv.ScrollableWidth - 0.5 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RibbonLeft_Click(object sender, RoutedEventArgs e) => RibbonScroller.ScrollToHorizontalOffset(RibbonScroller.HorizontalOffset - 60);
    void RibbonRight_Click(object sender, RoutedEventArgs e) => RibbonScroller.ScrollToHorizontalOffset(RibbonScroller.HorizontalOffset + 60);

    static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    /// <summary>Dernier menu deroulant ouvert (captures de la documentation et tests).</summary>
    internal ContextMenu? LastMenu { get; private set; }

    string _lastContext = "";

    /// <summary>Onglets contextuels : "Format de la forme" (formes, zones de texte) et "Outils image" (images).</summary>
    void RefreshContextTabs()
    {
        var s = Sel;
        bool pic = s is { IsImage: true };
        bool shape = s is not null && !pic;
        TabShape.Visibility = shape ? Visibility.Visible : Visibility.Collapsed;
        TabPicture.Visibility = pic ? Visibility.Visible : Visibility.Collapsed;
        bool table = s is { IsTable: true };
        TabTable.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        var ctx = pic ? "pic" : table ? "table" : shape ? (s!.IsText ? "text" : "shape") : "";
        if (ctx != _lastContext)
        {
            // comme Office : l'onglet contextuel s'ouvre quand on selectionne une forme ou une image
            if (pic) TabPicture.IsChecked = true;
            else if (table) TabTable.IsChecked = true;
            else if (ctx == "shape") TabShape.IsChecked = true;
            else if ((TabShape.IsChecked == true && !shape) || (TabPicture.IsChecked == true && !pic) || (TabTable.IsChecked == true && !table)) TabHome.IsChecked = true;
            _lastContext = ctx;
        }
        if (s is null) return;
        TextFrameGroup.Visibility = LinkGroup.Visibility = s.IsText ? Visibility.Visible : Visibility.Collapsed;
        _updatingFormat = true;
        FillSwatch.Background = s.Fill?.Color is { } fc ? PageRenderer.Brush(fc) : Brushes.Transparent;
        LineSwatch.Background = s.Line?.Color is { } lc ? PageRenderer.Brush(lc) : Brushes.Transparent;
        if (!LineWeightBox.IsKeyboardFocusWithin) LineWeightBox.Text = s.Line is { } l ? l.Weight.ToString("0.##", CultureInfo.CurrentCulture) : "";
        DashCheck.IsChecked = s.Line?.Dash == true;
        ArrowOptions.Visibility = s.Path is { Count: >= 2 } && s.Kind == ShapeKind.Path && !s.Path.Any(q => q.A == "Z") ? Visibility.Visible : Visibility.Collapsed;
        ArrowStartCheck.IsChecked = s.Line?.ArrowStart == true;
        ArrowEndCheck.IsChecked = s.Line?.ArrowEnd == true;
        var geo = s.Geometry ?? "";
        GeometryBox.SelectedItem = GeometryBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == geo) ?? GeometryBox.Items[0];
        RadiusSlider.IsEnabled = s.Geometry == ShapeGeometry.RoundRect;
        RadiusSlider.Value = s.CornerRadius is { } r ? r / Math.Max(1, Math.Min(s.W, s.H)) * 100 : 15;
        ShapeShadowToggle.IsChecked = PicShadowToggle.IsChecked = s.Shadow is not null;
        PicReflectionToggle.IsChecked = (s.ImageFx?.ReflectionSize ?? 0) > 0;
        if (!RotationBox.IsKeyboardFocused) RotationBox.Text = s.Rotation.ToString("0.#", CultureInfo.CurrentCulture);
        RefreshShapeTools(s);              // lot 9 (MainWindow.Shapes.cs)
        _updatingFormat = false;
    }

    void FileButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FileButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        LastMenu = menu;
        void Add(string header, string gesture, Action act, bool enabled = true, string? glyph = null)
        {
            var it = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            if (glyph is not null)
                it.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 15, Foreground = (Brush)FindResource("IconBrush") };
            it.Click += (_, _) => act();
            menu.Items.Add(it);
        }
        bool has = _session is not null;
        Add("Nouveau…", "Ctrl+N", NewDocument_Click, glyph: "\uE8A5");
        Add("Ouvrir…", "Ctrl+O", () => ApplicationCommands.Open.Execute(null, this), glyph: "\uE8E5");
        AddRecent(menu);
        Add("Enregistrer", "Ctrl+S", () => SaveDocument(false), has, glyph: "\uE74E");
        Add("Enregistrer sous…", "Ctrl+Maj+S", () => SaveDocument(true), has, glyph: "\uE792");
        Add("Enregistrer comme modèle…", "", SaveAsTemplate_Click, has, glyph: "\uE8F4");
        menu.Items.Add(new Separator());
        AddOutputItems(menu, has);
        Add("Exporter pour le web ou un e-mail…", "", ExportHtml_Click, has, glyph: "\uE774");
        menu.Items.Add(new Separator());
        Add("Aide en ligne", "F1", () => Help.HelpService.Open(CurrentHelpTopic()), glyph: "\uE897");
        Add("Raccourcis clavier", "", () => Help.HelpService.Open(Help.HelpService.Shortcuts), glyph: "\uE765");
        Add("Guide : préparer le numéro suivant", "", () => Help.HelpService.Open(Help.HelpService.NextIssue), glyph: "\uE7C3");
        menu.Items.Add(new Separator());
        Add("Rechercher des mises à jour…", "", () => _ = CheckForUpdates(manual: true), glyph: "\uE895");
        var auto = new MenuItem { Header = "Rechercher automatiquement au démarrage", IsCheckable = true, IsChecked = Update.UpdateService.AutoCheck };
        auto.Click += (_, _) => Update.UpdateService.AutoCheck = auto.IsChecked;
        menu.Items.Add(auto);
        var prefs = AppSettings.Load();
        var autosave = new MenuItem { Header = "Copie de secours automatique (toutes les 5 min)", IsCheckable = true, IsChecked = prefs.AutoSave };
        autosave.Click += (_, _) => AppSettings.Update(x => x.AutoSave = autosave.IsChecked);
        menu.Items.Add(autosave);
        var backup = new MenuItem { Header = "Garder la version précédente (.bak) à l'enregistrement", IsCheckable = true, IsChecked = prefs.KeepBackup };
        backup.Click += (_, _) => AppSettings.Update(x => x.KeepBackup = backup.IsChecked);
        menu.Items.Add(backup);
        var crash = new MenuItem { Header = "Signaler automatiquement les erreurs (rapport anonyme)", IsCheckable = true, IsChecked = prefs.CrashReports };
        crash.Click += (_, _) => AppSettings.Update(x => x.CrashReports = crash.IsChecked);
        menu.Items.Add(crash);
        Add("À propos d'OpenPub", "", ShowAbout, glyph: "\uE946");
        menu.Items.Add(new Separator());
        Add("Quitter", "Alt+F4", Close, glyph: "\uE7E8");
        menu.IsOpen = true;
    }

    /// <summary>Section « Documents récents » du menu Fichier : nom du document et dossier, clic pour rouvrir.</summary>
    void AddRecent(ContextMenu menu)
    {
        var recent = RecentOverride ?? (TrackRecent ? RecentFiles.List() : Array.Empty<string>());
        if (recent.Count == 0) return;
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = "DOCUMENTS RÉCENTS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted") },
            IsHitTestVisible = false, Focusable = false, Padding = new Thickness(10, 4, 10, 2),
        });
        var current = _session?.Package.FilePath;
        int n = 0;
        foreach (var file in recent)
        {
            n++;
            bool isCurrent = current is not null && string.Equals(Path.GetFullPath(current), file, StringComparison.OrdinalIgnoreCase);
            var head = new StackPanel();
            head.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(file), FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Medium,
                MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            head.Children.Add(new TextBlock
            {
                Text = Path.GetDirectoryName(file), FontSize = 11, Foreground = (Brush)FindResource("Muted"),
                MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            });
            var it = new MenuItem
            {
                Header = head, ToolTip = file, Padding = new Thickness(10, 5, 10, 5),
                InputGestureText = isCurrent ? "ouvert" : n <= 9 ? $"Ctrl+Maj+{n}" : "",
                Icon = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = (Brush)FindResource("SoftBadge"),
                    Child = new TextBlock
                    {
                        Text = n.ToString(CultureInfo.InvariantCulture), FontSize = 11, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("SelectedText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
            it.Click += (_, _) => OpenRecent(file);
            menu.Items.Add(it);
        }
        var clear = new MenuItem { Header = "Effacer la liste", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Padding = new Thickness(10, 5, 10, 5) };
        clear.Click += (_, _) => { RecentFiles.Clear(); StatusText.Text = "Liste des documents récents effacée."; };
        menu.Items.Add(clear);
        menu.Items.Add(new Separator());
    }

    void OpenRecent(string file)
    {
        if (!File.Exists(file))
        {
            MessageBox.Show(this, $"Ce document n'existe plus (déplacé ou supprimé) :\n{file}", "OpenPub", MessageBoxButton.OK, MessageBoxImage.Warning);
            RecentFiles.Remove(file);
            return;
        }
        if (_session?.Package.FilePath is { } cur && string.Equals(Path.GetFullPath(cur), file, StringComparison.OrdinalIgnoreCase)) return;
        OpenFile(file);
    }

    void ShowAbout() => new AboutWindow(() => _ = CheckForUpdates(manual: true)) { Owner = this }.ShowDialog();

    void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1);

    // ---------------------------------------------------------------- dessin

    void SetDrawTool(string? tool)
    {
        if (_session is null) return;
        _session.EndTextEdit();
        _session.DrawTool = tool;
        foreach (var v in _views) v.CancelDrawing();
        foreach (var tb in ToolButtons())
            tb.IsChecked = (tb.CommandParameter as string ?? "") == (tool ?? "");
        RefreshHelpLink();
        StatusText.Text = tool switch
        {
            null => "",
            "freehand" => "Crayon : dessinez à main levée sur la page (Échap pour terminer).",
            "polygon" => "Forme libre : cliquez chaque sommet ; double-clic ou Entrée pour terminer, clic sur le premier point pour fermer.",
            _ => "Tracez l'objet sur la page (Maj : carré / cercle / 45°, Échap pour annuler).",
        };
    }

    /// <summary>Boutons d'outil de dessin du ruban (Insertion et Dessin), y compris la galerie des formes.</summary>
    IEnumerable<ToggleButton> ToolButtons()
    {
        static IEnumerable<ToggleButton> Walk(DependencyObject d)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            {
                if (child is ToggleButton tb && tb.CommandParameter is string) yield return tb;
                foreach (var x in Walk(child)) yield return x;
            }
        }
        return Walk(PanelInsert).Concat(Walk(PanelDraw));
    }

    void DrawTool_Click(object sender, RoutedEventArgs e)
    {
        var tool = (string)((FrameworkElement)sender).GetValue(ButtonBase.CommandParameterProperty);
        if (tool == "") { SetDrawTool(null); return; }
        SetDrawTool(_session?.DrawTool == tool ? null : tool);
    }

    void OnShapeCreated(PageView view, Shape s, bool keepTool)
    {
        if (_session is null) return;
        var page = view.Page;
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        if (keepTool) { StatusText.Text = "Trait ajouté. Continuez à dessiner, ou Échap pour terminer."; return; }
        SetDrawTool(null);
        _session.Select(s, page);
    }

    void PenColor_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var (ok, c) = PickColor(_session.PenColor, false, "Couleur du trait");
        if (ok && c is not null) { _session.PenColor = c; PenSwatch.Background = PageRenderer.Brush(c); }
    }

    void ApplyPenWidth(string text)
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (_session is not null && double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, CultureInfo.CurrentCulture, out var w) && w > 0 && w <= 50)
            _session.PenWidth = w;
    }
    void PenWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PenWidthBox.SelectedItem is { } v) ApplyPenWidth(Convert.ToString(v, CultureInfo.CurrentCulture)!);
    }
    void PenWidth_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyPenWidth(PenWidthBox.Text); e.Handled = true; } }

    void Arrow_Click(object sender, RoutedEventArgs e)
    {
        bool start = ArrowStartCheck.IsChecked == true, end = ArrowEndCheck.IsChecked == true;
        ChangeSel(x => { x.Line ??= new LineStyle { Color = "#000000", Weight = 1.5 }; x.Line.ArrowStart = start; x.Line.ArrowEnd = end; },
            x => x.Path is { Count: >= 2 } && x.Kind == ShapeKind.Path);
    }

    // galerie par categories : MainWindow.Shapes.cs
    void ShapesButton_Click(object sender, RoutedEventArgs e) => ShowShapesMenu(ShapesButton);

    void OnShapeDrawn(PageView view, string tool, Rect r)
    {
        if (_session is null) return;
        var page = view.Page;
        bool click = r.Width < 4 && r.Height < 4;
        Shape s;
        if (tool is "line" or "arrow")
        {
            // le sens du trace est celui du glissement ; Maj : ligne horizontale, verticale ou a 45 deg
            var a = view.DrawStart;
            var b = click ? new Point(a.X + 120, a.Y) : view.DrawEnd;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !click)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4), len = Math.Sqrt(dx * dx + dy * dy);
                b = new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
            }
            s = Editing.DrawingFactory.Line(a, b, _session.PenColor, _session.PenWidth, arrow: tool == "arrow");
        }
        else if (tool == "text")
        {
            if (click) r = new Rect(r.X, r.Y, 200, 80);
            s = new Shape
            {
                Kind = ShapeKind.Text, X = r.X, Y = r.Y, W = r.Width, H = r.Height, Padding = new[] { 2.85, 2.85, 2.85, 2.85 },
                AutoFit = Shape.NewAutoFit,
                Paragraphs = new List<Paragraph> { new() { Align = "left", Runs = { new Run { Text = "", Font = new FontSpec { Family = "Cavolini", Size = 12, Color = "#000000", Lang = "fr" } } } } },
            };
        }
        else
        {
            if (click) r = new Rect(r.X, r.Y, 100, 100);
            s = new Shape
            {
                Kind = tool == ShapeGeometry.Ellipse ? ShapeKind.Ellipse : ShapeKind.Rect, Geometry = tool, X = r.X, Y = r.Y, W = r.Width, H = r.Height,
                Fill = new Fill { Color = "#5b9bd5" }, Line = new LineStyle { Color = "#2e5f8a", Weight = 1 },
                AutoFit = Shape.NewAutoFit,
            };
        }
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        SetDrawTool(null);
        if (s.IsText) { _session.BeginTextEdit(page, s); view.Focus(); }
        else _session.Select(s, page);
    }

    // ---------------------------------------------------------------- format de la forme

    /// <summary>Modifie l'objet selectionne (annulable) puis rafraichit.</summary>
    void ChangeSel(Action<Shape> change, Func<Shape, bool>? only = null)
    {
        if (_session is null || Sel is null || SelPage is not { } page || _updatingFormat) return;
        var targets = _session.Selection.Where(x => only is null || only(x)).ToList();
        if (targets.Count == 0) return;
        _session.Change(page, () => { foreach (var x in targets) change(x); });
        RefreshProperties();
    }

    void FillColor_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { } s) return;
        var (ok, c) = PickColor(s.Fill?.Color, true, "Couleur de remplissage");
        if (ok) ChangeSel(x => x.Fill = c is null ? null : new Fill { Color = c });
    }

    void LineColor_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { } s) return;
        var (ok, c) = PickColor(s.Line?.Color, true, "Couleur du contour");
        if (ok) ChangeSel(x => x.Line = c is null ? null : new LineStyle { Color = c, Weight = x.Line?.Weight ?? 1, Dash = x.Line?.Dash ?? false });
    }

    void ApplyLineWeight(string text)
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (!double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, CultureInfo.CurrentCulture, out var w) || w < 0 || w > 50) return;
        ChangeSel(x => { if (w == 0) x.Line = null; else { x.Line ??= new LineStyle { Color = "#000000" }; x.Line.Weight = w; } });
    }
    void LineWeight_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFormat && LineWeightBox.SelectedItem is { } v) ApplyLineWeight(Convert.ToString(v, CultureInfo.CurrentCulture)!);
    }
    void LineWeight_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyLineWeight(LineWeightBox.Text); e.Handled = true; } }

    void Dash_Click(object sender, RoutedEventArgs e)
    {
        var dash = DashCheck.IsChecked == true;
        ChangeSel(x => { x.Line ??= new LineStyle { Color = "#000000", Weight = 1 }; x.Line.Dash = dash; });
    }

    void GeometryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFormat || GeometryBox.SelectedItem is not ComboBoxItem it || Sel is not { } s) return;
        var tag = (string)it.Tag;
        var geo = tag == "" ? null : tag;
        if (geo == s.Geometry) return;
        ChangeSel(x =>
        {
            if (x.Path is not null && geo is not null && x.Kind != ShapeKind.Image) { x.Path = null; x.Kind = ShapeKind.Rect; }
            x.Geometry = geo;
            x.Adjust = null;               // les reglages (poignees jaunes) sont propres a chaque forme
            // une zone de texte sans contour recoit un contour visible pour voir son nouveau cadre
            if (x.IsText && geo is not null && x.Line is null && x.Fill is null) x.Line = new LineStyle { Color = "#000000", Weight = 1.5 };
        });
    }

    void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingFormat || Sel is not { Geometry: ShapeGeometry.RoundRect } s) return;
        var v = e.NewValue;
        ChangeSel(x => x.CornerRadius = Math.Min(x.W, x.H) * v / 100);
    }

    void ShadowToggle_Click(object sender, RoutedEventArgs e)
    {
        var on = ((ToggleButton)sender).IsChecked == true;
        ChangeSel(x => x.Shadow = on ? new ShadowFx() : null);
    }

    void RotateBy(double deg) => ChangeSel(x => x.Rotation = Geo.Normalize(x.Rotation + deg));
    void RotateLeft_Click(object sender, RoutedEventArgs e) => RotateBy(-90);
    void RotateRight_Click(object sender, RoutedEventArgs e) => RotateBy(90);

    void ApplyRotationBox()
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (double.TryParse(RotationBox.Text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, CultureInfo.CurrentCulture, out var deg)
            && Sel is { } s && Math.Abs(Geo.Normalize(deg) - s.Rotation) > 0.01)
            ChangeSel(x => x.Rotation = Geo.Normalize(deg));
    }
    void RotationBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyRotationBox(); e.Handled = true; } }
    void RotationBox_LostFocus(object sender, RoutedEventArgs e) => ApplyRotationBox();

    // ---------------------------------------------------------------- outils image

    void EditImage(bool crop)
    {
        if (_session is null || Sel is not { IsImage: true } s || SelPage is not { } page) return;
        if (s.IsPlaceholder) { FillPlaceholder(s); return; }        // cadre vide : choisir l'image (lot 9)
        var dlg = new ImageEditorWindow(_session, s) { Owner = this };
        if (crop) dlg.Loaded += (_, _) => dlg.StartCrop();
        if (dlg.ShowDialog() == true) _session.Change(page, dlg.Apply);
        RefreshProperties();
    }
    void EditImage_Click(object sender, RoutedEventArgs e) => EditImage(false);
    void CropImage_Click(object sender, RoutedEventArgs e) => EditImage(true);

    void Recolor_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).CommandParameter;
        ChangeSel(x => { x.ImageFx ??= new ImageFx(); x.ImageFx.Recolor = tag == "" ? null : tag; if (x.ImageFx.IsEmpty) x.ImageFx = null; }, x => x.IsImage);
    }
    void FlipH_Click(object sender, RoutedEventArgs e) => ChangeSel(x => { x.ImageFx ??= new ImageFx(); x.ImageFx.FlipH = !x.ImageFx.FlipH; if (x.ImageFx.IsEmpty) x.ImageFx = null; }, x => x.IsImage);
    void FlipV_Click(object sender, RoutedEventArgs e) => ChangeSel(x => { x.ImageFx ??= new ImageFx(); x.ImageFx.FlipV = !x.ImageFx.FlipV; if (x.ImageFx.IsEmpty) x.ImageFx = null; }, x => x.IsImage);
    void ReflectionToggle_Click(object sender, RoutedEventArgs e)
    {
        var on = PicReflectionToggle.IsChecked == true;
        ChangeSel(x => { x.ImageFx ??= new ImageFx(); x.ImageFx.ReflectionSize = on ? 0.35 : 0; if (x.ImageFx.IsEmpty) x.ImageFx = null; }, x => x.IsImage);
    }
    void ResetImage_Click(object sender, RoutedEventArgs e)
    {
        if (Sel is not { IsImage: true } s || s.ImageFx is null && s.Shadow is null && s.Geometry is null) return;
        ChangeSel(x =>
        {
            // le recadrage est annule en rendant au cadre la taille de l'image entiere
            if (x.ImageFx is { HasCrop: true } fx)
            {
                double fw = 1 - fx.CropLeft - fx.CropRight, fh = 1 - fx.CropTop - fx.CropBottom;
                double fullW = x.W / fw, fullH = x.H / fh;
                PageView.MoveShape(x, new Rect(x.X - fx.CropLeft * fullW, x.Y - fx.CropTop * fullH, fullW, fullH));
            }
            x.ImageFx = null; x.Shadow = null; x.Geometry = null; x.CornerRadius = null;
        });
    }

    // ================================================================ pages

    void PageOp(Action<List<Page>, int> op, int selectAfter)
    {
        if (_session is null || ActivePage() is not { } page) return;
        _session.EndTextEdit();
        var pages = PageList;
        int idx = pages.IndexOf(page);
        _session.BeginChange();
        op(pages, idx);
        for (int i = 0; i < pages.Count; i++) pages[i].Index = i + 1;
        if (EditingMasters) EnsureMasterIds();
        _session.EndChange();
        _session.StructureChanged();
        var target = pages[Math.Clamp(idx + selectAfter, 0, pages.Count - 1)];
        _session.Select(null, target);
        HighlightActivePage();
        _views.FirstOrDefault(v => v.Page == target)?.BringIntoView();
    }

    void AddPage_Click(object sender, RoutedEventArgs e) => PageOp((pages, i) =>
    {
        var model = pages[i];
        pages.Insert(i + 1, new Page { Width = model.Width, Height = model.Height });
    }, 1);

    void DuplicatePage_Click(object sender, RoutedEventArgs e) => PageOp((pages, i) =>
    {
        // copie profonde par serialisation ; les chaines restent propres a la page (identifiants inchanges)
        var json = System.Text.Json.JsonSerializer.Serialize(pages[i], DocumentJson.Options);
        pages.Insert(i + 1, System.Text.Json.JsonSerializer.Deserialize<Page>(json, DocumentJson.Options)!);
    }, 1);

    void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page) return;
        if (PageList.Count <= 1) { StatusText.Text = EditingMasters ? "Gardez au moins une page maître (ou désactivez-les pour toutes les pages)." : "Le document doit garder au moins une page."; return; }
        if (MessageBox.Show(this, $"Supprimer la page {page.Index} ? (Annuler permet de la récupérer)", "OpenPub",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        PageOp((pages, i) => pages.RemoveAt(i), 0);
    }

    void MovePageUp_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page || PageList.IndexOf(page) == 0) return;
        PageOp((pages, i) => { var p = pages[i]; pages.RemoveAt(i); pages.Insert(i - 1, p); }, -1);
    }

    void MovePageDown_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ActivePage() is not { } page || PageList.IndexOf(page) >= PageList.Count - 1) return;
        PageOp((pages, i) => { var p = pages[i]; pages.RemoveAt(i); pages.Insert(i + 1, p); }, 1);
    }

    void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.25, 4);
        foreach (var v in _views) v.Zoom = _zoom;
        ZoomText.Text = ZoomStatus.Text = $"{_zoom * 100:0} %";
        Dispatcher.BeginInvoke(UpdateRulers, System.Windows.Threading.DispatcherPriority.Loaded);
        _syncZoom = true;
        ZoomSlider.Value = _zoom * 100;
        _syncZoom = false;
    }

    bool _syncZoom;
    void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncZoom && IsLoaded) SetZoom(e.NewValue / 100);
    }
    void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.2);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.2);
    void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || PageList.FirstOrDefault() is not { } p) return;
        var avail = Scroller.ActualWidth - 90;
        if (avail > 100) SetZoom(avail / ((SpreadView && !EditingMasters ? 2 : 1) * p.Width * PageView.DipPerPoint));
    }
    void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        SetZoom(e.Delta > 0 ? _zoom * 1.1 : _zoom / 1.1);
        e.Handled = true;
    }

    // ================================================================ selection et proprietes

    Shape? Sel => _session?.Selected;
    Page? SelPage => _session?.SelectedPage;

    void UpdateUi()
    {
        bool has = _session is not null;
        PanelInsert.IsEnabled = PanelTable.IsEnabled = PanelShape.IsEnabled = PanelPicture.IsEnabled = PanelDraw.IsEnabled = PanelReview.IsEnabled = PanelDesign.IsEnabled = has;
        if (PenWidthBox.ItemsSource is null)
        {
            PenWidthBox.ItemsSource = new[] { 0.5, 1, 1.5, 2, 3, 4.5, 6, 8, 12 };
            PenWidthBox.Text = "2";
        }
        if (ShapeGallery.Children.Count == 0) FillShapeGallery();           // MainWindow.Shapes.cs
        if (LineWeightBox.ItemsSource is null)
            LineWeightBox.ItemsSource = new[] { 0.25, 0.5, 0.75, 1, 1.5, 2.25, 3, 4.5, 6 };
        if (GeometryBox.Items.Count == 0) FillGeometryBox();
    }

    void UpdateTitle()
    {
        if (_session is null) { Title = "OpenPub"; return; }
        var name = _session.Package.FilePath is { } f ? Path.GetFileName(f) : (_session.Doc.Source ?? "Sans titre") + " (importé)";
        Title = $"{(_session.Modified ? "● " : "")}{name} — OpenPub";
        DocTitle.Text = (_session.Modified ? "● " : "") + name;
        StatusText.Text = $"{_session.Doc.Pages.Count} pages · {(_session.Package.FilePath ?? "non enregistré")}";
    }

    static string Cm(double pt) => (pt * CmPerPoint).ToString("0.00", CultureInfo.CurrentCulture);

    void RefreshProperties()
    {
        var s = Sel;
        UpdateRulers();
        GeometryPanel.Visibility = TextPanel.Visibility = ImagePanel.Visibility = Visibility.Collapsed;
        RefreshFormat();
        RefreshContextTabs();
        RefreshHelpLink();
        HighlightActivePage();
        if (s is null || _session is null)
        {
            SelTitle.Text = "Aucune sélection";
            SelInfo.Visibility = Visibility.Visible;
            return;
        }
        SelInfo.Visibility = Visibility.Collapsed;
        GeometryPanel.Visibility = Visibility.Visible;
        if (_session.MultiSelection)
        {
            var b = ShapeOps.Bounds(_session.Selection);
            bool grouped = _session.Selection.All(x => x.Group is not null && x.Group == s.Group);
            SelTitle.Text = (grouped ? "Groupe de " : "") + $"{_session.Selection.Count} objets" + (AnyLockedSel ? " (verrouillé)" : "");
            if (!BoxX.IsKeyboardFocused) BoxX.Text = Cm(b.X);
            if (!BoxY.IsKeyboardFocused) BoxY.Text = Cm(b.Y);
            if (!BoxW.IsKeyboardFocused) BoxW.Text = Cm(b.W);
            if (!BoxH.IsKeyboardFocused) BoxH.Text = Cm(b.H);
            return;
        }
        SelTitle.Text = (s.Locked ? "🔒 " : "") + s.Kind switch
        {
            ShapeKind.Text => "Zone de texte",
            ShapeKind.Image => s.IsPlaceholder ? "Espace réservé pour image" : "Image",
            ShapeKind.WordArt => "WordArt",
            ShapeKind.Ellipse => "Ellipse",
            ShapeKind.Rect => "Rectangle",
            _ => "Forme",
        };
        if (!BoxX.IsKeyboardFocused) BoxX.Text = Cm(s.X);
        if (!BoxY.IsKeyboardFocused) BoxY.Text = Cm(s.Y);
        if (!BoxW.IsKeyboardFocused) BoxW.Text = Cm(s.W);
        if (!BoxH.IsKeyboardFocused) BoxH.Text = Cm(s.H);
        if (s.IsText)
        {
            TextPanel.Visibility = Visibility.Visible;
            var head = StoryHead(s);
            var chars = head?.Paragraphs?.Sum(p => p.PlainText.Length) ?? 0;
            var info = $"{chars} caractères";
            if (s.Chain is not null && SelPage is not null)
                info += $" · zone {(s.ChainIndex ?? 0) + 1} sur {SelPage.ChainFrames(s.Chain).Count} (texte lié)";
            var layout = SelPage is null ? null : _session.Layout(SelPage);
            var chain = layout?.Chains.FirstOrDefault(c => c.Frames.Any(f => f.Shape == s));
            if (chain is not null && chain.Scale < 0.999) info += $" · texte réduit à {chain.Scale * 100:0} % pour tenir";
            if (chain is { Overflow: true }) info += " · ⚠ le texte déborde";
            TextInfo.Text = info;
        }
        if (s.IsImage)
        {
            ImagePanel.Visibility = Visibility.Visible;
            var bmp = _session.Images.Get(s.Src);
            ImageInfo.Text = bmp is null ? (s.IsPlaceholder ? "Cadre vide : cliquez sur son bouton pour insérer une image" : "Image introuvable") : $"{bmp.PixelWidth} × {bmp.PixelHeight} px · {Path.GetFileName(s.Src)}";
        }
    }

    Shape? StoryHead(Shape s)
    {
        if (s.Chain is null || SelPage is null) return s;
        return SelPage.ChainFrames(s.Chain).FirstOrDefault();
    }

    void ApplyGeometryBox(TextBox box)
    {
        if (Sel is not { } s || SelPage is not { } page || _session is null) return;
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (!double.TryParse(box.Text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, CultureInfo.CurrentCulture, out var cm))
        { RefreshProperties(); return; }
        var pt = cm / CmPerPoint;
        if (_session.MultiSelection)
        {
            var b0 = ShapeOps.Bounds(_session.Selection);
            var from = new Rect(b0.X, b0.Y, b0.W, b0.H);
            var to = from;
            switch (box.Tag as string)
            {
                case "X": to.X = pt; break;
                case "Y": to.Y = pt; break;
                case "W": to.Width = Math.Max(1, pt); break;
                case "H": to.Height = Math.Max(1, pt); break;
            }
            if (to == from) return;
            var sel = _session.Selection.Where(x => !x.Locked).ToList();
            _session.Change(page, () => PageView.FitShapes(sel, from, to));
            RefreshProperties();
            return;
        }
        if (s.Locked) { RefreshProperties(); return; }
        var r = new Rect(s.X, s.Y, s.W, s.H);
        switch (box.Tag as string)
        {
            case "X": r.X = pt; break;
            case "Y": r.Y = pt; break;
            case "W": r.Width = Math.Max(1, pt); break;
            case "H": r.Height = Math.Max(1, pt); break;
        }
        if (Math.Abs(r.X - s.X) < 1e-6 && Math.Abs(r.Y - s.Y) < 1e-6 && Math.Abs(r.Width - s.W) < 1e-6 && Math.Abs(r.Height - s.H) < 1e-6) return;
        _session.Change(page, () => PageView.MoveShape(s, r));
    }
    void GeoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyGeometryBox((TextBox)sender); e.Handled = true; }
    }
    void GeoBox_LostFocus(object sender, RoutedEventArgs e) => ApplyGeometryBox((TextBox)sender);

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session is not null && e.Key == Key.F7) { SpellingDialog_Click(this, e); e.Handled = true; return; }
        if (_session is not null && Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.H)
        {
            OpenFind(replace: e.Key == Key.H);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F1)
        {
            Help.HelpService.Open(CurrentHelpTopic());
            e.Handled = true;
            return;
        }
        // Ctrl+Maj+1..9 : rouvrir un document recent (meme numero que dans le menu Fichier)
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is >= Key.D1 and <= Key.D9 && TrackRecent)
        {
            var recent = RecentFiles.List();
            int i = e.Key - Key.D1;
            if (i < recent.Count) { e.Handled = true; OpenRecent(recent[i]); }
            return;
        }
        if (_session is null || Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (_session.TextEdit is not null) return;          // la page gere le clavier pendant l'edition du texte
        if (e.Key == Key.Escape && _session.LinkSource is not null) { CancelLink(); e.Handled = true; return; }
        if (e.Key == Key.Escape && _painterFont is not null) { EndPainter(); StatusText.Text = ""; e.Handled = true; return; }
        if (e.Key == Key.Escape) { SetDrawTool(null); _session.Select(null); e.Handled = true; return; }
        if (Sel is null || SelPage is not { } page) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-step, 0.0), Key.Right => (step, 0.0), Key.Up => (0.0, -step), Key.Down => (0.0, step), _ => (0.0, 0.0),
        };
        if (dx != 0 || dy != 0)
        {
            var moving = _session.Selection.Where(x => !x.Locked).ToList();
            if (moving.Count > 0) _session.Change(page, () => { foreach (var x in moving) ShapeOps.Translate(x, dx, dy); });
            e.Handled = true;
        }
    }

    // ================================================================ actions sur la selection

    void Renumber(Page page)
    {
        for (int i = 0; i < page.Shapes.Count; i++) page.Shapes[i].Z = i + 1;
    }

    Page? ActivePage()
    {
        if (_session is null) return null;
        if (SelPage is not null) return SelPage;
        // page la plus visible a l'ecran
        foreach (var v in _views)
        {
            var top = v.TranslatePoint(new Point(0, 0), Scroller).Y;
            if (top + v.ActualHeight / 2 > 0) return v.Page;
        }
        return PageList.FirstOrDefault();
    }

    void AddText_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePage() is not { } page || _session is null) return;
        var s = new Shape
        {
            Kind = ShapeKind.Text, X = page.Width / 2 - 100, Y = page.Height / 2 - 40, W = 200, H = 80,
            Padding = new[] { 2.85, 2.85, 2.85, 2.85 }, AutoFit = Shape.NewAutoFit,
            Paragraphs = new List<Paragraph>
            {
                new() { Align = "left", Runs = { new Run { Text = "Votre texte ici", Font = new FontSpec { Family = "Cavolini", Size = 12, Color = "#000000", Lang = "fr" } } } },
            },
        };
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.BeginTextEdit(page, s);
        _session.TextEdit?.SelectAll();
        _views.FirstOrDefault(v => v.Page == page)?.Focus();
    }

    void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (ActivePage() is not { } page || _session is null) return;
        var file = PickImage();
        if (file is null) return;
        var src = _session.Package.AddAsset(file);
        var bmp = _session.Images.Get(src);
        double w = 200, h = bmp is null ? 150 : 200.0 * bmp.PixelHeight / Math.Max(1, bmp.PixelWidth);
        var s = new Shape { Kind = ShapeKind.Image, Src = src, X = page.Width / 2 - w / 2, Y = page.Height / 2 - h / 2, W = w, H = h };
        _session.Change(page, () => { page.Shapes.Add(s); Renumber(page); });
        _session.Select(s, page);
    }

    string? PickImage()
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif)|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff" };
        return dlg.ShowDialog(this) == true ? dlg.FileName : null;
    }

    void ReplaceImage_Click(object sender, RoutedEventArgs e) { if (Sel is { IsImage: true } s) ReplaceImage(s); }

    void ReplaceImage(Shape s)
    {
        if (_session is null || _session.PageOf(s) is not { } page) return;
        if (s.IsPlaceholder) { FillPlaceholder(s); return; }        // cadre vide : il garde sa taille (lot 9)
        var file = PickImage();
        if (file is null) return;
        var src = _session.Package.AddAsset(file);
        var bmp = _session.Images.Get(src);
        _session.Change(page, () =>
        {
            s.Src = src;
            s.Mime = null;
            // conserve la largeur, adapte la hauteur aux proportions de la nouvelle image
            if (bmp is not null && bmp.PixelWidth > 0)
            {
                var r = new Rect(s.X, s.Y, s.W, s.W * bmp.PixelHeight / bmp.PixelWidth);
                s.Clip = null;
                PageView.MoveShape(s, r);
            }
        });
        _session.Select(s, page);
    }

    void EditText_Click(object sender, RoutedEventArgs e) { if (Sel is { IsText: true } s) EditText(s); }

    void EditInPlace_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Sel is not { IsText: true } s || SelPage is not { } page) return;
        _session.BeginTextEdit(page, s);
        _views.FirstOrDefault(v => v.Page == page)?.Focus();
    }

    // ================================================================ mise en forme du texte

    bool _updatingFormat;

    /// <summary>
    /// Applique une mise en forme : a la selection (ou au mot) pendant l'edition sur la page,
    /// sinon a tout le texte de la zone selectionnee.
    /// </summary>
    void ApplyFont(Action<FontSpec> change)
    {
        if (_session is null) return;
        if (_session.TextEdit is { } ed) { ed.ApplyFont(change); RefocusPage(); return; }
        if (SelPage is not { } page || SelectedStories() is not { Count: > 0 } stories) return;
        _session.Change(page, () => { foreach (var paras in stories) StoryEditor.ApplyFont(paras, new TextPos(0, 0), StoryEditor.End(paras), change); });
    }

    /// <summary>Textes des zones selectionnees (une fois par chaine), hors edition sur la page.</summary>
    List<List<Paragraph>> SelectedStories()
    {
        var res = new List<List<Paragraph>>();
        if (_session is null) return res;
        foreach (var s in _session.Selection.Where(x => x.IsText))
            if (StoryHead(s) is { } head && !res.Contains(head.Paragraphs ??= new List<Paragraph>())) res.Add(head.Paragraphs);
        return res;
    }

    void ApplyParagraph(Action<Paragraph> change)
    {
        if (_session is null) return;
        if (_session.TextEdit is { } ed) { ed.ApplyParagraph(change); RefocusPage(); return; }
        if (SelPage is not { } page || SelectedStories() is not { Count: > 0 } stories) return;
        _session.Change(page, () => { foreach (var paras in stories) StoryEditor.ApplyParagraph(paras, new TextPos(0, 0), StoryEditor.End(paras), change); });
    }

    void RefocusPage()
    {
        if (_session?.TextEdit is { } ed) _views.FirstOrDefault(v => v.Page == ed.Page)?.Focus();
    }

    FontSpec? CurrentFont()
    {
        if (_session?.TextEdit is { } ed) return ed.CurrentFont();
        if (Sel is { IsText: true } s && StoryHead(s)?.Paragraphs is { Count: > 0 } paras)
            return paras.SelectMany(p => p.Runs).FirstOrDefault(r => r.Text.Trim().Length > 0)?.Font ?? paras[0].Runs.FirstOrDefault()?.Font;
        return null;
    }

    void RefreshFormat()
    {
        RefreshStyleBox();
        bool text = Sel is { IsText: true };
        FormatHint.Text = "";
        if (!text) return;
        bool editing = _session?.TextEdit is not null;
        FormatHint.Text = editing
            ? "S'applique au texte sélectionné, ou au mot sous le curseur. Échap pour terminer l'écriture."
            : "Police et paragraphe (onglet Accueil) s'appliquent à tout le texte. Double-cliquez dans le texte pour ne mettre en forme qu'une partie.";
        if (CurrentFont() is not { } f) return;
        _updatingFormat = true;
        if (!FontBox.IsKeyboardFocusWithin) FontBox.Text = f.Family ?? "";
        if (!SizeBox.IsKeyboardFocusWithin) SizeBox.Text = (f.Size ?? 10).ToString("0.#", CultureInfo.CurrentCulture);
        ColorSwatch.Background = PageRenderer.Brush(f.Color);
        _updatingFormat = false;
    }

    void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFormat && FontBox.SelectedItem is string name) ApplyFont(f => f.Family = name);
    }
    void FontBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(FontBox.Text)) return;
        var name = FontBox.Text.Trim();
        ApplyFont(f => f.Family = name);
        e.Handled = true;
    }
    void ApplySize(string text)
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, CultureInfo.CurrentCulture, out var pt) && pt >= 1 && pt <= 500)
            ApplyFont(f => f.Size = pt);
    }
    void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFormat && SizeBox.SelectedItem is { } v) ApplySize(Convert.ToString(v, CultureInfo.CurrentCulture)!);
    }
    void SizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplySize(SizeBox.Text); e.Handled = true; }
    }

    void Toggle(Func<FontSpec, bool> get, Action<FontSpec, bool> set)
    {
        if (_session?.TextEdit is { } ed) { ed.ToggleFont(get, set); RefocusPage(); return; }
        var on = CurrentFont() is { } f && get(f);
        ApplyFont(x => set(x, !on));
    }
    void Bold_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Bold, (f, v) => f.Bold = v);
    void Italic_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Italic, (f, v) => f.Italic = v);
    void Underline_Click(object sender, RoutedEventArgs e) => Toggle(f => f.Underline, (f, v) => f.Underline = v);
    void Align_Click(object sender, RoutedEventArgs e)
    {
        var a = (string)((Button)sender).CommandParameter;
        ApplyParagraph(p => p.Align = a);
    }

    IEnumerable<string> DocumentColors() =>
        _session is null ? Array.Empty<string>() :
        (_session.Doc.Theme?.Colors ?? new List<string>()).Concat(_session.Doc.AllShapes().SelectMany(sh => (sh.Paragraphs ?? new List<Paragraph>()).SelectMany(p => p.Runs).Select(r => r.Font?.Color)
            .Append(sh.Fill?.Color).Append(sh.Line?.Color))).Where(c => c is not null).Select(c => c!);

    /// <summary>Ouvre le selecteur de couleur ; renvoie (choisi, couleur ou null pour "aucune").</summary>
    (bool Picked, string? Color) PickColor(string? current, bool allowNone, string title)
    {
        var dlg = new ColorPickerWindow(current, DocumentColors(), allowNone, title) { Owner = this };
        return dlg.ShowDialog() == true ? (dlg.Picked, dlg.Result) : (false, null);
    }

    void Color_Click(object sender, RoutedEventArgs e)
    {
        var (ok, color) = PickColor(CurrentFont()?.Color, false, "Couleur du texte");
        if (ok && color is not null) ApplyFont(f => f.Color = color);
    }

    void EditText(Shape s)
    {
        if (_session is null || _session.PageOf(s) is not { } page) return;
        var head = s.Chain is null ? s : page.ChainFrames(s.Chain).First();
        var dlg = new TextEditorWindow(_session, head.Paragraphs ?? new List<Paragraph>()) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } paras)
            _session.Change(page, () => head.Paragraphs = paras);
        _session.Select(s, page);
    }
}
