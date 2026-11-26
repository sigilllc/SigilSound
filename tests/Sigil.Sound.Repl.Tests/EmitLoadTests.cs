using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// <see cref="SnippetCompiler.Emit"/> and <see cref="SnippetCompiler.Load"/> split <see cref="SnippetCompiler.Compile"/> in two, for hosts
/// that compile in one runtime and run in another. Together they must behave exactly like compiling in place.
/// </summary>
public class EmitLoadTests
{
    public static TheoryData<string> ExampleNames()
    {
        var data = new TheoryData<string>();
        foreach (var example in Examples.All)
            data.Add(example.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void An_example_emitted_then_loaded_sounds_like_the_example_compiled_in_place(string name)
    {
        string code = Examples.All.Single(e => e.Name == name).Code;

        var emitted = SnippetCompiler.Emit(code);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var fromImage = HostRender.Render(SnippetCompiler.Load(emitted.Image!, emitted.Diagnostics).Run(), 0.25);
        var inPlace = HostRender.Render(SnippetCompiler.Compile(code).Run(), 0.25);

        // Examples with unseeded noise (Twang's pick, for one) differ from run to run even in place: for those, the image only has to
        // play. Everything deterministic must come out sample for sample the same.
        var again = HostRender.Render(SnippetCompiler.Compile(code).Run(), 0.25);
        if (!inPlace.Left().SequenceEqual(again.Left()))
        {
            AudioAssert.IsFinite(fromImage);
            Assert.True(fromImage.Left().Any(x => x != 0), "The image rendered silence.");
            return;
        }

        Assert.Equal(inPlace.Left(), fromImage.Left());
        Assert.Equal(inPlace.Right(), fromImage.Right());
    }

    [Fact]
    public void A_compile_error_gives_no_image_and_the_same_diagnostic_as_Compile()
    {
        const string code = "var x = 1;\nSine(440).Lowpass(500)";

        var emitted = SnippetCompiler.Emit(code);
        var compiled = SnippetCompiler.Compile(code);

        Assert.False(emitted.Success);
        Assert.Null(emitted.Image);
        Assert.Equal(compiled.Diagnostics, emitted.Diagnostics);
    }

    [Fact]
    public void A_snippet_the_policy_rejects_gives_no_image()
    {
        var emitted = SnippetCompiler.Emit("System.IO.File.ReadAllText(\"secrets.txt\"); Sine(440)");

        Assert.False(emitted.Success);
        Assert.Contains(emitted.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void The_loop_guards_are_in_the_image()
    {
        var emitted = SnippetCompiler.Emit("double x = 0; while (true) { x += 1; } Sine(440)");
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var loaded = SnippetCompiler.Load(emitted.Image!);
        Assert.ThrowsAny<Exception>(() => loaded.Run());
    }

    [Fact]
    public void Warnings_given_to_Load_come_back_on_the_result()
    {
        var emitted = SnippetCompiler.Emit("Sine(440)");
        var warning = new SnippetDiagnostic("TEST01", IsError: false, "carried", 1, 1, 1, 2);

        var loaded = SnippetCompiler.Load(emitted.Image!, [warning]);

        Assert.True(loaded.Success);
        Assert.Equal([warning], loaded.Diagnostics);
    }

    [Fact]
    public void Two_images_of_the_same_snippet_load_side_by_side()
    {
        var first = SnippetCompiler.Load(SnippetCompiler.Emit("Sine(440)").Image!);
        var second = SnippetCompiler.Load(SnippetCompiler.Emit("Sine(440)").Image!);

        AudioAssert.FrequencyNear(first.Run().Render(0.5), 440, 1);
        AudioAssert.FrequencyNear(second.Run().Render(0.5), 440, 1);
    }

    [Fact]
    public void Bytes_that_are_not_a_snippet_are_refused()
    {
        var notASnippet = File.ReadAllBytes(typeof(EmitLoadTests).Assembly.Location);

        Assert.Throws<ArgumentException>(() => SnippetCompiler.Load(notASnippet));
    }
}
