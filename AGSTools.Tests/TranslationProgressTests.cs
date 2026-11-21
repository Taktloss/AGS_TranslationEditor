namespace AGSTools.Tests;

public class TranslationProgressTests
{
    private static readonly TextOrigin Room2Kevin = new("RoomScript", "room2.crm", "Kevin");
    private static readonly TextOrigin Room10Rosa = new("RoomScript", "room10.crm", "Rosa");
    private static readonly TextOrigin Room10Hotspot = new("Hotspot", "room10.crm", null);
    private static readonly TextOrigin GuiButton = new("Gui", "btnQuit", null);

    private static readonly (TextOrigin?, bool)[] Texts =
    {
        (Room2Kevin, true), (Room2Kevin, false),
        (Room10Rosa, true), (Room10Hotspot, true), (Room10Hotspot, false),
        (GuiButton, true), (new("Gui", "btnSave", null), false),
        (null, false),
    };

    [Fact]
    public void Groups_by_place_in_natural_order_with_unknown_last()
    {
        var groups = TranslationProgress.Compute(Texts, ProgressGrouping.Place);

        // All GUI controls are one group
        Assert.Equal(new[] { "Gui", "room2.crm", "room10.crm", "" }, groups.Select(g => g.Name));
        Assert.Equal(2, groups[0].Total);
        ProgressGroup room10 = groups.Single(g => g.Name == "room10.crm");
        Assert.Equal((3, 2, 1), (room10.Total, room10.Translated, room10.Open));
        Assert.Equal(200.0 / 3, room10.Percent, 6);
    }

    [Fact]
    public void Groups_by_speaker_with_texts_without_one_last()
    {
        var groups = TranslationProgress.Compute(Texts, ProgressGrouping.Speaker);

        Assert.Equal(new[] { "Kevin", "Rosa", "" }, groups.Select(g => g.Name));
        Assert.Equal(5, groups[^1].Total); // hotspot x2, gui x2, unknown
    }

    [Theory]
    [InlineData("room9.crm", "room10.crm")]
    [InlineData("Room1", "room2")]
    [InlineData("dDialog2", "dDialog012")]
    public void Numbers_are_ordered_by_value(string first, string second)
    {
        var texts = new (TextOrigin?, bool)[] { (new("Script", second, null), true), (new("Script", first, null), true) };
        Assert.Equal(new[] { first, second }, TranslationProgress.Compute(texts, ProgressGrouping.Place).Select(g => g.Name));
    }
}
