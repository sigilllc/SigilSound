using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>Inputs a host or a script can produce by mistake: an empty block, a parameter that is not a number.</summary>
public class RobustnessTests
{
    private const int Rate = 48_000;

    private static IEnumerable<(string Name, Sound Sound)> Everything()
    {
        var src = Saw(110);
        yield return ("Sine", Sine());
        yield return ("Saw", Saw());
        yield return ("Square", Square());
        yield return ("Triangle", Triangle());
        yield return ("Noise", Noise());
        yield return ("PinkNoise", PinkNoise());
        yield return ("BrownNoise", BrownNoise());
        yield return ("Fn", Fn(t => (float)Math.Sin(t)));
        yield return ("Constant", Constant(0.5));
        yield return ("Sample", Sample(src.Render(0.1)));
        yield return ("LowPass", src.LowPass());
        yield return ("HighPass", src.HighPass());
        yield return ("BandPass", src.BandPass());
        yield return ("Notch", src.Notch());
        yield return ("Peak", src.Peak());
        yield return ("LowShelf", src.LowShelf());
        yield return ("HighShelf", src.HighShelf());
        yield return ("Vowel", src.Vowel());
        yield return ("Adsr", src.Adsr());
        yield return ("Range", src.Range());
        yield return ("Range(Param, Param)", src.Range(Sine(1), Constant(2)));
        yield return ("Pan", src.Pan(0.3));
        yield return ("Balance", src.Balance(0.3));
        yield return ("Spread", src.Spread());
        yield return ("Limit", src.Limit());
        yield return ("Compress", src.Compress());
        yield return ("FollowEnvelope", src.FollowEnvelope());
        yield return ("Delay", src.Delay());
        yield return ("TapeDelay", src.TapeDelay());
        yield return ("Reverb", src.Reverb());
        yield return ("FdnReverb", src.FdnReverb());
        yield return ("Comb", src.Comb());
        yield return ("Chorus", src.Chorus());
        yield return ("Phaser", src.Phaser());
        yield return ("PitchShift", src.PitchShift());
        yield return ("Distort", src.Distort());
        yield return ("Fold", src.Fold());
        yield return ("BitCrush", src.BitCrush());
        yield return ("Feedback", src.Feedback(l => l * 0.5));
        yield return ("Convolve", src.Convolve(Noise().Adsr(0.001, 0.05, 0, 0.01).Render(0.1)));
        yield return ("a + b", src + Sine());
        yield return ("a * b", src * Sine());
        yield return ("a - b", src - Sine());
        yield return ("-a", -src);
        yield return ("Mix", src.Mix(Sine()));
        yield return ("Slew", src.Slew());
        yield return ("Hold", src.Hold());
        yield return ("Gate", src.Gate());
        yield return ("DcBlock", src.DcBlock());
    }

    public static TheoryData<string> Names() => new(Everything().Select(e => e.Name));

    [Theory]
    [MemberData(nameof(Names))]
    public void An_empty_block_renders_nothing_and_does_not_throw(string name)
    {
        var sound = Everything().Single(e => e.Name == name).Sound;
        var plain = sound.Compile(new RenderContext(Rate));
        var clocked = sound.Compile(new RenderContext(Rate) { Clock = new HostClock(Rate) });

        plain.RenderBlock(Span<StereoFrame>.Empty);
        clocked.RenderBlock(Span<StereoFrame>.Empty);

        // And still renders normally afterwards.
        var block = new StereoFrame[256];
        plain.RenderBlock(block);
        AudioAssert.IsFinite(new AudioBuffer(block, Rate));
    }

    public static TheoryData<string> Mixed() => new("Delay", "TapeDelay", "Reverb", "FdnReverb", "Phaser", "Fold");

    [Theory]
    [MemberData(nameof(Mixed))]
    public void A_mix_that_is_not_a_number_plays_dry(string name)
    {
        Func<double, float> mix = t => t is > 0.1 and < 0.11 ? float.NaN : 0.5f;
        var src = Saw(220);
        var sound = name switch
        {
            "Delay" => src.Delay(mix: mix),
            "TapeDelay" => src.TapeDelay(mix: mix),
            "Reverb" => src.Reverb(mix: mix),
            "FdnReverb" => src.FdnReverb(mix: mix),
            "Phaser" => src.Phaser(mix: mix),
            _ => src.Fold(mix: mix),
        };

        AudioAssert.IsFinite(sound.Render(0.3, Rate));
    }
}
