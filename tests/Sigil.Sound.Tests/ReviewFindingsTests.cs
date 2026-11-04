using Sigil.Sound.Operators;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>Regression tests for the issues found in the open-source readiness review.</summary>
public class ReviewFindingsTests
{
    private const int SampleRate = 48_000;

    private static float[] Mono(Sound sound, double seconds) => sound.Render(seconds, SampleRate).ToMono();

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * SampleRate)];

    // ---- Envelopes ----

    [Fact]
    public void Adsr_with_instant_attack_and_decay_sustains_instead_of_going_silent()
    {
        var samples = Mono(Constant(1).Adsr(0, 0, 0.7, 0.5, hold: 0.5), 1.5);

        Assert.Equal(0.7f, At(samples, 0.25), 1e-4f);
        Assert.InRange(At(samples, 0.75), 0.05f, 0.5f);   // releasing from 0.7, not from silence
        Assert.Equal(0f, At(samples, 1.4));
    }

    [Fact]
    public void Adsr_without_a_hold_sustains_for_as_long_as_it_plays()
    {
        var samples = Mono(Constant(1).Adsr(0.01, 0.2, 0.6, 1.5), 4.0);

        Assert.Equal(0.6f, At(samples, 3.9), 1e-4f);
    }

    [Fact]
    public void Adsr_released_during_the_attack_ramps_down_from_where_it_got_to()
    {
        var samples = Mono(Constant(1).Adsr(1.0, 0, 1, 0.5, hold: 0.5), 1.2);

        Assert.Equal(0.5f, At(samples, 0.4999), 1e-2f);
        Assert.InRange(At(samples, 0.75), 0.2f, 0.3f);
    }

    [Fact]
    public void Adsr_accepts_param_for_envelope_times()
    {
        var releaseMod = Sine(1).Range(0.2, 0.6);
        var sound = Constant(1).Adsr(0.01, 0.1, 0.5, releaseMod, hold: 0.5);
        var samples = Mono(sound, 1.5);
        Assert.InRange(At(samples, 0.2), 0.45f, 0.55f);
    }

    // ---- Note voices ----

    [Fact]
    public void Voices_play_the_pitch_of_each_note_they_are_reused_for()
    {
        var sound = Notes.Parse("A3 A4").Every(Beats.Quarter).Play(v => Sine(v.Frequency).Adsr(0, 0, 1, 0), bpm: 120, polyphony: 1);
        var audio = Mono(sound, 1.0);

        double first = SignalAnalysis.EstimateFrequency(audio.AsSpan((int)(0.05 * SampleRate), (int)(0.4 * SampleRate)), SampleRate);
        double second = SignalAnalysis.EstimateFrequency(audio.AsSpan((int)(0.55 * SampleRate), (int)(0.4 * SampleRate)), SampleRate);
        Assert.InRange(first, 218, 222);
        Assert.InRange(second, 438, 442);
    }

    [Fact]
    public void A_reused_voice_sounds_exactly_like_a_fresh_one()
    {
        // Noise, a filter and an echo all hold state. With polyphony 1 the third note runs in the voice the first one used,
        // and must start from none of what that note left behind.
        var sound = new[] { new Note(60, 1f), new Note(60, 1f), new Note(60, 1f) }.Every(Beats.Quarter, gate: 0.5)
            .Play(_ => Noise(3).LowPass(1500).Adsr(0.001, 0.01, 0.5, 0.01).Delay(0.05, feedback: 0, mix: 0.5), bpm: 120, polyphony: 1);
        var audio = Mono(sound, 1.6);

        int note = SampleRate / 2;
        for (int i = 0; i < note - 1000; i++)
            Assert.True(Math.Abs(audio[i] - audio[2 * note + i]) < 1e-4f, $"differs at {i}: {audio[i]} vs {audio[2 * note + i]}");
        Assert.True(SignalAnalysis.Peak(audio.AsSpan(0, 1000)) > 0.001f);
    }
    [Fact]
    public void Playing_notes_allocates_nothing_on_the_audio_thread()
    {
        var sound = Notes.Parse("C3 E3 G3 B3").Every(Beats.Sixteenth, gate: 3).Loop()
            .Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.003, 0.1, 0.4, 0.2).Delay(0.1), bpm: 240, polyphony: 4);
        var context = RenderContext.Default;
        var op = sound.Compile(context);
        var block = new StereoFrame[context.BlockSize];

        for (int i = 0; i < 2000; i++)
            op.RenderBlock(block);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
            op.RenderBlock(block);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Voice_gate_follows_the_note()
    {
        var sound = new[] { new Note(60, 1f) }.Every(Beats.Quarter).Play(v => v.Gate, bpm: 120);
        var samples = Mono(sound, 1.0);

        Assert.Equal(1f, At(samples, 0.25));
        Assert.Equal(0f, At(samples, 0.75));
    }

    [Fact]
    public void Voice_signals_support_arithmetic()
    {
        var sound = new[] { new Note(69, 1f) }.Every(Beats.Quarter).Play(v => Sine(v.Frequency * 2).Adsr(0, 0, 1, 0), bpm: 120);

        AudioAssert.FrequencyNear(sound.Render(0.4, SampleRate), 880, 3);
    }

    // ---- Sharing ----

    [Fact]
    public void A_sound_used_twice_in_one_graph_is_built_once()
    {
        int builds = 0;
        var source = Sound.FromOperator(_ =>
        {
            builds++;
            return Operator.From(block => block.Fill(StereoFrame.Mono(0.5f)));
        });

        var mix = source + source.Delay(0.01, feedback: 0, mix: 1);
        var samples = Mono(mix, 0.1);

        Assert.Equal(1, builds);
        Assert.Equal(0.5f, samples[10], 1e-6f);                 // dry only, the echo has not arrived
        Assert.Equal(1.0f, At(samples, 0.05), 1e-6f);           // dry plus echo
    }

    [Fact]
    public void Sharing_gives_the_same_audio_as_building_each_use_separately()
    {
        var shared = Saw(110).LowPass(900);
        var viaShared = shared * 0.5 + shared.HighPass(300) * 0.5;
        var viaCopies = Saw(110).LowPass(900) * 0.5 + Saw(110).LowPass(900).HighPass(300) * 0.5;

        Assert.Equal(Mono(viaCopies, 0.3), Mono(viaShared, 0.3));
    }

    [Fact]
    public void One_lfo_can_drive_two_parameters()
    {
        int builds = 0;
        var lfo = Sound.FromOperator(ctx =>
        {
            builds++;
            return Sine(1).Compile(ctx);
        });

        var sound = Saw(110).LowPass(lfo.Range(300, 2000)).Pan(lfo * 0.5);

        AudioAssert.IsFinite(sound.Render(0.2, SampleRate));
        Assert.Equal(1, builds);
    }

    [Fact]
    public void Slots_swap_and_share_correctly_across_recompiles()
    {
        var slot = new Slot(Sine(220));
        var chain = slot.Sound * 0.5 + slot.Sound.HighPass(5000) * 0.1;
        var op = chain.Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        op.RenderBlock(block);

        slot.Swap(Sine(440), 0);
        for (int i = 0; i < 40; i++) op.RenderBlock(block);

        AudioAssert.IsFinite(new AudioBuffer(block, SampleRate));
    }

    // ---- Constant parameters ----

    [Fact]
    public void Constant_parameters_keep_producing_their_value()
    {
        var source = ((Param)0.25f).Compile(RenderContext.Default);

        Assert.True(source.IsConstant);
        for (int block = 0; block < 3; block++)
            Assert.All(source.Next(256).ToArray(), v => Assert.Equal(0.25f, v));
    }

    [Fact]
    public void Adding_a_number_to_a_sound_offsets_it()
    {
        var samples = Mono(Constant(1) + 12, 0.01);

        Assert.Equal(13f, samples[100]);
        Assert.Equal(14f, Mono(2 + Constant(12), 0.01)[100]);
    }

    // ---- Robustness ----

    [Fact]
    public void A_nan_from_a_parameter_does_not_poison_filters_or_oscillators()
    {
        var wobble = Fn(t => t < 0.05 ? double.NaN : 1000);
        var filtered = Saw(110).LowPass(wobble).Render(0.5, SampleRate);
        var pitched = Sine(Fn(t => t < 0.05 ? double.NaN : 440)).Render(1.0, SampleRate);

        AudioAssert.IsFinite(filtered);
        Assert.True(SignalAnalysis.Rms(filtered.ToMono().AsSpan(SampleRate / 4)) > 0.05);
        AudioAssert.FrequencyNear(new AudioBuffer(pitched.Frames[(SampleRate / 2)..].ToArray(), SampleRate), 440, 1);
    }

    [Fact]
    public void Oscillators_driven_past_nyquist_stay_in_range()
    {
        var audio = Saw(Sine(5).Range(1000, 90_000)).Render(0.5, SampleRate);
        var square = Square(Sine(5).Range(1000, 90_000)).Render(0.5, SampleRate);

        Assert.True(SignalAnalysis.Peak(audio.ToMono()) < 2.5f);
        Assert.True(SignalAnalysis.Peak(square.ToMono()) < 3.5f);
    }

    [Fact]
    public void Filters_stay_stable_under_fast_modulation()
    {
        var audio = Noise().LowPass(Sine(2000).Range(20, 20_000), q: 12).Render(1.0, SampleRate);

        AudioAssert.IsFinite(audio);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) < 200f);
    }

    [Fact]
    public void BandPass_keeps_its_center_and_Notch_removes_it()
    {
        double Level(Sound s) => SignalAnalysis.Rms(Mono(s, 0.5).AsSpan(SampleRate / 10));

        Assert.True(Level(Sine(1000).BandPass(1000, q: 4)) > 0.6);
        Assert.True(Level(Sine(100).BandPass(1000, q: 4)) < 0.1);
        Assert.True(Level(Sine(1000).Notch(1000, q: 4)) < 0.05);
        Assert.True(Level(Sine(100).Notch(1000, q: 4)) > 0.6);
    }

    [Fact]
    public void BitCrush_with_a_reduced_rate_takes_its_first_sample_at_once()
    {
        var samples = Mono(Constant(0.5).BitCrush(bits: 16, rate: 4000), 0.01);

        Assert.Equal(0.5f, samples[0], 1e-3f);
    }

    [Fact]
    public void Fuzz_has_the_same_character_at_other_sample_rates()
    {
        double Rms(int rate) => SignalAnalysis.Rms((Sine(220) * 0.5).Distort(5, DistortionMode.Fuzz).Render(0.5, rate).ToMono());

        Assert.InRange(Rms(96_000) / Rms(48_000), 0.9, 1.1);
    }

    // ---- Disposal ----

    [Fact]
    public void Disposing_a_compiled_sound_disposes_operators_anywhere_inside_it()
    {
        var inner = new Tracked();
        var sound = Sound.FromOperator(_ => inner).Gain(0.5).LowPass(1000) + Sine(100);

        var op = sound.Compile(RenderContext.Default);
        Assert.False(inner.Disposed);
        ((IDisposable)op).Dispose();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public void Swapped_out_operators_are_disposed_without_another_swap()
    {
        var first = new Tracked();
        var slot = new Slot(Sound.FromOperator(_ => first).Gain(0.5), 0.0);
        var op = slot.Sound.Compile(RenderContext.Default);
        var block = new StereoFrame[256];

        slot.Swap(Sine(100), 0);
        op.RenderBlock(block);

        SpinWait.SpinUntil(() => first.Disposed, TimeSpan.FromSeconds(3));
        Assert.True(first.Disposed);
    }

    [Fact]
    public void Disposing_a_sound_context_disposes_what_is_playing()
    {
        var playing = new Tracked();
        var output = new SoundContext(SampleRate);
        output.Play(Sound.FromOperator(_ => playing), 0);
        output.Render(new StereoFrame[256]);

        output.Dispose();

        Assert.True(playing.Disposed);
    }

    [Fact]
    public void Through_passes_reset_to_its_input_and_operator()
    {
        int resets = 0;
        var sound = new[] { new Note(60, 1f), new Note(60, 1f) }.Every(Beats.Quarter).Play(_ =>
            Noise(2).Through(input => Operator.From(block => input.RenderBlock(block), () => resets++)).Adsr(0, 0, 1, 0), bpm: 120, polyphony: 1);

        AudioAssert.IsFinite(sound.Render(1.0, SampleRate));

        Assert.Equal(2, resets);
    }

    private sealed class Tracked : IOperator, IDisposable
    {
        public bool Disposed { get; private set; }

        public void RenderBlock(Span<StereoFrame> block) => block.Fill(StereoFrame.Mono(1));

        public void Dispose() => Disposed = true;
    }
}
