using System.Text.Json;
using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>Bords pour l'alignement de plusieurs objets.</summary>
public enum AlignEdge { Left, Center, Right, Top, Middle, Bottom }

/// <summary>
/// Operations sur un ensemble d'objets d'une page : alignement, repartition, groupes, ordre
/// d'empilement, copie (presse-papiers) avec renumerotation des chaines et des groupes.
/// Les coordonnees utilisees sont la boite englobante des objets tournes (<see cref="Shape.Bounds"/>).
/// </summary>
public static class ShapeOps
{
    /// <summary>Deplace un objet ; ses traces (chemin, detourage) suivent (meme logique que l'editeur).</summary>
    public static void Translate(Shape s, double dx, double dy)
    {
        static void Map(List<PathSegment>? path, double dx, double dy)
        {
            if (path is null) return;
            foreach (var seg in path)
            {
                if (seg.X is { } x) seg.X = x + dx;
                if (seg.Y is { } y) seg.Y = y + dy;
                if (seg.X1 is { } x1) seg.X1 = x1 + dx;
                if (seg.Y1 is { } y1) seg.Y1 = y1 + dy;
                if (seg.X2 is { } x2) seg.X2 = x2 + dx;
                if (seg.Y2 is { } y2) seg.Y2 = y2 + dy;
            }
        }
        Map(s.Path, dx, dy);
        Map(s.Clip, dx, dy);
        s.X += dx;
        s.Y += dy;
    }

    /// <summary>Boite englobante d'un ensemble d'objets (objets tournes compris).</summary>
    public static (double X, double Y, double W, double H) Bounds(IEnumerable<Shape> shapes)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var s in shapes)
        {
            var b = s.Bounds();
            x0 = Math.Min(x0, b.X); y0 = Math.Min(y0, b.Y);
            x1 = Math.Max(x1, b.X + b.W); y1 = Math.Max(y1, b.Y + b.H);
        }
        return x0 > x1 ? (0, 0, 0, 0) : (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Aligne les objets sur un bord de leur boite englobante commune, ou de la page (relativeTo).
    /// Les membres d'un meme groupe bougent ensemble (le groupe est aligne comme un seul objet).
    /// </summary>
    public static void Align(IReadOnlyList<Shape> shapes, AlignEdge edge, (double X, double Y, double W, double H)? relativeTo = null)
    {
        var units = Units(shapes);
        if (units.Count == 0) return;
        var box = relativeTo ?? Bounds(shapes);
        foreach (var u in units)
        {
            var b = Bounds(u);
            double dx = 0, dy = 0;
            switch (edge)
            {
                case AlignEdge.Left: dx = box.X - b.X; break;
                case AlignEdge.Center: dx = box.X + box.W / 2 - (b.X + b.W / 2); break;
                case AlignEdge.Right: dx = box.X + box.W - (b.X + b.W); break;
                case AlignEdge.Top: dy = box.Y - b.Y; break;
                case AlignEdge.Middle: dy = box.Y + box.H / 2 - (b.Y + b.H / 2); break;
                case AlignEdge.Bottom: dy = box.Y + box.H - (b.Y + b.H); break;
            }
            foreach (var s in u) Translate(s, dx, dy);
        }
    }

    /// <summary>Repartit les objets (ou groupes) pour que les espaces entre eux soient egaux (3 objets au moins).</summary>
    public static void Distribute(IReadOnlyList<Shape> shapes, bool horizontal)
    {
        var units = Units(shapes);
        if (units.Count < 3) return;
        var sorted = units.Select(u => (Unit: u, B: Bounds(u)))
            .OrderBy(t => horizontal ? t.B.X : t.B.Y).ToList();
        double start = horizontal ? sorted[0].B.X : sorted[0].B.Y;
        var last = sorted[^1].B;
        double end = horizontal ? last.X + last.W : last.Y + last.H;
        double total = sorted.Sum(t => horizontal ? t.B.W : t.B.H);
        double gap = (end - start - total) / (sorted.Count - 1);
        double pos = start;
        foreach (var (unit, b) in sorted)
        {
            double cur = horizontal ? b.X : b.Y;
            foreach (var s in unit) Translate(s, horizontal ? pos - cur : 0, horizontal ? 0 : pos - cur);
            pos += (horizontal ? b.W : b.H) + gap;
        }
    }

    /// <summary>Regroupe les objets par groupe (un objet sans groupe est seul dans son unite).</summary>
    static List<List<Shape>> Units(IReadOnlyList<Shape> shapes)
    {
        var units = new List<List<Shape>>();
        var byGroup = new Dictionary<string, List<Shape>>();
        foreach (var s in shapes)
        {
            if (s.Group is null) { units.Add(new List<Shape> { s }); continue; }
            if (!byGroup.TryGetValue(s.Group, out var l)) { l = new List<Shape>(); byGroup[s.Group] = l; units.Add(l); }
            l.Add(s);
        }
        return units;
    }

    /// <summary>Membres du groupe de l'objet sur la page (l'objet seul s'il n'est pas groupe).</summary>
    public static List<Shape> GroupOf(Page page, Shape s) =>
        s.Group is null ? new List<Shape> { s } : page.Shapes.Where(x => x.Group == s.Group).ToList();

    /// <summary>Groupe les objets (2 au moins) ; renvoie l'identifiant du nouveau groupe.</summary>
    public static string? Group(IReadOnlyList<Shape> shapes)
    {
        if (shapes.Count < 2) return null;
        var id = "g" + Guid.NewGuid().ToString("N")[..8];
        foreach (var s in shapes) s.Group = id;
        return id;
    }

    /// <summary>Dissocie : retire tous les objets de leur groupe.</summary>
    public static void Ungroup(IEnumerable<Shape> shapes)
    {
        foreach (var s in shapes) s.Group = null;
    }

    /// <summary>
    /// Avance (+1) ou recule (-1) les objets d'un plan : chacun passe au-dessus (au-dessous) de l'objet
    /// suivant (precedent) qui le chevauche et n'est pas selectionne ; les fonds de page restent dessous.
    /// </summary>
    public static void StepOrder(Page page, IReadOnlyCollection<Shape> shapes, int dir)
    {
        var list = page.Shapes;
        var ordered = (dir > 0 ? list.Where(shapes.Contains).Reverse() : list.Where(shapes.Contains)).ToList();
        int floor = list.FindLastIndex(x => x.Background) + 1;
        foreach (var s in ordered)
        {
            int i = list.IndexOf(s);
            var b = s.Bounds();
            // premier objet non selectionne qui chevauche, dans la direction voulue (a defaut : voisin immediat)
            int j = i + dir;
            while (j >= 0 && j < list.Count && (shapes.Contains(list[j]) || !Overlaps(b, list[j].Bounds()))) j += dir;
            if (j < 0 || j >= list.Count) j = i + dir;
            if (dir < 0 && j < floor) j = floor;
            if (j < 0 || j >= list.Count || j == i || shapes.Contains(list[j])) continue;
            // apres retrait de s, inserer en j le place juste au-dessus (avancer) ou juste au-dessous (reculer) de la cible
            list.RemoveAt(i);
            list.Insert(j, s);
        }
        for (int k = 0; k < list.Count; k++) list[k].Z = k + 1;
    }

    static bool Overlaps((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    // ---------------------------------------------------------------- presse-papiers

    /// <summary>
    /// Copie profonde d'objets pour le presse-papiers. Les zones d'une chaine dont la tete n'est pas copiee
    /// recoivent le texte de l'histoire ; les chaines et groupes sont renumerotes au collage.
    /// </summary>
    public static ShapeClip Copy(Page page, IEnumerable<Shape> shapes)
    {
        var sel = page.Shapes.Where(shapes.Contains).ToList();     // ordre d'empilement de la page
        var clip = new ShapeClip();
        foreach (var chain in sel.Where(s => s.Chain is not null).Select(s => s.Chain!).Distinct())
        {
            var frames = page.ChainFrames(chain);
            var copied = frames.Where(sel.Contains).ToList();
            if (!copied.Contains(frames[0]) && frames[0].Paragraphs is { } story)
                clip.Stories[chain] = Clone(story);
        }
        clip.Shapes = sel.Select(Clone).ToList();
        return clip;
    }

    /// <summary>Objets a coller : nouvelles chaines et nouveaux groupes, decales de offset points.</summary>
    public static List<Shape> Paste(ShapeClip clip, double offset)
    {
        var shapes = clip.Shapes.Select(Clone).ToList();
        var chainMap = new Dictionary<string, string>();
        var groupMap = new Dictionary<string, string>();
        foreach (var grp in shapes.Where(s => s.Chain is not null).GroupBy(s => s.Chain!))
        {
            var frames = grp.OrderBy(s => s.ChainIndex ?? 0).ToList();
            if (frames.Count == 1)
            {
                var only = frames[0];
                if ((only.ChainIndex ?? 0) != 0 && clip.Stories.TryGetValue(grp.Key, out var st)) only.Paragraphs = Clone(st);
                only.Chain = null; only.ChainIndex = null; only.FlowFrom = null;
                continue;
            }
            var id = "c" + Guid.NewGuid().ToString("N")[..8];
            chainMap[grp.Key] = id;
            if ((frames[0].ChainIndex ?? 0) != 0 && clip.Stories.TryGetValue(grp.Key, out var story)) frames[0].Paragraphs = Clone(story);
            for (int i = 0; i < frames.Count; i++)
            {
                frames[i].Chain = id;
                frames[i].ChainIndex = i;
                frames[i].FlowFrom = null;
                if (i > 0) frames[i].Paragraphs = new List<Paragraph>();
            }
        }
        foreach (var s in shapes)
        {
            if (s.Group is { } g)
            {
                if (!groupMap.TryGetValue(g, out var ng)) groupMap[g] = ng = "g" + Guid.NewGuid().ToString("N")[..8];
                s.Group = ng;
            }
            s.Background = false;
            if (offset != 0) Translate(s, offset, offset);
        }
        // un groupe reduit a un seul objet n'a plus de raison d'etre
        foreach (var g in shapes.Where(s => s.Group is not null).GroupBy(s => s.Group).Where(g => g.Count() == 1))
            foreach (var s in g) s.Group = null;
        return shapes;
    }

    static readonly JsonSerializerOptions Json = DocumentJson.Options;
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
}

/// <summary>Contenu du presse-papiers OpenPub : objets, histoires des chaines incompletes, images (base64).</summary>
public sealed class ShapeClip
{
    public const string Format = "OpenPub.Shapes.v1";
    public List<Shape> Shapes { get; set; } = new();
    public Dictionary<string, List<Paragraph>> Stories { get; set; } = new();
    /// <summary>Images referencees (chemin relatif "assets/..." -> contenu base64), pour coller dans un autre document.</summary>
    public Dictionary<string, string> Assets { get; set; } = new();
    /// <summary>Dossier de travail du document d'origine (meme document : pas besoin de recopier les images).</summary>
    public string? SourceWorkDir { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, DocumentJson.Options);
    public static ShapeClip? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ShapeClip>(json, DocumentJson.Options); }
        catch (JsonException) { return null; }
    }
}
