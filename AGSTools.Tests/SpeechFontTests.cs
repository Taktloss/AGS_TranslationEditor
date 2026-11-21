using System.Text;

namespace AGSTools.Tests;

public class SpeechFontTests
{
    private static List<(string, int)> Calls(byte[] script)
    {
        using var reader = new AgsReader(new MemoryStream(script), Encoding.Latin1);
        return CompiledScript.Read(reader).FindNumberCalls("Game::set_SpeechFont", "SetSpeechFont").ToList();
    }

    [Fact]
    public void Finds_the_number_a_script_sets_the_speech_font_to()
    {
        byte[] script = new ScriptBuilder()
            .Section("GlobalScript.asc")
            .Line(1).SetNumber("Game::set_NormalFont", 3)
            .Line(2).SetNumber("Game::set_SpeechFont", 7)
            .Line(3).Call("Display", "Hello", 5)
            .Line(4).SetNumber("SetSpeechFont", 2)
            .Build();

        Assert.Equal(new[] { ("Game::set_SpeechFont", 7), ("SetSpeechFont", 2) }, Calls(script));
    }

    [Fact]
    public void A_number_from_an_earlier_line_is_not_taken()
    {
        // Line 2 sets the font from a variable: nothing is known about the value
        byte[] script = new ScriptBuilder()
            .Line(1).Call("Display", "x", 7)
            .Line(2).CallWithoutNumber("Game::set_SpeechFont")
            .Build();

        Assert.Empty(Calls(script));
    }
}
