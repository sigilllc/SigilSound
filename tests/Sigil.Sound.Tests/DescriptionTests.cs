using System.Globalization;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// <c>ToString</c> says how a sound is built. It must not depend on the machine's culture, and it must cost nothing until it is
/// asked for and stay small when it is, however a sound is reused.
/// </summary>
public class DescriptionTests
{
    // A culture that writes 0,5 for a half, without relying on a culture being installed.
    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        return culture;
    }

    private static T InCommaCulture<T>(Func<T> work)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CommaCulture();
        try { return work(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void A_description_reads_the_same_in_every_culture()
    {
        var sounds = new Func<Sound>[]
        {
            () => Sine(0.25).Range(0.5, 2000.5),
            () => Saw(110.5).Reverb(size: 1.5, decay: 2.5),
            () => Saw(110).FdnReverb(decay: 3.5, size: 1.5),
            () => Saw(110).Limit(0.9),
            () => Constant(0.25),
            () => Saw(110.5).LowPass(800.5),
        };

        foreach (var make in sounds)
        {
            string expected = make().ToString();
            string inCommaCulture = InCommaCulture(() => make().ToString());

            Assert.Equal(expected, inCommaCulture);
        }

        Assert.Equal("Sine(0.25).Range(0.5, 2000.5)", Sine(0.25).Range(0.5, 2000.5).ToString());
        Assert.Equal("Constant(0.25)", InCommaCulture(() => Constant(0.25).ToString()));
    }

    [Fact]
    public void A_sample_describes_its_length_the_same_in_every_culture()
    {
        var buffer = new AudioBuffer(new StereoFrame[24_000], 48_000);

        Assert.Equal("Sample(0.5s, OneShot)", Sample(buffer).ToString());
        Assert.Equal("Sample(0.5s, OneShot)", InCommaCulture(() => Sample(buffer).ToString()));
    }

    [Fact]
    public void Ordinary_descriptions_are_unchanged()
    {
        Assert.Equal("Saw(110).LowPass(800, 0.7071)", Saw(110).LowPass(800).ToString());
        Assert.Equal("(Sine(440) + Saw(220))", (Sine(440) + Saw(220)).ToString());
        Assert.Equal("Saw(Sine(5))", Saw(Sine(5)).ToString());
    }

    [Fact]
    public void Reusing_a_sound_does_not_make_its_description_grow_without_bound()
    {
        // Each step doubles the sound's text if it is written out in full: twenty-two steps would be about 60 million characters.
        var sound = Sine(440);
        for (int i = 0; i < 22; i++)
            sound = sound + sound;

        string text = sound.ToString();

        Assert.InRange(text.Length, 1, 20_000);
        Assert.Contains("...", text);
    }

    [Fact]
    public void Building_a_sound_describes_nothing_and_a_very_long_chain_is_cheap()
    {
        var sound = Saw(110);
        for (int i = 0; i < 20_000; i++)
            sound = sound.Gain(0.999);

        Assert.InRange(sound.ToString().Length, 1, 20_000);
    }

    [Fact]
    public void A_description_that_fits_is_written_out_in_full()
    {
        var sound = Saw(110);
        for (int i = 0; i < 20; i++)
            sound = sound.Gain(0.5);

        Assert.DoesNotContain("...", sound.ToString());
        Assert.StartsWith("Saw(110).Gain(0.5)", sound.ToString());
    }

    [Fact]
    public void Describing_a_sound_twice_gives_the_same_text()
    {
        var sound = Saw(110).LowPass(Sine(0.5).Range(200, 2000)).Reverb();

        Assert.Equal(sound.ToString(), sound.ToString());
    }
}
