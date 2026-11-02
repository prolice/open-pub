using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenPub.Core.Model;

namespace OpenPub.Core.Io;

/// <summary>Lecture / ecriture de document.json.</summary>
public static class DocumentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    public static PubDocument Parse(string json)
    {
        // "meta" contient des valeurs heterogenes : on la lit a part, en texte.
        using var jd = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var doc = JsonSerializer.Deserialize<PubDocument>(StripMeta(jd.RootElement), Options)
                  ?? throw new InvalidDataException("document.json vide");
        if (jd.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
            doc.Meta = meta.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
        Normalize(doc);
        return doc;
    }

    static string StripMeta(JsonElement root)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in root.EnumerateObject())
                if (p.Name != "meta") p.WriteTo(w);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public static PubDocument Load(string path) => Parse(File.ReadAllText(path));

    public static string Serialize(PubDocument doc, bool indented = true) =>
        JsonSerializer.Serialize(doc, indented ? Options : Compact);

    public static void Save(PubDocument doc, string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(doc), new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Copie profonde (utilisee par l'historique annuler/retablir).</summary>
    public static PubDocument Clone(PubDocument doc) => Parse(Serialize(doc, indented: false));

    /// <summary>Valeurs par defaut et coherence apres chargement.</summary>
    static void Normalize(PubDocument doc)
    {
        foreach (var p in doc.Pages)
        {
            if (p.Width <= 0) p.Width = doc.PageWidth;
            if (p.Height <= 0) p.Height = doc.PageHeight;
            foreach (var s in p.Shapes)
            {
                if (s.IsText) s.Paragraphs ??= new List<Paragraph>();
                if (s.Columns < 1) s.Columns = 1;
            }
        }
    }
}
