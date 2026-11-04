using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class EqualizerTests
{
    // The gain of a sound at one frequency: its steady-state RMS after the filter has settled, relative to the input.
    private static double GainAt(Sound filtered, Sound source)
    {
        var input = Measure.Slice(Measure.Mono(source, 1.0), 0.4, 1.0);
        var output = Measure.Slice(Measure.Mono(filtered, 1.0), 0.4, 1.0);
        return Measure.Rms(output) / Measure.Rms(input);
    }

    private static double ToDb(double linear) => 20 * Math.Log10(linear);

    [Fact]
    public void Bell_boosts_its_centre_by_the_stated_decibels()
    {
        var tone = Sine(1000) * 0.25;

        double gain = GainAt(tone.Peak(1000, q: 1, gainDb: 6), tone);

        Assert.InRange(ToDb(gain), 5.8, 6.2);
    }

    [Fact]
    public void Bell_leaves_distant_frequencies_alone()
    {
        var tone = Sine(100) * 0.25;

        double gain = GainAt(tone.Peak(1000, q: 3, gainDb: 12), tone);

        Assert.InRange(ToDb(gain), -0.5, 0.5);
    }

    [Fact]
    public void Zero_gain_bands_are_transparent()
    {
        var tone = Sine(440) * 0.3;

        var input = Measure.Mono(tone, 0.3);
        var output = Measure.Mono(tone.Peak(2000, q: 2, gainDb: 0).HighShelf(9000, 0).LowShelf(100, 0), 0.3);

        for (int i = 0; i < input.Length; i++)
            Assert.InRange(MathF.Abs(output[i] - input[i]), 0f, 1e-5f);
    }

    [Fact]
    public void Low_shelf_gives_direct_current_the_full_boost_and_spares_the_treble()
    {
        // The shelf's DC gain is exactly its boost: six dB doubles a constant level.
        var dc = Constant(0.1);
        Assert.InRange(Measure.Mono(dc.LowShelf(200, gainDb: 6), 0.5)[^1], 0.1995f, 0.2005f);

        var treble = Sine(10_000) * 0.2;
        Assert.InRange(ToDb(GainAt(treble.LowShelf(200, gainDb: 6), treble)), -0.1, 0.1);
    }

    [Fact]
    public void High_shelf_cuts_the_treble_and_spares_the_bass()
    {
        var treble = Sine(20_000) * 0.2;
        Assert.InRange(ToDb(GainAt(treble.HighShelf(8000, gainDb: -6), treble)), -6.5, -5.5);

        var bass = Sine(100) * 0.2;
        Assert.InRange(ToDb(GainAt(bass.HighShelf(8000, gainDb: -6), bass)), -0.1, 0.1);
    }

    [Fact]
    public void Equalize_with_only_a_low_boost_matches_a_low_shelf()
    {
        var tone = Sine(60) * 0.2;

        var viaTone = Measure.Mono(tone.Equalize(lowDb: 6), 0.5);
        var viaShelf = Measure.Mono(tone.LowShelf(200, 6), 0.5);

        for (int i = 0; i < viaTone.Length; i++)
            Assert.Equal(viaShelf[i], viaTone[i], 1e-6f);
    }
}
