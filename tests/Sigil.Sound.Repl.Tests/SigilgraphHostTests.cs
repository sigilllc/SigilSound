using ReplHost;
using SigilgraphHost;
using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// The REPL's Sigilgraph mode as snippets see it: the device API reaches them through the mode's own <see cref="SnippetProfile"/>,
/// the controls a snippet declares are library <see cref="Control"/>s the page shows, and plain mode stays unaware of it all.
/// </summary>
public sealed class SigilgraphHostTests : IDisposable
{
    private const int SampleRate = 48_000;

    private readonly SigilgraphSession _session = new();

    public SigilgraphHostTests()
    {
        _session.Keys = ReplApi.Keys;
        ReplApi.Keys.AllNotesOff();
        SigilgraphSession.Instance.Keys = ReplApi.Keys;
    }

    public void Dispose() => ReplApi.Keys.AllNotesOff();

    private Sound Build(string code)
    {
        var result = SnippetCompiler.Compile(code, SigilgraphProfile.Instance);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return _session.Build(result.Run);
    }

    private static float[] Render(Sound sound, double seconds) =>
        HostRender.Render(sound, seconds, SampleRate).ToMono();

    private static double Roughness(float[] samples)
    {
        var diff = new float[samples.Length - 1];
        for (int i = 1; i < samples.Length; i++)
            diff[i - 1] = samples[i] - samples[i - 1];
        return Math.Sqrt(diff.Average(d => (double)d * d));
    }

    [Fact]
    public void A_knob_is_a_named_library_control_the_page_can_show()
    {
        Build("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 18000, Curve.Log))");

        var knob = Assert.Single(_session.Declared);
        Assert.Equal("Cutoff", knob.Name);
        Assert.Equal(1200, knob.Value, 3);
        Assert.Equal(40, knob.Min);
        Assert.Equal(18000, knob.Max);
        Assert.Equal(ControlTaper.Log, knob.Taper);
        Assert.Equal(ControlStyle.Knob, knob.Style);
    }

    [Fact]
    public void A_slider_is_a_control_with_the_slider_style_and_controls_keep_declaration_order()
    {
        Build("""
            var b = Knob("B", 1, 0, 2);
            var a = Slider("A", 1, 0, 2, Curve.Exp);
            var c = Knob("C", 1, 0, 2);
            In.Audio * (a + b + c)
            """);

        Assert.Equal(["B", "A", "C"], _session.Declared.Select(c => c.Name));
        Assert.Equal(ControlStyle.Slider, _session.Declared[1].Style);
        Assert.Equal(ControlTaper.Exp, _session.Declared[1].Taper);
    }

    [Fact]
    public void Turning_a_knob_changes_the_sound_while_it_plays()
    {
        var sound = Build("In.Audio.LowPass(Knob(\"Cutoff\", 200, 40, 18000, Curve.Log), q: 0.7)");
        var knob = _session.Declared[0];

        double dark = Roughness(Render(sound, 0.5));
        knob.Travel = 1;   // what the page does as the knob turns
        double bright = Roughness(Render(sound, 0.5));

        Assert.True(bright > dark * 2, $"opening the cutoff should brighten the saw: {dark} then {bright}");
    }

    [Fact]
    public void A_knob_keeps_the_value_you_set_when_the_code_is_edited()
    {
        Build("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 18000, Curve.Log))");
        var knob = _session.Declared[0];
        knob.Value = 5000;

        Build("In.Audio.HighPass(Knob(\"Cutoff\", 1200, 40, 18000, Curve.Log))");

        Assert.Same(knob, _session.Declared[0]);
        Assert.Equal(5000, _session.Declared[0].Value, 3);
    }

    [Fact]
    public void A_knob_whose_range_changed_starts_where_the_old_one_was_left_if_that_still_fits()
    {
        Build("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 18000, Curve.Log))");
        _session.Declared[0].Value = 5000;

        Build("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 9000, Curve.Log))");
        Assert.Equal(5000, _session.Declared[0].Value, 3);
        Assert.Equal(9000, _session.Declared[0].Max);

        Build("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 2000, Curve.Log))");
        Assert.Equal(1200, _session.Declared[0].Value, 3);   // 5000 no longer fits, so the default
    }

    [Fact]
    public void A_failed_run_changes_nothing_on_the_page()
    {
        Build("(In.Audio * Knob(\"Gain\", 0.5, 0, 1))");
        var before = _session.Declared;

        var bad = SnippetCompiler.Compile("(In.Audio * Knob(\"Gain\", 5, 0, 1))", SigilgraphProfile.Instance);
        Assert.True(bad.Success);
        var thrown = Assert.Throws<SigilgraphException>(() => _session.Build(bad.Run));

        Assert.Contains("default 5 outside 0 to 1", thrown.Message);
        Assert.Same(before, _session.Declared);
    }

    [Theory]
    [InlineData("Knob(\"\", 1, 0, 2)", "needs a name")]
    [InlineData("Knob(\"A\", 1, 2, 2)", "not below maximum")]
    [InlineData("Knob(\"A\", 3, 0, 2)", "outside 0 to 2")]
    [InlineData("Knob(\"A\", 1, 0, 2, Curve.Log)", "minimum must be above zero")]
    [InlineData("Knob(\"A\", 1, 0, 2) + Knob(\"A\", 1, 0, 5)", "declared twice with different values")]
    [InlineData("In.Control(4)", "There is no control input 4; use 0 to 3")]
    [InlineData("(In.Audio * Poly(0, v => Saw(v.Frequency)))", "Poly allows 1 to 32")]
    public void The_device_rules_apply_with_the_devices_messages(string expression, string message)
    {
        var result = SnippetCompiler.Compile(expression, SigilgraphProfile.Instance);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));

        var thrown = Assert.Throws<SigilgraphException>(() => _session.Build(result.Run));

        Assert.Contains(message, thrown.Message);
    }

    [Fact]
    public void The_same_control_asked_for_twice_is_one_control()
    {
        Build("Knob(\"A\", 1, 0, 2) + Knob(\"A\", 1, 0, 2)");

        Assert.Single(_session.Declared);
    }

    [Fact]
    public void The_device_has_no_limit_on_knobs_or_sliders()
    {
        string source = string.Join(" + ", Enumerable.Range(0, 12).Select(i => $"Knob(\"K{i}\", 0, 0, 1)").Concat(Enumerable.Range(0, 6).Select(i => $"Slider(\"S{i}\", 0, 0, 1)")));

        Build(source);

        Assert.Equal(18, _session.Declared.Count);
    }

    [Fact]
    public void A_control_input_is_a_bipolar_slider_that_reaches_the_sound()
    {
        var sound = Build("In.Control(1)");

        var input = Assert.Single(_session.ControlInputsUsed);
        Assert.Equal(ControlPolarity.Bipolar, input.Polarity);
        Assert.All(Render(sound, 0.05), s => Assert.Equal(0f, s));

        input.SetFromSignal(0.5);   // bipolar 0.5 is three quarters of the way up the -1 to 1 range
        Assert.Equal(0.5, input.Value, 3);
        Assert.All(Render(sound, 0.2)[^100..], s => Assert.Equal(0.5f, s, 3));
    }

    [Fact]
    public void The_audio_input_follows_the_chosen_test_source()
    {
        var sound = Build("In.Audio");
        Assert.True(_session.UsesAudioInput);

        Assert.InRange(SignalAnalysis.EstimateFrequency(Render(sound, 0.5)[(SampleRate / 10)..], SampleRate), 108, 112);

        _session.Source = SigilgraphSource.Silence;
        var rendered = Render(sound, 0.5);
        Assert.Equal(0f, Math.Abs(rendered[^200..].Max(Math.Abs)), 6);
    }

    [Fact]
    public void Poly_plays_the_pages_keyboard()
    {
        var sound = Build("Poly(4, v => Sine(v.Frequency).Adsr(0, 0, 1, 0.1))");
        Assert.True(_session.UsesNoteInput);
        var output = new SoundContext(SampleRate, fadeSeconds: 0);
        output.Play(sound, fadeSeconds: 0);

        ReplApi.Keys.NoteOn(69, 1f);
        var frames = new StereoFrame[SampleRate / 2];
        output.Render(frames);

        Assert.InRange(SignalAnalysis.EstimateFrequency(frames.Select(f => f.ToMono()).ToArray().AsSpan(SampleRate / 10), SampleRate), 439, 441);
        output.Dispose();
    }

    [Fact]
    public void Out_Notes_plays_through_a_default_voice_and_Out_Control_is_recorded()
    {
        var sound = Build("""
            Out.Control(0, Sine(2));
            Out.Notes(Notes.Parse("C4 E4 G4").Every(Beats.Quarter, gate: 1).Loop());
            Silence
            """);

        Assert.Equal([0], _session.ControlOutputsUsed);
        Assert.Contains(Render(sound, 1.0), s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    public void Asset_says_it_is_not_in_the_web_REPL_yet_instead_of_staying_silent()
    {
        var result = SnippetCompiler.Compile("Sample(Asset(\"Kick\"))", SigilgraphProfile.Instance);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));

        var thrown = Assert.Throws<SigilgraphException>(() => _session.Build(result.Run));

        Assert.Contains("not available in the web REPL yet", thrown.Message);
    }

    [Fact]
    public void Plain_mode_knows_nothing_of_the_device_API()
    {
        var plain = SnippetCompiler.Compile("In.Audio.LowPass(Knob(\"Cutoff\", 1200, 40, 18000))", ReplProfile.Instance);

        Assert.False(plain.Success);
        Assert.Contains(plain.Diagnostics, d => d.IsError && d.Id == "CS0103");
    }

    [Fact]
    public void Outside_a_run_the_device_API_refuses_to_work()
    {
        Assert.Throws<SigilgraphException>(() => SigilgraphApi.Knob("A", 1, 0, 2));
        Assert.Throws<SigilgraphException>(() => SigilgraphApi.In);
    }

    [Fact]
    public void The_base_restrictions_still_apply_in_Sigilgraph_mode()
    {
        var result = SnippetCompiler.Compile("System.IO.File.ReadAllText(\"x\"); Silence", SigilgraphProfile.Instance);

        Assert.False(result.Success);
    }

    [Fact]
    public void The_host_namespace_does_not_open_the_session_to_snippets()
    {
        var result = SnippetCompiler.Compile("SigilgraphHost.SigilgraphSession.Instance.Keys.AllNotesOff(); Silence", SigilgraphProfile.Instance);

        Assert.False(result.Success);   // internal, so a pasted snippet cannot reach it
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_Sigilgraph_example_compiles_and_makes_sound(string name)
    {
        var example = SigilgraphExamples.All.Single(e => e.Name == name);

        var sound = Build(example.Code);
        var output = new SoundContext(SampleRate, fadeSeconds: 0);
        output.Play(sound, fadeSeconds: 0);
        if (_session.UsesNoteInput)
            ReplApi.Keys.NoteOn(60, 1f);
        var frames = new StereoFrame[SampleRate];
        output.Render(frames);
        output.Dispose();

        var mono = frames.Select(f => f.ToMono()).ToArray();
        Assert.All(mono, s => Assert.True(float.IsFinite(s)));
        Assert.Contains(mono, s => Math.Abs(s) > 0.001f);
    }

    [Fact]
    public void The_Sigilgraph_keyboard_example_uses_Poly_not_Keys_and_is_silent_until_a_key_is_played()
    {
        var example = SigilgraphExamples.All.Single(e => e.Name == "Keyboard");
        Assert.DoesNotContain("Keys", example.Code.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).SelectMany(l => l.Split(' ', '.', '(')));   // device code, not page code
        Assert.True(SigilgraphUsage.UsesPoly(example.Code));
        Assert.False(SigilgraphUsage.UsesFaceplate(example.Code));

        var sound = Build(example.Code);
        Assert.True(_session.UsesNoteInput);
        var output = new SoundContext(SampleRate, fadeSeconds: 0);
        output.Play(sound, fadeSeconds: 0);

        var quiet = new StereoFrame[SampleRate / 10];
        output.Render(quiet);
        Assert.All(quiet, f => Assert.Equal(0f, f.Left));

        ReplApi.Keys.NoteOn(64, 0.8f);
        var played = new StereoFrame[SampleRate / 3];
        output.Render(played);
        Assert.True(played.Max(f => Math.Abs(f.Left)) > 0.01f);
        output.Dispose();
    }

    public static TheoryData<string> ExampleNames() => new(SigilgraphExamples.All.Select(e => e.Name));

    [Fact]
    public void Sigilgraph_example_names_do_not_collide_with_the_plain_ones()
    {
        var plain = ReplExamples.All.Select(e => e.Name).ToHashSet();

        Assert.DoesNotContain(SigilgraphExamples.All, e => plain.Contains(e.Name));
    }
}
