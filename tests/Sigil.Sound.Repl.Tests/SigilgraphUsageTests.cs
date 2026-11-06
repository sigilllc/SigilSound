namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// When the page offers Sigilgraph mode and when it shows the keys for <c>Poly</c>. Like the keyboard's test, it is about tokens:
/// a mention in a comment or a string, or a member of something else, must not count.
/// </summary>
public class SigilgraphUsageTests
{
    [Theory]
    [InlineData("In.Audio.LowPass(800)")]
    [InlineData("Silence; Out.Control(0, Sine(2))")]
    [InlineData("Sine(Knob(\"Rate\", 2, 0.1, 10))")]
    [InlineData("Saw(110) * Slider(\"Level\", 0.5, 0, 1)")]
    [InlineData("Sample(Asset(\"Kick\"))")]
    [InlineData("Poly(8, v => Saw(v.Frequency))")]
    public void The_device_API_is_recognised(string code) => Assert.True(SigilgraphUsage.Uses(code));

    [Theory]
    [InlineData("Saw(110).LowPass(800)")]
    [InlineData("// Knob(\"x\", 1, 0, 2) is a Sigilgraph call\nSaw(110)")]
    [InlineData("var s = \"In.Audio\"; Saw(110)")]
    [InlineData("var knob = Knob; Saw(110)")]
    [InlineData("settings.Knob(1); Saw(110)")]
    [InlineData("var Input = 1; Saw(110 * Input)")]
    [InlineData("Keys.Play(v => Sine(v.Frequency))")]
    public void Other_snippets_are_not(string code) => Assert.False(SigilgraphUsage.Uses(code));

    [Theory]
    [InlineData("Saw(Knob(\"A\", 1, 0, 2))", true)]
    [InlineData("In.Audio", true)]
    [InlineData("Out.Control(0, Sine(1)); Silence", true)]
    [InlineData("Poly(8, v => Saw(v.Frequency))", false)]   // notes only: the keyboard, no empty strip
    [InlineData("Saw(110)", false)]
    public void A_snippet_gets_the_controls_strip_only_when_it_declares_a_faceplate(string code, bool expected) =>
        Assert.Equal(expected, SigilgraphUsage.UsesFaceplate(code));

    [Theory]
    [InlineData("Poly(4, v => Sine(v.Frequency))", true)]
    [InlineData("var p = Poly(4, v => Sine(v.Frequency)); p * 0.3", true)]
    [InlineData("// Poly(4, ...) plays the keys\nSaw(110)", false)]
    [InlineData("pattern.Poly(4)", false)]
    [InlineData("Saw(110)", false)]
    public void Poly_shows_the_keys(string code, bool expected) => Assert.Equal(expected, SigilgraphUsage.UsesPoly(code));
}
