using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenPub.App.Controls;
using OpenPub.App.Rendering;
using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>Auto-test du lot 9 : formes, remplissages, effets, modification des points, WordArt, images.</summary>
public static partial class SelfTest
{
    static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!.Invoke(target, args);

    /// <summary>Image de test generee (degrade et disque), jamais une photo du document.</summary>
    static string TestImage(string dir, string name, int w, int h, Color a, Color b)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(a, b, 30), null, new Rect(0, 0, w, h));
            dc.DrawEllipse(Brushes.White, null, new Point(w * 0.35, h * 0.45), h * 0.25, h * 0.25);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        BitmapEncoder enc = name.EndsWith(".png") ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var path = Path.Combine(dir, name);
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    static void CaptureView(PageView view, string path)
    {
        view.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(view);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    static MouseButtonEventArgs Down() => new(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
    static MouseEventArgs MoveArgs() => new(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent };

    /// <summary>Glisser a la souris dans la vue (gestionnaires du lot 9 appeles directement, coordonnees de page).</summary>
    static bool Drag(PageView view, Point from, Point to)
    {
        if (!(bool)Call(view, "ShapeEditMouseDown", from, Down())!) return false;
        Call(view, "ShapeEditMouseMove", new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), MoveArgs());
        Call(view, "ShapeEditMouseMove", to, MoveArgs());
        Call(view, "ShapeEditMouseUp");
        return true;
    }

    static void Lot9(MainWindow w, EditorSession session, string outDir)
    {
        Console.WriteLine("Formes, remplissages, effets, points, WordArt, images (lot 9)");
        var mw = typeof(MainWindow);
        var imgDir = Path.Combine(Path.GetTempPath(), "openpub-selftest-img-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(imgDir);
        try
        {
            // page vierge a la fin du document : les captures ne montrent aucun contenu de la newsletter
            session.EndTextEdit();
            session.Select(null, session.Doc.Pages[^1]);
            Click(w, "AddPage_Click");
            var pg = session.Doc.Pages[^1];
            Pump();
            PageView View() => ((List<PageView>)mw.GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!).First(v => v.Page == session.Doc.Pages[^1]);
            Check(pg.Shapes.Count == 0, "page vierge pour les essais");

            // galeries
            Click(w, "ShapesButton_Click");
            var menu = w.LastMenu!;
            int buttons = menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(i => i.Header).OfType<System.Windows.Controls.WrapPanel>().Sum(p => p.Children.Count);
            Check(buttons == ShapeLibrary.All.Count && ShapeLibrary.All.Count >= 48, $"menu Formes : {buttons} formes en {ShapeLibrary.Categories.Length} categories");
            menu.IsOpen = false;
            var gbox = (System.Windows.Controls.ComboBox)w.FindName("GeometryBox");
            Check(gbox.Items.Cast<System.Windows.Controls.ComboBoxItem>().Count(i => !i.IsEnabled) == ShapeLibrary.Categories.Length, "liste Forme avec intertitres de categories");

            // formes tracees, poignees jaunes
            var onDrawn = mw.GetMethod("OnShapeDrawn", BindingFlags.NonPublic | BindingFlags.Instance)!;
            onDrawn.Invoke(w, new object[] { View(), "arrowRight", new Rect(40, 40, 160, 70) });
            var arrow = pg.Shapes[^1];
            onDrawn.Invoke(w, new object[] { View(), "star12", new Rect(230, 30, 90, 90) });
            var star = pg.Shapes[^1];
            onDrawn.Invoke(w, new object[] { View(), "roundCallout", new Rect(360, 40, 150, 60) });
            var callout = pg.Shapes[^1];
            Check(arrow.Geometry == "arrowRight" && star.Geometry == "star12" && callout.Geometry == "roundCallout", "nouvelles formes tracees");
            session.Select(arrow, pg);
            Pump();
            var view = View();
            var hpos = ShapeLibrary.Find("arrowRight")!.Handles[0].Position(arrow);
            Check((int)Call(view, "AdjustHit", new Point(hpos.X, hpos.Y))! == 0, "poignee jaune sous la souris");
            Check(Drag(view, new Point(hpos.X, hpos.Y), new Point(hpos.X + 20, arrow.Y + 5)) && arrow.Adjust is { Count: 2 } && arrow.Adjust[0] > 0.8,
                "poignée jaune : le corps de la flèche s'élargit");
            session.Undo();
            pg = session.Doc.Pages[^1];
            arrow = pg.Shapes.First(x => x.Geometry == "arrowRight");
            Check(arrow.Adjust is null, "annuler le reglage");
            callout = pg.Shapes.First(x => x.Geometry == "roundCallout");
            star = pg.Shapes.First(x => x.Geometry == "star12");
            session.Select(callout, pg);
            Pump();
            view = View();
            var tip = ShapeLibrary.Find("roundCallout")!.Handles[0].Position(callout);
            Drag(view, new Point(tip.X, tip.Y), new Point(callout.X + callout.W + 40, callout.Y + callout.H + 30));
            var cb = PageRenderer.GeometryOf(callout)!.Bounds;
            Check(cb.Right > callout.X + callout.W + 35, "pointe de la legende deplacee hors du cadre");

            // remplissages
            session.Select(star, pg);
            var presets = (List<(string Label, Fill Fill)>)Call(w, "GradientPresets")!;
            Call(w, "ApplyFill", presets.First(p => p.Label.StartsWith("Radial")).Fill);
            Check(star.Fill is { HasGradient: true, IsRadial: true } && star.Fill.Color is not null, "degrade radial applique (couleur unie gardee pour les anciennes versions)");
            session.Select(callout, pg);
            Call(w, "ApplyFill", new Fill { Color = "#ffffff", Pattern = "dots", PatternColor = "#2e5f8a" });
            Check(callout.Fill?.Pattern == "dots", "remplissage a motif");
            session.Select(arrow, pg);
            Call(w, "ApplyFill", presets[0].Fill);
            session.Change(pg, () => { arrow.Fill!.Opacity = 0.6; arrow.Line!.Opacity = 0.5; });
            Check(DecorRenderer.FillBrush(arrow.Fill, session.Images) is LinearGradientBrush { Opacity: 0.6 }, "degrade lineaire transparent a 40 %");

            // effets et bordure decorative
            session.Select(star, pg);
            Call(w, "SetEffects", new Action<EffectsFx>(fx => { fx.GlowColor = "#ffd23f"; fx.GlowSize = 10; }));
            Check(star.Effects?.HasGlow == true, "lueur");
            onDrawn.Invoke(w, new object[] { View(), "rect", new Rect(40, 140, 140, 90) });
            var soft = pg.Shapes[^1];
            Call(w, "SetEffects", new Action<EffectsFx>(fx => fx.SoftEdge = 8));
            onDrawn.Invoke(w, new object[] { View(), "roundRect", new Rect(200, 140, 140, 90) });
            var inner = pg.Shapes[^1];
            session.Change(pg, () => inner.Fill = new Fill { Color = "#ffffff" });
            Call(w, "SetEffects", new Action<EffectsFx>(fx => fx.InnerShadow = new ShadowFx { Angle = 45, Distance = 4, Blur = 6, Opacity = 0.5 }));
            Check(soft.Effects?.SoftEdge == 8 && inner.Effects?.InnerShadow is not null, "bords adoucis et ombre interieure");
            onDrawn.Invoke(w, new object[] { View(), "rect", new Rect(360, 140, 160, 100) });
            var framed = pg.Shapes[^1];
            session.Change(pg, () => { framed.Fill = new Fill { Color = "#fff8e1" }; framed.Line = null; framed.BorderArt = new BorderArtFx { Pattern = "stars", Size = 12, Color = "#d13438", Color2 = "#ffc000" }; });
            Check(BorderArtLayout.Tiles(framed.W, framed.H, 12).Count > 20, "bordure decorative d'etoiles");

            // modifier les points
            onDrawn.Invoke(w, new object[] { View(), "star5", new Rect(40, 260, 110, 100) });
            var pts = pg.Shapes[^1];
            view = View();
            Check(view.BeginPointEdit(pts) && pts.Kind == ShapeKind.Path && pts.Geometry is null && PathEditor.Nodes(pts.Path!).Count == 10, "modifier les points : l'etoile devient un trace de 10 sommets");
            var top = PathEditor.NodePoint(pts.Path!, 0);
            Drag(view, new Point(top.X, top.Y), new Point(top.X, top.Y - 30));
            Check(Math.Abs(PathEditor.NodePoint(pts.Path!, 0).Y - (top.Y - 30)) < 0.5 && Math.Abs(pts.Y - (top.Y - 30)) < 0.5, "sommet deplace, cadre recalcule");
            var seg = PathEditor.Nearest(pts.Path!, (top.X + PathEditor.NodePoint(pts.Path!, 1).X) / 2, (top.Y - 30 + PathEditor.NodePoint(pts.Path!, 1).Y) / 2);
            Keyboard.Focus(view);
            int nodes = PathEditor.Nodes(pts.Path!).Count;
            session.Change(pg, () => PathEditor.InsertNode(pts.Path!, seg.Seg, 0.5));
            Check(PathEditor.Nodes(pts.Path!).Count == nodes + 1, "sommet ajoute sur le contour");
            typeof(PageView).GetProperty("SelectedNode")!.SetValue(view, PathEditor.Nodes(pts.Path!)[2]);
            view.ToggleSmooth();
            Check(PathEditor.IsSmooth(pts.Path!, PathEditor.Nodes(pts.Path!)[2]), "point lisse");
            Check(view.DeleteSelectedNode(PathEditor.Nodes(pts.Path!)[3]) && PathEditor.Nodes(pts.Path!).Count == nodes, "sommet supprime");
            Pump();
            CaptureView(view, Path.Combine(outDir, "selftest-9-points.png"));
            view.EndPointEdit();
            Check(view.PointEditShape is null, "fin du mode modifier les points");

            // WordArt
            var wa = (Shape)Call(w, "InsertWordArt", 0, "Fête de l'école", false)!;
            Pump();
            Check(wa.IsWordArt && session.Selected == wa, "WordArt insere");
            var tabShape = (System.Windows.Controls.RadioButton)w.FindName("TabShape");
            Check(tabShape.IsChecked == true && ((UIElement)w.FindName("WordArtTextButton")).Visibility == Visibility.Visible, "onglet Format de la forme avec les boutons WordArt");
            session.Change(pg, () => { wa.X = 170; wa.Y = 280; });
            var g = DecorRenderer.WordArtGeometry(wa, session)!.Bounds;
            Check(g.Left >= wa.X - 1 && g.Right <= wa.X + wa.W + 1 && g.Width > wa.W * 0.9 && g.Height > wa.H * 0.9, "WordArt droit : le texte remplit le cadre");
            var arc = (Shape)Call(w, "InsertWordArt", 2, "Kermesse de juin", false)!;
            var wfx = arc.WordArt!.Clone();
            wfx.Warp = WordArtWarps.ArcUp;
            session.Change(pg, () => { mw.GetMethod("ApplyWordArt", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { arc, wfx }); arc.X = 200; arc.Y = 560; arc.W = 300; arc.H = 140; });
            var ga = DecorRenderer.WordArtGeometry(arc, session)!.Bounds;
            Check(arc.WordArt!.Warp == WordArtWarps.ArcUp && ga.Top >= arc.Y - 2 && ga.Bottom <= arc.Y + arc.H + 2, "WordArt en arc : texte dans le cadre");
            var circle = (Shape)Call(w, "InsertWordArt", 4, "Notre journal * Notre journal *", false)!;
            var cfx = circle.WordArt!.Clone();
            cfx.Warp = WordArtWarps.Circle;
            session.Change(pg, () => { mw.GetMethod("ApplyWordArt", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { circle, cfx }); circle.X = 400; circle.Y = 430; circle.W = circle.H = 150; });
            Check(DecorRenderer.WordArtGeometry(circle, session) is { } gc && Math.Abs(gc.Bounds.Width - gc.Bounds.Height) < 30, "WordArt en cercle");

            // espaces reserves, permutation, legende
            var ph = (Shape)Call(w, "AddPlaceholder", pg)!;
            session.Change(pg, () => { ph.X = 40; ph.Y = 400; ph.W = 160; ph.H = 120; });
            Check(ph.IsPlaceholder && DecorRenderer.PlaceholderButton(ph).Contains(new Point(ph.X + ph.W / 2, ph.Y + ph.H / 2)), "espace reserve avec son bouton");
            var wide = TestImage(imgDir, "photo-large.jpg", 3600, 1800, Color.FromRgb(0x08, 0x7a, 0xff), Color.FromRgb(0x99, 0x35, 0xf4));
            Call(w, "FillPlaceholder", ph, wide);
            Check(!ph.IsPlaceholder && ph.ImageFx is { CropLeft: > 0.1 } fx1 && Math.Abs(fx1.CropLeft - fx1.CropRight) < 1e-6, "image placee dans le cadre, recadree pour le remplir");
            var ph2 = (Shape)Call(w, "AddPlaceholder", pg)!;
            session.Change(pg, () => { ph2.X = 220; ph2.Y = 400; ph2.W = 90; ph2.H = 130; });
            var square = TestImage(imgDir, "carre.png", 400, 400, Color.FromRgb(0xff, 0xc0, 0x00), Color.FromRgb(0xd1, 0x34, 0x38));
            Call(w, "FillPlaceholder", ph2, square);
            string s1 = ph.Src!, s2 = ph2.Src!;
            var r1 = new Rect(ph.X, ph.Y, ph.W, ph.H);
            session.SelectMany(new[] { ph, ph2 }, ph2, pg);
            Check((bool)Call(w, "SwapImages")! && ph.Src == s2 && ph2.Src == s1 && new Rect(ph.X, ph.Y, ph.W, ph.H) == r1, "permuter deux images : les cadres restent en place");
            Check(ph2.ImageFx is { CropLeft: > 0.1 } && ph.ImageFx is { CropTop: > 0.05 }, "recadrage recalcule pour chaque cadre");
            session.Select(ph, pg);
            var cap = (Shape)Call(w, "AddCaption")!;
            Check(cap.IsText && cap.Group is not null && cap.Group == ph.Group && cap.Y >= ph.Y + ph.H, "legende sous l'image, groupee avec elle");
            Check(OpenPub.Layout.PageLayouter.Obstacles(pg).Contains(cap), "le texte de la page contourne la legende");

            session.Select(null, pg);
            Pump();
            view = View();
            view.BringIntoView();
            Pump();
            CaptureView(view, Path.Combine(outDir, "selftest-9-lot9.png"));
            session.Select(wa, pg);
            Pump();
            Capture(w, Path.Combine(outDir, "selftest-9-ruban-forme.png"));

            // fenetres du lot 9 : construites et affichees hors ecran (captures de relecture)
            foreach (var (name, win) in new (string, Window)[]
            {
                ("remplissage", new FillWindow(star.Fill, 0.5, session.Images, (c, _) => (false, c), () => null)),
                ("effets", new EffectsWindow(star.Effects, (c, _) => (false, c))),
                ("wordart", new WordArtWindow(arc, session)),
            })
            {
                win.Owner = w; win.Left = -6000; win.Top = 0; win.ShowActivated = false; win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.Show();
                Pump();
                Capture(win, Path.Combine(outDir, $"selftest-9-fenetre-{name}.png"));
                Check(win.IsLoaded, $"fenetre {name}");
                win.Close();
            }

            // compression des images : la grande photo (3600 px pour 160 pt de large) est reduite a 300 ppp
            int before = DecorRenderer.PixelSize(session.Images, ph2.Src!)!.Value.W;
            int n = (int)Call(w, "CompressImages", false, 300.0)!;
            var after = DecorRenderer.PixelSize(session.Images, ph2.Src!)!.Value;
            double needed = 90 / 72.0 * 300 / (1 - ph2.ImageFx!.CropLeft - ph2.ImageFx.CropRight);
            Check(n >= 1 && before == 3600 && after.W < before && after.W >= needed - 1 && after.W <= needed * 1.05 + 2, $"compression : {before} -> {after.W} pixels de large (utile : {needed:0})");
            var file = Path.Combine(imgDir, "compression.opub");
            session.Save(file);
            using (var zip = ZipFile.OpenRead(file))
                Check(zip.Entries.Any(e => e.FullName.Contains("300ppp")) && !zip.Entries.Any(e => e.FullName.EndsWith("photo-large.jpg")), "l'ancienne image quitte le paquet a l'enregistrement");
        }
        finally
        {
            try { Directory.Delete(imgDir, true); } catch { }
        }
    }
}
