namespace Sigil.Sound.Repl.Tests;

/// <summary>The page shows its keyboard only for snippets that use <c>Keys</c>; this decides which those are.</summary>
public class KeyboardUsageTests
{
    [Theory]
    [InlineData("Keys.Play(v => Sine(v.Frequency))")]
    [InlineData("var synth = Keys.Play(v => Saw(v.Frequency));\nsynth * 0.3")]
    [InlineData("var k = Keys;\nk.Play(v => Sine(v.Frequency))")]
    [InlineData("ReplHost.ReplApi.Keys.Play(v => Sine(v.Frequency))")]
    [InlineData("ReplApi.Keys.Play(v => Sine(v.Frequency))")]
    [InlineData("// held chords\nKeys.Play(v => Sine(v.Frequency)) // live")]
    [InlineData("Sine(440);\nKeys.NoteOn(60);\nKeys.Play(v => Sine(v.Frequency))")]
    public void Snippets_that_use_Keys_show_the_keyboard(string code) => Assert.True(KeyboardUsage.Uses(code));

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("Sine(440)")]
    [InlineData("// Keys is mentioned in a comment only\nSine(440)")]
    [InlineData("/* Keys.Play(...) */ Sine(440)")]
    [InlineData("var note = \"Keys.Play\";\nSine(note.Length)")]
    [InlineData("var myKeys = 3;\nvar KeysDown = 2;\nSine(myKeys * 100)")]
    [InlineData("var map = new Dictionary<int, int>();\nSine(map.Keys.Count + 440)")]
    [InlineData("Notes.Parse(\"C4 E4\").Select(n => n.Frequency).Count()")]
    public void Snippets_that_do_not_use_Keys_hide_it(string code) => Assert.False(KeyboardUsage.Uses(code));

    [Fact]
    public void Only_the_live_note_examples_use_Keys()
    {
        var users = ReplExamples.All.Where(e => KeyboardUsage.Uses(e.Code)).Select(e => e.Name).ToArray();

        Assert.Equal(["Live notes (Keys)", "Falling notes (Keys)"], users);
    }

    [Fact]
    public void Unfinished_code_does_not_throw()
    {
        // Typing produces half-written text all the time; the check runs on every pause.
        Assert.False(KeyboardUsage.Uses("Sine(\"unterminated"));
        Assert.True(KeyboardUsage.Uses("Keys.Play(v => Sine(v.Freq"));
        Assert.False(KeyboardUsage.Uses("/* never closed Keys"));
    }
}
