using Sigil.Sound.Spectral;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class PitchShiftTests
{
    private const int Rate = 48_000;

    // Skips the start-up (the frame latency plus the time for the phases to settle) and keeps a steady stretch.
    private static AudioBuffer Settled(Sound sound, double seconds = 1.5, int sampleRate = Rate)
    {
        var audio = sound.Render(seconds, sampleRate);
        int from = (int)(0.4 * sampleRate);
        return new AudioBuffer(audio.Frames[from..].ToArray(), sampleRate);
    }

    private static double Cents(double actual, double expected) => 1200 * Math.Log2(actual / expected);

    [Theory]
    [InlineData(12, 880.0)]
    [InlineData(-12, 220.0)]
    [InlineData(7, 659.255)]
    [InlineData(-5, 329.628)]
    [InlineData(0, 440.0)]
    [InlineData(24, 1760.0)]
    [InlineData(-24, 110.0)]
    [InlineData(1, 466.164)]
    public void A_sine_lands_on_the_shifted_pitch(double semitones, double expectedHz)
    {
        var shifted = Settled(Sine(440).PitchShift(semitones));

        double actual = SignalAnalysis.PeakFrequency(shifted.ToMono(), Rate);

        Assert.True(Math.Abs(Cents(actual, expectedHz)) < 8, $"expected {expectedHz} Hz, got {actual:0.##} Hz ({Cents(actual, expectedHz):0.#} cents)");
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void The_pitch_is_right_at_every_sample_rate(int sampleRate)
    {
        var shifted = Settled(Sine(523.25).PitchShift(5), sampleRate: sampleRate);

        double actual = SignalAnalysis.PeakFrequency(shifted.ToMono(), sampleRate);

        Assert.True(Math.Abs(Cents(actual, 523.25 * Math.Pow(2, 5 / 12.0))) < 8, $"got {actual:0.##} Hz");
    }

    [Fact]
    public void A_harmonic_tone_keeps_its_harmonics_in_tune()
    {
        // Eight harmonics of 220 Hz, shifted up a fifth: the fundamental and every partial must move by the same ratio.
        var tone = Sine(220) * 0.5 + Sine(440) * 0.4 + Sine(660) * 0.3 + Sine(880) * 0.25 + Sine(1100) * 0.2 + Sine(1320) * 0.15 + Sine(1540) * 0.1 + Sine(1760) * 0.08;
        var shifted = Settled(tone.PitchShift(7), 2.0).ToMono();
        var spectrum = SignalAnalysis.MagnitudeSpectrum(shifted, 1 << 17);
        double ratio = Math.Pow(2, 7 / 12.0);

        float Level(double hz)
        {
            int bin = (int)Math.Round(hz * (1 << 17) / Rate);
            return spectrum.AsSpan(bin - 3, 7).ToArray().Max();
        }

        for (int harmonic = 1; harmonic <= 8; harmonic++)
        {
            double expected = 220.0 * harmonic * ratio;
            Assert.True(Level(expected) > 0.5f * (0.5f / harmonic), $"harmonic {harmonic} at {expected:0.#} Hz is missing");
        }
        // The old fundamental is gone.
        Assert.True(Level(220) < 0.03f, "the original fundamental should have moved");
    }

    [Fact]
    public void Shifting_down_lowers_the_top_of_the_spectrum_too()
    {
        // White noise has energy to Nyquist. An octave down must leave nothing above half of it.
        var noise = Noise(7) * 0.3;
        double Above(Sound sound, double hz)
        {
            var audio = Settled(sound).ToMono();
            var spectrum = SignalAnalysis.MagnitudeSpectrum(audio, 1 << 16);
            int from = (int)(hz * (1 << 16) / Rate);
            double high = 0, all = 0;
            for (int k = 1; k < spectrum.Length; k++)
            {
                double power = (double)spectrum[k] * spectrum[k];
                all += power;
                if (k >= from) high += power;
            }
            return high / all;
        }

        double plain = Above(noise.PitchShift(0), 13_000);
        double dived = Above(noise.PitchShift(-12), 13_000);

        Assert.True(plain > 0.3, $"the unshifted noise should have plenty above 13 kHz, has {plain:0.###}");
        Assert.True(dived < 0.01, $"an octave down should leave almost nothing above 13 kHz, has {dived:0.####}");
    }

    [Fact]
    public void Shifting_up_discards_what_would_pass_nyquist_and_adds_nothing_low()
    {
        // A tone near the top of the spectrum, shifted up an octave, would land past Nyquist and must vanish rather than fold back.
        var shifted = Settled(Sine(16_000).PitchShift(12)).ToMono();

        Assert.True(SignalAnalysis.Rms(shifted) < 0.02, $"rms {SignalAnalysis.Rms(shifted)}");
    }

    [Fact]
    public void The_level_is_preserved()
    {
        foreach (double semitones in new[] { 0.0, 7.0, -7.0, 12.0 })
        {
            var original = Settled(Sine(440) * 0.5).ToMono();
            var shifted = Settled(Sine(440).PitchShift(semitones) * 0.5).ToMono();

            double ratio = SignalAnalysis.Rms(shifted) / SignalAnalysis.Rms(original);
            Assert.InRange(ratio, 0.8, 1.25);
        }
    }

    [Fact]
    public void The_output_lags_the_input_by_one_frame_and_the_dry_signal_matches()
    {
        // With mix 0 the output is exactly the input delayed by the frame length (2048 at 48 kHz).
        var input = Saw(300).Render(0.5, Rate).ToMono();
        var dry = Saw(300).PitchShift(5, mix: 0).Render(0.5, Rate).ToMono();

        Assert.All(dry.Take(2048), v => Assert.Equal(0f, v));
        for (int i = 2048; i < dry.Length; i += 13)
            Assert.Equal(input[i - 2048], dry[i], 1e-5f);
    }

    [Fact]
    public void A_half_mix_adds_the_harmony_to_the_original()
    {
        var mixed = Settled((Sine(440)).PitchShift(12, mix: 0.5), 1.5).ToMono();
        var spectrum = SignalAnalysis.MagnitudeSpectrum(mixed, 1 << 16);
        int bin440 = 440 * (1 << 16) / Rate, bin880 = 880 * (1 << 16) / Rate;

        Assert.InRange(spectrum.AsSpan(bin440 - 2, 5).ToArray().Max(), 0.4f, 0.6f);
        Assert.InRange(spectrum.AsSpan(bin880 - 2, 5).ToArray().Max(), 0.4f, 0.6f);
    }

    [Fact]
    public void The_output_does_not_depend_on_the_block_size()
    {
        var sound = (Sine(330) * 0.4 + Saw(110) * 0.2).PitchShift(Fn(t => 3 * Math.Sin(t * 4)), mix: 0.8);

        float[] Pull(params int[] sizes)
        {
            var op = sound.Compile(RenderContext.Default);
            var result = new float[Rate / 2];
            for (int done = 0, i = 0; done < result.Length; i++)
            {
                var block = new StereoFrame[Math.Min(sizes[i % sizes.Length], result.Length - done)];
                op.RenderBlock(block);
                for (int k = 0; k < block.Length; k++) result[done + k] = block[k].ToMono();
                done += block.Length;
            }
            return result;
        }

        var reference = Pull(256);
        Assert.Equal(reference, Pull(37));
        Assert.Equal(reference, Pull(1, 100, 7, 256, 48));
    }

    [Fact]
    public void A_left_only_signal_stays_on_the_left()
    {
        var shifted = Settled(Sine(440).Balance(-1).PitchShift(12));

        AudioAssert.IsSilent(shifted.Right());
        Assert.InRange(SignalAnalysis.PeakFrequency(shifted.Left(), Rate), 872, 888);
    }

    [Fact]
    public void A_bend_sweeps_the_pitch_down()
    {
        // A dive from 0 to -12 over two seconds: the pitch early on is high and late is low.
        var dive = Sine(440).PitchShift(Fn(t => -12 * Math.Min(t / 2, 1))).Render(2.6, Rate).ToMono();

        double early = SignalAnalysis.PeakFrequency(dive.AsSpan((int)(0.4 * Rate), 4096), Rate);
        double late = SignalAnalysis.PeakFrequency(dive.AsSpan((int)(2.3 * Rate), 4096), Rate);

        Assert.InRange(early, 365, 410); // about 2.5 semitones down by then
        Assert.InRange(late, 200, 240);
    }

    [Fact]
    public void A_control_can_drive_the_shift()
    {
        var control = new Control(0, -12, 12, slewSeconds: 0.05);
        var op = Sine(440).PitchShift(control).Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        for (int i = 0; i < 300; i++) op.RenderBlock(block);

        control.Value = 12;
        var captured = new float[Rate / 2];
        for (int done = 0; done < captured.Length; done += 256)
        {
            op.RenderBlock(block);
            for (int k = 0; k < 256 && done + k < captured.Length; k++) captured[done + k] = block[k].Left;
        }

        Assert.InRange(SignalAnalysis.PeakFrequency(captured.AsSpan(captured.Length / 2).ToArray(), Rate), 870, 890);
    }

    [Fact]
    public void Silence_and_extreme_input_stay_finite()
    {
        AudioAssert.IsSilent(Silence.PitchShift(7).Render(0.3, Rate).ToMono());
        AudioAssert.IsFinite((Constant(1)).PitchShift(5).Render(0.3, Rate));
        AudioAssert.IsFinite((Noise(3) * 50).PitchShift(-9).Render(0.3, Rate));
        AudioAssert.IsFinite(Sine(440).PitchShift(Fn(t => double.NaN)).Render(0.3, Rate));
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        var op = Sine(440).PitchShift(5).Compile(RenderContext.Default);
        var first = Measure.Pull(op, 12_000);

        ((IResettable)op).Reset();

        Assert.Equal(first, Measure.Pull(op, 12_000));
    }

    [Fact]
    public void Rendering_allocates_nothing()
    {
        var op = (Saw(110) + Saw(165)).PitchShift(Fn(t => 3 * Math.Sin(t * 5)), mix: 0.7).Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        for (int i = 0; i < 200; i++) op.RenderBlock(block);

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 500; i++) op.RenderBlock(block);
        });
    }

    [Fact]
    public void The_frame_size_follows_the_sample_rate()
    {
        Assert.Equal(2048, Operators.PitchShiftOperator.FrameSizeFor(44_100));
        Assert.Equal(2048, Operators.PitchShiftOperator.FrameSizeFor(48_000));
        Assert.Equal(4096, Operators.PitchShiftOperator.FrameSizeFor(96_000));
        Assert.Equal(1024, Operators.PitchShiftOperator.FrameSizeFor(22_050));
    }

    [Fact]
    public void A_pitch_shifted_sound_can_sit_in_a_slot()
    {
        var output = new SoundContext(Rate);
        output.Play(Saw(220).PitchShift(7), 0);
        var block = new StereoFrame[2048];
        output.Render(block);

        output.Play(Saw(330).PitchShift(-7), 0.01);
        output.Render(block);

        AudioAssert.IsFinite(new AudioBuffer(block, Rate));
    }
}

/// <summary>
/// Writes WAV files of the pitch shifter at work for listening, when SIGIL_DEMO_DIR names a folder. Does nothing otherwise.
/// <c>SIGIL_DEMO_DIR=C:\temp\demo dotnet test --filter PitchShiftDemos</c>
/// </summary>
public class PitchShiftDemos
{
    [EnvironmentFact("SIGIL_DEMO_DIR")]
    public void Write_listening_files()
    {
        string folder = Environment.GetEnvironmentVariable("SIGIL_DEMO_DIR")!;
        Directory.CreateDirectory(folder);

        var pad = (Saw(110) * 0.25 + Saw(165.2) * 0.2 + Saw(220.4) * 0.2 + Saw(277) * 0.15).LowPass(2500).Reverb(mix: 0.2);
        var noteLine = Mini.Notes("c3 [e3 g3] <b3 d4> g3").Play(v => Saw(v.Frequency).LowPass(2200).Adsr(0.005, 0.12, 0.4, 0.15), bpm: 240);

        void Write(string name, Sound sound, double seconds) => sound.Limit().RenderToWav(Path.Combine(folder, name + ".wav"), seconds);

        Write("01 pad dry", pad, 4);
        Write("02 pad up a fifth (mix 0.5 harmony)", pad.PitchShift(7, mix: 0.5), 4);
        Write("03 pad dive to -12 (bomb)", pad.PitchShift(Fn(t => -12 * Math.Min(t / 3.0, 1))), 4.5);
        Write("04 noise dive to -12", (Noise(5) * 0.3).LowPass(12000).PitchShift(Fn(t => -12 * Math.Min(t / 3.0, 1))), 4.5);
        Write("05 melody up an octave", noteLine.PitchShift(12), 3);
        Write("06 melody down a third + dry", noteLine.PitchShift(-4, mix: 0.5), 3);
        Write("07 slow vibrato", pad.PitchShift(Fn(t => 0.4 * Math.Sin(t * 5.5))), 4);
    }
}
