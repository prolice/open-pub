using System.IO.Compression;
using OpenPub.Core.Geometry;
using OpenPub.Core.Imaging;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 9 : bibliotheque de formes, poignees de reglage, modification des points, WordArt, bordures, images.</summary>
public class ShapeLibraryTests
{
    static readonly Box B = new(100, 50, 200, 120);

    static Box BoundsOf(List<PathSegment> path) => PathTools.Bounds(path) ?? throw new Exception("trace vide");

    [Fact]
    public void La_bibliotheque_compte_une_cinquantaine_de_formes_en_quatre_categories()
    {
        Assert.InRange(ShapeLibrary.All.Count, 48, 60);
        Assert.Equal(ShapeLibrary.All.Count, ShapeLibrary.All.Select(d => d.Id).Distinct().Count());
        foreach (var cat in ShapeLibrary.Categories) Assert.Contains(ShapeLibrary.All, d => d.Category == cat);
        // les identifiants des versions precedentes restent valables (fichiers existants)
        foreach (var id in new[] { "rect", "roundRect", "ellipse", "triangle", "diamond", "hexagon", "star5", "arrowRight", "bubble", "heart" })
            Assert.NotNull(ShapeLibrary.Find(id));
        foreach (var n in new[] { 4, 5, 6, 8, 10, 12, 16, 24, 32 }) Assert.NotNull(ShapeLibrary.Find($"star{n}"));
    }

    [Fact]
    public void Chaque_forme_tient_dans_son_cadre()
    {
        foreach (var d in ShapeLibrary.All.Where(d => d.Category != ShapeLibrary.Callouts))
        {
            var b = BoundsOf(ShapeLibrary.Outline(d.Id, B));
            Assert.True(b.X >= B.X - 0.5 && b.Y >= B.Y - 0.5 && b.X + b.W <= B.X + B.W + 0.5 && b.Y + b.H <= B.Y + B.H + 0.5, $"{d.Id} deborde : {b}");
            Assert.True(b.W > B.W * 0.6 && b.H > B.H * 0.6, $"{d.Id} trop petite : {b}");
        }
    }

    [Fact]
    public void Identifiant_inconnu_donne_un_rectangle()
    {
        var b = BoundsOf(ShapeLibrary.Outline("formeInconnue", B));
        Assert.Equal(B, b);
    }

    [Fact]
    public void Poignee_jaune_regle_l_epaisseur_de_la_fleche()
    {
        var s = new Shape { Kind = ShapeKind.Rect, Geometry = "arrowRight", X = 0, Y = 0, W = 200, H = 100 };
        var d = ShapeLibrary.Find(s.Geometry)!;
        var (hx, hy) = d.Handles[0].Position(s);
        Assert.Equal(120, hx, 3);         // debut de la pointe : 60 % de la longueur
        Assert.Equal(30, hy, 3);          // bord du corps : epaisseur 40 %
        d.Handles[0].Apply(s, 150, 10);
        Assert.Equal(new[] { 0.8, 0.25 }, s.Adjust!.Select(v => Math.Round(v, 3)));
        // le corps s'elargit : son bord passe a 10 % de la hauteur
        var pts = PathTools.Flatten(ShapeLibrary.Outline(s)!)[0].Points;
        Assert.Contains(pts, p => Math.Abs(p.Y - 10) < 0.01 && p.X < 1);
    }

    [Fact]
    public void Poignee_de_l_etoile_regle_la_profondeur_des_branches()
    {
        var s = new Shape { Geometry = "star8", X = 0, Y = 0, W = 100, H = 100 };
        var h = ShapeLibrary.Find("star8")!.Handles[0];
        h.Apply(s, 50 + 20 * Math.Cos(-Math.PI / 2 + Math.PI / 8), 50 + 20 * Math.Sin(-Math.PI / 2 + Math.PI / 8));
        Assert.Equal(0.4, s.Adjust![0], 3);
        var pts = PathTools.Flatten(ShapeLibrary.Outline(s)!)[0].Points;
        double minR = pts.Min(p => Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50)));
        Assert.Equal(20, minR, 1);
    }

    [Fact]
    public void Pointe_de_la_bulle_deplacable_hors_du_cadre()
    {
        var s = new Shape { Geometry = "rectCallout", X = 0, Y = 0, W = 100, H = 50 };
        ShapeLibrary.Find(s.Geometry)!.Handles[0].Apply(s, 160, 25);       // pointe a droite, hors du cadre
        var b = BoundsOf(ShapeLibrary.Outline(s)!);
        Assert.Equal(160, b.X + b.W, 3);
        Assert.Equal(0, b.X, 3);
        Assert.Equal(50, b.H, 3);
        // pointe ramenee dans la bulle : plus de pointe
        ShapeLibrary.Find(s.Geometry)!.Handles[0].Apply(s, 50, 25);
        Assert.Equal(new Box(0, 0, 100, 50), BoundsOf(ShapeLibrary.Outline(s)!));
    }

    [Fact]
    public void Bulle_par_defaut_comme_avant()
    {
        // l'ancienne bulle : corps sur 80 % de la hauteur, base de la pointe de 26 % a 42 % de la largeur, pointe a 18 %
        var pts = PathTools.Flatten(ShapeLibrary.Outline("bubble", new Box(0, 0, 100, 100)), 2)[0].Points;
        Assert.Contains(pts, p => Math.Abs(p.X - 18) < 0.01 && Math.Abs(p.Y - 100) < 0.01);
        Assert.Contains(pts, p => Math.Abs(p.X - 26) < 0.01 && Math.Abs(p.Y - 80) < 0.01);
        Assert.Contains(pts, p => Math.Abs(p.X - 42) < 0.01 && Math.Abs(p.Y - 80) < 0.01);
    }

    [Fact]
    public void Coins_arrondis_regles_par_leur_poignee()
    {
        var s = new Shape { Geometry = "roundRect", X = 10, Y = 10, W = 100, H = 60 };
        var h = ShapeLibrary.Find("roundRect")!.Handles[0];
        Assert.Equal(10 + 9, h.Position(s).X, 3);         // 15 % du petit cote par defaut
        h.Apply(s, 30, 10);
        Assert.Equal(20, s.CornerRadius);
        h.Apply(s, 500, 10);
        Assert.Equal(30, s.CornerRadius);                   // au plus la moitie du petit cote
    }

    [Fact]
    public void Habillage_serre_suit_le_contour_d_une_etoile()
    {
        var t = new Shape
        {
            Kind = ShapeKind.Text, X = 0, Y = 0, W = 300, H = 300, Z = 1,
            Paragraphs = new List<Paragraph> { new() { Runs = { new Run { Text = "mot", Font = new FontSpec { Family = "Mono", Size = 10 } } } } },
        };
        var star = new Shape { Kind = ShapeKind.Rect, Geometry = "star5", X = 0, Y = 100, W = 100, H = 100, Z = 5, Wrap = "tight", Fill = new Fill { Color = "#ff0000" } };
        var page = new Page { Width = 600, Height = 800, Shapes = { t, star } };
        var ex = PageLayouter.Geometry(t, PageLayouter.Obstacles(page)).Exclusions;
        Assert.True(ex.Count > 10);
        // pres de la pointe du haut, l'etoile est etroite ; au niveau des branches, elle est large
        var top = ex.First(e => Math.Abs(e.Top - (100 - PageLayouter.Gap)) < 0.01);       // bande 100-104 : pointe du haut
        var mid = ex.First(e => Math.Abs(e.Top - (132 - PageLayouter.Gap)) < 0.01);       // bande 132-136 : branches
        Assert.True(top.RawRight < 60 && mid.RawRight > 90, $"haut {top.RawRight}, branches {mid.RawRight}");
    }

    [Fact]
    public void Polygones_d_habillage_d_un_cadre_creux()
    {
        var s = new Shape { Geometry = "frame", X = 0, Y = 0, W = 100, H = 100 };
        var polys = ShapeLibrary.Polygons(s)!;
        Assert.Equal(2, polys.Count);          // contour exterieur et interieur
    }

    // ---------------------------------------------------------------- modification des points

    static List<PathSegment> Square() => new PathBuilder().Poly((0, 0), (100, 0), (100, 100), (0, 100)).Segments;

    [Fact]
    public void Une_forme_devient_un_trace_modifiable()
    {
        var s = new Shape { Kind = ShapeKind.Rect, Geometry = "star5", X = 0, Y = 0, W = 100, H = 100, Adjust = new List<double> { 0.5 } };
        Assert.True(PathEditor.ToEditable(s));
        Assert.Equal(ShapeKind.Path, s.Kind);
        Assert.Null(s.Geometry);
        Assert.Null(s.Adjust);
        Assert.Equal(10, PathEditor.Nodes(s.Path!).Count);
        Assert.False(PathEditor.ToEditable(new Shape { Kind = ShapeKind.Text }));
    }

    [Fact]
    public void Deplacer_un_sommet_et_le_point_de_depart_d_un_trace_ferme()
    {
        var p = Square();
        PathEditor.Normalize(p);
        var nodes = PathEditor.Nodes(p);
        Assert.Equal(4, nodes.Count);
        PathEditor.MoveNode(p, nodes[0], -10, -10);
        var f = PathTools.Flatten(p)[0];
        Assert.True(f.Closed);
        Assert.Equal((-10.0, -10.0), f.Points[0]);
        Assert.Equal((-10.0, -10.0), f.Points[^1]);        // le sommet de fermeture suit
    }

    [Fact]
    public void Ajouter_un_sommet_ne_change_pas_la_courbe()
    {
        var p = new PathBuilder().M(0, 0).C(0, 100, 100, 100, 100, 0).Segments;
        var before = PathEditor.PointOn(p, 1, 0.3);
        int i = PathEditor.InsertNode(p, 1, 0.5);
        Assert.Equal(3, p.Count);
        var mid = PathEditor.NodePoint(p, i);
        Assert.Equal(50, mid.X, 6);
        Assert.Equal(75, mid.Y, 6);
        // un point de la premiere moitie de l'ancienne courbe est sur la nouvelle courbe (t = 0,6)
        var after = PathEditor.PointOn(p, 1, 0.6);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Supprimer_un_sommet_garde_au_moins_trois_sommets()
    {
        var p = Square();
        PathEditor.Normalize(p);
        Assert.True(PathEditor.DeleteNode(p, PathEditor.Nodes(p)[1]));
        Assert.Equal(3, PathEditor.Nodes(p).Count);
        Assert.False(PathEditor.DeleteNode(p, PathEditor.Nodes(p)[1]));
        // supprimer le depart : le sommet suivant le remplace et la fermeture le rejoint
        var q = Square();
        PathEditor.Normalize(q);
        Assert.True(PathEditor.DeleteNode(q, 0));
        var f = PathTools.Flatten(q)[0];
        Assert.Equal((100.0, 0.0), f.Points[0]);
        Assert.Equal(f.Points[0], f.Points[^1]);
        Assert.Equal(3, PathEditor.Nodes(q).Count);
    }

    [Fact]
    public void Point_lisse_puis_angle_vif()
    {
        var p = new PathBuilder().M(0, 0).L(50, 50).L(100, 0).Segments;
        Assert.False(PathEditor.IsSmooth(p, 1));
        PathEditor.SetSmooth(p, 1, true);
        Assert.True(PathEditor.IsSmooth(p, 1));
        Assert.Equal("C", p[1].A);
        Assert.Equal("C", p[2].A);
        Assert.Equal(p[1].Y2!.Value, p[2].Y1!.Value, 6);        // tangente horizontale (voisins a la meme hauteur)
        // deplacer une poignee d'un point lisse : l'autre reste alignee
        PathEditor.MoveControl(p, 1, 1, 2, 30, 40);
        Assert.True(PathEditor.IsSmooth(p, 1));
        PathEditor.SetSmooth(p, 1, false);
        Assert.False(PathEditor.IsSmooth(p, 1));
        Assert.Equal((50.0, 50.0), PathEditor.ControlPoint(p, 1, 2));
    }

    [Fact]
    public void Recalcul_du_cadre_sans_bouger_un_objet_tourne()
    {
        var s = new Shape { Kind = ShapeKind.Path, X = 0, Y = 0, W = 100, H = 100, Rotation = 30, Path = Square() };
        PathEditor.Normalize(s.Path);
        static (double X, double Y) ToPage(Shape s, double x, double y)
        {
            double a = s.Rotation * Math.PI / 180, cx = s.X + s.W / 2, cy = s.Y + s.H / 2;
            return (cx + (x - cx) * Math.Cos(a) - (y - cy) * Math.Sin(a), cy + (x - cx) * Math.Sin(a) + (y - cy) * Math.Cos(a));
        }
        var fixedBefore = ToPage(s, 0, 100);
        PathEditor.MoveNode(s.Path, 2, 160, 140);      // le coin (100, 100) s'eloigne : le cadre grandit
        PathEditor.FitFrame(s);
        Assert.Equal(160, s.W, 6);
        var corner = s.Path.Last(q => q.A == "L" && Math.Abs(q.X!.Value - s.X) < 1e-6 && Math.Abs(q.Y!.Value - (s.Y + 100)) < 1e-6);
        var fixedAfter = ToPage(s, corner.X!.Value, corner.Y!.Value);
        Assert.Equal(fixedBefore.X, fixedAfter.X, 6);
        Assert.Equal(fixedBefore.Y, fixedAfter.Y, 6);
    }

    [Fact]
    public void Aplatissement_d_un_arc_svg()
    {
        var p = new List<PathSegment> { new() { A = "M", X = 0, Y = 50 }, new() { A = "A", X = 100, Y = 50, Rx = 50, Ry = 50, Sweep = 1 } };
        var b = BoundsOf(p);
        Assert.Equal(0, b.Y, 1);          // demi-cercle vers le haut (sens horaire a l'ecran)
        Assert.Equal(100, b.W, 1);
    }

    // ---------------------------------------------------------------- WordArt

    static readonly double[] Adv = Enumerable.Repeat(0.6, 10).ToArray();

    [Fact]
    public void WordArt_droit_remplit_le_cadre()
    {
        var l = TextOnPath.Layout(Adv, 0.7, 0.3, WordArtWarps.None, 300, 50);
        Assert.Equal(50, l.Size, 6);
        Assert.Equal(10, l.Glyphs.Count);
        Assert.Equal(15, l.Glyphs[0].X, 6);                      // milieu du 1er glyphe, texte etire sur 300 pt
        Assert.Equal(285, l.Glyphs[^1].X, 6);
        Assert.All(l.Glyphs, g => Assert.Equal(35, g.Y, 6));     // ligne de base : hauteur au-dessus = 0,7 x 50
        Assert.All(l.Glyphs, g => Assert.Equal(0, g.AngleDeg, 6));
    }

    [Fact]
    public void WordArt_en_arc_vers_le_haut()
    {
        var l = TextOnPath.Layout(Adv, 0.7, 0.3, WordArtWarps.ArcUp, 300, 150);
        Assert.True(l.Size > 5);
        var first = l.Glyphs[0]; var last = l.Glyphs[^1];
        Assert.True(first.AngleDeg < -20 && last.AngleDeg > 20, $"{first.AngleDeg} / {last.AngleDeg}");     // monte puis descend
        Assert.True(l.Glyphs.Min(g => g.Y) < first.Y);
        Assert.Equal(300, first.X + last.X, 1);                   // symetrique autour du milieu (x = 150)
        // le texte tient dans le cadre (le sommet des glyphes du milieu reste sous le bord)
        Assert.True(l.Glyphs.Min(g => g.Y) - 0.7 * l.Size >= -0.01);
    }

    [Fact]
    public void WordArt_en_arc_vers_le_bas_et_en_cercle()
    {
        var down = TextOnPath.Layout(Adv, 0.7, 0.3, WordArtWarps.ArcDown, 300, 150);
        Assert.True(down.Glyphs[0].AngleDeg > 20 && down.Glyphs[^1].AngleDeg < -20);
        Assert.True(down.Glyphs.Max(g => g.Y) > down.Glyphs[0].Y);
        var circle = TextOnPath.Layout(Enumerable.Repeat(0.6, 30).ToArray(), 0.7, 0.3, WordArtWarps.Circle, 200, 200);
        Assert.Equal(30, circle.Glyphs.Count);
        // le milieu du texte est en haut du cercle
        var mid = circle.Glyphs[15];
        Assert.True(mid.Y < 40 && Math.Abs(mid.X - 100) < 15, $"{mid.X} {mid.Y}");
    }

    [Fact]
    public void WordArt_en_vague()
    {
        var l = TextOnPath.Layout(Adv, 0.7, 0.3, WordArtWarps.Wave, 300, 100);
        Assert.True(l.Glyphs.Select(g => g.Y).Max() - l.Glyphs.Select(g => g.Y).Min() > 20);
        Assert.Contains(l.Glyphs, g => g.AngleDeg > 5);
        Assert.Contains(l.Glyphs, g => g.AngleDeg < -5);
        Assert.Empty(TextOnPath.Layout(Array.Empty<double>(), 0.7, 0.3, WordArtWarps.Wave, 300, 100).Glyphs);
    }

    // ---------------------------------------------------------------- bordures, images

    [Fact]
    public void Bordure_decorative_motifs_dans_les_coins_et_le_long_des_bords()
    {
        var tiles = BorderArtLayout.Tiles(200, 100, 20);
        Assert.Equal(4 + 2 * 8 + 2 * 3, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.X >= 10 - 1e-9 && t.X <= 190 + 1e-9 && t.Y >= 10 - 1e-9 && t.Y <= 90 + 1e-9));
        Assert.Contains(tiles, t => t.X == 10 && t.Y == 10);
        Assert.Contains(tiles, t => t.X == 190 && t.Y == 90 && t.AngleDeg == 180);
        Assert.Equal(Enumerable.Range(0, tiles.Count), tiles.Select(t => t.Index));
        Assert.Empty(BorderArtLayout.Tiles(10, 10, 20));
        foreach (var (id, _) in BorderArtLayout.Patterns)
            Assert.NotEmpty(BorderArtLayout.Motif(id, new Box(0, 0, 10, 10)));
    }

    [Fact]
    public void Recadrage_pour_remplir_un_cadre()
    {
        var (l, t, r, b) = ImageFit.CoverCrop(4000, 2000, 100, 100);          // image 2:1 dans un carre
        Assert.Equal(0.25, l, 6); Assert.Equal(0.25, r, 6); Assert.Equal(0, t); Assert.Equal(0, b);
        (l, t, r, b) = ImageFit.CoverCrop(1000, 2000, 200, 100);              // portrait dans un paysage
        Assert.Equal(0, l); Assert.Equal(0.375, t, 6); Assert.Equal(0.375, b, 6);
        Assert.Null(ImageFit.WithCover(null, 100, 100, 50, 50));             // memes proportions : aucune retouche
    }

    [Fact]
    public void Compression_des_images_a_300_ppp()
    {
        (int, int)? Px(string s) => s switch { "assets/grande.jpg" => (6000, 4000), "assets/petite.jpg" => (600, 400), "assets/mosaique.png" => (3000, 3000), _ => null };
        var uses = new List<ImageUse>
        {
            new("assets/grande.jpg", 144, 96),                 // 2 pouces x 300 ppp = 600 pixels utiles
            new("assets/petite.jpg", 144, 96),
            new("assets/mosaique.png", 72, 72, Tile: true),
        };
        var plan = ImageCompression.Plan(uses, Px);
        var t = Assert.Single(plan);
        Assert.Equal("assets/grande.jpg", t.Src);
        Assert.Equal((600, 400), (t.NewWidth, t.NewHeight));
        // la meme image recadree (seule la moitie gauche est visible) demande deux fois plus de pixels
        uses.Add(new ImageUse("assets/grande.jpg", 144, 96, VisibleW: 0.5, VisibleH: 1));
        Assert.Equal(1200, ImageCompression.Plan(uses, Px).Single().NewWidth);
        // une image deja juste assez grande n'est pas touchee
        Assert.Empty(ImageCompression.Plan(new[] { new ImageUse("assets/grande.jpg", 6000 * 72 / 300.0, 4000 * 72 / 300.0) }, Px));
    }

    [Fact]
    public void Utilisations_des_images_d_un_document()
    {
        var doc = new PubDocument
        {
            Pages =
            {
                new Page
                {
                    Shapes =
                    {
                        new Shape { Kind = ShapeKind.Image, Src = "assets/a.jpg", W = 100, H = 50, ImageFx = new ImageFx { CropLeft = 0.25, CropRight = 0.25 } },
                        new Shape { Kind = ShapeKind.Image, W = 100, H = 50 },                                     // espace reserve
                        new Shape { Kind = ShapeKind.Rect, W = 80, H = 80, Fill = new Fill { Color = "#fff", Image = "assets/b.png" } },
                    },
                },
            },
        };
        var uses = ImageCompression.Uses(doc);
        Assert.Equal(2, uses.Count);
        Assert.Equal(0.5, uses[0].VisibleW, 6);
        Assert.True(uses[1].Cover);
    }

    // ---------------------------------------------------------------- format de fichier

    [Fact]
    public void Nouveaux_champs_facultatifs_et_anciens_fichiers()
    {
        // un ancien fichier (drapeau d'import « gradient ») se lit toujours
        var old = DocumentJson.Parse("""{"pages":[{"shapes":[{"kind":"rect","x":1,"y":2,"w":3,"h":4,"fill":{"color":"#ff0000","gradient":true}}]}]}""");
        var s0 = old.Pages[0].Shapes[0];
        Assert.True(s0.Fill!.Gradient);
        Assert.False(s0.Fill.HasGradient);
        Assert.Null(s0.Adjust); Assert.Null(s0.Effects); Assert.Null(s0.WordArt); Assert.Null(s0.BorderArt);
        // sans champ du lot 9, rien de nouveau n'est ecrit
        var json0 = DocumentJson.Serialize(old);
        foreach (var k in new[] { "adjust", "effects", "wordArt", "borderArt", "stops", "pattern" }) Assert.DoesNotContain($"\"{k}\"", json0);
        // aller-retour des nouveaux champs
        s0.Adjust = new List<double> { 0.3 };
        s0.Fill = new Fill { Color = "#ffffff", Stops = new List<GradientStopFx> { new() { Pos = 0, Color = "#ffffff" }, new() { Pos = 1, Color = "#000000", Opacity = 0.5 } }, GradientType = "radial" };
        s0.Line = new LineStyle { Color = "#000000", Opacity = 0.4 };
        s0.Effects = new EffectsFx { GlowColor = "#ffd23f", SoftEdge = 4, InnerShadow = new ShadowFx { Distance = 2 } };
        s0.BorderArt = new BorderArtFx { Pattern = "stars", Color2 = "#00ff00" };
        var wa = new Shape { Kind = ShapeKind.WordArt, WordArt = new WordArtFx { Text = "Fête", Warp = WordArtWarps.Circle } };
        old.Pages[0].Shapes.Add(wa);
        var back = DocumentJson.Parse(DocumentJson.Serialize(old));
        var s1 = back.Pages[0].Shapes[0];
        Assert.Equal(0.3, s1.Adjust![0]);
        Assert.True(s1.Fill!.HasGradient && s1.Fill.IsRadial);
        Assert.Equal(0.5, s1.Fill.Stops![1].Opacity);
        Assert.Equal(0.4, s1.Line!.Opacity);
        Assert.Equal(4, s1.Effects!.SoftEdge);
        Assert.Equal(2, s1.Effects.InnerShadow!.Distance);
        Assert.Equal("#00ff00", s1.BorderArt!.Color2);
        Assert.True(back.Pages[0].Shapes[1].IsWordArt);
        Assert.Equal("Fête", back.Pages[0].Shapes[1].WordArt!.Text);
        Assert.True(new Shape { Kind = ShapeKind.Image }.IsPlaceholder);
    }

    [Fact]
    public void Une_image_de_remplissage_reste_dans_le_paquet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openpub-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "document.json"), """{"pages":[{"shapes":[]}]}""");
            using var pkg = OpubPackage.ImportFolder(dir);
            var fillImg = pkg.AddAsset(new byte[] { 1, 2, 3 }, "motif.png");
            pkg.AddAsset(new byte[] { 4, 5 }, "inutile.png");
            pkg.Document.Pages[0].Shapes.Add(new Shape { Kind = ShapeKind.Rect, W = 10, H = 10, Fill = new Fill { Color = "#fff", Image = fillImg } });
            var file = Path.Combine(dir, "test.opub");
            pkg.Save(file);
            using var zip = ZipFile.OpenRead(file);
            var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
            Assert.Contains(fillImg, names);
            Assert.DoesNotContain(names, n => n.Contains("inutile"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
