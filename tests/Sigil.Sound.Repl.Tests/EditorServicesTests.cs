namespace Sigil.Sound.Repl.Tests;

public class EditorServicesTests
{
    [Fact]
    public void Top_level_completion_offers_sources()
    {
        var items = EditorServices.Complete("Si", 2);

        var sine = Assert.Single(items, i => i.Label == "Sine");
        Assert.Equal("Method", sine.Kind);
        Assert.Equal("A sine wave. `frequency` is in Hz.", sine.Documentation);
    }

    [Fact]
    public void Member_completion_offers_fluent_operators()
    {
        const string code = "Saw(110).";
        var labels = EditorServices.Complete(code, code.Length).Select(i => i.Label).ToList();

        Assert.Contains("LowPass", labels);
        Assert.Contains("Adsr", labels);
        Assert.DoesNotContain("FromOperator", labels);
    }

    [Fact]
    public void Member_completion_works_mid_snippet_and_on_locals()
    {
        const string code = "var lfo = Sine(0.25);\nlfo.Ra\nSaw(110)";
        int offset = code.IndexOf("Ra", StringComparison.Ordinal) + 2;

        Assert.Contains(EditorServices.Complete(code, offset), i => i.Label == "Range");
    }

    [Fact]
    public void Static_member_completion_on_a_type()
    {
        const string code = "Notes.";
        var labels = EditorServices.Complete(code, code.Length).Select(i => i.Label).ToList();

        Assert.Contains("Parse", labels);
        Assert.Contains("ParsePitch", labels);
    }

    [Fact]
    public void Lambda_parameter_completion()
    {
        const string code = "Notes.Parse(\"C4\").Select(n => n.";
        var labels = EditorServices.Complete(code, code.Length).Select(i => i.Label).ToList();

        Assert.Contains("Frequency", labels);
        Assert.Contains("Transpose", labels);
    }

    [Fact]
    public void Signature_help_tracks_the_active_parameter()
    {
        const string code = "Saw(110).Adsr(0.01, ";
        var help = EditorServices.Signatures(code, code.Length);

        Assert.NotNull(help);
        var signature = help.Signatures[help.ActiveSignature];
        Assert.StartsWith("Adsr(", signature.Label);
        Assert.Equal(1, help.ActiveParameter);
        var decay = signature.Parameters[1];
        Assert.Equal("double decay", signature.Label[decay.Start..decay.End]);
    }

    [Fact]
    public void Crlf_offsets_are_normalized()
    {
        const string code = "var a = 1;\r\nSaw(110).";
        Assert.Contains(EditorServices.Complete(code, code.Length), i => i.Label == "LowPass");
    }

    [Fact]
    public void Signature_help_inside_auto_closed_parentheses()
    {
        const string code = "var lfo = Sine(0.25).Range(200, 2000);\nSaw(110).LowPass(lfo, )";
        var help = EditorServices.Signatures(code, code.Length - 1);

        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public void Describe_a_method_shows_signature_summary_and_parameters()
    {
        const string code = "Saw(110).LowPass(800)";
        var markdown = EditorServices.Describe(code, code.IndexOf("LowPass", StringComparison.Ordinal) + 2);

        Assert.NotNull(markdown);
        Assert.Contains("Sound LowPass(Param cutoff", markdown);
        Assert.Contains("A resonant two-pole low-pass filter.", markdown);
        Assert.Contains("- `cutoff`:", markdown);
    }

    [Fact]
    public void Describe_includes_the_example_from_the_docs()
    {
        const string code = "new Slot(Sine(220))";
        var markdown = EditorServices.Describe(code, code.IndexOf("Slot", StringComparison.Ordinal) + 1);

        Assert.NotNull(markdown);
        Assert.Contains("hot-swappable", markdown);
        Assert.Contains("**Example**", markdown);
        Assert.Contains("slot.Swap(Saw(220));", markdown);
    }

    [Fact]
    public void Describe_a_local_shows_its_type()
    {
        const string code = "var lfo = Sine(0.25);\nlfo";
        var markdown = EditorServices.Describe(code, code.Length - 1);

        Assert.NotNull(markdown);
        Assert.Contains("(local) Sound lfo", markdown);
    }

    [Fact]
    public void Describe_returns_null_away_from_symbols()
    {
        Assert.Null(EditorServices.Describe("Sine(440)", 6));
    }

    [Fact]
    public void Describe_the_sequence_API_from_the_notes_package()
    {
        const string code = "Notes.Parse(\"C4\").Every(Beats.Eighth).Play(v => Sine(v.Frequency))";
        var play = EditorServices.Describe(code, code.IndexOf("Play", StringComparison.Ordinal) + 1);
        var every = EditorServices.Describe(code, code.IndexOf("Every", StringComparison.Ordinal) + 1);

        Assert.NotNull(play);
        Assert.Contains("polyphony", play);
        Assert.Contains("**Example**", play);
        Assert.NotNull(every);
        Assert.Contains("- `gate`:", every);
    }
}
