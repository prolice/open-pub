using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>
/// Styles de texte et themes du document : styles de base, appliquer un style, creer un style d'apres un texte,
/// modifier un style (mise a jour de tout le texte qui l'utilise), jeux de couleurs et de polices.
/// </summary>
public static class StyleOps
{
    /// <summary>Styles proposes dans un document qui n'en a pas encore.</summary>
    public static List<TextStyle> Defaults(string body = "Arial", string heading = "Georgia") => new()
    {
        new() { Id = "normal", Name = "Normal", FontRole = "body", Font = new FontSpec { Family = body, Size = 11, Color = "#1f2a44" }, SpaceAfter = 4 },
        new() { Id = "titre1", Name = "Titre 1", FontRole = "heading", Font = new FontSpec { Family = heading, Size = 24, Bold = true, Color = "#111c43" }, SpaceAfter = 6 },
        new() { Id = "titre2", Name = "Titre 2", FontRole = "heading", Font = new FontSpec { Family = heading, Size = 16, Bold = true, Color = "#4e32cf" }, SpaceBefore = 6, SpaceAfter = 3 },
        new() { Id = "chapeau", Name = "Chapeau", FontRole = "body", Font = new FontSpec { Family = body, Size = 13, Italic = true, Color = "#52617e" }, SpaceAfter = 6 },
        new() { Id = "citation", Name = "Citation", FontRole = "body", Font = new FontSpec { Family = body, Size = 12, Italic = true, Color = "#4e32cf" }, Align = "center", SpaceBefore = 6, SpaceAfter = 6 },
        new() { Id = "legende", Name = "Légende", FontRole = "body", Font = new FontSpec { Family = body, Size = 9, Italic = true, Color = "#52617e" } },
        new() { Id = "accent", Name = "Mise en valeur", Kind = "character", Font = new FontSpec { Bold = true, Color = "#9935f4" } },
    };

    /// <summary>Styles du document (crees avec les styles de base au premier usage).</summary>
    public static List<TextStyle> Ensure(PubDocument doc) =>
        doc.Styles ??= Defaults(doc.Theme?.BodyFont ?? "Arial", doc.Theme?.HeadingFont ?? "Georgia");

    public static TextStyle? Find(PubDocument doc, string? id) => id is null ? null : doc.Styles?.FirstOrDefault(s => s.Id == id);

    /// <summary>Recopie les valeurs du style de caractere dans une police (seules les valeurs definies changent).</summary>
    public static void ApplyFont(TextStyle st, FontSpec f)
    {
        if (st.Font is not { } sf) return;
        if (sf.Family is not null) f.Family = sf.Family;
        if (sf.Size is not null) f.Size = sf.Size;
        if (st.Kind == "paragraph" || sf.Bold) f.Bold = sf.Bold;
        if (st.Kind == "paragraph" || sf.Italic) f.Italic = sf.Italic;
        if (st.Kind == "paragraph" || sf.Underline) f.Underline = sf.Underline;
        if (sf.Color is not null) f.Color = sf.Color;
    }

    /// <summary>Applique un style de paragraphe : reglages du paragraphe et police de tout son texte.</summary>
    public static void ApplyParagraphStyle(Paragraph p, TextStyle st)
    {
        p.Style = st.Id;
        if (st.Align is not null) p.Align = st.Align;          // un style sans alignement garde celui du paragraphe
        p.LineHeight = st.LineHeight;
        p.SpaceBefore = st.SpaceBefore;
        p.SpaceAfter = st.SpaceAfter;
        if (p.List is null) { p.IndentLeft = st.IndentLeft; p.FirstIndent = st.FirstIndent; }
        foreach (var r in p.Runs)
        {
            r.Font = (r.Font ?? TableOps.DefaultFont).Clone();
            ApplyFont(st, r.Font);
        }
    }

    /// <summary>Applique un style a une plage de texte : de paragraphe (paragraphes touches) ou de caractere (selection).</summary>
    public static void Apply(List<Paragraph> paras, TextPos a, TextPos b, TextStyle st)
    {
        if (st.Kind == "character")
        {
            var from = StoryEditor.Clamp(paras, TextPos.Min(a, b));
            var to = StoryEditor.Clamp(paras, TextPos.Max(a, b));
            StoryEditor.ApplyFont(paras, from, to, f => ApplyFont(st, f));
            MarkRuns(paras, from, to, st.Id);
            return;
        }
        StoryEditor.ApplyParagraph(paras, a, b, p => ApplyParagraphStyle(p, st));
    }

    /// <summary>Memorise le style de caractere sur les segments de la plage (deja decoupes par ApplyFont).</summary>
    static void MarkRuns(List<Paragraph> paras, TextPos from, TextPos to, string id)
    {
        for (int pi = from.Para; pi <= to.Para; pi++)
        {
            int s = pi == from.Para ? from.Offset : 0, e = pi == to.Para ? to.Offset : StoryEditor.Length(paras[pi]);
            int acc = 0;
            foreach (var r in paras[pi].Runs)
            {
                if (acc >= s && acc + r.Text.Length <= e && r.Text.Length > 0) r.Style = id;
                acc += r.Text.Length;
            }
        }
    }

    /// <summary>Nouveau style d'apres un paragraphe et sa police.</summary>
    public static TextStyle FromText(PubDocument doc, string name, string kind, Paragraph p, FontSpec font)
    {
        var styles = Ensure(doc);
        var baseId = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (baseId.Length == 0) baseId = "style";
        var id = baseId;
        for (int i = 2; styles.Any(s => s.Id == id); i++) id = baseId + i;
        var st = new TextStyle
        {
            Id = id, Name = name, Kind = kind,
            Font = new FontSpec { Family = font.Family, Size = font.Size, Bold = font.Bold, Italic = font.Italic, Underline = font.Underline, Color = font.Color },
        };
        if (kind == "paragraph")
        {
            st.Align = p.Align; st.LineHeight = p.LineHeight; st.SpaceBefore = p.SpaceBefore; st.SpaceAfter = p.SpaceAfter;
            st.IndentLeft = p.IndentLeft; st.FirstIndent = p.FirstIndent;
        }
        styles.Add(st);
        return st;
    }

    /// <summary>Tous les paragraphes du document (zones, cellules de tableau, pages maitres).</summary>
    public static IEnumerable<Paragraph> AllParagraphs(PubDocument doc)
    {
        foreach (var s in doc.AllShapes())
        {
            if (s.Paragraphs is { } ps) foreach (var p in ps) yield return p;
            if (s.Table is { } t)
                foreach (var c in t.Cells.SelectMany(r => r))
                    if (c.Paragraphs is { } cps) foreach (var p in cps) yield return p;
        }
    }

    /// <summary>Le style a change : tout le texte qui l'utilise est mis a jour ; renvoie le nombre de paragraphes ou segments touches.</summary>
    public static int Reapply(PubDocument doc, TextStyle st)
    {
        int n = 0;
        foreach (var p in AllParagraphs(doc))
        {
            if (st.Kind == "paragraph" && p.Style == st.Id)
            {
                ApplyParagraphStyle(p, st);
                // les styles de caractere du paragraphe restent au-dessus
                foreach (var r in p.Runs) if (Find(doc, r.Style) is { Kind: "character" } cs) ApplyFont(cs, r.Font!);
                n++;
            }
            else if (st.Kind == "character")
                foreach (var r in p.Runs.Where(r => r.Style == st.Id))
                {
                    r.Font = (r.Font ?? TableOps.DefaultFont).Clone();
                    ApplyFont(st, r.Font);
                    n++;
                }
        }
        return n;
    }

    /// <summary>Supprime un style : le texte garde sa mise en forme, sans lien avec le style.</summary>
    public static void Delete(PubDocument doc, string id)
    {
        doc.Styles?.RemoveAll(s => s.Id == id);
        foreach (var p in AllParagraphs(doc))
        {
            if (p.Style == id) p.Style = null;
            foreach (var r in p.Runs) if (r.Style == id) r.Style = null;
        }
    }

    // ---------------------------------------------------------------- jeux de couleurs et de polices

    /// <summary>Jeux de couleurs : principale, accent 1 a 4.</summary>
    public static readonly (string Name, string[] Colors)[] ColorSchemes =
    {
        ("OpenPub", new[] { "#111c43", "#087aff", "#9935f4", "#06c7c0", "#f5a524" }),
        ("Océan", new[] { "#0b2a4a", "#1565c0", "#00838f", "#4fc3f7", "#ffb74d" }),
        ("Forêt", new[] { "#1b3a2b", "#2e7d32", "#7cb342", "#a1887f", "#f9a825" }),
        ("Coucher de soleil", new[] { "#3e1f47", "#e65100", "#d81b60", "#ffb300", "#5e35b1" }),
        ("Pastel", new[] { "#4a4e69", "#9fa8da", "#f48fb1", "#80cbc4", "#ffe082" }),
        ("Classique", new[] { "#000000", "#c00000", "#1f4e79", "#7f7f7f", "#bf9000" }),
        ("École", new[] { "#263238", "#e53935", "#1e88e5", "#43a047", "#fdd835" }),
    };

    /// <summary>Jeux de polices : titres, texte (polices fournies avec Windows).</summary>
    public static readonly (string Name, string Heading, string Body)[] FontSchemes =
    {
        ("Georgia / Arial", "Georgia", "Arial"),
        ("Cambria / Calibri", "Cambria", "Calibri"),
        ("Segoe UI Semibold / Segoe UI", "Segoe UI Semibold", "Segoe UI"),
        ("Trebuchet MS / Verdana", "Trebuchet MS", "Verdana"),
        ("Arial Black / Arial", "Arial Black", "Arial"),
        ("Garamond / Garamond", "Garamond", "Garamond"),
        ("Comic Sans MS / Comic Sans MS", "Comic Sans MS", "Comic Sans MS"),
    };

    /// <summary>Toutes les couleurs ecrites dans le document (remplissages, contours, texte, tableaux, styles, arriere-plans).</summary>
    static IEnumerable<(Func<string?> Get, Action<string> Set)> ColorSlots(PubDocument doc)
    {
        foreach (var page in doc.Pages.Concat(doc.Masters ?? new List<Page>()))
        {
            var pg = page;
            yield return (() => pg.BackColor, v => pg.BackColor = v);
        }
        foreach (var s in doc.AllShapes())
        {
            var sh = s;
            if (sh.Fill is { } f) yield return (() => f.Color, v => f.Color = v);
            if (sh.Line is { } l) yield return (() => l.Color, v => l.Color = v);
            if (sh.Table is { } t)
            {
                if (t.Border is { } tb) yield return (() => tb.Color, v => tb.Color = v);
                foreach (var c in t.Cells.SelectMany(r => r))
                {
                    if (c.Fill is { } cf) yield return (() => cf.Color, v => cf.Color = v);
                    foreach (var b in new[] { c.Top, c.Right, c.Bottom, c.Left }) if (b is { } bb) yield return (() => bb.Color, v => bb.Color = v);
                }
            }
        }
        foreach (var p in AllParagraphs(doc))
            foreach (var r in p.Runs)
                if (r.Font is { } rf)
                {
                    yield return (() => rf.Color, v => rf.Color = v);
                    yield return (() => rf.Highlight, v => rf.Highlight = v);
                }
        foreach (var st in doc.Styles ?? new List<TextStyle>())
            if (st.Font is { } sf) yield return (() => sf.Color, v => sf.Color = v);
    }

    /// <summary>
    /// Change le jeu de couleurs : chaque couleur du jeu precedent est remplacee partout par la couleur de meme rang
    /// du nouveau jeu. Renvoie le nombre de couleurs remplacees.
    /// </summary>
    public static int ApplyColorScheme(PubDocument doc, string name, IReadOnlyList<string> colors)
    {
        var old = doc.Theme?.Colors is { Count: > 0 } oc ? oc : ColorSchemes[0].Colors.ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Math.Min(old.Count, colors.Count); i++) if (!old[i].Equals(colors[i], StringComparison.OrdinalIgnoreCase)) map[old[i]] = colors[i];
        int n = 0;
        foreach (var (get, set) in ColorSlots(doc))
            if (get() is { } c && map.TryGetValue(c, out var nc)) { set(nc); n++; }
        doc.Theme ??= new DocTheme();
        doc.Theme.Name = name;
        doc.Theme.Colors = colors.ToList();
        return n;
    }

    /// <summary>Change le jeu de polices : les polices de titres et de texte sont remplacees partout (styles compris).</summary>
    public static int ApplyFontScheme(PubDocument doc, string heading, string body)
    {
        var oldH = doc.Theme?.HeadingFont ?? "Georgia";
        var oldB = doc.Theme?.BodyFont ?? "Arial";
        int n = 0;
        foreach (var p in AllParagraphs(doc))
            foreach (var r in p.Runs)
            {
                if (r.Font?.Family is not { } fam) continue;
                if (fam.Equals(oldH, StringComparison.OrdinalIgnoreCase)) { r.Font.Family = heading; n++; }
                else if (fam.Equals(oldB, StringComparison.OrdinalIgnoreCase)) { r.Font.Family = body; n++; }
            }
        foreach (var st in doc.Styles ?? new List<TextStyle>())
            if (st.Font is { } sf && st.FontRole is { } role) sf.Family = role == "heading" ? heading : body;
        doc.Theme ??= new DocTheme { Colors = ColorSchemes[0].Colors.ToList() };
        doc.Theme.HeadingFont = heading;
        doc.Theme.BodyFont = body;
        return n;
    }
}
