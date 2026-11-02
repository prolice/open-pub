using OpenPub.Core.Geometry;
using OpenPub.Core.Model;

namespace OpenPub.Tests;

/// <summary>Lot 12 : texte le long d'un trace libre.</summary>
public class TextPathTests
{
    [Fact]
    public void Les_lettres_suivent_le_trace()
    {
        var adv = Enumerable.Repeat(0.6, 10).ToList();
        // trace en V : descend puis remonte
        var path = new List<(double X, double Y)> { (0, 0), (100, 100), (200, 0) };
        var lay = TextOnPath.Layout(adv, 0.7, 0.2, WordArtWarps.Path, 200, 100, path);
        Assert.Equal(10, lay.Glyphs.Count);
        Assert.True(lay.Size > 0);
        Assert.True(lay.Glyphs.First().AngleDeg > 0 && lay.Glyphs.Last().AngleDeg < 0);    // descend puis remonte
        Assert.All(lay.Glyphs, g => Assert.InRange(Math.Abs(g.Y - (g.X <= 100 ? g.X : 200 - g.X)), 0, 0.01));   // sur le trace
        Assert.True(10 * 0.6 * lay.Size <= Math.Sqrt(2) * 200 * 0.97);                    // le texte tient sur la longueur
        // sans trace : texte droit
        Assert.Equal(10, TextOnPath.Layout(adv, 0.7, 0.2, WordArtWarps.Path, 200, 100).Glyphs.Count);
    }
}
