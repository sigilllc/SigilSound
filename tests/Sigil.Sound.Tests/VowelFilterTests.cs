using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class VowelFilterTests
{
    // Filtered white noise: every formant shows up, so the energy at each frequency reads the formant's strength.
    private static float[] Spoken(double vowel, double resonance = 0.5) =>
        Measure.Slice(Measure.Mono(Noise(7).Vowel(vowel, resonance), 1.0), 0.1, 1.0);

    [Fact]
    public void Vowel_I_has_its_second_formant_near_2140_Hz()
    {
        var a = Spoken(vowel: 0.0);   // A: F2 at 1150 Hz
        var i = Spoken(vowel: 0.5);   // I: F2 at 2140 Hz

        Assert.True(Measure.Power(i, 2140) > Measure.Power(a, 2140) * 5,
            "I should have far more energy at 2140 Hz than A, which has no formant there.");
    }

    [Fact]
    public void Vowel_A_has_its_second_formant_near_1150_Hz()
    {
        var a = Spoken(vowel: 0.0);
        var i = Spoken(vowel: 0.5);

        Assert.True(Measure.Power(a, 1150) > Measure.Power(i, 1150) * 2,
            "A should have more energy at 1150 Hz than I, which has no formant there.");
    }

    [Fact]
    public void Higher_resonance_makes_the_formant_peak_sharper()
    {
        // The formant's peak over the energy a little off it: sharper resonance gives a bigger ratio.
        double Sharpness(double resonance)
        {
            var samples = Spoken(vowel: 0.0, resonance: resonance);
            return Measure.Power(samples, 800) / Measure.Power(samples, 1000);
        }

        Assert.True(Sharpness(1.0) > Sharpness(0.0), "Resonance 1 should ring more narrowly than resonance 0.");
    }

    [Fact]
    public void Silence_in_gives_silence_out()
    {
        var samples = Measure.Mono(Constant(0).Vowel(0.3), 0.3);

        AudioAssert.IsSilent(samples);
    }
}
