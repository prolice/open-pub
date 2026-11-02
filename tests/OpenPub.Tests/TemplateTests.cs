using OpenPub.Core.Editing;
using OpenPub.Core.Io;
using OpenPub.Core.Model;
using OpenPub.Layout;

namespace OpenPub.Tests;

/// <summary>Lot 11 : modeles et blocs de construction.</summary>
public class TemplateTests
{
    [Fact]
    public void Les_modeles_se_construisent_et_se_mettent_en_page()
    {
        var layouter = new PageLayouter(new TextLayoutEngine(new MonoMeasurer(), null));
        foreach (var (id, name, _, build) in Templates.BuiltIn)
        {
            var doc = build();
            Assert.NotEmpty(doc.Pages);
            Assert.All(doc.Pages, p => { Assert.True(p.Width > 100); layouter.LayoutPage(p, out _); });
            // relu tel qu'enregistre
            var back = DocumentJson.Parse(DocumentJson.Serialize(doc, indented: false));
            Assert.Equal(doc.Pages.Count, back.Pages.Count);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
        var news = Templates.BuiltIn.First(t => t.Id == "newsletter").Build();
        var chain = news.Pages[0].Shapes.Where(s => s.Chain is not null).ToList();
        Assert.Equal(2, chain.Count);                                // articles sur deux colonnes liees
        Assert.Contains(news.Masters![0].Shapes, s => s.Paragraphs?.Any(p => p.Runs.Any(r => r.Field == "page")) == true);
        Assert.Equal("openpub/2", PubFormat.Required(news));
    }

    [Fact]
    public void Les_blocs_se_collent()
    {
        foreach (var (id, _, _) in Templates.Blocks)
        {
            var clip = Templates.BlockClip(id);
            Assert.NotEmpty(clip.Shapes);
            if (clip.Shapes.Count > 1) Assert.All(clip.Shapes, s => Assert.Equal(clip.Shapes[0].Group, s.Group));
            var pasted = ShapeOps.Paste(ShapeClip.FromJson(clip.ToJson())!, 0);
            Assert.Equal(clip.Shapes.Count, pasted.Count);
        }
    }
}
