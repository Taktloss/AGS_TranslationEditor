namespace AGSTools.Tests;

public class TextEscapingTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData(@"Line1\nLine2", "Line1\nLine2")]
    [InlineData(@"Tab\there", "Tab\there")]
    [InlineData(@"Say \""hi\""", "Say \"hi\"")]
    [InlineData(@"It\'s", "It's")]
    [InlineData(@"back\\slash", @"back\slash")]
    [InlineData(@"\[bracket", @"\[bracket")]        // old-style escaped bracket stays
    [InlineData(@"\q unknown", "q unknown")]        // unknown escapes drop the backslash
    [InlineData(@"trailing\", @"trailing\")]
    public void ToRuntime_matches_ags_compiler(string trs, string runtime)
    {
        Assert.Equal(runtime, TextEscaping.ToRuntime(trs));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("Line1\nLine2\r\tEnd")]
    [InlineData(@"back\slash")]
    [InlineData(@"\[bracket")]
    [InlineData(@"\\[double")]
    [InlineData("Say \"hi\"")]
    [InlineData("\a\b\f\v")]
    public void ToTrs_is_inverse_of_ToRuntime(string runtime)
    {
        Assert.Equal(runtime, TextEscaping.ToRuntime(TextEscaping.ToTrs(runtime)));
    }

    [Fact]
    public void ToTrs_keeps_quotes_readable()
    {
        Assert.Equal("Say \"hi\"", TextEscaping.ToTrs("Say \"hi\""));
    }

    [Theory]
    [InlineData("&12 Hello[world", "Hello\nworld")]
    [InlineData("&3 Hi", "Hi")]
    [InlineData("A \\[bracket]", "A [bracket]")]
    [InlineData("&amp; more", "&amp; more")]
    [InlineData("Plain", "Plain")]
    public void ToDisplay_drops_the_voice_prefix_and_breaks_lines_at_brackets(string runtime, string expected) =>
        Assert.Equal(expected, TextEscaping.ToDisplay(runtime));
}
