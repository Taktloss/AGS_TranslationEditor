namespace AGSTools.Tests;

public class SpriteUsesTests
{
    [Fact]
    public void Gui_backgrounds_and_button_images_are_found()
    {
        var builder = new GameDataBuilder();
        builder.Guis.Add(("gMenu", 511));
        builder.Guis.Add(("gEmpty", 0));
        builder.Buttons.AddRange(new[] { "New Game", "Quit" });
        builder.ButtonImages.Add((298, 299, 0));
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());

        var uses = SpriteUses.Find(game);

        Assert.Equal(new[] { "GUI gMenu: background" }, uses[511]);
        Assert.Equal(new[] { "GUI button btn0" }, uses[298]);
        Assert.Equal(new[] { "GUI button btn0 (mouse over)" }, uses[299]);
        Assert.Equal(3, uses.Count); // no image (0) is not a use
        // The button texts are still read after the images
        Assert.Contains(TextExtractor.Extract(game).Texts, t => t.Text == "Quit");
    }
}
