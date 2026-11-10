using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class EffectsTests
{
    private const int SampleRate = 48_000;

    // One millisecond of full level, then silence: the simplest signal to follow through an effect.
    private static Sound Impulse => Fn(t => t < 0.001 ? 1.0 : 0.0);

    private static float[] Mono(Sound sound, double seconds) => sound.Render(seconds, SampleRate).ToMono();

    private static float[] Slice(float[] samples, double fromSeconds, double toSeconds) =>
        samples[(int)(fromSeconds * SampleRate)..(int)(toSeconds * SampleRate)];

    private static int IndexOfPeak(float[] samples, double fromSeconds, double toSeconds)
    {
        int start = (int)(fromSeconds * SampleRate);
        var window = Slice(samples, fromSeconds, toSeconds);
        return start + Array.IndexOf(window, window.MaxBy(MathF.Abs));
    }

    // The lag (in frames) with the strongest self-similarity within the range, refined to a fraction, as a pitch in Hz.
    // Zero crossings are no good here: a plucked string is full of overtones.
    private static double PitchByAutocorrelation(float[] samples, int minLag, int maxLag)
    {
        double Correlation(int lag)
        {
            double sum = 0;
            for (int i = 0; i + lag < samples.Length; i++)
                sum += samples[i] * samples[i + lag];
            return sum;
        }

        int best = minLag;
        for (int lag = minLag; lag <= maxLag; lag++)
            if (Correlation(lag) > Correlation(best)) best = lag;

        double before = Correlation(best - 1), at = Correlation(best), after = Correlation(best + 1);
        double refined = best + 0.5 * (before - after) / (before - 2 * at + after);
        return SampleRate / refined;
    }

    [Fact]
    public void Delay_repeats_the_sound_after_the_delay_time()
    {
        var samples = Mono(Impulse.Delay(0.1, feedback: 0, mix: 1), 0.3);

        AudioAssert.IsSilent(Slice(samples, 0, 0.099));
        Assert.InRange(IndexOfPeak(samples, 0.099, 0.15), (int)(0.099 * SampleRate), (int)(0.103 * SampleRate));
    }

    [Fact]
    public void Delay_feedback_repeats_the_repeats_each_quieter()
    {
        var samples = Mono(Impulse.Delay(0.1, feedback: 0.5, mix: 1), 0.5);

        float first = MathF.Abs(samples[IndexOfPeak(samples, 0.099, 0.15)]);
        float second = MathF.Abs(samples[IndexOfPeak(samples, 0.199, 0.25)]);
        Assert.InRange(second / first, 0.45f, 0.55f);
    }

    [Fact]
    public void Delay_with_mix_zero_is_the_dry_sound()
    {
        var sound = Saw(220);

        Assert.Equal(Mono(sound, 0.2), Mono(sound.Delay(0.05, mix: 0), 0.2));
    }

    [Fact]
    public void Reverb_leaves_a_tail_that_decays()
    {
        var samples = Mono(Impulse.Reverb(decay: 1.0, mix: 1), 2.0);

        double early = SignalAnalysis.Rms(Slice(samples, 0.05, 0.4));
        double late = SignalAnalysis.Rms(Slice(samples, 1.2, 1.9));
        Assert.True(early > 0.001, $"no tail: {early}");
        Assert.True(late < early * 0.2, $"early {early}, late {late}");
        Assert.False(SignalAnalysis.HasNonFinite(samples));
    }

    [Fact]
    public void Reverb_decay_sets_how_long_the_tail_lasts()
    {
        double Late(double decay) =>
            SignalAnalysis.Rms(Slice(Mono(Impulse.Reverb(decay: decay, mix: 1), 2.0), 1.0, 1.9));

        Assert.True(Late(4.0) > Late(0.5) * 3);
    }

    [Fact]
    public void Reverb_makes_the_two_channels_differ()
    {
        var audio = Impulse.Reverb(mix: 1).Render(0.5, SampleRate);

        Assert.NotEqual(audio.Left(), audio.Right());
    }

    [Fact]
    public void Comb_rings_at_its_frequency_like_a_plucked_string()
    {
        var burst = Noise(seed: 3) * Fn(t => t < 0.01 ? 1.0 : 0.0);
        var samples = Mono(burst.Comb(220, feedback: 0.995), 1.0);

        double hz = PitchByAutocorrelation(Slice(samples, 0.2, 0.9), 100, 400);
        Assert.InRange(hz, 217, 223);
        Assert.True(SignalAnalysis.Peak(Slice(samples, 0.5, 1.0)) > 0.01f, "the string stopped ringing too soon");
    }

    [Fact]
    public void Comb_without_feedback_passes_the_sound_through()
    {
        var sound = Sine(300) * 0.5;

        var combed = Mono(sound.Comb(200, feedback: 0), 0.2);

        Assert.Equal(Mono(sound, 0.2), combed);
    }

    [Theory]
    [InlineData(DistortionMode.Soft)]
    [InlineData(DistortionMode.Hard)]
    [InlineData(DistortionMode.Fold)]
    [InlineData(DistortionMode.Fuzz)]
    public void Distort_stays_finite_and_in_range_in_every_mode(DistortionMode mode)
    {
        var audio = (Sine(220) * 0.8).Distort(drive: 10, mode).Render(0.5, SampleRate);

        AudioAssert.IsFinite(audio);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) <= 2.5f);
        Assert.True(SignalAnalysis.Peak(audio.ToMono()) > 0.1f);
    }

    [Fact]
    public void Hard_distortion_turns_a_sine_into_something_close_to_a_square()
    {
        double clean = SignalAnalysis.Rms(Mono(Sine(220), 0.5));
        var hard = Mono(Sine(220).Distort(drive: 20, DistortionMode.Hard), 0.5);

        Assert.True(SignalAnalysis.Rms(hard) > clean * 1.3);
        Assert.Equal(1f, SignalAnalysis.Peak(hard), 1e-6f);
        AudioAssert.FrequencyNear(Sine(220).Distort(drive: 20, DistortionMode.Hard).Render(1.0, SampleRate), 220, 1.0);
    }

    [Fact]
    public void Distort_level_scales_the_output()
    {
        var loud = SignalAnalysis.Peak(Mono(Sine(220).Distort(drive: 10, level: 1), 0.2));
        var quiet = SignalAnalysis.Peak(Mono(Sine(220).Distort(drive: 10, level: 0.25), 0.2));

        Assert.Equal(loud * 0.25f, quiet, 1e-4f);
    }

    [Fact]
    public void BitCrush_leaves_few_distinct_levels()
    {
        var samples = Mono(Sine(220).BitCrush(bits: 3), 0.2);

        // 3 bits: 4 steps either side of zero.
        Assert.True(samples.Distinct().Count() <= 9);
    }

    [Fact]
    public void BitCrush_rate_holds_samples()
    {
        var samples = Mono(Sine(220).BitCrush(bits: 16, rate: 4_000), 0.1);

        int changes = 0;
        for (int i = 1; i < samples.Length; i++)
            if (samples[i] != samples[i - 1]) changes++;
        // About 4,000 new values per second, so about 400 in a tenth of a second.
        Assert.InRange(changes, 350, 450);
    }

    [Fact]
    public void Chorus_with_mix_zero_is_the_dry_sound_and_otherwise_changes_it()
    {
        var sound = Saw(220);

        Assert.Equal(Mono(sound, 0.3), Mono(sound.Chorus(mix: 0), 0.3));
        var wet = sound.Chorus(mix: 1).Render(0.5, SampleRate);
        AudioAssert.IsFinite(wet);
        Assert.NotEqual(Mono(sound, 0.5), wet.ToMono());
    }

    [Fact]
    public void Chorus_spreads_across_the_stereo_field()
    {
        var audio = Saw(220).Chorus(mix: 1, voices: 4).Render(0.5, SampleRate);

        Assert.NotEqual(audio.Left(), audio.Right());
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(220).Chorus(voices: 0));
    }

    [Fact]
    public void Phaser_changes_the_sound_without_changing_its_loudness_much()
    {
        var noise = Noise(seed: 5) * 0.5;
        var phased = noise.Phaser(mix: 0.5).Render(1.0, SampleRate);

        AudioAssert.IsFinite(phased);
        double ratio = SignalAnalysis.Rms(phased.ToMono()) / SignalAnalysis.Rms(Mono(noise, 1.0));
        Assert.InRange(ratio, 0.4, 1.1);
        Assert.Equal(Mono(noise, 0.2), Mono(noise.Phaser(mix: 0), 0.2));
    }

    [Fact]
    public void Limit_keeps_loud_sound_under_the_ceiling_and_leaves_quiet_sound_alone()
    {
        var loud = Mono(((Saw(110) + Saw(165) + Saw(220)) * 3).Limit(), 0.5);
        var quiet = Sine(220) * 0.3;

        Assert.True(SignalAnalysis.Peak(loud) <= 0.95f + 1e-6f);
        Assert.True(SignalAnalysis.Peak(Mono((Saw(110) * 3).Limit(), 0.5)) <= 0.95f + 1e-6f);
        Assert.Equal(Mono(quiet, 0.3), Mono(quiet.Limit(), 0.3));
    }

    [Fact]
    public void HighPass_removes_lows_and_keeps_highs()
    {
        double lowIn = SignalAnalysis.Rms(Mono(Sine(100), 0.5));
        double lowOut = SignalAnalysis.Rms(Slice(Mono(Sine(100).HighPass(2000), 0.5), 0.1, 0.5));
        double highIn = SignalAnalysis.Rms(Mono(Sine(8000), 0.5));
        double highOut = SignalAnalysis.Rms(Slice(Mono(Sine(8000).HighPass(2000), 0.5), 0.1, 0.5));

        Assert.True(lowOut < lowIn * 0.05, $"low {lowOut / lowIn}");
        Assert.True(highOut > highIn * 0.9, $"high {highOut / highIn}");
    }

    [Fact]
    public void Pan_turns_down_the_far_side()
    {
        var center = Saw(220).Pan(0).Render(0.1, SampleRate);
        var left = Saw(220).Pan(-1).Render(0.1, SampleRate);
        var right = Saw(220).Pan(1).Render(0.1, SampleRate);

        var dry = Saw(220).Render(0.1, SampleRate).Left();
        Assert.All(dry.Zip(center.Left()), pair => Assert.Equal(pair.First, pair.Second, 1e-5f));
        AudioAssert.IsSilent(left.Right());
        AudioAssert.IsSilent(right.Left());
        // Equal power: a hard pan carries the whole signal on one side, sqrt(2) above the centered level.
        var expected = center.Left().Select(x => x * MathF.Sqrt(2)).ToArray();
        Assert.All(expected.Zip(left.Left()), pair => Assert.Equal(pair.First, pair.Second, 1e-4f));
    }

    [Fact]
    public void Pan_keeps_the_same_power_at_every_position()
    {
        double Power(double position)
        {
            var audio = Saw(220).Pan(position).Render(0.1, SampleRate);
            double l = Measure.Rms(audio.Left()), r = Measure.Rms(audio.Right());
            return l * l + r * r;
        }

        double center = Power(0);
        foreach (double p in new[] { -1.0, -0.5, 0.3, 1.0 })
            Assert.Equal(center, Power(p), center * 1e-3);
    }

    [Fact]
    public void Balance_leaves_the_near_side_alone_and_keeps_stereo()
    {
        var source = Measure.Stereo(0.6f, 0.2f);

        var right = source.Balance(1).Render(0.01, SampleRate);

        AudioAssert.IsSilent(right.Left());
        Assert.Equal(0.2f, right.Right()[^1], 4);
        Assert.Equal(0.6f, source.Balance(-1).Render(0.01, SampleRate).Left()[^1], 4);
    }

    [Fact]
    public void Multiplying_two_sounds_works_channel_by_channel()
    {
        var product = (Measure.Stereo(0.5f, 1f) * Measure.Stereo(0.5f, 0.25f)).Render(0.01, SampleRate);

        Assert.Equal(0.25f, product.Left()[^1], 5);
        Assert.Equal(0.25f, product.Right()[^1], 5);
    }

    [Fact]
    public void Effects_validate_their_structural_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Reverb(size: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Reverb(damping: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Delay(0.1, maxTime: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Phaser(stages: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Phaser(minFrequency: 800, maxFrequency: 400));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Limit(ceiling: 0));
    }

    // A script must not be able to ask for gigabytes (a delay line is 0.38 MB per second at 48 kHz, a reverb room grows with its
    // size) or for a tail that never dies, and a long but real setting must still work: a delay line of a minute, a hall 100
    // times a medium room, a two minute tail.
    [Theory]
    [InlineData(60.01)]
    [InlineData(40000)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    public void Delay_lines_reject_a_buffer_that_is_absurd(double maxTime)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Delay(0.1, maxTime: maxTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).TapeDelay(0.1, maxTime: maxTime));
    }

    [Theory]
    [InlineData(101, 2.5)]
    [InlineData(100000, 2.5)]
    [InlineData(double.NaN, 2.5)]
    [InlineData(double.PositiveInfinity, 2.5)]
    [InlineData(1, 120.01)]
    [InlineData(1, 1e9)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    public void Reverbs_reject_a_room_or_a_tail_that_is_absurd(double size, double decay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Reverb(size: size, decay: decay));
        if (size == 1) Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).FdnReverb(decay: decay));
    }

    [Fact]
    public void Reverb_damping_rejects_NaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Reverb(damping: double.NaN));
    }

    [Fact]
    public void Long_but_real_delays_and_reverbs_are_still_allowed()
    {
        var sound = Saw(110).Delay(0.5, maxTime: 60).TapeDelay(0.5, maxTime: 60).Reverb(size: 100, decay: 120).FdnReverb(decay: 120);
        Assert.NotNull(sound);

        // A minute-long tail compiles and renders; a size-100 room is a few MB.
        var rendered = Saw(110).Adsr(0.005, 0.1, 0, 0.1).Reverb(size: 100, decay: 60).Render(0.25);
        Assert.False(SignalAnalysis.HasNonFinite(rendered.Left()));
    }

    [Fact]
    public void A_full_chain_renders_finite_audio_and_can_be_reused()
    {
        var chain = Saw(110).Distort(6).HighPass(80).Phaser().Chorus().Delay(0.25, 0.4).Reverb(mix: 0.3).Limit().Pan(0.2);

        var first = chain.Render(0.5, SampleRate);
        AudioAssert.IsFinite(first);
        Assert.Equal(first.ToInterleaved(), chain.Render(0.5, SampleRate).ToInterleaved());
    }
}
