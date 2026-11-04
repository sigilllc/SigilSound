using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class WavefolderTests
{
    // The window is 0.8 s, which holds a whole number of cycles of 110 Hz and of each of its harmonics,
    // so the Goertzel bins measure the harmonics cleanly.
    [Fact]
    public void Symmetric_folding_produces_only_odd_harmonics()
    {
        var samples = Measure.Slice(Measure.Mono(Sine(110).Fold(amount: 3, symmetry: 0, mix: 1), 1.0), 0.2, 1.0);

        double third = Measure.Power(samples, 330);
        Assert.True(Measure.Power(samples, 220) < third * 1e-4, "A zero-symmetry fold is odd-symmetric, so there is no second harmonic.");
        Assert.True(Measure.Power(samples, 440) < third * 1e-4, "A zero-symmetry fold is odd-symmetric, so there is no fourth harmonic.");
        Assert.True(third > 0, "The third harmonic should be present.");
    }

    [Fact]
    public void Symmetry_brings_in_even_harmonics()
    {
        var samples = Measure.Slice(Measure.Mono(Sine(110).Fold(amount: 3, symmetry: 0.25, mix: 1), 1.0), 0.2, 1.0);

        double fundamental = Measure.Power(samples, 110);
        Assert.True(Measure.Power(samples, 220) > fundamental * 1e-3, "An offset before the fold should add a second harmonic.");
    }

    [Fact]
    public void A_quiet_sine_under_unit_drive_keeps_its_level()
    {
        // Below ±1 the fold is the identity, so the only change is the DC blocker. Its corner is low, so it
        // shifts a 110 Hz sine slightly in phase, which is correct; the level must not change.
        var input = Measure.Slice(Measure.Mono(Sine(110) * 0.3, 1.0), 0.2, 1.0);
        var output = Measure.Slice(Measure.Mono((Sine(110) * 0.3).Fold(amount: 1, symmetry: 0, mix: 1), 1.0), 0.2, 1.0);

        Assert.InRange(Measure.Rms(output) / Measure.Rms(input), 0.99, 1.01);
    }

    [Fact]
    public void Heavy_folding_stays_close_to_the_unit_range()
    {
        var samples = Measure.Mono(Sine(110).Fold(amount: 7, symmetry: 0.3, mix: 1), 1.0);

        Assert.True(Measure.Peak(Measure.Slice(samples, 0.2, 1.0)) < 1.2f, "The fold must reflect peaks, not let them grow.");
        Assert.False(SignalAnalysis.HasNonFinite(samples));
    }
}
