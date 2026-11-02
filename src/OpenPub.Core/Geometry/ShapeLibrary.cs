using OpenPub.Core.Model;

namespace OpenPub.Core.Geometry;

/// <summary>Poignee jaune de reglage d'une forme : position (repere non tourne de l'objet) et effet d'un glissement.</summary>
public sealed record AdjustHandle(Func<Shape, (double X, double Y)> Position, Action<Shape, double, double> Apply);

/// <summary>Forme automatique de la bibliotheque.</summary>
public sealed class ShapeDef
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    /// <summary>Valeurs par defaut des reglages (<see cref="Shape.Adjust"/>).</summary>
    public double[] Defaults { get; init; } = Array.Empty<double>();
    /// <summary>Construit le contour dans le cadre, a partir des reglages resolus et du rayon des coins.</summary>
    public required Func<Box, double[], double?, PathBuilder> Build { get; init; }
    public AdjustHandle[] Handles { get; init; } = Array.Empty<AdjustHandle>();
}

/// <summary>
/// Bibliotheque des formes automatiques (valeurs de <see cref="Shape.Geometry"/>) : contour en segments M/L/C/Q/Z,
/// calcule a partir du cadre et des reglages, donc independant de WPF (rendu, habillage serre, modification des
/// points, tests). Les reglages sont des parts du cadre : ils suivent le redimensionnement.
/// </summary>
public static class ShapeLibrary
{
    public const string Basic = "Formes de base";
    public const string Arrows = "Flèches pleines";
    public const string Stars = "Étoiles et bannières";
    public const string Callouts = "Bulles et légendes";
    public static readonly string[] Categories = { Basic, Arrows, Stars, Callouts };

    static readonly List<ShapeDef> Defs = new();
    static readonly Dictionary<string, ShapeDef> ById = new();
    public static IReadOnlyList<ShapeDef> All => Defs;

    public static ShapeDef? Find(string? id) => id is not null && ById.TryGetValue(id, out var d) ? d : null;

    /// <summary>Reglages effectifs : ceux de l'objet, completes par les valeurs par defaut.</summary>
    public static double[] Resolve(ShapeDef d, IReadOnlyList<double>? adjust)
    {
        var a = (double[])d.Defaults.Clone();
        if (adjust is not null)
            for (int i = 0; i < a.Length && i < adjust.Count; i++)
                if (double.IsFinite(adjust[i])) a[i] = adjust[i];
        return a;
    }

    /// <summary>Contour d'une forme dans un cadre ; identifiant inconnu = rectangle.</summary>
    public static List<PathSegment> Outline(string? id, Box b, IReadOnlyList<double>? adjust = null, double? cornerRadius = null)
    {
        var d = Find(id) ?? ById[ShapeGeometry.Rect];
        return d.Build(b, Resolve(d, adjust), cornerRadius).Segments;
    }

    /// <summary>Contour de la forme automatique d'un objet (null s'il n'en a pas).</summary>
    public static List<PathSegment>? Outline(Shape s) =>
        s.Geometry is null ? null : Outline(s.Geometry, Box.Of(s), s.Adjust, s.CornerRadius);

    /// <summary>Polygones de l'emprise d'un objet (habillage serre) : forme automatique, trace ou detourage, aplatis.</summary>
    public static List<List<(double X, double Y)>>? Polygons(Shape s)
    {
        var path = s.Geometry is not null && s.Geometry != ShapeGeometry.Rect ? Outline(s)
                 : s.Path is { Count: >= 3 } ? s.Path
                 : s.Clip is { Count: >= 3 } ? s.Clip
                 : null;
        if (path is null) return null;
        var polys = PathTools.Flatten(path, 8).Where(f => f.Points.Count >= 3).Select(f => f.Points).ToList();
        return polys.Count > 0 ? polys : null;
    }

    /// <summary>Change un reglage (cree la liste a partir des valeurs par defaut si besoin).</summary>
    public static void SetAdjust(Shape s, int index, double value)
    {
        var d = Find(s.Geometry);
        if (d is null || index >= d.Defaults.Length) return;
        var a = Resolve(d, s.Adjust);
        a[index] = value;
        s.Adjust = a.ToList();
    }

    static double Adj(Shape s, int i) => Find(s.Geometry) is { } d ? Resolve(d, s.Adjust)[i] : 0;
    static double Frac(double v, double origin, double size) => size <= 1e-9 ? 0 : (v - origin) / size;

    static void Add(string id, string label, string cat, Func<Box, double[], double?, PathBuilder> build, double[]? defaults = null, params AdjustHandle[] handles)
    {
        var d = new ShapeDef { Id = id, Label = label, Category = cat, Build = build, Defaults = defaults ?? Array.Empty<double>(), Handles = handles };
        Defs.Add(d);
        ById[id] = d;
    }

    /// <summary>Poignee sur une part horizontale du cadre (reglage = part de la largeur), sur la ligne y (part de la hauteur).</summary>
    static AdjustHandle XHandle(int i, double yFrac, double min, double max, Func<double, double>? toX = null, Func<double, double>? fromX = null) => new(
        s => (s.X + s.W * (toX ?? (v => v))(Adj(s, i)), s.Y + s.H * yFrac),
        (s, x, _) => SetAdjust(s, i, Math.Clamp((fromX ?? (v => v))(Frac(x, s.X, s.W)), min, max)));

    static (double X, double Y) P(Box b, double fx, double fy) => (b.Px(fx), b.Py(fy));

    static PathBuilder Poly(Box b, params (double X, double Y)[] fr) => new PathBuilder().Poly(fr.Select(p => P(b, p.X, p.Y)));

    static ShapeLibrary()
    {
        // ------------------------------------------------------------ formes de base
        Add(ShapeGeometry.Rect, "Rectangle", Basic, (b, _, _) => Poly(b, (0, 0), (1, 0), (1, 1), (0, 1)));
        Add(ShapeGeometry.RoundRect, "Rectangle à coins arrondis", Basic, (b, _, r) => RoundRect(new PathBuilder(), b, Radius(b, r)), null,
            new AdjustHandle(s => (s.X + Radius(Box.Of(s), s.CornerRadius), s.Y),
                (s, x, _) => s.CornerRadius = Math.Clamp(x - s.X, 0, Math.Min(s.W, s.H) / 2)));
        Add(ShapeGeometry.Ellipse, "Ellipse / cercle", Basic, (b, _, _) => new PathBuilder().Ellipse(b.Cx, b.Cy, b.W / 2, b.H / 2));
        Add(ShapeGeometry.Triangle, "Triangle", Basic, (b, a, _) => Poly(b, (a[0], 0), (1, 1), (0, 1)), new[] { 0.5 }, XHandle(0, 0, 0, 1));
        Add("rightTriangle", "Triangle rectangle", Basic, (b, _, _) => Poly(b, (0, 0), (1, 1), (0, 1)));
        Add(ShapeGeometry.Diamond, "Losange", Basic, (b, _, _) => Poly(b, (0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)));
        Add("parallelogram", "Parallélogramme", Basic, (b, a, _) => Poly(b, (a[0], 0), (1, 0), (1 - a[0], 1), (0, 1)), new[] { 0.25 }, XHandle(0, 0, 0, 0.95));
        Add("trapezoid", "Trapèze", Basic, (b, a, _) => Poly(b, (a[0], 0), (1 - a[0], 0), (1, 1), (0, 1)), new[] { 0.25 }, XHandle(0, 0, 0, 0.5));
        Add("pentagon", "Pentagone", Basic, (b, _, _) => Poly(b, (0.5, 0), (1, 0.38), (0.81, 1), (0.19, 1), (0, 0.38)));
        Add(ShapeGeometry.Hexagon, "Hexagone", Basic, (b, a, _) => Poly(b, (a[0], 0), (1 - a[0], 0), (1, 0.5), (1 - a[0], 1), (a[0], 1), (0, 0.5)),
            new[] { 0.25 }, XHandle(0, 0, 0, 0.5));
        Add("octagon", "Octogone", Basic, (b, a, _) =>
        {
            double c = a[0] * b.Min;
            return new PathBuilder().Poly((b.X + c, b.Y), (b.X + b.W - c, b.Y), (b.X + b.W, b.Y + c), (b.X + b.W, b.Y + b.H - c),
                (b.X + b.W - c, b.Y + b.H), (b.X + c, b.Y + b.H), (b.X, b.Y + b.H - c), (b.X, b.Y + c));
        }, new[] { 0.29 }, new AdjustHandle(s => (s.X + Adj(s, 0) * Math.Min(s.W, s.H), s.Y),
            (s, x, _) => SetAdjust(s, 0, Math.Clamp((x - s.X) / Math.Max(1e-6, Math.Min(s.W, s.H)), 0, 0.5))));
        Add("plus", "Croix", Basic, (b, a, _) =>
        {
            double t = a[0];
            return Poly(b, (t, 0), (1 - t, 0), (1 - t, t), (1, t), (1, 1 - t), (1 - t, 1 - t), (1 - t, 1), (t, 1), (t, 1 - t), (0, 1 - t), (0, t), (t, t));
        }, new[] { 0.3 }, XHandle(0, 0, 0.02, 0.49));
        Add("frame", "Cadre", Basic, (b, a, _) =>
        {
            double t = Math.Min(a[0] * b.Min, b.Min / 2 - 0.1);
            return new PathBuilder().Poly((b.X, b.Y), (b.X + b.W, b.Y), (b.X + b.W, b.Y + b.H), (b.X, b.Y + b.H))
                .Poly((b.X + t, b.Y + t), (b.X + t, b.Y + b.H - t), (b.X + b.W - t, b.Y + b.H - t), (b.X + b.W - t, b.Y + t));
        }, new[] { 0.12 }, new AdjustHandle(s => (s.X + Adj(s, 0) * Math.Min(s.W, s.H), s.Y + Adj(s, 0) * Math.Min(s.W, s.H)),
            (s, x, _) => SetAdjust(s, 0, Math.Clamp((x - s.X) / Math.Max(1e-6, Math.Min(s.W, s.H)), 0.01, 0.49))));
        Add("donut", "Anneau", Basic, (b, a, _) =>
        {
            double t = a[0] * b.Min / 2;
            return new PathBuilder().Ellipse(b.Cx, b.Cy, b.W / 2, b.H / 2).Ellipse(b.Cx, b.Cy, Math.Max(0.1, b.W / 2 - t), Math.Max(0.1, b.H / 2 - t));
        }, new[] { 0.4 }, new AdjustHandle(s => (s.X + Adj(s, 0) * Math.Min(s.W, s.H) / 2, s.Y + s.H / 2),
            (s, x, _) => SetAdjust(s, 0, Math.Clamp((x - s.X) / Math.Max(1e-6, Math.Min(s.W, s.H) / 2), 0.05, 0.95))));
        Add(ShapeGeometry.Heart, "Cœur", Basic, (b, _, _) =>
        {
            var p = new PathBuilder().M(b.Px(0.5), b.Py(1));
            p.C(b.Px(-0.1), b.Py(0.55), b.Px(0.05), b.Py(-0.1), b.Px(0.5), b.Py(0.25));
            p.C(b.Px(0.95), b.Py(-0.1), b.Px(1.1), b.Py(0.55), b.Px(0.5), b.Py(1));
            return p.Z();
        });
        Add("lightning", "Éclair", Basic, (b, _, _) =>
        {
            // contour de la forme « lightningBolt » d'Office (unites de 21600)
            var pts = new (double, double)[] { (8472, 0), (12860, 6080), (11050, 6797), (16577, 12007), (14767, 12877), (21600, 21600),
                (10012, 14915), (12222, 13987), (5022, 9705), (7602, 8382), (0, 3890) };
            return Poly(b, pts.Select(p => (p.Item1 / 21600, p.Item2 / 21600)).ToArray());
        });
        Add("sun", "Soleil", Basic, (b, a, _) =>
        {
            double r = a[0], rb = r + 0.07;
            var p = new PathBuilder().Ellipse(b.Cx, b.Cy, b.W * r, b.H * r);
            for (int k = 0; k < 8; k++)
            {
                double t = k * Math.PI / 4, d = 0.16;
                p.Poly((b.Cx + b.W * 0.5 * Math.Cos(t), b.Cy + b.H * 0.5 * Math.Sin(t)),
                    (b.Cx + b.W * rb * Math.Cos(t + d), b.Cy + b.H * rb * Math.Sin(t + d)),
                    (b.Cx + b.W * rb * Math.Cos(t - d), b.Cy + b.H * rb * Math.Sin(t - d)));
            }
            return p;
        }, new[] { 0.25 }, new AdjustHandle(s => (s.X + s.W * (0.5 + Adj(s, 0)), s.Y + s.H / 2),
            (s, x, _) => SetAdjust(s, 0, Math.Clamp(Frac(x, s.X, s.W) - 0.5, 0.1, 0.36))));
        Add("moon", "Lune", Basic, (b, a, _) =>
        {
            // croissant : moitie gauche d'une ellipse, creusee par une ellipse plus etroite
            var p = new PathBuilder().Arc(b.X + b.W, b.Cy, b.W, b.H / 2, 270, -180, move: true);
            p.Arc(b.X + b.W, b.Cy, b.W * (1 - a[0]), b.H / 2, 90, 180);
            return p.Z();
        }, new[] { 0.5 }, XHandle(0, 0.5, 0.05, 0.95));
        Add("cloud", "Nuage", Basic, (b, _, _) => Cloud(b));
        Add("smiley", "Visage souriant", Basic, (b, a, _) =>
        {
            var p = new PathBuilder().Ellipse(b.Cx, b.Cy, b.W / 2, b.H / 2);
            p.Ellipse(b.Px(0.35), b.Py(0.37), b.W * 0.06, b.H * 0.08).Ellipse(b.Px(0.65), b.Py(0.37), b.W * 0.06, b.H * 0.08);
            // sourire : croissant entre deux courbes (a[0] : courbure, negative = moue)
            double y0 = b.Py(0.66), k = a[0] * b.H;
            p.M(b.Px(0.27), y0).Q(b.Px(0.5), y0 + k * 2, b.Px(0.73), y0).Q(b.Px(0.5), y0 + k * 2 + b.H * 0.06 * Math.Sign(k == 0 ? 1 : k), b.Px(0.27), y0).Z();
            return p;
        }, new[] { 0.08 }, new AdjustHandle(s => (s.X + s.W / 2, s.Y + s.H * (0.66 + Adj(s, 0))),
            (s, _, y) => SetAdjust(s, 0, Math.Clamp(Frac(y, s.Y, s.H) - 0.66, -0.12, 0.14))));

        // ------------------------------------------------------------ fleches
        // a[0] : epaisseur du corps (part de la largeur de la fleche), a[1] : longueur de la pointe (part de la longueur)
        var arrowDefaults = new[] { 0.4, 0.4 };
        Add(ShapeGeometry.Arrow, "Flèche droite", Arrows, (b, a, _) => Arrow(b, a, 0), arrowDefaults, ArrowHandle(0));
        Add("arrowLeft", "Flèche gauche", Arrows, (b, a, _) => Arrow(b, a, 180), arrowDefaults, ArrowHandle(180));
        Add("arrowUp", "Flèche haut", Arrows, (b, a, _) => Arrow(b, a, 270), arrowDefaults, ArrowHandle(270));
        Add("arrowDown", "Flèche bas", Arrows, (b, a, _) => Arrow(b, a, 90), arrowDefaults, ArrowHandle(90));
        Add("arrowLeftRight", "Double flèche gauche-droite", Arrows, (b, a, _) =>
        {
            double t0 = (1 - a[0]) / 2, t1 = 1 - t0, h = a[1];
            return Poly(b, (0, 0.5), (h, 0), (h, t0), (1 - h, t0), (1 - h, 0), (1, 0.5), (1 - h, 1), (1 - h, t1), (h, t1), (h, 1));
        }, new[] { 0.4, 0.25 }, new AdjustHandle(s => (s.X + s.W * Adj(s, 1), s.Y + s.H * (1 - Adj(s, 0)) / 2), (s, x, y) =>
        {
            SetAdjust(s, 1, Math.Clamp(Frac(x, s.X, s.W), 0.02, 0.5));
            SetAdjust(s, 0, Math.Clamp(1 - 2 * Frac(y, s.Y, s.H), 0.02, 1));
        }));
        Add("arrowUpDown", "Double flèche haut-bas", Arrows, (b, a, _) =>
        {
            double t0 = (1 - a[0]) / 2, t1 = 1 - t0, h = a[1];
            return Poly(b, (0.5, 0), (1, h), (t1, h), (t1, 1 - h), (1, 1 - h), (0.5, 1), (0, 1 - h), (t0, 1 - h), (t0, h), (0, h));
        }, new[] { 0.4, 0.25 }, new AdjustHandle(s => (s.X + s.W * (1 - Adj(s, 0)) / 2, s.Y + s.H * Adj(s, 1)), (s, x, y) =>
        {
            SetAdjust(s, 1, Math.Clamp(Frac(y, s.Y, s.H), 0.02, 0.5));
            SetAdjust(s, 0, Math.Clamp(1 - 2 * Frac(x, s.X, s.W), 0.02, 1));
        }));
        Add("arrowQuad", "Flèche quadruple", Arrows, (b, a, _) =>
        {
            double t = a[0] / 2, h = 0.22, w = 0.2;          // demi-epaisseur, longueur et demi-largeur des pointes
            double c0 = 0.5 - t, c1 = 0.5 + t, hw0 = 0.5 - w, hw1 = 0.5 + w;
            return Poly(b, (0.5, 0), (hw1, h), (c1, h), (c1, c0), (1 - h, c0), (1 - h, hw0), (1, 0.5), (1 - h, hw1), (1 - h, c1), (c1, c1),
                (c1, 1 - h), (hw1, 1 - h), (0.5, 1), (hw0, 1 - h), (c0, 1 - h), (c0, c1), (h, c1), (h, hw1), (0, 0.5), (h, hw0), (h, c0), (c0, c0), (c0, h), (hw0, h));
        }, new[] { 0.16 }, new AdjustHandle(s => (s.X + s.W * (0.5 + Adj(s, 0) / 2), s.Y + s.H * 0.22),
            (s, x, _) => SetAdjust(s, 0, Math.Clamp(2 * (Frac(x, s.X, s.W) - 0.5), 0.02, 0.38))));
        Add("chevron", "Chevron", Arrows, (b, a, _) => Poly(b, (0, 0), (1 - a[0], 0), (1, 0.5), (1 - a[0], 1), (0, 1), (a[0], 0.5)),
            new[] { 0.3 }, XHandle(0, 0, 0, 0.95, v => 1 - v, f => 1 - f));
        Add("homePlate", "Pentagone (flèche)", Arrows, (b, a, _) => Poly(b, (0, 0), (1 - a[0], 0), (1, 0.5), (1 - a[0], 1), (0, 1)),
            new[] { 0.3 }, XHandle(0, 0, 0, 1, v => 1 - v, f => 1 - f));
        Add("notchedArrow", "Flèche à encoche", Arrows, (b, a, _) =>
        {
            double t0 = (1 - a[0]) / 2, t1 = 1 - t0, x0 = 1 - a[1], n = Math.Min(x0 * 0.9, a[0] / 2 * b.H / Math.Max(1e-6, b.W));
            return Poly(b, (0, t0), (x0, t0), (x0, 0), (1, 0.5), (x0, 1), (x0, t1), (0, t1), (n, 0.5));
        }, arrowDefaults, ArrowHandle(0));
        Add("curvedArrow", "Flèche courbe", Arrows, (b, a, _) => ArcArrow(b, a[0], 180, 145), new[] { 0.22 });
        Add("circularArrow", "Flèche circulaire", Arrows, (b, a, _) => ArcArrow(b, a[0], 150, 280), new[] { 0.18 });

        // ------------------------------------------------------------ etoiles et bannieres
        foreach (var (n, depth) in new[] { (4, 0.38), (5, 0.382), (6, 0.577), (8, 0.7), (10, 0.75), (12, 0.75), (16, 0.8), (24, 0.82), (32, 0.85) })
        {
            int branches = n;
            Add(n == 5 ? ShapeGeometry.Star : $"star{n}", $"Étoile à {n} branches", Stars, (b, a, _) => Star(b, branches, a[0]), new[] { depth },
                new AdjustHandle(s =>
                {
                    double t = -Math.PI / 2 + Math.PI / branches, k = Adj(s, 0);
                    return (s.X + s.W / 2 + Math.Cos(t) * s.W / 2 * k, s.Y + s.H / 2 + Math.Sin(t) * s.H / 2 * k);
                }, (s, x, y) =>
                {
                    double dx = Frac(x, s.X + s.W / 2, s.W / 2), dy = Frac(y, s.Y + s.H / 2, s.H / 2);
                    SetAdjust(s, 0, Math.Clamp(Math.Sqrt(dx * dx + dy * dy), 0.05, 0.98));
                }));
        }
        Add("explosion", "Explosion", Stars, (b, _, _) =>
        {
            // eclat irregulier (rayons fixes : la forme ne change pas d'un dessin a l'autre)
            double[] r = { 1, 0.55, 0.92, 0.5, 0.8, 0.58, 1, 0.47, 0.86, 0.56, 0.95, 0.5, 0.78, 0.6, 0.9, 0.45 };
            var pts = r.Select((k, i) => { double t = -Math.PI / 2 + i * 2 * Math.PI / r.Length + (i % 3 - 1) * 0.08; return (0.5 + 0.5 * k * Math.Cos(t), 0.5 + 0.5 * k * Math.Sin(t)); });
            return new PathBuilder().Fit(new PathBuilder().Poly(pts), b);
        });
        Add("ribbon", "Ruban", Stars, (b, a, _) =>
            Poly(b, (0, 0), (1, 0), (1 - a[0], 0.5), (1, 1), (0, 1), (a[0], 0.5)),
            new[] { 0.12 }, XHandle(0, 0.5, 0, 0.45));
        Add("wave", "Bannière ondulée", Stars, (b, a, _) =>
        {
            double k = a[0];
            var p = new PathBuilder().M(b.Px(0), b.Py(k));
            p.C(b.Px(0.33), b.Py(k - 2 * k), b.Px(0.67), b.Py(k + 2 * k), b.Px(1), b.Py(k));
            p.L(b.Px(1), b.Py(1 - k));
            p.C(b.Px(0.67), b.Py(1 - k + 2 * k), b.Px(0.33), b.Py(1 - k - 2 * k), b.Px(0), b.Py(1 - k));
            return p.Z();
        }, new[] { 0.12 }, new AdjustHandle(s => (s.X, s.Y + s.H * Adj(s, 0)),
            (s, _, y) => SetAdjust(s, 0, Math.Clamp(Frac(y, s.Y, s.H), 0, 0.25))));

        // ------------------------------------------------------------ bulles et legendes
        // a[0], a[1] : pointe (part du cadre ; hors du cadre pour une legende)
        var tip = new AdjustHandle(s => (s.X + s.W * Adj(s, 0), s.Y + s.H * Adj(s, 1)), (s, x, y) =>
        {
            SetAdjust(s, 0, Math.Clamp(Frac(x, s.X, s.W), -2, 3));
            SetAdjust(s, 1, Math.Clamp(Frac(y, s.Y, s.H), -2, 3));
        });
        Add(ShapeGeometry.Bubble, "Bulle de dialogue", Callouts, (b, a, _) =>
            Callout(new Box(b.X, b.Y, b.W, b.H * 0.8), (b.Px(a[0]), b.Py(a[1])), Math.Min(b.W, b.H * 0.8) * 0.2), new[] { 0.18, 1.0 }, tip);
        Add("rectCallout", "Légende rectangulaire", Callouts, (b, a, _) => Callout(b, (b.Px(a[0]), b.Py(a[1])), 0), new[] { 0.2, 1.3 }, tip);
        Add("roundCallout", "Légende arrondie", Callouts, (b, a, _) => Callout(b, (b.Px(a[0]), b.Py(a[1])), b.Min * 0.18), new[] { 0.2, 1.3 }, tip);
        Add("ovalCallout", "Légende ovale", Callouts, (b, a, _) => OvalCallout(b, (b.Px(a[0]), b.Py(a[1]))), new[] { 0.2, 1.3 }, tip);
        Add("cloudCallout", "Bulle de pensée", Callouts, (b, a, _) => CloudCallout(b, (b.Px(a[0]), b.Py(a[1]))), new[] { 0.15, 1.35 }, tip);
        Add("lineCallout", "Légende avec trait", Callouts, (b, a, _) =>
        {
            var p = new PathBuilder().Poly((b.X, b.Y), (b.X + b.W, b.Y), (b.X + b.W, b.Y + b.H), (b.X, b.Y + b.H));
            var (tx, ty) = (b.Px(a[0]), b.Py(a[1]));
            var (ex, ey) = EdgePoint(b, tx, ty);
            return p.M(ex, ey).L(tx, ty);
        }, new[] { -0.3, 1.4 }, tip);
    }

    public static double Radius(Box b, double? r) => Math.Min(r ?? b.Min * 0.15, b.Min / 2);

    static PathBuilder RoundRect(PathBuilder p, Box b, double r)
    {
        if (r <= 0.01) return p.Poly((b.X, b.Y), (b.X + b.W, b.Y), (b.X + b.W, b.Y + b.H), (b.X, b.Y + b.H));
        p.M(b.X + r, b.Y).L(b.X + b.W - r, b.Y);
        p.Arc(b.X + b.W - r, b.Y + r, r, r, 270, 90);
        p.L(b.X + b.W, b.Y + b.H - r);
        p.Arc(b.X + b.W - r, b.Y + b.H - r, r, r, 0, 90);
        p.L(b.X + r, b.Y + b.H);
        p.Arc(b.X + r, b.Y + b.H - r, r, r, 90, 90);
        p.L(b.X, b.Y + r);
        p.Arc(b.X + r, b.Y + r, r, r, 180, 90);
        return p.Z();
    }

    static PathBuilder Star(Box b, int n, double inner)
    {
        var pts = new (double, double)[n * 2];
        for (int i = 0; i < n * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / n, k = i % 2 == 0 ? 1 : inner;
            pts[i] = (b.Cx + Math.Cos(a) * b.W / 2 * k, b.Cy + Math.Sin(a) * b.H / 2 * k);
        }
        return new PathBuilder().Poly(pts);
    }

    /// <summary>Fleche pleine orientee (0 = vers la droite, 90 = vers le bas...) dans le cadre.</summary>
    static PathBuilder Arrow(Box b, double[] a, int dir)
    {
        double t0 = (1 - a[0]) / 2, t1 = 1 - t0, x0 = 1 - a[1];
        var unit = new (double U, double V)[] { (0, t0), (x0, t0), (x0, 0), (1, 0.5), (x0, 1), (x0, t1), (0, t1) };
        return Poly(b, unit.Select(p => Orient(p.U, p.V, dir)).ToArray());
    }

    /// <summary>Repere de la fleche (u le long de la fleche, v en travers) vers les parts du cadre.</summary>
    static (double X, double Y) Orient(double u, double v, int dir) => dir switch
    {
        180 => (1 - u, v),
        90 => (v, u),
        270 => (v, 1 - u),
        _ => (u, v),
    };

    static AdjustHandle ArrowHandle(int dir) => new(s =>
    {
        // coin de la pointe cote corps : (debut de la pointe, bord du corps)
        double t0 = (1 - Adj(s, 0)) / 2, x0 = 1 - Adj(s, 1);
        var (fx, fy) = Orient(x0, t0, dir);
        return (s.X + s.W * fx, s.Y + s.H * fy);
    }, (s, x, y) =>
    {
        double fx = Frac(x, s.X, s.W), fy = Frac(y, s.Y, s.H);
        (double u, double v) = dir switch { 180 => (1 - fx, fy), 90 => (fy, fx), 270 => (1 - fy, fx), _ => (fx, fy) };
        SetAdjust(s, 1, Math.Clamp(1 - u, 0.02, 0.98));
        SetAdjust(s, 0, Math.Clamp(1 - 2 * v, 0.02, 1));
    });

    /// <summary>Fleche en arc (bande le long d'une ellipse terminee par une pointe), etiree dans le cadre.</summary>
    static PathBuilder ArcArrow(Box b, double thickness, double startDeg, double sweepDeg)
    {
        double ro = 1, ri = 1 - thickness * 2, t1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p = new PathBuilder().Arc(0, 0, ro, ro, startDeg, sweepDeg, move: true);
        // pointe : base elargie de part et d'autre de la bande, sommet dans le sens du parcours
        double mid = (ro + ri) / 2, half = (ro - ri) / 2, head = half * 2.1;
        double ex = Math.Cos(t1), ey = Math.Sin(t1), tx = -Math.Sin(t1), ty = Math.Cos(t1);
        p.L((mid + head) * ex, (mid + head) * ey);
        p.L(mid * ex + tx * head * 1.6, mid * ey + ty * head * 1.6);
        p.L((mid - head) * ex, (mid - head) * ey);
        p.Arc(0, 0, ri, ri, startDeg + sweepDeg, -sweepDeg);
        p.Z();
        return new PathBuilder().Fit(p, b);
    }

    /// <summary>Nuage : bosses en courbes de Bezier quadratiques autour d'une ellipse, etire dans le cadre.</summary>
    static PathBuilder Cloud(Box b)
    {
        double[] jitter = { 1, 0.86, 1.08, 0.92, 1.04, 0.88, 1.1, 0.9, 1, 0.95 };
        int n = jitter.Length;
        var p = new PathBuilder();
        (double X, double Y) Pt(int i) { double t = -Math.PI / 2 + i * 2 * Math.PI / n; return (Math.Cos(t), Math.Sin(t) * 0.8); }
        var first = Pt(0);
        p.M(first.X, first.Y);
        for (int i = 0; i < n; i++)
        {
            var q = Pt(i + 1);
            double tm = -Math.PI / 2 + (i + 0.5) * 2 * Math.PI / n, k = 1.45 * jitter[i];
            p.Q(Math.Cos(tm) * k, Math.Sin(tm) * 0.8 * k, q.X, q.Y);
        }
        p.Z();
        return new PathBuilder().Fit(p, b);
    }

    /// <summary>Point du bord du cadre dans la direction de la pointe, depuis le centre.</summary>
    static (double X, double Y) EdgePoint(Box b, double tx, double ty)
    {
        double dx = (tx - b.Cx) / (b.W / 2), dy = (ty - b.Cy) / (b.H / 2), m = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (m < 1e-9) return (b.Cx, b.Y + b.H);
        return (b.Cx + dx / m * b.W / 2, b.Cy + dy / m * b.H / 2);
    }

    /// <summary>
    /// Bulle rectangulaire (coins arrondis de rayon r) avec une pointe vers (tx, ty) ; la base de la pointe est posee
    /// sur le bord tourne vers la pointe, centree entre la pointe et le centre. Pointe dans la bulle : pas de pointe.
    /// </summary>
    static PathBuilder Callout(Box b, (double X, double Y) tip, double r)
    {
        r = Math.Min(r, b.Min / 2);
        double dx = (tip.X - b.Cx) / (b.W / 2), dy = (tip.Y - b.Cy) / (b.H / 2);
        int edge = Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 ? -1
                 : Math.Abs(dy) >= Math.Abs(dx) ? (dy > 0 ? 2 : 0) : (dx > 0 ? 1 : 3);   // 0 haut, 1 droite, 2 bas, 3 gauche
        double midX = (tip.X + b.Cx) / 2, midY = (tip.Y + b.Cy) / 2;
        double bwH = Math.Max(4, b.W * 0.16), bwV = Math.Max(4, b.H * 0.16);
        double Cl(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);
        var p = new PathBuilder().M(b.X + r, b.Y);
        double x0 = b.X, y0 = b.Y, x1 = b.X + b.W, y1 = b.Y + b.H;
        if (edge == 0) { double c = Cl(midX, x0 + r + bwH / 2, x1 - r - bwH / 2); p.L(c - bwH / 2, y0).L(tip.X, tip.Y).L(c + bwH / 2, y0); }
        p.L(x1 - r, y0);
        if (r > 0.01) p.Arc(x1 - r, y0 + r, r, r, 270, 90);
        if (edge == 1) { double c = Cl(midY, y0 + r + bwV / 2, y1 - r - bwV / 2); p.L(x1, c - bwV / 2).L(tip.X, tip.Y).L(x1, c + bwV / 2); }
        p.L(x1, y1 - r);
        if (r > 0.01) p.Arc(x1 - r, y1 - r, r, r, 0, 90);
        if (edge == 2) { double c = Cl(midX, x0 + r + bwH / 2, x1 - r - bwH / 2); p.L(c + bwH / 2, y1).L(tip.X, tip.Y).L(c - bwH / 2, y1); }
        p.L(x0 + r, y1);
        if (r > 0.01) p.Arc(x0 + r, y1 - r, r, r, 90, 90);
        if (edge == 3) { double c = Cl(midY, y0 + r + bwV / 2, y1 - r - bwV / 2); p.L(x0, c + bwV / 2).L(tip.X, tip.Y).L(x0, c - bwV / 2); }
        p.L(x0, y0 + r);
        if (r > 0.01) p.Arc(x0 + r, y0 + r, r, r, 180, 90);
        return p.Z();
    }

    static PathBuilder OvalCallout(Box b, (double X, double Y) tip)
    {
        double dx = (tip.X - b.Cx) / (b.W / 2), dy = (tip.Y - b.Cy) / (b.H / 2);
        if (dx * dx + dy * dy <= 1) return new PathBuilder().Ellipse(b.Cx, b.Cy, b.W / 2, b.H / 2);
        double t = Math.Atan2(dy, dx) * 180 / Math.PI, d = 12;
        var p = new PathBuilder().M(tip.X, tip.Y);
        p.Arc(b.Cx, b.Cy, b.W / 2, b.H / 2, t + d, 360 - 2 * d);
        return p.Z();
    }

    static PathBuilder CloudCallout(Box b, (double X, double Y) tip)
    {
        var p = Cloud(b);
        double dx = (tip.X - b.Cx) / (b.W / 2), dy = (tip.Y - b.Cy) / (b.H / 2), m = Math.Sqrt(dx * dx + dy * dy);
        if (m <= 1) return p;
        // petites bulles entre le nuage et la pointe
        var edge = (X: b.Cx + dx / m * b.W / 2, Y: b.Cy + dy / m * b.H / 2);
        foreach (var (f, k) in new[] { (0.25, 0.09), (0.6, 0.06), (1.0, 0.035) })
        {
            double cx = edge.X + (tip.X - edge.X) * f, cy = edge.Y + (tip.Y - edge.Y) * f;
            p.Ellipse(cx, cy, b.Min * k * 1.2, b.Min * k);
        }
        return p;
    }

    /// <summary>Remplace le contenu par le trace <paramref name="src"/> etire dans le cadre.</summary>
    static PathBuilder Fit(this PathBuilder dst, PathBuilder src, Box b)
    {
        dst.Segments.AddRange(PathTools.FitTo(src.Segments, b));
        return dst;
    }
}
