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
public static class SelfTest
{
    static int _ok, _ko;

    static void Check(bool cond, string what)
    {
        if (cond) _ok++; else _ko++;
        Console.WriteLine($"  {(cond ? "OK " : "ECHEC")} {what}");
    }

    static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    static void Capture(Window w, string path)
    {
        Pump();
        // taille du contenu (sans la barre de titre), sinon une bande noire apparait en bas
        var content = (FrameworkElement)w.Content;
        var rtb = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render((Visual)w.Content);
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
        typeof(MainWindow).GetMethod("ZoomFit_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { w, new RoutedEventArgs() });
        Pump();
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
                Capture(ed, png);
                ed.Close();
                w.Close();
                return 0;
            }
            case "file-menu" or "shapes-menu":
            {
                if (tab == "shapes-menu") ((System.Windows.Controls.RadioButton)w.FindName("TabInsert")).IsChecked = true;
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
        Capture(w, png);
        w.Close();
        return 0;
    }

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

        w.SkipConfirm = true;
        w.Close();
        if (tmp != file) try { File.Delete(tmp); } catch { }
        Console.WriteLine($"\n{_ok} verifications reussies, {_ko} echec(s)");
        return _ko == 0 ? 0 : 1;
    }
}
