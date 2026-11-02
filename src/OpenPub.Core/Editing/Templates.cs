using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>
/// Modeles de documents et blocs de construction fournis avec OpenPub, construits en code (aucun fichier,
/// libres de droits) : lettre d'information, affiche, carte postale, invitation ; encadre, bandeau, coupon...
/// </summary>
public static class Templates
{
    public const double A4W = 595.276, A4H = 841.89, A5W = 419.528, A5H = 595.276, A6W = 297.638, A6H = 419.528;

    static FontSpec F(string family, double size, string color = "#1f2a44", bool bold = false, bool italic = false) =>
        new() { Family = family, Size = size, Color = color, Bold = bold, Italic = italic, Lang = "fr" };

    static List<Paragraph> P(FontSpec font, string align, params string[] lines) =>
        lines.Select(l => new Paragraph { Align = align, SpaceAfter = 4, Runs = { new Run { Text = l, Font = font.Clone() } } }).ToList();

    static Shape Text(double x, double y, double w, double h, List<Paragraph> paras, string? fill = null, string? autofit = "none") => new()
    {
        Kind = ShapeKind.Text, X = x, Y = y, W = w, H = h, Padding = new[] { 5.7, 5.7, 5.7, 5.7 }, Paragraphs = paras, AutoFit = autofit,
        Fill = fill is null ? null : new Fill { Color = fill },
    };

    static Shape Box(double x, double y, double w, double h, string fill, string? geometry = ShapeGeometry.Rect, double? radius = null) => new()
    {
        Kind = geometry == ShapeGeometry.Ellipse ? ShapeKind.Ellipse : ShapeKind.Rect, Geometry = geometry, X = x, Y = y, W = w, H = h,
        Fill = new Fill { Color = fill }, CornerRadius = radius,
    };

    /// <summary>Cadre en attente d'une photo (a remplacer par Format ▸ Remplacer l'image, ou a supprimer).</summary>
    static Shape PhotoSlot(double x, double y, double w, double h) => Text(x, y, w, h,
        P(F("Arial", 11, "#52617e", italic: true), "center", "Emplacement de la photo", "(insérez une image ici)"), "#e9eef8").Middle();

    static Shape Middle(this Shape s) { s.VAlign = "middle"; return s; }

    static List<Paragraph> Lorem(FontSpec font, int sentences)
    {
        var s = "Remplacez ce texte par le vôtre : il s'écoule d'une colonne à l'autre et se coupe en fin de ligne selon les règles du français.";
        return P(font, "justify", string.Join(" ", Enumerable.Repeat(s, Math.Max(1, sentences))));
    }

    static void Renumber(Page p) { for (int i = 0; i < p.Shapes.Count; i++) p.Shapes[i].Z = i + 1; }

    static PubDocument NewDoc(double w, double h, int pages, string source)
    {
        var d = new PubDocument { PageWidth = w, PageHeight = h, Format = "openpub/2", Source = source, Margins = new[] { 36.0, 36.0, 36.0, 36.0 } };
        for (int i = 0; i < pages; i++) d.Pages.Add(new Page { Index = i + 1, Width = w, Height = h });
        return d;
    }

    /// <summary>Modeles fournis : identifiant, nom, description, fabrique.</summary>
    public static readonly (string Id, string Name, string Description, Func<PubDocument> Build)[] BuiltIn =
    {
        ("newsletter", "Lettre d'information", "A4, 2 pages : bandeau, articles sur deux colonnes liées, photo, agenda, numéros de page.", Newsletter),
        ("affiche", "Affiche", "A4 : grand titre, photo, informations pratiques.", Poster),
        ("carte", "Carte postale", "A6 paysage, recto (photo) et verso (message et adresse).", Postcard),
        ("invitation", "Invitation", "A5 : titre décoratif, date, lieu, réponse souhaitée.", Invitation),
    };

    static PubDocument Newsletter()
    {
        var d = NewDoc(A4W, A4H, 2, "Lettre d'information");
        var p1 = d.Pages[0];
        p1.Shapes.Add(Box(0, 0, A4W, 120, "#087aff"));
        p1.Shapes.Add(Text(36, 26, 523, 70, P(F("Georgia", 34, "#ffffff", bold: true), "left", "La Lettre"), autofit: "shrink"));
        p1.Shapes.Add(Text(36, 86, 523, 26, P(F("Arial", 11, "#dbeaff"), "left", "Numéro 1 · " + DateTime.Today.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")))));
        p1.Shapes.Add(Text(36, 140, 523, 36, P(F("Georgia", 20, "#111c43", bold: true), "left", "À la une : le titre de votre article")));
        p1.Shapes.Add(PhotoSlot(36, 184, 523, 200));
        var left = Text(36, 396, 254, 400, Lorem(F("Arial", 10.5), 26), autofit: null);
        var right = Text(305, 396, 254, 400, new List<Paragraph>(), autofit: null);
        left.Chain = right.Chain = "c1"; left.ChainIndex = 0; right.ChainIndex = 1;
        p1.Shapes.Add(left); p1.Shapes.Add(right);
        Renumber(p1);
        var p2 = d.Pages[1];
        p2.Shapes.Add(Text(36, 36, 330, 36, P(F("Georgia", 18, "#4e32cf", bold: true), "left", "Deuxième article")));
        p2.Shapes.Add(Text(36, 78, 330, 330, Lorem(F("Arial", 10.5), 6), autofit: null));
        p2.Shapes.Add(PhotoSlot(36, 420, 330, 220));
        p2.Shapes.Add(Text(386, 36, 173, 600, P(F("Arial", 10.5), "left", "Agenda", "", "Lundi : …", "Mercredi : …", "Samedi : …"), "#f3e8ff"));
        p2.Shapes[^1].Paragraphs![0].Runs[0].Font!.Bold = true;
        p2.Shapes[^1].Paragraphs![0].Runs[0].Font!.Size = 14;
        Renumber(p2);
        // numero de page sur une page maitre
        var master = new Page { Id = "A", Name = "Page maître", Index = 1, Width = A4W, Height = A4H };
        var num = new List<Paragraph> { new() { Align = "center", Runs = { new Run { Text = "#", Font = F("Arial", 9, "#52617e"), Field = "page" } } } };
        master.Shapes.Add(new Shape { Kind = ShapeKind.Text, X = A4W / 2 - 40, Y = A4H - 28, W = 80, H = 16, Paragraphs = num, AutoFit = "none" });
        d.Masters = new List<Page> { master };
        return d;
    }

    static PubDocument Poster()
    {
        var d = NewDoc(A4W, A4H, 1, "Affiche");
        var p = d.Pages[0];
        p.BackColor = "#fff8e7";
        p.Shapes.Add(Text(36, 40, 523, 120, P(F("Georgia", 48, "#9935f4", bold: true), "center", "Grand titre"), autofit: "shrink"));
        p.Shapes.Add(Text(36, 160, 523, 40, P(F("Arial", 18, "#111c43"), "center", "Le sous-titre de l'évènement")));
        p.Shapes.Add(PhotoSlot(66, 220, 463, 330));
        p.Shapes.Add(Box(66, 580, 463, 190, "#087aff", ShapeGeometry.RoundRect, 18));
        p.Shapes.Add(Text(86, 596, 423, 160, P(F("Arial", 16, "#ffffff", bold: true), "center", "Samedi 1er janvier · 14 h", "Salle des fêtes", "Entrée libre"), autofit: "shrink").Middle());
        Renumber(p);
        return d;
    }

    static PubDocument Postcard()
    {
        var d = NewDoc(A6H, A6W, 2, "Carte postale");
        var recto = d.Pages[0];
        recto.Shapes.Add(PhotoSlot(0, 0, A6H, A6W));
        recto.Shapes.Add(Text(20, A6W - 70, A6H - 40, 50, P(F("Georgia", 26, "#ffffff", bold: true), "left", "Bons baisers !")));
        Renumber(recto);
        var verso = d.Pages[1];
        verso.Shapes.Add(new Shape { Kind = ShapeKind.Path, X = A6H / 2, Y = 20, W = 0.1, H = A6W - 40, Line = new LineStyle { Color = "#9aa3b5", Weight = 0.75 },
            Path = new List<PathSegment> { new() { A = "M", X = A6H / 2, Y = 20 }, new() { A = "L", X = A6H / 2, Y = A6W - 20 } } });
        verso.Shapes.Add(Text(16, 20, A6H / 2 - 30, A6W - 40, P(F("Comic Sans MS", 11), "left", "Votre message…")));
        verso.Shapes.Add(Box(A6H - 70, 20, 50, 60, "#e9eef8"));
        for (int i = 0; i < 4; i++)
            verso.Shapes.Add(new Shape { Kind = ShapeKind.Path, X = A6H / 2 + 16, Y = 130 + i * 30, W = A6H / 2 - 36, H = 0.1, Line = new LineStyle { Color = "#9aa3b5", Weight = 0.5 },
                Path = new List<PathSegment> { new() { A = "M", X = A6H / 2 + 16, Y = 130 + i * 30 }, new() { A = "L", X = A6H - 20, Y = 130 + i * 30 } } });
        Renumber(verso);
        return d;
    }

    static PubDocument Invitation()
    {
        var d = NewDoc(A5W, A5H, 1, "Invitation");
        var p = d.Pages[0];
        p.Shapes.Add(Box(20, 20, A5W - 40, A5H - 40, "#fdf2ff", ShapeGeometry.RoundRect, 24));
        p.Shapes[^1].Line = new LineStyle { Color = "#9935f4", Weight = 2 };
        p.Shapes.Add(Text(40, 60, A5W - 80, 70, P(F("Georgia", 30, "#9935f4", bold: true, italic: true), "center", "Vous êtes invités !"), autofit: "shrink"));
        p.Shapes.Add(Text(40, 150, A5W - 80, 200, P(F("Arial", 13), "center", "à l'occasion de …", "", "le samedi … à … h", "à l'adresse …"), autofit: "shrink"));
        p.Shapes.Add(Box(A5W / 2 - 50, 380, 100, 100, "#06c7c0", ShapeGeometry.Star));
        p.Shapes.Add(Text(40, 500, A5W - 80, 50, P(F("Arial", 10, "#52617e", italic: true), "center", "Merci de répondre avant le …")));
        Renumber(p);
        return d;
    }

    // ---------------------------------------------------------------- blocs de construction

    /// <summary>Blocs fournis : identifiant, nom, fabrique (objets places en haut a gauche, a 0,0).</summary>
    public static readonly (string Id, string Name, Func<List<Shape>> Build)[] Blocks =
    {
        ("encadre", "Encadré coloré", () => new List<Shape>
        {
            Text(0, 0, 220, 140, P(F("Arial", 10.5), "left", "Le saviez-vous ?", "Un court texte mis en valeur dans un encadré."), "#eef5ff"),
        }.Also(l => { l[0].Geometry = ShapeGeometry.RoundRect; l[0].CornerRadius = 12; l[0].Paragraphs![0].Runs[0].Font!.Bold = true; l[0].Paragraphs[0].Runs[0].Font!.Color = "#087aff"; })),
        ("bandeau", "Bandeau de titre", () => new List<Shape>
        {
            Box(0, 0, 523, 60, "#9935f4", ShapeGeometry.RoundRect, 10),
            Text(14, 10, 495, 40, P(F("Georgia", 22, "#ffffff", bold: true), "left", "Titre de la rubrique")),
        }),
        ("citation", "Citation", () => new List<Shape>
        {
            Text(0, 0, 300, 90, P(F("Georgia", 15, "#4e32cf", italic: true), "center", "« Une phrase marquante, entre guillemets. »", "— Son auteur")),
        }),
        ("coupon", "Coupon-réponse", () => new List<Shape>
        {
            Text(0, 0, 300, 150, P(F("Arial", 10.5), "left", "Coupon-réponse", "Nom : ………………………………", "Prénom : ………………………………", "Participera : oui  /  non", "À rendre avant le …")),
        }.Also(l => { l[0].Line = new LineStyle { Color = "#52617e", Weight = 1, Dash = true }; l[0].Paragraphs![0].Runs[0].Font!.Bold = true; })),
        ("pastille", "Pastille « Nouveau »", () => new List<Shape>
        {
            Box(0, 0, 80, 80, "#f5a524", ShapeGeometry.Star),
            Text(10, 28, 60, 24, P(F("Arial", 10, "#111c43", bold: true), "center", "Nouveau !")),
        }),
        ("contact", "Bloc contact", () => new List<Shape>
        {
            Text(0, 0, 220, 100, P(F("Arial", 10), "left", "Contact", "Téléphone : …", "Courriel : …", "Site : …"), "#f3e8ff"),
        }.Also(l => l[0].Paragraphs![0].Runs[0].Font!.Bold = true)),
    };

    static List<Shape> Also(this List<Shape> l, Action<List<Shape>> a) { a(l); return l; }

    /// <summary>Bloc pret a coller : groupe (plusieurs objets) et presse-papiers OpenPub.</summary>
    public static ShapeClip BlockClip(string id)
    {
        var shapes = Blocks.First(b => b.Id == id).Build();
        if (shapes.Count > 1) ShapeOps.Group(shapes);
        return new ShapeClip { Shapes = shapes };
    }
}
