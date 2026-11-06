using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

public class SnippetCompilerTests
{
    [Theory]
    [InlineData("Sine(440)")]
    [InlineData("Sine(440) * 0.5")]
    [InlineData("var f = 440;\nSine(f)")]
    [InlineData("var lfo = Sine(0.25).Range(200, 2000);\nSaw(110)\n    .LowPass(lfo, q: 0.7)\n    .Adsr(0.01, 0.2, 0.6, 1.5, hold: 3)")]
    [InlineData("return Sine(440);")]
    [InlineData("var notes = Notes.Parse(\"A4\").Select(n => n.Frequency).ToList();\nSine(notes[0])")]
    public void Snippets_compile_and_produce_a_sound(string code)
    {
        var result = SnippetCompiler.Compile(code);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        AudioAssert.IsFinite(result.Run().Render(0.1));
    }

    [Fact]
    public void Snippet_renders_what_it_describes()
    {
        var audio = SnippetCompiler.Compile("Sine(Notes.Parse(\"A4\")[0].Frequency)").Run().Render(1.0);

        AudioAssert.FrequencyNear(audio, 440, 0.5);
    }

    [Fact]
    public void Errors_map_to_the_users_line_and_column()
    {
        var result = SnippetCompiler.Compile("var x = 1;\nSine(440).Lowpass(500)");

        Assert.False(result.Success);
        var error = Assert.Single(result.Diagnostics, d => d.IsError);
        Assert.Equal(2, error.StartLine);
        Assert.Equal(11, error.StartColumn);
        Assert.Contains("Lowpass", error.Message);
    }

    [Fact]
    public void Missing_final_expression_gets_a_friendly_error()
    {
        var result = SnippetCompiler.Compile("var x = Sine(440);");

        Assert.Contains(result.Diagnostics, d => d.Message.StartsWith("End the snippet with an expression"));
    }

    [Fact]
    public void Runtime_errors_surface_as_the_original_exception()
    {
        var result = SnippetCompiler.Compile("Sine(440).Adsr(-1, 0, 1, 0)");

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => result.Run());
        Assert.Equal("attack", error.ParamName);
    }

    public static TheoryData<string> ExampleNames() => new(Examples.All.Select(e => e.Name));

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_example_compiles_and_renders(string name)
    {
        var example = Examples.All.Single(e => e.Name == name);
        var result = SnippetCompiler.Compile(example.Code);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var audio = HostRender.Render(result.Run(), 2.0);
        AudioAssert.IsFinite(audio);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) > 0.01f, "the example is silent");
    }

    [Fact]
    public void Warming_up_runs_its_snippet_without_error()
    {
        Assert.Null(Record.Exception(SnippetCompiler.Warm));
    }
}
