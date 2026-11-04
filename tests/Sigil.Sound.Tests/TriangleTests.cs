using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class TriangleTests
{
    private const int SampleRate = Measure.SampleRate;

    private static float At(float[] samples, double seconds) => samples[(int)Math.Round(seconds * SampleRate)];

    // The unrounded triangle at a phase, for comparison with the band-limited output away from its corners.
    private static double NaiveAt(double phase, double skew) =>
        phase < skew ? -1 + 2 * phase / skew : 1 - 2 * (phase - skew) / (1 - skew);

    // A naive triangle built from Fn, so the test can compare the rounded corners with the sharp ones.
    private static Sound NaiveTriangle(double frequency) =>
        Fn(t => NaiveAt(t * frequency - Math.Floor(t * frequency), 0.5));

    [Fact]
    public void Symmetric_triangle_peaks_at_half_the_cycle_and_crosses_zero_at_the_quarters()
    {
        var samples = Measure.Mono(Triangle(1, skew: 0.5), 1.0);

        Assert.InRange(At(samples, 0.5), 0.98f, 1f);
        Assert.InRange(At(samples, 0.0), -1f, -0.98f);
        Assert.InRange(At(samples, 0.25), -0.02f, 0.02f);
        Assert.InRange(At(samples, 0.75), -0.02f, 0.02f);
    }

    [Fact]
    public void Skew_moves_the_peak_within_the_cycle()
    {
        var samples = Measure.Mono(Triangle(1, skew: 0.8), 1.0);

        Assert.InRange(At(samples, 0.8), 0.98f, 1f);
        Assert.InRange(At(samples, 0.4), -0.02f, 0.02f);
    }

    [Fact]
    public void Skew_at_zero_is_the_steepest_allowed_fall_across_the_cycle()
    {
        // A true saw would need a jump, which a triangle must not have, so skew 0 is limited to 0.01: a 1% rise, then a fall
        // across the rest of the cycle. Away from that short rise the wave falls like a saw.
        var samples = Measure.Mono(Triangle(1, skew: 0), 1.0);

        Assert.InRange(At(samples, 0.5), -0.02f, 0.02f);
        Assert.InRange(At(samples, 0.999), -1f, -0.98f);
        Assert.InRange(At(samples, 0.001), -1f, -0.7f);
    }

    [Fact]
    public void Band_limited_triangle_matches_the_naive_shape_away_from_its_corners()
    {
        // At 100 Hz the corners are a few samples wide, so a sample a few milliseconds from any corner must match exactly.
        var rounded = Measure.Mono(Triangle(100), 0.1);
        var naive = Measure.Mono(NaiveTriangle(100), 0.1);

        for (int i = 0; i < rounded.Length; i++)
        {
            double phase = (i / (double)SampleRate * 100) % 1.0;
            if (Math.Min(Math.Abs(phase - 0.5), Math.Min(phase, 1 - phase)) > 0.01)
                Assert.InRange(MathF.Abs(rounded[i] - naive[i]), 0f, 1e-3f);
        }
    }

    [Fact]
    public void Rounded_corners_reduce_aliasing_at_high_pitch()
    {
        // 9.7 kHz at 48 kHz: the fifth harmonic folds down to 500 Hz, where no true harmonic sits, so energy there is pure aliasing.
        var rounded = Measure.Mono(Triangle(9_700), 0.1);
        var naive = Measure.Mono(NaiveTriangle(9_700), 0.1);

        double roundedAlias = Measure.Power(rounded, 500);
        double naiveAlias = Measure.Power(naive, 500);
        Assert.True(roundedAlias < naiveAlias * 0.5,
            $"Rounded corners should alias less: rounded {roundedAlias:E3}, naive {naiveAlias:E3}.");
    }

    [Fact]
    public void Output_stays_within_the_unit_range_at_every_pitch_and_skew()
    {
        foreach (double skew in new[] { 0.01, 0.3, 0.5, 0.97 })
        foreach (double hz in new[] { 40.0, 1_000, 15_000 })
        {
            var samples = Measure.Mono(Triangle(hz, skew: skew), 0.2);
            Assert.True(Measure.Peak(samples) < 1.05f, $"peak {Measure.Peak(samples)} at {hz} Hz, skew {skew}");
        }
    }

    [Fact]
    public void Constant_skew_outside_zero_to_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Triangle(110, skew: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Triangle(110, skew: -0.1));
    }

    [Fact]
    public void Triangle_as_a_modulator_maps_its_extremes_onto_range()
    {
        // Used as a parameter, a 1 Hz triangle through Range runs from 200 at the start of the cycle, up to 400 at the middle,
        // and back down: the values a filter cutoff would take, one per beat of the sweep.
        var cutoff = Measure.Mono(Triangle(1).Range(200, 400), 1.0);

        Assert.InRange(At(cutoff, 0.0), 199f, 201f);
        Assert.InRange(At(cutoff, 0.25), 299f, 301f);
        Assert.InRange(At(cutoff, 0.5), 399f, 400.5f);
        Assert.InRange(At(cutoff, 0.75), 299f, 301f);
    }
}
