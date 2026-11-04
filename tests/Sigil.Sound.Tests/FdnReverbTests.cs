using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class FdnReverbTests
{
    private static Sound Impulse => Fn(t => t < 0.001 ? 1.0 : 0.0);

    [Fact]
    public void Tail_falls_away_over_the_decay_time()
    {
        // With a one-second decay the tail is 60 dB down after a second. Compare the level after the first half second
        // of the tail with the level after the second half second: it should be far lower, not a slow fade.
        var audio = Impulse.FdnReverb(decay: 1.0, size: 1, damping: 0.2, mix: 1).Render(2.0, Measure.SampleRate);
        var samples = audio.ToMono();

        double early = Measure.Rms(Measure.Slice(samples, 0.2, 0.4));
        double late = Measure.Rms(Measure.Slice(samples, 1.4, 1.6));
        Assert.True(late < early * 0.05, $"Late tail {late} should be at least 26 dB under the early tail {early}.");
    }

    [Fact]
    public void Tail_is_stereo_and_its_channels_are_decorrelated()
    {
        var audio = Impulse.FdnReverb(decay: 2.0, size: 1, damping: 0.3, mix: 1).Render(1.0, Measure.SampleRate);
        var left = Measure.Slice(audio.Left(), 0.2, 0.9);
        var right = Measure.Slice(audio.Right(), 0.2, 0.9);

        double dot = 0, leftEnergy = 0, rightEnergy = 0;
        for (int i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftEnergy += left[i] * left[i];
            rightEnergy += right[i] * right[i];
        }
        double correlation = dot / Math.Sqrt(leftEnergy * rightEnergy);
        Assert.True(correlation < 0.8, $"The channels correlate at {correlation}; a stereo reverb should spread them.");
    }

    // Sum of the power in 50 Hz steps across a band: a coarse but direct measure of how much energy sits in it.
    private static double BandPower(float[] samples, double fromHz, double toHz)
    {
        double total = 0;
        for (double f = fromHz; f <= toHz; f += 50)
            total += Measure.Power(samples, f);
        return total;
    }

    [Fact]
    public void Sheen_lifts_the_high_band_of_a_noisy_tail()
    {
        // Noise is the tail a reverb is made of. The octave-up copy moves the 2-4 kHz content into 4-8 kHz.
        var burst = Noise(3) * Fn(t => t < 0.2 ? 0.5 : 0.0);
        var plain = Measure.Slice(Measure.Mono(burst.FdnReverb(decay: 2.0, size: 1, sheen: 0, mix: 1), 1.5), 0.4, 1.4);
        var shimmer = Measure.Slice(Measure.Mono(burst.FdnReverb(decay: 2.0, size: 1, sheen: 0.7, mix: 1), 1.5), 0.4, 1.4);

        Assert.True(BandPower(shimmer, 4000, 8000) > BandPower(plain, 4000, 8000) * 1.5,
            "Sheen should put clearly more energy between 4 and 8 kHz.");
    }

    [Fact]
    public void Sheen_does_not_change_how_fast_the_tail_decays()
    {
        // The octave-up copy is mixed in after the loop, so the decay time must be the same with or without it.
        var plain = Measure.Mono(Impulse.FdnReverb(decay: 5.0, size: 1, sheen: 0, mix: 1), 4.0);
        var shimmer = Measure.Mono(Impulse.FdnReverb(decay: 5.0, size: 1, sheen: 0.7, mix: 1), 4.0);

        double plainFall = Measure.Rms(Measure.Slice(plain, 3.0, 4.0)) / Measure.Rms(Measure.Slice(plain, 0.5, 1.5));
        double shimmerFall = Measure.Rms(Measure.Slice(shimmer, 3.0, 4.0)) / Measure.Rms(Measure.Slice(shimmer, 0.5, 1.5));
        Assert.InRange(shimmerFall / plainFall, 0.75, 1.25);
    }

    [Fact]
    public void Maximum_sheen_with_a_long_decay_stays_stable_on_a_sustained_tone()
    {
        // A steady tone is the input most likely to make a feedback path grow. The level must level off, not climb.
        var audio = (Sine(1000) * 0.5).FdnReverb(decay: 5.0, size: 1, sheen: 0.7, mix: 1).Render(4.0, Measure.SampleRate);

        AudioAssert.IsFinite(audio);
        var samples = audio.ToMono();
        double early = Measure.Rms(Measure.Slice(samples, 0.5, 1.0));
        double late = Measure.Rms(Measure.Slice(samples, 3.0, 4.0));
        Assert.True(late < early * 2, $"The tone's level grew from {early} to {late} with sheen at maximum.");
    }

    [Fact]
    public void Maximum_sheen_with_a_long_decay_stays_stable_after_an_impulse()
    {
        var audio = Impulse.FdnReverb(decay: 5.0, size: 1, sheen: 0.7, mix: 1).Render(4.0, Measure.SampleRate);

        AudioAssert.IsFinite(audio);
        var samples = audio.ToMono();
        Assert.True(Measure.Rms(Measure.Slice(samples, 3.0, 4.0)) < Measure.Rms(Measure.Slice(samples, 0.5, 1.5)),
            "The tail must keep falling with sheen turned all the way up.");
    }

    [Fact]
    public void Predelay_holds_back_the_first_reflection_by_its_time()
    {
        // The lines are at least 30 ms long, so with a 0.1 s predelay nothing can come out before 0.1 s.
        var audio = Impulse.FdnReverb(decay: 2, predelay: 0.1, mix: 1).Render(0.5, Measure.SampleRate);

        AudioAssert.IsSilent(Measure.Slice(audio.ToMono(), 0.0, 0.09));
        Assert.True(Measure.Peak(Measure.Slice(audio.ToMono(), 0.1, 0.5)) > 0);
    }

    [Fact]
    public void Arguments_outside_their_ranges_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.FdnReverb(decay: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.FdnReverb(size: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.FdnReverb(size: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.FdnReverb(damping: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Impulse.FdnReverb(predelay: 0.9));
    }
}
