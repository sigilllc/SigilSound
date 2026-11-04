using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class CompressorTests
{
    private static double ToDb(double linear) => 20 * Math.Log10(linear);

    private static float Last(float[] samples) => samples[^1];

    [Fact]
    public void Steady_level_above_threshold_is_reduced_by_the_ratio()
    {
        // 0.5 is -6.02 dB. Four dB of input above a -20 dB threshold comes out as one dB, so the level is
        // -20 + (13.98 / 4) = -16.5 dB, a reduction of 10.5 dB.
        var samples = Measure.Mono(Constant(0.5).Compress(thresholdDb: -20, ratio: 4, attack: 0.001, release: 0.05, kneeDb: 0), 1.0);

        Assert.InRange(ToDb(Last(samples)), -16.6, -16.45);
    }

    [Fact]
    public void Level_below_threshold_passes_unchanged()
    {
        var samples = Measure.Mono(Constant(0.05).Compress(thresholdDb: -20, ratio: 8, attack: 0.001, release: 0.05), 0.5);

        Assert.Equal(0.05f, Last(samples), 1e-5f);
    }

    [Fact]
    public void Makeup_gain_adds_the_stated_decibels()
    {
        // Six dB of makeup doubles the level of a signal that stays under the threshold.
        var samples = Measure.Mono(Constant(0.05).Compress(thresholdDb: -20, ratio: 8, makeupGainDb: 6), 0.5);

        Assert.Equal(0.1f, Last(samples), 1e-3f);
    }

    [Fact]
    public void Soft_knee_eases_the_gain_in_without_a_corner()
    {
        // Sweep the input level in half-decibel steps through the knee (threshold -20 dB, knee 6 dB). Each step may change
        // the output by at most the half-decibel the input moved, and never by less than the ratio allows above the knee.
        double previous = double.NaN, previousStep = double.NaN;
        for (double inputDb = -30; inputDb <= -10; inputDb += 0.5)
        {
            var samples = Measure.Mono(Constant(Math.Pow(10, inputDb / 20)).Compress(thresholdDb: -20, ratio: 4, attack: 0.001, release: 0.05, kneeDb: 6), 0.3);
            double outputDb = ToDb(Last(samples));
            if (!double.IsNaN(previous))
            {
                double step = outputDb - previous;
                Assert.InRange(step, 0.1, 0.5 + 1e-3);
                if (!double.IsNaN(previousStep))
                    Assert.True(step <= previousStep + 0.02, $"The compression slope steepened at {inputDb} dB, which would be a corner.");
                previousStep = step;
            }
            previous = outputDb;
        }
    }

    [Fact]
    public void Sidechain_ducks_the_main_sound_while_the_key_is_loud()
    {
        var key = Fn(t => t < 0.3 ? 1.0 : 0.0);
        var ducked = Measure.Mono(Constant(0.5).Compress(thresholdDb: -20, ratio: 8, attack: 0.002, release: 0.05, kneeDb: 0, sidechain: key), 1.0);

        // A key at 0 dB is 20 dB over the threshold: 1:8 turns that into 17.5 dB of reduction, so 0.5 becomes 0.067.
        Assert.InRange(Measure.Slice(ducked, 0.2, 0.3)[^1], 0.06f, 0.075f);
        Assert.Equal(0.5f, Measure.Slice(ducked, 0.8, 0.9)[^1], 1e-3f);
    }

    [Fact]
    public void Lookahead_lowers_the_gain_before_a_transient_reaches_the_output()
    {
        // A sudden loud step at 0.5 s. With a 5 ms lookahead the detector has heard the step before the audio plays it.
        var step = Fn(t => t < 0.5 ? 0.0 : 0.8);
        var withoutLookahead = Measure.Mono(step.Compress(thresholdDb: -20, ratio: 8, attack: 0.005, release: 0.1, lookahead: 0), 0.6);
        var withLookahead = Measure.Mono(step.Compress(thresholdDb: -20, ratio: 8, attack: 0.005, release: 0.1, lookahead: 0.005), 0.6);

        float peakWithout = Measure.Peak(Measure.Slice(withoutLookahead, 0.5, 0.52));
        float peakWith = Measure.Peak(Measure.Slice(withLookahead, 0.5, 0.52));
        Assert.True(peakWith < peakWithout - 0.2f, $"Lookahead peak {peakWith} should be well below the no-lookahead peak {peakWithout}.");
    }

    [Fact]
    public void Lookahead_defaults_to_three_milliseconds_of_latency()
    {
        // A single loud frame, below the threshold so nothing is compressed. With the default lookahead it comes out 3 ms late.
        var click = Fn(t => t < 0.5 / Measure.SampleRate ? 0.1 : 0.0);

        var samples = Measure.Mono(click.Compress(thresholdDb: 0), 0.05);

        Assert.InRange(Array.IndexOf(samples, samples.Max()), (int)(0.0029 * Measure.SampleRate), (int)(0.0031 * Measure.SampleRate));
    }

    [Fact]
    public void Stereo_detection_is_linked_so_both_channels_share_one_gain()
    {
        // The detector follows the louder channel, so the quiet right channel is turned down by the same amount as the left.
        var stereo = Measure.Stereo(0.5f, 0.05f)
            .Compress(thresholdDb: -20, ratio: 4, attack: 0.001, release: 0.05, kneeDb: 0)
            .Render(0.5);

        var last = stereo[stereo.FrameCount - 1];
        Assert.InRange(last.Right / last.Left, 0.0999f, 0.1001f);
    }
}
