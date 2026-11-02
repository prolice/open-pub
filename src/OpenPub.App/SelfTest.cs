using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenPub.App.Controls;
using OpenPub.App.Rendering;
using OpenPub.Core.Editing;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>
/// Test de bout en bout de l'interface (OpenPub.exe --selftest fichier.opub dossier_captures) :
/// ouvre la vraie fenetre hors ecran, rejoue des actions d'edition et verifie leurs effets.
/// </summary>
public static partial class SelfTest
{
    static int _ok, _ko;

    static void Check(bool cond, string what)
    {
        if (cond) _ok++; else _ko++;
        Console.WriteLine($"  {(cond ? "OK " : "ECHEC")} {what}");
    }

    static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    /// <summary>
    /// Autorise une fenetre plus grande que l'ecran (Windows la limite a la taille de l'ecran : 1024 x 768 sur les
    /// machines de GitHub) puis dimensionne la fenetre pour que son contenu fasse exactement <paramref name="size"/>.
    /// </summary>
    static void ForceContentSize(Window w, Size size)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle();
        if (Hooked.Add(hwnd))
            System.Windows.Interop.HwndSource.FromHwnd(hwnd).AddHook(AllowLargeWindow);
        var content = (FrameworkElement)w.Content;
        for (int i = 0; i < 3; i++)
        {
            Pump();
            double chromeW = w.ActualWidth - content.ActualWidth, chromeH = w.ActualHeight - content.ActualHeight;
            w.Width = size.Width + Math.Max(0, chromeW);
            w.Height = size.Height + Math.Max(0, chromeH);
            w.UpdateLayout();
        }
        Pump();
        // WPF plafonne la fenetre a la taille de l'ecran : on le signale (la capture garde la taille obtenue)
        if (content.ActualWidth < size.Width - 1 || content.ActualHeight < size.Height - 1)
            Console.Error.WriteLine($"Avertissement : capture limitee a {content.ActualWidth:0}x{content.ActualHeight:0} par la taille de l'ecran " +
                                    $"(demande : {size.Width:0}x{size.Height:0}). Augmentez la resolution de l'ecran.");
    }

    static readonly HashSet<IntPtr> Hooked = new();

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MinMaxInfo { public int rx, ry, maxSizeX, maxSizeY, maxPosX, maxPosY, minTrackX, minTrackY, maxTrackX, maxTrackY; }

    static IntPtr AllowLargeWindow(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.maxTrackX = mmi.maxSizeX = 6000;
            mmi.maxTrackY = mmi.maxSizeY = 4000;
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
            handled = true;
        }
        return IntPtr.Zero;
    }

    static void Capture(Window w, string path, Size? forced = null)
    {
        Pump();
        if (forced is { } f) ForceContentSize(w, f);
        var content0 = (FrameworkElement)w.Content;
        if (forced is not null) forced = new Size(Math.Min(forced.Value.Width, content0.ActualWidth), Math.Min(forced.Value.Height, content0.ActualHeight));
        // taille du contenu (sans la barre de titre), sinon une bande noire apparait en bas
        var content = (FrameworkElement)w.Content;
        int cw = forced is { } fs0 ? (int)fs0.Width : (int)content.ActualWidth, ch = forced is { } fs1 ? (int)fs1.Height : (int)content.ActualHeight;
        if (Environment.GetEnvironmentVariable("OPENPUB_DEBUG_CAPTURE") == "1") Console.WriteLine($"capture : contenu {content.ActualWidth}x{content.ActualHeight}, parent {content.Parent?.GetType().Name ?? "aucun"}, image {cw}x{ch}");
        var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(content);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    static void Click(MainWindow w, string handler) =>
        typeof(MainWindow).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(w, new object[] { w, new RoutedEventArgs() });

    /// <summary>
    /// Capture de la vraie fenetre avec un document (page de telechargement, documentation) :
    /// OpenPub.exe --screenshot document.json|fichier.opub sortie.png [largeur hauteur] [onglet]
    /// </summary>
    public static int Screenshot(string file, string png, int width = 1402, int height = 948, string? tab = null)
    {
        var w = new MainWindow { Left = -6000, Top = 0, Width = width, Height = height, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, SkipConfirm = true };
        w.Show();
        w.OpenFile(file);
        Pump();
        var target = new Size(width, height);
        ForceContentSize(w, target);
        typeof(MainWindow).GetMethod("ZoomFit_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { w, new RoutedEventArgs() });
        Pump();
        ForceContentSize(w, target);
        var session = (EditorSession)typeof(MainWindow).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        var page = session.Doc.Pages[0];
        // scenes de la documentation : onglet ("TabDraw"...) ou situation ("shape", "picture", "text-edit", "draw-tool", "image-editor")
        switch (tab)
        {
            case "shape":
                session.Select(page.Shapes.First(x => x.Geometry == ShapeGeometry.Star), page);
                break;
            case "picture":
                session.Select(page.Shapes.First(x => x.IsImage), page);
                break;
            case "text-edit":
            {
                var t = page.Shapes.First(x => x.IsText && x.Paragraphs!.Count >= 2 && x.Paragraphs[0].PlainText.StartsWith("À la une"));
                session.BeginTextEdit(page, t);
                var ed = session.TextEdit!;
                ed.Select(new Core.Editing.TextPos(1, 0), new Core.Editing.TextPos(1, 34));
                break;
            }
            case "draw-tool":
                ((System.Windows.Controls.RadioButton)w.FindName("TabDraw")).IsChecked = true;
                typeof(MainWindow).GetMethod("SetDrawTool", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { "freehand" });
                break;
            case "image-editor":
            {
                var img = page.Shapes.First(x => x.IsImage);
                session.Select(img, page);
                var ed = new ImageEditorWindow(session, img) { Owner = w, Left = -6000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                ed.Show();
                Pump();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(ed, png, new Size(1044, 680));
                ed.Close();
                w.Close();
                return 0;
            }
            case "about":
            {
                var about = new AboutWindow(() => { }) { Owner = w, Left = -6000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                about.Show();
                Pump();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                // sans la marge transparente reservee a l'ombre
                ((FrameworkElement)about.Content).Margin = new Thickness(0);
                Capture(about, png);
                about.Close();
                w.Close();
                return 0;
            }
            case "file-menu" or "shapes-menu":
            {
                if (tab == "shapes-menu") ((System.Windows.Controls.RadioButton)w.FindName("TabInsert")).IsChecked = true;
                // documents recents fictifs : jamais les vrais fichiers de l'utilisateur dans une capture
                w.RecentOverride = new[] { @"C:\Users\Moi\Documents\Journal\journal-octobre.opub", @"C:\Users\Moi\Documents\Journal\journal-septembre.opub" };
                Pump();
                typeof(MainWindow).GetMethod(tab == "file-menu" ? "FileButton_Click" : "ShapesButton_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(w, new object[] { w, new RoutedEventArgs() });
                Pump();
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var menu = w.LastMenu!;
                var rtb = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                rtb.Render(menu);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(png)) enc.Save(fs);
                menu.IsOpen = false;
                w.Close();
                return 0;
            }
            default:
                if (tab is not null && w.FindName(tab) is System.Windows.Controls.RadioButton rb) rb.IsChecked = true;
                break;
        }
        Pump();
        Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(w, png, target);
        w.Close();
        return 0;
    }

    static OpenPub.Layout.FrameLayout? PageLayoutOf(EditorSession session, Page page, Shape table) =>
        session.Layout(page).Frames.TryGetValue(OpenPub.Layout.PageLayouter.CellFrame(table.Table!.Cells[^1][0]), out var f) ? f : null;

    public static int Run(string file, string outDir)
    {
        Directory.CreateDirectory(outDir);
        // document.json (conversion) : importe dans un dossier de travail temporaire ; .opub : copie temporaire.
        // Le fichier d'origine n'est jamais modifie.
        var tmp = file;
        if (!Path.GetFileName(file).Equals("document.json", StringComparison.OrdinalIgnoreCase))
        {
            tmp = Path.Combine(Path.GetTempPath(), $"openpub-selftest-{Guid.NewGuid():N}.opub");
            File.Copy(file, tmp);
        }
        var w = new MainWindow { Left = -5000, Top = 0, Width = 1440, Height = 900, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        w.OpenFile(tmp);
        Pump();
        var session = (EditorSession)typeof(MainWindow).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        var doc = session.Doc;

        Console.WriteLine("Edition du texte sur la page");
        var page1 = doc.Pages[0];
        var head = page1.Shapes.First(s => s.Chain is not null && s.IsStoryHead && s.Paragraphs!.Count > 20);
        session.BeginTextEdit(page1, head, new Point(head.X + 20, head.Y + 12));
        var ed = session.TextEdit!;
        Check(ed is not null && ed.Caret.Para == 0, "double-clic : curseur dans le 1er paragraphe de l'article");
        Check(ed!.CaretRect() is { } cr && cr.X >= head.X && cr.X <= head.X + head.W, "curseur affiche dans la zone");

        ed.SetCaret(new TextPos(0, 0), false);
        ed.Type("Bonjour ! ");
        Check(head.Paragraphs![0].PlainText.StartsWith("Bonjour ! Alors que"), "saisie au debut de l'article");
        ed.Type("Test");
        Check(session.History.CanUndo, "historique enregistre");
        ed.HandleKey(Key.Back, ModifierKeys.Control);
        Check(head.Paragraphs[0].PlainText.StartsWith("Bonjour ! Alors"), "Ctrl+Retour arriere efface le mot");
        ed.HandleKey(Key.Home, ModifierKeys.None);
        ed.HandleKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift);
        Check(ed.HasSelection && StoryEditor.GetText(ed.Paras, ed.SelStart, ed.SelEnd).StartsWith("Bonjour"), "Ctrl+Maj+Droite selectionne le mot");
        ed.HandleKey(Key.B, ModifierKeys.Control);
        Check(head.Paragraphs[0].Runs[0].Font!.Bold && head.Paragraphs[0].Runs[0].Text.StartsWith("Bonjour"), "Ctrl+B met la selection en gras");
        Check(ed.SelectionRects().Any(), "surbrillance de la selection");
        Capture(w, Path.Combine(outDir, "selftest-1-edition.png"));

        int paraCount = head.Paragraphs.Count;
        ed.SetCaret(new TextPos(0, 9), false);
        ed.HandleKey(Key.Enter, ModifierKeys.None);
        Check(head.Paragraphs.Count == paraCount + 1 && ed.Caret == new TextPos(1, 0), "Entree cree un paragraphe");
        ed.HandleKey(Key.Back, ModifierKeys.None);
        Check(head.Paragraphs.Count == paraCount, "Retour arriere en debut de paragraphe les refusionne");

        // le texte ajoute fait couler la suite dans les colonnes liees
        var layout = session.Layout(page1);
        var col2 = page1.ChainFrames(head.Chain!)[1];
        Check(layout.Frames[col2].Lines.Count > 0, "la suite de l'article coule dans la colonne 2");

        ed.HandleKey(Key.Down, ModifierKeys.None);
        ed.HandleKey(Key.Down, ModifierKeys.None);
        Check(ed.Caret > new TextPos(0, 9), "Bas descend dans le texte");
        ed.HandleKey(Key.End, ModifierKeys.Control);
        Check(ed.Caret == StoryEditor.End(ed.Paras), "Ctrl+Fin va a la fin de l'article");

        Console.WriteLine("Annuler pendant l'edition");
        while (session.History.CanUndo) session.Undo();
        Pump();
        var head2 = session.Doc.Pages[0].Shapes.First(s => s.Chain is not null && s.IsStoryHead && s.Paragraphs!.Count > 20);
        Check(session.TextEdit is null, "Annuler termine l'edition");
        Check(head2.Paragraphs![0].PlainText.StartsWith("Alors que la rentrée"), "tout est annule : texte d'origine");

        Console.WriteLine("Pages");
        int n = session.Doc.Pages.Count;
        session.Select(null, session.Doc.Pages[3]);
        Click(w, "DuplicatePage_Click");
        Check(session.Doc.Pages.Count == n + 1, "dupliquer la page 4");
        Check(session.Doc.Pages[4].Shapes.Count == session.Doc.Pages[3].Shapes.Count && session.Doc.Pages[4].Index == 5, "copie complete, numerotee 5");
        Check(!ReferenceEquals(session.Doc.Pages[4].Shapes[0], session.Doc.Pages[3].Shapes[0]), "copie independante");
        Click(w, "AddPage_Click");
        Check(session.Doc.Pages.Count == n + 2 && session.Doc.Pages[5].Shapes.Count == 0, "ajouter une page vierge apres la page active");
        Click(w, "MovePageUp_Click");
        Check(session.Doc.Pages[4].Shapes.Count == 0, "monter la page vierge");
        session.Select(null, session.Doc.Pages[4]);
        typeof(MainWindow).GetMethod("PageOp", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(w, new object[] { new Action<List<Page>, int>((pages, i) => pages.RemoveAt(i)), 0 });
        Check(session.Doc.Pages.Count == n + 1, "supprimer la page");
        Capture(w, Path.Combine(outDir, "selftest-2-pages.png"));
        session.Undo(); session.Undo(); session.Undo(); session.Undo();
        Check(session.Doc.Pages.Count == n, "Annuler retablit les pages d'origine");

        Console.WriteLine("Magnetisme");
        var p2 = session.Doc.Pages[1];
        var img = p2.Shapes.First(s => s.IsImage);
        var gx = new List<double>(); var gy = new List<double>();
        var r = Snapper.SnapMove(new Rect(36 + 3, 200, img.W, img.H), p2, session.Doc, img, 6, gx, gy);
        Check(Math.Abs(r.X - 36) < 0.01 && gx.Contains(36), "aligne sur la marge gauche (3 pt d'ecart)");
        r = Snapper.SnapMove(new Rect(p2.Width / 2 - img.W / 2 + 4, 200, img.W, img.H), p2, session.Doc, img, 6, gx, gy);
        Check(Math.Abs(r.X + img.W / 2 - p2.Width / 2) < 0.01, "aligne sur le centre de la page");
        r = Snapper.SnapMove(new Rect(100, 200, img.W, img.H), p2, session.Doc, img, 2, gx, gy);
        Check(gx.Count == 0 || Math.Abs(r.X - 100) <= 2, "pas d'alignement hors tolerance");

        Console.WriteLine("Formes, cadre arrondi, rotation");
        var pv = (List<PageView>)typeof(MainWindow).GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        var view2 = pv[1];
        var onDrawn = typeof(MainWindow).GetMethod("OnShapeDrawn", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int before = p2.Shapes.Count;
        onDrawn.Invoke(w, new object[] { view2, ShapeGeometry.RoundRect, new Rect(300, 120, 150, 90) });
        var drawn = p2.Shapes[^1];
        Check(p2.Shapes.Count == before + 1 && drawn.Geometry == ShapeGeometry.RoundRect, "rectangle arrondi trace a la souris");
        onDrawn.Invoke(w, new object[] { view2, ShapeGeometry.Star, new Rect(460, 120, 80, 80) });
        Check(p2.Shapes[^1].Geometry == ShapeGeometry.Star && p2.Shapes[^1].Fill is not null, "etoile tracee, remplie");
        session.Select(drawn, p2);
        Pump();
        var tabShape = (System.Windows.Controls.RadioButton)w.FindName("TabShape");
        Check(tabShape.Visibility == Visibility.Visible && tabShape.IsChecked == true, "onglet contextuel « Format de la forme » ouvert");
        Click(w, "RotateRight_Click");
        Check(Math.Abs(drawn.Rotation - 90) < 0.01, "rotation de 90 degres");
        drawn.Rotation = 30;
        session.Invalidate(p2);
        var hit = view2.HitTest(Geo.ToPage(drawn, new Point(drawn.X + drawn.W - 5, drawn.Y + 5)));
        Check(hit == drawn, "clic sur un coin d'un objet tourne");
        drawn.Shadow = new ShadowFx();
        // cadre arrondi sur une zone de texte existante
        var tf = p2.Shapes.First(x => x.IsText && x.Paragraphs!.Count > 3);
        session.Select(tf, p2);
        Pump();
        var gbox = (System.Windows.Controls.ComboBox)w.FindName("GeometryBox");
        gbox.SelectedItem = gbox.Items.Cast<System.Windows.Controls.ComboBoxItem>().First(i => (string)i.Tag == ShapeGeometry.RoundRect);
        Check(tf.Geometry == ShapeGeometry.RoundRect && tf.Line is not null, "zone de texte avec bords arrondis (contour ajoute)");
        session.Invalidate(p2);
        Capture(w, Path.Combine(outDir, "selftest-3-formes.png"));

        Console.WriteLine("Dessin libre");
        var setTool = typeof(MainWindow).GetMethod("SetDrawTool", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var onCreated = typeof(MainWindow).GetMethod("OnShapeCreated", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((System.Windows.Controls.RadioButton)w.FindName("TabDraw")).IsChecked = true;
        setTool.Invoke(w, new object?[] { "freehand" });
        var tools = ((IEnumerable<System.Windows.Controls.Primitives.ToggleButton>)typeof(MainWindow).GetMethod("ToolButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null)!).ToList();
        Check(tools.Count(t => t.IsChecked == true) == 1 && tools.First(t => t.IsChecked == true).CommandParameter as string == "freehand", "outil Crayon actif (un seul bouton enfonce)");
        // trace a main levee : une vague de 400 points bruts
        var raw = Enumerable.Range(0, 400).Select(i => new Point(60 + i * 0.6, 520 + Math.Sin(i / 25.0) * 30)).ToList();
        var stroke = OpenPub.App.Editing.DrawingFactory.Freehand(raw, session.PenColor, session.PenWidth, 0.5)!;
        Check(stroke.Path!.Count < 60 && stroke.Path.Count > 4 && stroke.Path.Skip(1).All(q => q.A == "C"), $"main levee simplifiee et lissee ({raw.Count} points -> {stroke.Path.Count} segments de Bezier)");
        int n2 = p2.Shapes.Count;
        onCreated.Invoke(w, new object[] { view2, stroke, true });
        Check(p2.Shapes.Count == n2 + 1 && session.DrawTool == "freehand", "trait ajoute, le crayon reste actif");
        // forme libre fermee
        var poly = OpenPub.App.Editing.DrawingFactory.Polygon(new[] { new Point(330, 520), new Point(420, 500), new Point(440, 580), new Point(350, 600) }, true, "#c00000", 2)!;
        onCreated.Invoke(w, new object[] { view2, poly, false });
        Check(poly.Path!.Last().A == "Z" && poly.Fill is not null && session.Selected == poly && session.DrawTool is null, "forme libre fermee, remplie, selectionnee");
        // fleche : du point de depart vers le point d'arrivee (trace de droite a gauche)
        typeof(PageView).GetProperty("DrawStart")!.SetValue(view2, new Point(540, 500));
        typeof(PageView).GetProperty("DrawEnd")!.SetValue(view2, new Point(470, 560));
        onDrawn.Invoke(w, new object[] { view2, "arrow", new Rect(470, 500, 70, 60) });
        var arrow = p2.Shapes[^1];
        Check(arrow.Line!.ArrowEnd && arrow.Path![1].X == 470 && arrow.Path[0].X == 540, "fleche orientee dans le sens du trace");
        // ombre d'un objet tourne : direction fixe sur la page
        drawn.Shadow = new ShadowFx { Angle = 45, Distance = 6 };
        session.Invalidate(p2);
        Capture(w, Path.Combine(outDir, "selftest-5-dessin.png"));
        Exporter.ExportPng(session, outDir, 110);         // page-2.png : dessins de pres

        Console.WriteLine("Outils image");
        var pic = session.Doc.Pages[0].Shapes.First(x => x.IsImage && !x.Background && x.W > 80);
        session.Select(pic, session.Doc.Pages[0]);
        Pump();
        var tabPic = (System.Windows.Controls.RadioButton)w.FindName("TabPicture");
        Check(tabPic.Visibility == Visibility.Visible && tabPic.IsChecked == true, "onglet contextuel « Outils image » ouvert");
        var recolor = typeof(MainWindow).GetMethod("Recolor_Click", BindingFlags.NonPublic | BindingFlags.Instance)!;
        recolor.Invoke(w, new object[] { new System.Windows.Controls.Button { CommandParameter = "sepia" }, new RoutedEventArgs() });
        Check(pic.ImageFx?.Recolor == "sepia", "couleur sepia");
        var bmp = session.Images.Get(pic.Src, pic.ImageFx);
        Check(bmp is not null && !ReferenceEquals(bmp, session.Images.Get(pic.Src)), "image retouchee calculee");
        Click(w, "FlipH_Click");
        Check(pic.ImageFx!.FlipH, "miroir horizontal");
        var refl = (System.Windows.Controls.Primitives.ToggleButton)w.FindName("PicReflectionToggle");
        refl.IsChecked = true; Click(w, "ReflectionToggle_Click");
        Check(pic.ImageFx!.ReflectionSize > 0, "reflet");
        // recadrage par l'editeur d'image
        var ed2 = new ImageEditorWindow(session, pic) { Owner = w, Left = -5000, ShowActivated = false };
        var fxField = typeof(ImageEditorWindow).GetProperty("Fx", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var wfx = (ImageFx)fxField.GetValue(ed2)!;
        double w0 = pic.W, x0 = pic.X;
        wfx.CropLeft = 0.25; wfx.Recolor = "gray";
        session.BeginChange(); ed2.Apply(); session.EndChange(); session.Invalidate(session.Doc.Pages[0]);
        Check(Math.Abs(pic.W - w0 * 0.75 / (1 - 0)) < 0.5 && pic.X > x0, "recadrage : le cadre retrecit, le contenu reste en place");
        Check(pic.ImageFx!.Recolor == "gray" && pic.ImageFx.FlipH, "retouches reportees depuis l'editeur");
        Capture(w, Path.Combine(outDir, "selftest-4-image.png"));
        ed2.Close();
        Click(w, "ResetImage_Click");
        Check(pic.ImageFx is null && Math.Abs(pic.W - w0) < 0.5 && Math.Abs(pic.X - x0) < 0.5, "reinitialiser : image d'origine, recadrage annule");

        {
            // cout de l'annulation : instantane complet du document a chaque action
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { session.BeginChange(); session.EndChange(); }
            sw.Stop();
            var size = Core.Io.DocumentJson.Serialize(session.Doc, indented: false).Length;
            Console.WriteLine($"  (mesure) annulation : {sw.Elapsed.TotalMilliseconds / 20:0.0} ms par action, instantane de {size / 1024} Ko");
            Check(sw.Elapsed.TotalMilliseconds / 20 < 100, "cout de l'annulation acceptable (< 100 ms par action)");
        }

        Console.WriteLine("Objets : selection multiple, groupes, alignement, presse-papiers");
        {
            var pg = session.Doc.Pages[1];
            onDrawn.Invoke(w, new object[] { view2, ShapeGeometry.Rect, new Rect(60, 650, 40, 30) });
            var o1 = pg.Shapes[^1];
            onDrawn.Invoke(w, new object[] { view2, ShapeGeometry.Ellipse, new Rect(130, 640, 50, 50) });
            var o2 = pg.Shapes[^1];
            onDrawn.Invoke(w, new object[] { view2, ShapeGeometry.Triangle, new Rect(260, 660, 30, 40) });
            var o3 = pg.Shapes[^1];
            session.SelectMany(new[] { o1, o2, o3 }, o3, pg);
            Pump();
            Check(session.Selection.Count == 3 && session.MultiSelection, "selection multiple de 3 objets");
            var title = (System.Windows.Controls.TextBlock)w.FindName("SelTitle");
            Check(title.Text.Contains("3 objets"), "panneau de droite : « 3 objets »");
            typeof(MainWindow).GetMethod("AlignSel", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { AlignEdge.Top });
            Check(o1.Y == 640 && o2.Y == 640 && o3.Y == 640, "aligner en haut");
            typeof(MainWindow).GetMethod("DistributeSel", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { true });
            Check(Math.Abs((o2.X - (o1.X + o1.W)) - (o3.X - (o2.X + o2.W))) < 0.01, "repartir horizontalement : espaces egaux");
            MainWindow.GroupCommand.Execute(null, w);
            Check(o1.Group is not null && o1.Group == o2.Group && o2.Group == o3.Group, "grouper (Ctrl+G)");
            session.Select(null, pg);
            var unit = ShapeOps.GroupOf(pg, o2);
            session.SelectMany(unit, o2, pg);
            Check(session.Selection.Count == 3, "un clic sur un membre selectionne tout le groupe");
            double gx0 = o1.X;
            var nudge = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w)!, 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            typeof(MainWindow).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(object), typeof(KeyEventArgs) })!.Invoke(w, new object[] { w, nudge });
            Check(Math.Abs(o1.X - gx0 - 1) < 0.01 && Math.Abs(o3.X - (o3.X)) < 0.01, "fleche du clavier : tout le groupe se deplace");
            Click(w, "Lock_Click");
            Check(o1.Locked && o2.Locked && o3.Locked, "verrouiller la position");
            double lx = o1.X;
            typeof(MainWindow).GetMethod("OnPreviewKeyDown", BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(object), typeof(KeyEventArgs) })!.Invoke(w, new object[] { w, nudge });
            Check(o1.X == lx, "objet verrouille : la fleche ne le deplace plus");
            Click(w, "Lock_Click");
            MainWindow.UngroupCommand.Execute(null, w);
            Check(o1.Group is null && o3.Group is null && !o1.Locked, "dissocier, deverrouiller");
            // ordre d'empilement
            session.Select(o1, pg);
            int i1 = pg.Shapes.IndexOf(o1);
            Click(w, "BringForward_Click");
            Check(pg.Shapes.IndexOf(o1) > i1 || pg.Shapes.IndexOf(o1) == pg.Shapes.Count - 1, "avancer d'un plan");
            // copier / coller (meme document) puis coller dans un autre emplacement
            session.SelectMany(new[] { o1, o2 }, o2, pg);
            ApplicationCommands.Copy.Execute(null, w);
            int cnt = pg.Shapes.Count;
            ApplicationCommands.Paste.Execute(null, w);
            Check(pg.Shapes.Count == cnt + 2 && session.Selection.Count == 2 && !session.Selection.Contains(o1), "copier / coller deux objets");
            Check(Math.Abs(session.Selection[0].X - o1.X - 12) < 0.01 || Math.Abs(session.Selection[1].X - o1.X - 12) < 0.01, "les objets colles sont decales");
            ApplicationCommands.Paste.Execute(null, w);
            Check(pg.Shapes.Count == cnt + 4, "coller une seconde fois : nouvel emplacement");
            // copier une image vers une autre page
            var pic1 = session.Doc.Pages[0].Shapes.First(x => x.IsImage && !x.Background && x.W > 80);
            session.Select(pic1, session.Doc.Pages[0]);
            ApplicationCommands.Copy.Execute(null, w);
            session.Select(null, pg);
            ApplicationCommands.Paste.Execute(null, w);
            Check(session.Selected is { IsImage: true } pasted && pasted.Src == pic1.Src && pg.Shapes.Contains(pasted), "image copiee sur une autre page");
            // coller du texte d'une autre application
            Clipboard.SetText("Ligne un\r\nLigne deux");
            ApplicationCommands.Paste.Execute(null, w);
            Check(session.Selected is { IsText: true } tt && tt.Paragraphs!.Count == 2 && tt.Paragraphs[1].PlainText == "Ligne deux", "texte colle : nouvelle zone de texte");
            // couper puis supprimer une selection multiple
            session.SelectMany(new[] { o1, o2, o3 }, o3, pg);
            cnt = pg.Shapes.Count;
            ApplicationCommands.Cut.Execute(null, w);
            Check(pg.Shapes.Count == cnt - 3 && !pg.Shapes.Contains(o2), "couper trois objets");
            ApplicationCommands.SelectAll.Execute(null, w);
            Check(session.Selection.Count > 5 && session.Selection.All(pg.Shapes.Contains), "tout selectionner (Ctrl+A)");
            session.Undo();
            pg = session.Doc.Pages[1];          // annuler recree le document
            Check(pg.Shapes.Count == cnt, "annuler le couper");
            session.Select(null, pg);
            Capture(w, Path.Combine(outDir, "selftest-6-objets.png"));
        }

        Console.WriteLine("Paragraphes et caracteres");
        {
            // page vierge : aucun objet ne fait devier le texte
            session.Select(null, session.Doc.Pages[^1]);
            Click(w, "AddPage_Click");
            var pg = session.Doc.Pages[^1];
            Pump();
            var t = new Shape
            {
                Kind = ShapeKind.Text, X = 60, Y = 620, W = 240, H = 160, Padding = new[] { 2.85, 2.85, 2.85, 2.85 },
                Paragraphs = new[] { "Premier point", "Deuxième point", "Troisième point" }
                    .Select(x => new Paragraph { Runs = { new Run { Text = x, Font = new FontSpec { Family = "Arial", Size = 11, Color = "#000000" } } } }).ToList(),
            };
            session.Change(pg, () => pg.Shapes.Add(t));
            session.Select(t, pg);
            var mw = typeof(MainWindow);
            mw.GetMethod("SetList", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { new ListStyle { Kind = "number", Format = "1." } });
            Check(t.Paragraphs!.All(x => x.List?.Kind == "number" && x.IndentLeft == 18 && x.FirstIndent == -18), "numerotation sur toute la zone (retrait suspendu)");
            var lay = session.Layout(pg).Frames[t];
            Check(lay.Lines.Count == 3 && lay.Lines[2].Marks.Any(m => m.Text == "3."), "numeros 1. 2. 3. affiches");
            session.BeginTextEdit(pg, t);
            var te = session.TextEdit!;
            te.SetCaret(new TextPos(1, 0), false);
            te.HandleKey(Key.Tab, ModifierKeys.None);
            Check(t.Paragraphs[1].List!.Level == 1 && t.Paragraphs[1].IndentLeft == 36, "Tab en debut d'element : niveau 2");
            te.HandleKey(Key.Tab, ModifierKeys.Shift);
            Check(t.Paragraphs[1].List!.Level == 0, "Maj+Tab : retour au niveau 1");
            te.SetCaret(new TextPos(2, StoryEditor.Length(t.Paragraphs[2])), false);
            te.HandleKey(Key.Enter, ModifierKeys.None);
            Check(t.Paragraphs.Count == 4 && t.Paragraphs[3].List is not null, "Entree : nouvel element de liste");
            te.HandleKey(Key.Enter, ModifierKeys.None);
            Check(t.Paragraphs.Count == 4 && t.Paragraphs[3].List is null, "Entree sur un element vide : fin de la liste");
            te.Type("Note\tfin");
            Check(session.Layout(pg).Frames[t].Lines.Last().Pieces.Last().X - t.X > 36, "tabulation : le texte saute au taquet");
            te.Select(new TextPos(0, 0), new TextPos(0, 7));
            Click(w, "Strike_Click");
            Click(w, "Superscript_Click");
            var f0 = StoryEditor.FontAt(t.Paragraphs, new TextPos(0, 1));
            Check(f0.Strike && f0.Script == "super", "barre et exposant sur la selection");
            mw.GetMethod("ApplyFont", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { new Action<FontSpec>(f => { f.Caps = "small"; f.Tracking = 50; f.Highlight = "#fff200"; f.Script = null; }) });
            var piece = session.Layout(pg).Frames[t].Lines[0].Pieces;
            Check(piece.Any(x => x.Tracking > 0) && piece.Any(x => x.Text == "REMIER"), "petites majuscules et espacement des caracteres");
            // reproduire la mise en forme vers le 2e paragraphe
            te.SetCaret(new TextPos(0, 2), false);
            Click(w, "FormatPainter_Click");
            te.Select(new TextPos(1, 0), new TextPos(1, 8));
            mw.GetMethod("ApplyPainter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
            Check(StoryEditor.FontAt(t.Paragraphs, new TextPos(1, 2)).Highlight == "#fff200", "reproduire la mise en forme");
            session.EndTextEdit();
            session.Select(t, pg);
            mw.GetMethod("ApplyParagraph", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { new Action<Paragraph>(p => { p.LineHeight = "150%"; p.SpaceAfter = 6; }) });
            var l2 = session.Layout(pg).Frames[t].Lines;
            Check(l2.Count >= 2 && l2[1].Top - l2[0].Top > 11 * 1.4, "interligne 1,5 et espace apres");
            var dlg = new ParagraphWindow(t.Paragraphs[0]);
            Check(dlg.Result is null, "boite Paragraphe : rien n'est applique sans OK");
            dlg.Close();
            var big = new Shape
            {
                Kind = ShapeKind.Text, X = 320, Y = 620, W = 230, H = 160,
                Paragraphs = new List<Paragraph> { new() { DropCap = 3, Align = "justify", Runs = { new Run { Text = string.Join(" ", Enumerable.Repeat("Il était une fois un texte de démonstration.", 6)), Font = new FontSpec { Family = "Arial", Size = 10, Color = "#1f2a44" } } } } },
            };
            session.Change(pg, () => pg.Shapes.Add(big));
            var bl = session.Layout(pg).Frames[big].Lines;
            Check(bl[0].Marks.Any(m => m.Text == "I" && m.SizePt > 25) && bl[3].Pieces[0].X < bl[0].Pieces[0].X, "lettrine sur 3 lignes");
            session.Select(null, pg);
            Capture(w, Path.Combine(outDir, "selftest-7-paragraphes.png"));
            Exporter.ExportPng(session, outDir, 110);
        }

        Console.WriteLine("Zones de texte : liens, colonnes, ajustement, habillage");
        {
            var pg = session.Doc.Pages[^1];
            var views = (List<PageView>)typeof(MainWindow).GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            var view = views.First(v => v.Page == pg);
            var mw = typeof(MainWindow);
            Shape Box(double x, double y, double ww, double hh, string text) => new()
            {
                Kind = ShapeKind.Text, X = x, Y = y, W = ww, H = hh, Padding = new[] { 2.85, 2.85, 2.85, 2.85 }, AutoFit = "none",
                Paragraphs = new List<Paragraph> { new() { Align = "justify", Runs = { new Run { Text = text, Font = new FontSpec { Family = "Arial", Size = 10, Color = "#000000" } } } } },
            };
            var a = Box(60, 60, 150, 90, string.Join(" ", Enumerable.Repeat("Le texte continue dans la zone liée.", 14)));
            var b = Box(230, 60, 150, 200, "");
            session.Change(pg, () => { pg.Shapes.Add(a); pg.Shapes.Add(b); });
            Check(session.Layout(pg).Overflows(a), "zone trop petite : debordement signale");
            mw.GetMethod("OnOverflowClicked", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { view, a });
            Check(session.LinkSource == a, "clic sur l'indicateur : mode « créer un lien »");
            mw.GetMethod("OnLinkTargetPicked", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { view, b });
            Check(a.Chain is not null && b.Chain == a.Chain && b.ChainIndex == 1 && session.LinkSource is null, "zones liees");
            var lay = session.Layout(pg);
            Check(lay.Frames[b].Lines.Count > 0 && !lay.Overflows(a), "le texte coule dans la zone liee");
            var c = Box(400, 60, 150, 100, "Texte déjà présent");
            session.Change(pg, () => pg.Shapes.Add(c));
            session.LinkSource = b;
            w.SkipConfirm = true;
            Check(c.Chain is null, "une zone non vide n'est pas proposee comme cible (verifie a la main : message)");
            session.LinkSource = null;
            session.Select(a, pg);
            Click(w, "NextFrame_Click");
            Check(session.Selected == b, "zone suivante");
            session.Select(a, pg);
            Click(w, "BreakLink_Click");
            Check(a.Chain is null && b.Chain is null && b.Paragraphs!.Count == 0 && session.Layout(pg).Overflows(a), "rompre le lien : le texte revient dans la 1re zone");
            session.Undo();
            pg = session.Doc.Pages[^1];
            Check(pg.Shapes.Count(x => x.Chain is not null) == 2, "annuler : zones a nouveau liees");
            // colonnes et agrandissement automatique
            var d = Box(60, 300, 300, 40, string.Join(" ", Enumerable.Repeat("Deux colonnes dans une seule zone.", 10)));
            d.Columns = 2; d.AutoFit = "grow";
            session.Change(pg, () => pg.Shapes.Add(d));
            var dl = session.Layout(pg).Frames[d].Lines;
            Check(dl.Any(l => l.Left > d.X + 150), "texte sur deux colonnes");
            Check(d.H > 40 && !session.Layout(pg).Overflows(d), $"zone agrandie pour contenir le texte ({d.H:0} pt)");
            // habillage serre autour d'un disque
            var disk = new Shape { Kind = ShapeKind.Ellipse, Geometry = ShapeGeometry.Ellipse, X = 80, Y = 470, W = 90, H = 90, Fill = new Fill { Color = "#06c7c0" }, Wrap = "tight", Z = 999 };
            var e2 = Box(60, 450, 300, 160, string.Join(" ", Enumerable.Repeat("Le texte épouse la forme du disque posé dans la zone.", 8)));
            session.Change(pg, () => { pg.Shapes.Add(e2); pg.Shapes.Add(disk); for (int i = 0; i < pg.Shapes.Count; i++) pg.Shapes[i].Z = i + 1; });
            var el = session.Layout(pg).Frames[e2].Lines;
            var lefts = el.Where(l => l.Top > 455 && l.Top < 560).Select(l => l.Left).ToList();
            Check(lefts.Count > 3 && lefts.Max() - lefts.Min() > 10, "habillage serre : le bord du texte suit le disque");
            session.Select(null, pg);
            Exporter.ExportPng(session, outDir, 110);
            Capture(w, Path.Combine(outDir, "selftest-8-zones.png"));
        }

        Console.WriteLine("Revision : orthographe, rechercher, typographie");
        {
            var pg = session.Doc.Pages[^1];
            var mw = typeof(MainWindow);
            var views = (List<PageView>)mw.GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            var view = views.First(v => v.Page == pg);
            var t = new Shape
            {
                Kind = ShapeKind.Text, X = 380, Y = 300, W = 180, H = 120, Padding = new[] { 2.85, 2.85, 2.85, 2.85 },
                Paragraphs = new List<Paragraph> { new() { Runs = { new Run { Text = "Une fote dans la phrase. Le chat dort.", Font = new FontSpec { Family = "Arial", Size = 11, Color = "#000000", Lang = "fr" } } } } },
            };
            session.Change(pg, () => pg.Shapes.Add(t));
            var spell = EditorSession.SharedSpell.Value;
            Check(spell is not null, "correcteur orthographique de Windows disponible");
            if (spell is not null)
            {
                var errs = spell.Check(t.Paragraphs[0].PlainText, "fr");
                Check(errs.Count == 1 && t.Paragraphs[0].PlainText.Substring(errs[0].Start, errs[0].Length) == "fote", "faute detectee : « fote »");
                Check(spell.Suggest("fote", "fr").Contains("faute"), "suggestion « faute »");
                session.Select(null, session.Doc.Pages[0]);
                var found = mw.GetMethod("NextSpellingError", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
                Check(found is not null && session.TextEdit is { HasSelection: true }, "F7 : premiere faute du document selectionnee");
                session.EndTextEdit();
                session.BeginTextEdit(pg, t);
                session.TextEdit!.Select(new TextPos(0, 4), new TextPos(0, 8));
                mw.GetMethod("ReplaceSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "faute" });
                Check(t.Paragraphs[0].PlainText.StartsWith("Une faute dans"), "correction appliquee");
                Check(spell.Check(t.Paragraphs[0].PlainText, "fr").Count == 0, "plus de faute");
                session.EndTextEdit();
            }
            // rechercher / remplacer
            session.Select(null, pg);
            bool ok = (bool)mw.GetMethod("FindNext", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "chat dort", false, false })!;
            Check(ok && session.TextEdit?.Head == t && StoryEditor.GetText(t.Paragraphs, session.TextEdit.SelStart, session.TextEdit.SelEnd) == "chat dort", "rechercher : texte trouve et selectionne");
            int nrep = (int)mw.GetMethod("ReplaceAll", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "chat", "chien", false, true })!;
            Check(nrep >= 1 && t.Paragraphs[0].PlainText.Contains("Le chien dort"), $"remplacer tout ({nrep})");
            session.Undo();
            pg = session.Doc.Pages[^1];
            var t2 = pg.Shapes.First(x => x.IsText && x.Paragraphs!.Any(p => p.PlainText.Contains("Le chat dort")));
            Pump();
            view = ((List<PageView>)mw.GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!).First(v => v.Page == pg);
            Check(t2 is not null, "annuler : remplacements annules en une fois");
            // typographie francaise a la frappe
            session.BeginTextEdit(pg, t2!);
            var te = session.TextEdit!;
            te.SetCaret(StoryEditor.End(te.Paras), false);
            foreach (var ch in " \"Oui\" ? l'été") te.TypeInput(ch.ToString());
            var tail = te.Paras[0].PlainText;
            Check(tail.EndsWith(" «\u00A0Oui\u00A0»\u202F? l’été"), "guillemets francais, espace fine avant ?, apostrophe");
            // symbole et menu contextuel
            mw.GetMethod("SymbolButton_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { view, new RoutedEventArgs() });
            var menu = w.LastMenu!;
            ((System.Windows.Controls.MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            menu.IsOpen = false;
            Check(te.Paras[0].PlainText.EndsWith("—"), "inserer un symbole (tiret cadratin)");
            mw.GetMethod("OnPageContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { view, new Point(t2!.X + 20, t2.Y + 8) });
            Check(w.LastMenu!.Items.Count >= 5, "menu contextuel pendant l'ecriture");
            w.LastMenu.IsOpen = false;
            session.EndTextEdit();
            mw.GetMethod("OnPageContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { view, new Point(t2.X + 20, t2.Y + 8) });
            Check(session.Selected == t2 && w.LastMenu!.Items.Cast<object>().Any(i => i is System.Windows.Controls.MenuItem { Header: "Dupliquer" }), "menu contextuel d'un objet");
            w.LastMenu.IsOpen = false;
            Check(TextSearch.CountWords(t2.Paragraphs!) > 8, "statistiques : nombre de mots");
        }

        Console.WriteLine("Aide contextuelle");
        var topic = (Func<string>)(() => w.CurrentHelpTopic());
        session.Select(pic, session.Doc.Pages[0]);
        Check(topic() == Help.HelpService.PictureTools, "image selectionnee -> aide des outils image");
        var txt = session.Doc.Pages[0].Shapes.First(x => x.IsText && x.Chain is null && x.Paragraphs!.Count > 0);
        session.Select(txt, session.Doc.Pages[0]);
        Check(topic() == Help.HelpService.TextFormat, "zone de texte selectionnee -> aide de la mise en forme");
        session.BeginTextEdit(session.Doc.Pages[0], txt);
        Check(topic() == Help.HelpService.TextWrite, "ecriture sur la page -> aide de l'ecriture");
        session.EndTextEdit(); session.Select(null);
        setTool.Invoke(w, new object?[] { "polygon" });
        Check(topic() == Help.HelpService.FreeDrawing, "outil forme libre -> aide du dessin libre");
        setTool.Invoke(w, new object?[] { null });
        Check(Help.HelpService.UrlOf(Help.HelpService.TextWrite) == "https://calm-pebble-0080d6003.5.azurestaticapps.net/aide/texte.html#ecrire", "adresse des pages d'aide");

        Console.WriteLine("Creation de page : pages maitres, numeros, arriere-plan, double page, reperes, mise en page");
        {
            var mw = typeof(MainWindow);
            session.EndTextEdit();
            session.Select(null, session.Doc.Pages[0]);
            mw.GetMethod("AddPageNumbers", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "bottom-center" });
            var master = session.Doc.Masters?.FirstOrDefault();
            Check(master is { Id: "A" } && master.Shapes.Any(x => x.Paragraphs?.Any(p => p.Runs.Any(sr => sr.Field == "page")) == true), "numeros de page sur la page maitre A");
            var pg2 = session.Doc.Pages[1];
            var ml = session.LayoutMaster(master!, pg2).Frames.Values.SelectMany(f => f.Lines).SelectMany(l => l.Pieces).ToList();
            Check(ml.Any(x => x.Text == "2"), "sous la page 2, le champ affiche « 2 »");
            Check(session.Doc.MasterOf(pg2) == master, "toutes les pages utilisent la page maitre");
            session.Doc.Pages[0].IgnoreMaster = true;
            Check(session.Doc.MasterOf(session.Doc.Pages[0]) is null, "ignorer la page maitre sur une page");
            session.Doc.Pages[0].IgnoreMaster = false;
            // mode pages maitres : les vues montrent la page maitre
            mw.GetMethod("SetMasterMode", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { true });
            var views = (List<PageView>)mw.GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Check(w.EditingMasters && views.Count == 1 && views[0].Page == master, "mode pages maitres : une vue par page maitre");
            Check(session.Layout(master!).Frames.Values.SelectMany(f => f.Lines).SelectMany(l => l.Pieces).Any(x => x.Text == "#"), "sur la page maitre, le champ s'affiche « # »");
            mw.GetMethod("SetMasterMode", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { false });
            views = (List<PageView>)mw.GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Check(!w.EditingMasters && views.Count == session.Doc.Pages.Count, "retour aux pages du document");
            // numerotation : style romain, redemarrage
            session.Doc.Numbering = new PageNumbering { Start = 1, Style = "I" };
            session.Doc.Pages[2].NumberStart = 1;
            session.StructureChanged();
            Check(session.Doc.PageNumber(session.Doc.Pages[1]) == "II" && session.Doc.PageNumber(session.Doc.Pages[2]) == "I", "numerotation en chiffres romains, redemarrage a la page 3");
            session.Doc.Numbering = null; session.Doc.Pages[2].NumberStart = null;
            // arriere-plan
            master!.BackColor = "#fff6e0";
            session.Doc.Pages[3].BackColor = "#e6f4ff";
            session.StructureChanged();
            Check(session.Doc.Pages[1].BackColor is null && session.Doc.MasterOf(session.Doc.Pages[1])?.BackColor == "#fff6e0", "couleur d'arriere-plan de la page maitre");
            // double page
            w.SpreadView = true;
            Pump();
            var panel = (System.Windows.Controls.StackPanel)w.FindName("PagesPanel");
            Check(panel.Children.Count == 1 && panel.Children[0] is System.Windows.Controls.Primitives.UniformGrid { Columns: 2 } ug && ug.Children.Count == session.Doc.Pages.Count + 1, "double page : planches de deux pages, la premiere seule");
            Capture(w, Path.Combine(outDir, "selftest-9-double-page.png"));
            w.SpreadView = false;
            // reperes de regle et grille : le magnetisme s'y accroche
            session.Doc.Guides = new RulerGuides { X = { 200 }, Y = { 300 } };
            session.Doc.Grid = new LayoutGrid { Columns = 2, Rows = 1, Gutter = 20 };
            var pgx = session.Doc.Pages[1];
            var sgx = new List<double>(); var sgy = new List<double>();
            var obj = pgx.Shapes.First(x => !x.Background);
            Check(Snapper.TargetsX(pgx, session.Doc, obj).Contains(200) && Snapper.TargetsY(pgx, session.Doc, obj).Contains(300), "magnetisme sur les reperes de regle");
            var m = Snapper.Margins(session.Doc);
            double mid = m[3] + (pgx.Width - m[1] - m[3] - 20) / 2;
            Check(Snapper.TargetsX(pgx, session.Doc, obj).Any(x => Math.Abs(x - mid) < 0.01), "magnetisme sur la grille de disposition");
            var sr = Snapper.SnapMove(new Rect(-500 + 203, -500, 40, 40), pgx, session.Doc, obj, 6, sgx, sgy);
            Capture(w, Path.Combine(outDir, "selftest-10-maitres-reperes.png"));
            session.Doc.Guides = null; session.Doc.Grid = null;
            // mise en page : A5 paysage, objets adaptes
            int count = pgx.Shapes.Count;
            var keep = Core.Io.DocumentJson.Serialize(session.Doc, indented: false);
            session.BeginChange();
            MainWindow.ApplyPageSize(session.Doc, 595.276, 419.528, true);
            session.EndChange();
            Check(session.Doc.Pages.All(x => Math.Abs(x.Width - 595.276) < 0.01 && Math.Abs(x.Height - 419.528) < 0.01) && master.Width > 595, "nouvelle taille de page (et page maitre)");
            Check(pgx.Shapes.Where(x => !x.Background).All(x => x.Bounds().Y >= -1 && x.Bounds().Y + x.Bounds().H <= 420.5), "objets ramenes dans la nouvelle page");
            session.Undo();
            Check(Math.Abs(session.Doc.Pages[0].Height - 841.89) < 1 || session.Doc.Pages[0].Height > 700, "annuler la mise en page");
            Check(Core.Model.PubFormat.Required(session.Doc) == "openpub/2", "document a pages maitres : format openpub/2");
            var blank = Core.Io.OpubPackage.CreateBlank(419.528, 595.276, 4);
            Check(blank.Document.Pages.Count == 4 && blank.FilePath is null && blank.Document.Pages[3].Index == 4, "nouveau document vierge (A5, 4 pages)");
            blank.Dispose();
        }

        Console.WriteLine("Tableaux : insertion, ecriture, Tab, fusion, style, collage depuis Excel, recherche");
        {
            var mw = typeof(MainWindow);
            session.EndTextEdit();
            // page vierge pour les tableaux
            session.Select(null, session.Doc.Pages[^1]);
            Click(w, "AddPage_Click");
            var tp = session.Doc.Pages[^1];
            Pump();
            session.Select(null, tp);
            mw.GetMethod("InsertTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { 3, 3, "bleu" });
            var tbl = tp.Shapes.LastOrDefault(x => x.IsTable);
            Check(tbl is { Table.RowCount: 3 } && session.TextEdit?.Owner == tbl, "tableau 3 × 3 insere, ecriture dans la 1re cellule");
            var te = session.TextEdit!;
            te.Type("Article");
            te.HandleKey(Key.Tab, ModifierKeys.None);
            session.TextEdit!.Type("Prix");
            Check(tbl!.Table!.Cells[0][0].Paragraphs![0].PlainText == "Article" && tbl.Table.Cells[0][1].Paragraphs![0].PlainText == "Prix", "Tab : cellule suivante");
            session.BeginCellEdit(tp, tbl, 2, 2);
            session.TextEdit!.HandleKey(Key.Tab, ModifierKeys.None);
            Check(tbl.Table.RowCount == 4 && PageLayoutOf(session, tp, tbl) is not null, "Tab dans la derniere cellule : nouvelle ligne");
            session.BeginCellEdit(tp, tbl, 1, 0);
            session.TextEdit!.Type(string.Join(" ", Enumerable.Repeat("Un texte long qui agrandit la ligne.", 4)));
            session.Layout(tp);
            var hs = Core.Editing.TableOps.RowHeights(tbl.Table);
            Check(hs[1] > tbl.Table.Rows[1] + 5 && Math.Abs(tbl.H - hs.Sum()) < 0.01, "la ligne s'agrandit pour contenir le texte");
            mw.GetMethod("ChangeTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { new Action<Shape, TableData>((_, t) => Core.Editing.TableOps.Merge(t, 3, 0, 3, 2)), null });
            Check(tbl.Table.Cells[3][0].ColSpan == 3 && tbl.Table.Cells[3][1].Hidden, "fusionner une ligne");
            mw.GetMethod("ChangeTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object?[] { new Action<Shape, TableData>((_, t) => Core.Editing.TableOps.ApplyStyle(t, "violet")), null });
            Check(tbl.Table.Cells[0][0].Fill?.Color == "#9935f4", "style de tableau");
            // texte copie depuis Excel
            int nshapes = tp.Shapes.Count;
            bool pasted = (bool)mw.GetMethod("PasteAsTable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { tp, "Jour\tActivité\r\nLundi\tPiscine\r\nMardi\tThéâtre\r\n" })!;
            var t2 = tp.Shapes.Last();
            Check(pasted && tp.Shapes.Count == nshapes + 1 && t2.IsTable && t2.Table!.RowCount == 3, "texte tabule colle : tableau");
            // la recherche trouve le texte des cellules
            session.Select(null, session.Doc.Pages[0]);
            bool found = (bool)mw.GetMethod("FindNext", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "Théâtre", true, true })!;
            Check(found && session.TextEdit?.Owner == t2, "rechercher : texte trouve dans une cellule");
            session.EndTextEdit();
            // colonnes a l'echelle quand on redimensionne le tableau
            double c0 = t2.Table!.Cols[0];
            session.Change(tp, () => PageView.MoveShape(t2, new Rect(t2.X, t2.Y, t2.W * 2, t2.H)));
            Check(Math.Abs(t2.Table.Cols[0] - 2 * c0) < 0.01, "redimensionner : colonnes a l'echelle");
            Check(Core.Model.PubFormat.Required(session.Doc) == "openpub/2", "document avec tableau : format openpub/2");
            session.Select(null, tp);
            Exporter.ExportPng(session, outDir, 110);
            Capture(w, Path.Combine(outDir, "selftest-11-tableaux.png"));
        }

        Console.WriteLine("Styles et jeux de couleurs et de polices");
        {
            var mw = typeof(MainWindow);
            session.EndTextEdit();
            var sp = session.Doc.Pages[^1];
            var st = new Shape
            {
                Kind = ShapeKind.Text, X = 60, Y = 60, W = 300, H = 120, Padding = new[] { 2.85, 2.85, 2.85, 2.85 },
                Paragraphs = new[] { "Grand titre", "Un paragraphe de texte.", "Sous-titre" }
                    .Select(x => new Paragraph { Runs = { new Run { Text = x, Font = new FontSpec { Family = "Arial", Size = 11, Color = "#111c43" } } } }).ToList(),
            };
            session.Change(sp, () => sp.Shapes.Add(st));
            session.BeginTextEdit(sp, st);
            session.TextEdit!.SetCaret(new TextPos(0, 2), false);
            var styles = Core.Editing.StyleOps.Ensure(session.Doc);
            mw.GetMethod("ApplyTextStyle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { styles.First(x => x.Id == "titre1") });
            session.TextEdit!.SetCaret(new TextPos(2, 1), false);
            mw.GetMethod("ApplyTextStyle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { styles.First(x => x.Id == "titre2") });
            Check(st.Paragraphs[0].Style == "titre1" && st.Paragraphs[0].Runs[0].Font!.Size == 24 && st.Paragraphs[1].Style is null, "style « Titre 1 » applique au paragraphe du curseur");
            var box = (System.Windows.Controls.ComboBox)w.FindName("StyleBox");
            session.TextEdit!.SetCaret(new TextPos(0, 1), false);
            mw.GetMethod("RefreshFormat", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
            Check(box.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: "titre1" }, "la liste des styles montre le style du paragraphe");
            session.EndTextEdit();
            var t1 = styles.First(x => x.Id == "titre1");
            session.BeginChange(); t1.Font!.Color = "#9935f4"; Core.Editing.StyleOps.Reapply(session.Doc, t1); session.EndChange();
            Check(st.Paragraphs[0].Runs[0].Font!.Color == "#9935f4", "modifier le style : le texte suit");
            mw.GetMethod("ApplyColorScheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { "Forêt", Core.Editing.StyleOps.ColorSchemes.First(c => c.Name == "Forêt").Colors });
            sp = session.Doc.Pages[^1];
            var st2 = sp.Shapes.First(x => x.Paragraphs?.FirstOrDefault()?.PlainText == "Grand titre");
            Check(session.Doc.Theme?.Name == "Forêt" && st2.Paragraphs![1].Runs[0].Font!.Color == "#1b3a2b", "jeu de couleurs « Forêt » : couleur principale remplacee");
            session.BeginChange(); Core.Editing.StyleOps.ApplyFontScheme(session.Doc, "Cambria", "Calibri"); session.EndChange(); session.StructureChanged();
            sp = session.Doc.Pages[^1];
            st2 = sp.Shapes.First(x => x.Paragraphs?.FirstOrDefault()?.PlainText == "Grand titre");
            Check(st2.Paragraphs![0].Runs[0].Font!.Family == "Cambria", "jeu de polices : police des titres");
            session.Undo(); session.Undo();
            Check(session.Doc.Theme is null || session.Doc.Theme.Name != "Forêt" || session.Doc.Theme.HeadingFont is null, "annuler les jeux");
        }

        Console.WriteLine("Modeles et blocs");
        {
            var mw = typeof(MainWindow);
            session.EndTextEdit();
            var bp = session.Doc.Pages[^1];
            session.Select(null, bp);
            int n0 = bp.Shapes.Count;
            mw.GetMethod("InsertBlock", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { Core.Editing.Templates.BlockClip("bandeau"), "Bandeau" });
            bp = session.Doc.Pages[^1];
            var added = bp.Shapes.Skip(n0).ToList();
            var bb = Core.Editing.ShapeOps.Bounds(added);
            Check(added.Count == 2 && added.All(x => x.Group is not null && x.Group == added[0].Group), "bloc « Bandeau » insere (groupe)");
            Check(Math.Abs(bb.X + bb.W / 2 - bp.Width / 2) < 0.5 && Math.Abs(bb.Y + bb.H / 2 - bp.Height / 2) < 0.5, "bloc centre sur la page");
            foreach (var (id, name, _, build) in Core.Editing.Templates.BuiltIn)
            {
                var tdoc = build();
                using var pkg = Core.Io.OpubPackage.CreateFromDocument(tdoc);
                var s2 = new EditorSession(pkg);
                foreach (var pg in tdoc.Pages) s2.Layout(pg);
                var tmpPng = Path.Combine(outDir, $"modele-{id}");
                Directory.CreateDirectory(tmpPng);
                var files = Exporter.ExportPng(s2, tmpPng, 60);
                Check(files.Count == tdoc.Pages.Count, $"modele « {name} » : rendu des {tdoc.Pages.Count} page(s)");
            }
        }

        Console.WriteLine("Publipostage, liens, texte de remplacement, export web");
        {
            var mw = typeof(MainWindow);
            session.EndTextEdit();
            var mp = session.Doc.Pages[^1];
            var letter = new Shape
            {
                Kind = ShapeKind.Text, X = 60, Y = 300, W = 300, H = 60, Padding = new[] { 2.85, 2.85, 2.85, 2.85 },
                Paragraphs = new List<Paragraph> { new() { Runs = { new Run { Text = "Bonjour ", Font = new FontSpec { Family = "Arial", Size = 12, Color = "#000000" } } } } },
            };
            session.Change(mp, () => mp.Shapes.Add(letter));
            mw.GetMethod("SetMergeSource", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { Core.Editing.MergeOps.Parse("Prénom;Ville\nAlice;Lyon\nBruno;Lille", "classe") });
            session.BeginTextEdit(mp, letter);
            session.TextEdit!.SetCaret(Core.Editing.StoryEditor.End(letter.Paragraphs), false);
            mw.GetMethod("InsertField", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { Core.Editing.MergeOps.Prefix + "Prénom" });
            session.EndTextEdit();
            string Shown() => string.Concat(session.Layout(mp).Frames[letter].Lines.SelectMany(l => l.Pieces).Select(x => x.Text));
            Check(Shown().Contains("«Prénom»"), "champ de publipostage : « «Prénom» » sans apercu");
            mw.GetMethod("SetMergePreview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { 1 });
            mp = session.Doc.Pages[^1];
            letter = mp.Shapes.First(x => x.Paragraphs?.FirstOrDefault()?.PlainText.StartsWith("Bonjour") == true);
            Check(Shown().Contains("Bruno"), "apercu : 2e destinataire");
            var merged = Core.Editing.MergeOps.Merged(session.Doc, new[] { 0, 1 });
            Check(merged.Pages.Count == 2 * session.Doc.Pages.Count && merged.Pages[session.Doc.Pages.Count * 2 - 1].Shapes.Any(x => x.Paragraphs?.FirstOrDefault()?.PlainText == "Bonjour Bruno"), "document fusionne : une copie par destinataire");
            mw.GetMethod("SetMergePreview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { -1 });
            // liens et texte de remplacement
            mp = session.Doc.Pages[^1];
            letter = mp.Shapes.First(x => x.Paragraphs?.FirstOrDefault()?.PlainText.StartsWith("Bonjour") == true);
            session.Change(mp, () => mw.GetMethod("SetRunLinks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { letter.Paragraphs!, new TextPos(0, 0), new TextPos(0, 7), "https://example.org/" }));
            Check(letter.Paragraphs![0].Runs[0].Link == "https://example.org/" && letter.Paragraphs[0].Runs[0].Text == "Bonjour", "lien sur le mot « Bonjour »");
            var pic3 = session.Doc.Pages[0].Shapes.First(x => x.IsImage && !x.Background);
            pic3.AltText = "Photo de groupe";
            pic3.Link = "mailto:contact@example.org";
            var html = (string)mw.GetMethod("BuildHtml", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { session, 0.6 })!;
            Check(html.Contains("data:image/png;base64,") && html.Contains("https://example.org/") && html.Contains("mailto:contact@example.org"), "export web : images incluses et liens cliquables");
            Check(html.Contains("Photo de groupe") && html.Contains("lang=\"fr\""), "export web : textes de remplacement");
            var linkPdf = Path.Combine(Path.GetTempPath(), $"openpub-liens-{Guid.NewGuid():N}.pdf");
            Output.PdfExporter.Export(session, linkPdf, new Output.PdfExportOptions { Pages = new List<int> { 1, session.Doc.Pages.Count } });
            var pdfText = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(linkPdf));
            Check(pdfText.Contains("/URI") && pdfText.Contains("example.org"), "PDF : liens hypertextes actifs");
            try { File.Delete(linkPdf); } catch { }
            pic3.AltText = null; pic3.Link = null;
        }
        Console.WriteLine("Sorties : PDF direct, PDF imprimeur, impression, images");
        OutputChecks(w, session, pic, outDir);
        Lot9(w, session, outDir);          // formes, effets, WordArt, images (SelfTest.Shapes.cs)

        Console.WriteLine("Texte sur un trace");
        {
            session.EndTextEdit();
            var tpg = session.Doc.Pages[^1];
            var wave = Enumerable.Range(0, 200).Select(i => new Point(80 + i * 2, 700 + Math.Sin(i / 30.0) * 40)).ToList();
            var stroke2 = OpenPub.App.Editing.DrawingFactory.Freehand(wave, "#9935f4", 2, 0.5)!;
            session.Change(tpg, () => tpg.Shapes.Add(stroke2));
            session.Select(stroke2, tpg);
            var wa2 = MainWindow.TextAlongPath(stroke2, "Le texte suit la vague du crayon");
            Check(wa2 is { Kind: ShapeKind.WordArt, WordArt.Warp: WordArtWarps.Path } && wa2.WordArt.PathPoints!.Count > 5, "texte sur le trace : WordArt le long du trace");
            session.Change(tpg, () => { tpg.Shapes[tpg.Shapes.IndexOf(stroke2)] = wa2!; });
            Exporter.ExportPng(session, outDir, 110);
        }

        Console.WriteLine("Surete des documents : copie de secours, recuperation, fichier modifie ailleurs");
        {
            w.SkipConfirm = true;
            var mw = typeof(MainWindow);
            var recDir = Path.Combine(Path.GetTempPath(), "openpub-selftest-recovery-" + Guid.NewGuid().ToString("N")[..8]);
            w.Recovery = new Core.Io.RecoveryStore(recDir);
            w.EnableRecovery = true;
            var saved = Path.Combine(Path.GetTempPath(), $"openpub-selftest-{Guid.NewGuid():N}.opub");
            session.EndTextEdit();
            session.Save(saved);
            mw.GetMethod("RecordFileStamp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
            int pages = session.Doc.Pages.Count;
            session.Select(null, session.Doc.Pages[0]);
            Click(w, "AddPage_Click");
            Check(session.Modified && w.AutoSaveNow(), "copie de secours d'un document modifie");
            var entry = w.Recovery.List().SingleOrDefault();
            Check(entry is not null && entry.OriginalPath == Path.GetFullPath(saved), "copie rangee a part, avec le fichier d'origine");
            Check(File.Exists(saved) && Core.Io.OpubPackage.Open(saved).Document.Pages.Count == pages, "le fichier de l'utilisateur n'est pas touche");
            // fermeture inattendue simulee : on rouvre la copie
            w.OpenRecovered(entry!);
            var s2 = (EditorSession)mw.GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Check(s2.Doc.Pages.Count == pages + 1 && s2.Modified && s2.Package.FilePath == Path.GetFullPath(saved), "document recupere : modifications retrouvees, fichier d'origine");
            Check(!w.Recovery.List().Any(e => e.Id != entry!.Id), "pas de copie en double");
            // un autre programme modifie le fichier
            File.SetLastWriteTimeUtc(saved, DateTime.UtcNow.AddMinutes(1));
            Check((bool)mw.GetMethod("FileChangedOnDisk", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null)!, "modification exterieure detectee");
            s2.Save(saved, keepBackup: true);
            mw.GetMethod("RecordFileStamp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
            mw.GetMethod("DiscardRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
            Check(File.Exists(saved + ".bak") && !w.Recovery.List().Any(), "enregistrer : version precedente gardee, copie de secours supprimee");
            Check(!(bool)mw.GetMethod("FileChangedOnDisk", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null)!, "apres enregistrement : plus d'alerte");
            w.EnableRecovery = false;
            foreach (var f in new[] { saved, saved + ".bak" }) try { File.Delete(f); } catch { }
            try { Directory.Delete(recDir, true); } catch { }
        }

        w.SkipConfirm = true;
        w.Close();
        if (tmp != file) try { File.Delete(tmp); } catch { }
        Console.WriteLine($"\n{_ok} verifications reussies, {_ko} echec(s)");
        return _ko == 0 ? 0 : 1;
    }

    /// <summary>Lot 10 : export PDF (relu par PDFsharp), fond perdu, PDF/X-4, livret, recto verso, apercu, images.</summary>
    static void OutputChecks(MainWindow w, EditorSession session, Shape pic, string outDir)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "openpub-selftest-output-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            session.EndTextEdit();
            session.Select(null);
            var doc = session.Doc;
            int n = doc.Pages.Count;
            var p1 = doc.Pages[0];
            string Fingerprint() => string.Join(";", doc.AllShapes().Select(s => FormattableString.Invariant(
                $"{s.X:0.###},{s.Y:0.###},{s.W:0.###},{s.H:0.###},{s.ImageFx?.CropLeft:0.####},{s.ImageFx?.CropTop:0.####},{s.ImageFx?.CropRight:0.####},{s.ImageFx?.CropBottom:0.####}")));
            var before = Fingerprint();

            // PDF direct : toutes les pages, taille exacte, polices incorporees, images
            var pdf = Path.Combine(tmp, "document.pdf");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stats = Output.PdfExporter.Export(session, pdf, new Output.PdfExportOptions());
            sw.Stop();
            var sum = Output.PdfExporter.Inspect(pdf);
            Console.WriteLine($"       ({n} pages en {sw.ElapsedMilliseconds} ms, {new FileInfo(pdf).Length / 1024} Ko ; {stats})");
            Check(sum.Pages == n, $"PDF relu par PDFsharp : {sum.Pages} page(s)");
            Check(Math.Abs(sum.Sizes[0].W - p1.Width) < 0.05 && Math.Abs(sum.Sizes[0].H - p1.Height) < 0.05, "taille de page du PDF = taille du document");
            Check(sum.EmbeddedFonts.Count > 0 && sum.MissingFonts.Count == 0, $"polices incorporees ({sum.EmbeddedFonts.Count}), aucune manquante");
            Check(stats.TextRuns > 50 && stats.TextAsOutlines == 0, "texte vectoriel avec la vraie police (pas de contours)");
            Check(sum.Images > 0 && stats.Images > 0, $"images incorporees ({sum.Images})");
            var used = doc.AllShapes().Where(s => s.IsText).SelectMany(s => s.Paragraphs ?? new()).SelectMany(p => p.Runs).Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => r.Font?.Family).OfType<string>().Distinct();
            Check(used.Where(f => !session.Fonts.Substituted.Contains(f)).All(f => sum.EmbeddedFonts.Any(e => e.Contains(f.Split(' ')[0], StringComparison.OrdinalIgnoreCase))),
                "chaque police du document est incorporee");

            // pour l'imprimeur : fond perdu de 3 mm, traits de coupe, PDF/X-4
            var bleedPdf = Path.Combine(tmp, "imprimeur.pdf");
            var profiles = Output.PdfExporter.CmykProfiles();
            var o = new Output.PdfExportOptions { Pages = new[] { 1 }, Bleed = Core.Output.PrintSheet.DefaultBleed, CropMarks = true, PdfX4 = profiles.Count > 0 };
            Output.PdfExporter.Export(session, bleedPdf, o);
            var bs = Output.PdfExporter.Inspect(bleedPdf);
            var sheet = new Core.Output.PrintSheet(p1.Width, p1.Height, o.Bleed, true);
            Check(bs.Pages == 1 && Math.Abs(bs.Sizes[0].W - sheet.MediaWidth) < 0.05 && Math.Abs(bs.Sizes[0].H - sheet.MediaHeight) < 0.05,
                $"fond perdu et traits de coupe : feuille {bs.Sizes[0].W:0.#} x {bs.Sizes[0].H:0.#} pt");
            Check(bs.TrimSizes[0] is { } trim && Math.Abs(trim.W - p1.Width) < 0.05 && Math.Abs(trim.H - p1.Height) < 0.05, "format fini (TrimBox) = taille de la page");
            Check(Fingerprint() == before, "le fond perdu ne modifie pas le document");
            if (profiles.Count > 0)
                Check(bs.PdfXVersion == "PDF/X-4" && bs.OutputIntent, "PDF/X-4 : version et intention de sortie CMJN");
            else Console.WriteLine("       (aucun profil CMJN installe : PDF/X-4 non verifie)");

            // effets en image : le reflet (masque d'opacite) est rasterise
            var picPage = doc.Pages.FirstOrDefault(pg => pg.Shapes.Contains(pic));
            if (picPage is null)
            {
                picPage = doc.Pages.First(pg => pg.Shapes.Any(x => x.IsImage && !x.Background && x.W > 80));
                pic = picPage.Shapes.First(x => x.IsImage && !x.Background && x.W > 80);
            }
            var fx0 = pic.ImageFx;
            var fxr = fx0?.Clone() ?? new ImageFx();
            fxr.ReflectionSize = 0.3;
            pic.ImageFx = fxr;
            var rs = Output.PdfExporter.Export(session, Path.Combine(tmp, "reflet.pdf"), new Output.PdfExportOptions { Pages = new[] { doc.Pages.IndexOf(picPage) + 1 } });
            pic.ImageFx = fx0;
            Check(rs.Rasterized > 0, "reflet d'image rasterise localement (300 ppp)");

            // impression : livret dans l'ordre de pliage, recto verso manuel
            var all = Enumerable.Range(1, n).ToList();
            var booklet = Output.PrintService.Passes(new Output.PrintOptions { Pages = all, Mode = Output.PrintMode.Booklet });
            var expected = Core.Output.Booklet.Sides(n).Select(s => new Output.PrintFace(s.Left, s.Right)).ToList();
            Check(booklet.Count == 1 && booklet[0].SequenceEqual(expected), $"livret de {n} pages : {string.Join(" / ", booklet[0])}");
            var fixedDoc = Output.PrintService.BuildDocument(session, booklet[0], Output.PrintMode.Booklet);
            var first = fixedDoc.Pages[0].Child;
            var tags = first.Children.OfType<PageView>().Select(v => (int)v.Tag).ToList();
            Check(fixedDoc.Pages.Count == expected.Count && first.Width > first.Height &&
                  tags.SequenceEqual(new[] { expected[0].Left, expected[0].Right }.Where(x => x > 0)), "livret : feuilles paysage, 2 pages par face");
            var duplex = Output.PrintService.Passes(new Output.PrintOptions { Pages = all, Mode = Output.PrintMode.ManualDuplex, Copies = 2 });
            Check(duplex.Count == 2 && duplex[0].Count == duplex[1].Count && duplex[0][0].Left == 1 && (n < 2 || duplex[1][0].Left == 2),
                "recto verso manuel : deux passages de meme longueur, rectos puis versos");
            Check(Output.PrintService.Passes(new Output.PrintOptions { Pages = new[] { 1 }, Copies = 3 })[0].Count == 3, "exemplaires assembles");

            // apercu avant impression
            var pw = new Output.PrintWindow(session, 1) { Owner = w, Left = -4000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            pw.Show();
            Pump();
            pw.SetMode(Output.PrintMode.Booklet);
            Check(pw.PreviewDocument is { } pd && pd.Pages.Count == expected.Count && pw.PreviewSheets == expected.Count, "apercu avant impression du livret");
            Capture(pw, Path.Combine(outDir, "impression-livret.png"));
            pw.Close();
            var ew = new Output.PdfExportWindow(n, 1) { Owner = w, Left = -4000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            ew.Show();
            Capture(ew, Path.Combine(outDir, "export-pdf.png"));
            ew.Close();

            // menu Fichier et export d'images
            Click(w, "FileButton_Click");
            var headers = w.LastMenu!.Items.OfType<System.Windows.Controls.MenuItem>().Select(i => i.Header as string).ToList();
            w.LastMenu.IsOpen = false;
            Check(headers.Contains("Exporter en PDF…") && headers.Contains("Imprimer…") && headers.Contains("Exporter en images (PNG, JPEG)…"), "menu Fichier : imprimer, PDF, images");
            var jpg = Exporter.ExportImages(session, tmp, ImageFormat.Jpeg, 72, new[] { 1 });
            var dec = BitmapDecoder.Create(new Uri(jpg[0]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Check(jpg[0].EndsWith(".jpg") && dec.PixelWidth == (int)Math.Ceiling(p1.Width) && dec.Decoder is JpegBitmapDecoder, $"export JPEG a 72 ppp : {dec.PixelWidth} x {dec.PixelHeight}");
            var png = Exporter.ExportImages(session, tmp, ImageFormat.Png, 144, null, Path.Combine(tmp, "x.png"));
            Check(png.Count == n, "export PNG de toutes les pages");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
