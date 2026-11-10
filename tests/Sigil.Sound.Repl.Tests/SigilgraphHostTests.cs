using ReplHost;
using SigilgraphHost;
using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// The REPL's Sigilgraph mode as snippets see it: the device API reaches them through the mode's own <see cref="SnippetProfile"/>,
/// the controls a snippet declares are library <see cref="Control"/>s the page shows, and plain mode stays unaware of it all.
/// </summary>
// ReplApi.Keys is one instance for the whole process and each of these classes releases every key in its setup and cleanup.
// Test classes run in parallel, so without a shared collection one class can silence the other's held note mid-test.
[Collection("ReplApi.Keys")]
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

        // The page gives the live notes a clock that its engine advances with the audio it renders; here the context does, so a chord
        // that needs 35 ms to settle settles in the audio rendered below and not in the few milliseconds the test takes.
        var clock = new HostClock(SampleRate);
        ReplApi.Keys.Clock = clock;
        try
        {
            var sound = Build(example.Code);
            var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
            output.Play(sound, fadeSeconds: 0);
            if (_session.UsesNoteInput)
                ReplApi.Keys.NoteOn(60, 1f);
            var frames = new StereoFrame[SampleRate];
            output.Render(frames);
            output.Dispose();
            AssertSounds(frames);
        }
        finally
        {
            ReplApi.Keys.Clock = null;
        }
    }

    private static void AssertSounds(StereoFrame[] frames)
    {
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

    // --- Notes out: one voice plays them and the strip beside the scope draws them ----------------------------------------------

    // The trace is a flat array of [kind, id, pitch, velocity, ...]: kind 0 starts a note, 1 ends it.
    private static List<(int Kind, int Id, int Pitch)> Events(double[] flat) =>
        Enumerable.Range(0, flat.Length / 4).Select(i => ((int)flat[4 * i], (int)flat[4 * i + 1], (int)flat[4 * i + 2])).ToList();

    [Fact]
    public void Out_Notes_is_heard_and_the_trace_gets_each_note_and_its_end()
    {
        var clock = new HostClock(SampleRate);
        ReplApi.Keys.Clock = clock;
        try
        {
            NoteTrace.Page.Drain();
            var sound = Build("Out.Notes(In.Notes.Harmonize(0, 7));\nSilence");
            Assert.True(_session.SendsNotes);
            using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
            output.Play(sound, fadeSeconds: 0);

            ReplApi.Keys.NoteOn(60, 0.8f);
            var played = new StereoFrame[SampleRate / 4];
            output.Render(played);
            Assert.True(played.Max(f => Math.Abs(f.Left)) > 0.01f, "the notes sent out should be heard");
            var started = Events(NoteTrace.Page.Drain());
            Assert.Equal([60, 67], started.Where(e => e.Kind == 0).Select(e => e.Pitch).Order());

            ReplApi.Keys.NoteOff(60);
            output.Render(new StereoFrame[SampleRate / 10]);
            var ended = Events(NoteTrace.Page.Drain());
            Assert.Equal(started.Select(e => e.Id).Order(), ended.Where(e => e.Kind == 1).Select(e => e.Id).Order());
        }
        finally
        {
            ReplApi.Keys.Clock = null;
        }
    }

    [Fact]
    public void A_pattern_sent_out_is_traced_on_the_note_clock()
    {
        var clock = new HostClock(SampleRate);
        ReplApi.Keys.Clock = clock;
        try
        {
            NoteTrace.Page.Drain();
            var sound = Build("Out.Notes(Mini.Notes(\"c4 e4\"));\nSilence"); // two notes in a cycle: C4 at beat 0, E4 at beat 2
            using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
            output.Play(sound, fadeSeconds: 0);

            output.Render(new StereoFrame[SampleRate / 4]); // a quarter of a second is half a beat: only C4 has started
            var first = Events(NoteTrace.Page.Drain());
            Assert.Equal([60], first.Where(e => e.Kind == 0).Select(e => e.Pitch));
            Assert.DoesNotContain(first, e => e.Kind == 1);

            output.Render(new StereoFrame[SampleRate]); // 1.25 s: E4 at 1 s, and C4 ended at 1 s
            var later = Events(NoteTrace.Page.Drain());
            Assert.Contains(later, e => e.Kind == 0 && e.Pitch == 64);
            Assert.Contains(later, e => e.Kind == 1);
        }
        finally
        {
            ReplApi.Keys.Clock = null;
        }
    }

    [Fact]
    public void Replacing_the_sound_ends_the_notes_it_was_sending()
    {
        var clock = new HostClock(SampleRate);
        ReplApi.Keys.Clock = clock;
        try
        {
            NoteTrace.Page.Drain();
            var sound = Build("Out.Notes(In.Notes);\nSilence");
            var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
            output.Play(sound, fadeSeconds: 0);
            ReplApi.Keys.NoteOn("c3 e3");
            output.Render(new StereoFrame[SampleRate / 10]);
            var started = Events(NoteTrace.Page.Drain()).Count(e => e.Kind == 0);
            Assert.Equal(2, started);

            output.Dispose(); // the sound goes away with the keys still down

            Assert.Equal(started, Events(NoteTrace.Page.Drain()).Count(e => e.Kind == 1));
        }
        finally
        {
            ReplApi.Keys.Clock = null;
        }
    }

    [Theory]
    [InlineData("Out.Notes(In.Notes.Transpose(7)); Silence", true)]
    [InlineData("var n = In.Notes;\nSilence", true)]
    [InlineData("Out.Notes(Mini.Notes(\"c3\")); Silence", false)]
    [InlineData("In.Audio", false)]
    [InlineData("// In.Notes is in a comment\nSilence", false)]
    [InlineData("var x = other.In.Notes;\nSilence", false)]
    public void The_keyboard_is_offered_for_a_snippet_that_reads_In_Notes(string code, bool expected) =>
        Assert.Equal(expected, SigilgraphUsage.UsesNotes(code));

    public static TheoryData<string> ExampleNames() => new(SigilgraphExamples.All.Select(e => e.Name));

    [Fact]
    public void Sigilgraph_example_names_do_not_collide_with_the_plain_ones()
    {
        var plain = ReplExamples.All.Select(e => e.Name).ToHashSet();

        Assert.DoesNotContain(SigilgraphExamples.All, e => plain.Contains(e.Name));
    }
}
