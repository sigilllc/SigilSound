using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>Subtraction, Range over parameters, Mix, Slew, Hold, Gate, DcBlock and the noise colours.</summary>
public class SignalOperatorTests
{
    private const int Rate = 48_000;

    private static float[] Mono(Sound sound, double seconds = 0.5, int blockSize = 256) =>
        sound.Render(seconds, Rate, blockSize).ToMono();

    private static double Mean(float[] samples) => samples.Average(s => (double)s);

    // ---- Subtraction ----

    [Fact]
    public void A_sound_minus_itself_is_silence()
    {
        var saw = Saw(110);
        AudioAssert.IsSilent(Mono(saw - saw), 0f);
    }

    [Fact]
    public void Subtraction_is_addition_of_the_negative()
    {
        var a = Mono(Saw(110) - Square(165));
        var b = Mono(Saw(110) + Square(165) * -1);
        Assert.Equal(b, a);
    }

    [Fact]
    public void A_sound_minus_a_number_and_a_number_minus_a_sound()
    {
        Assert.All(Mono(Constant(0.75) - 0.5), s => Assert.Equal(0.25f, s, 6));
        Assert.All(Mono(1 - Constant(0.75)), s => Assert.Equal(0.25f, s, 6));
    }

    [Fact]
    public void A_sound_minus_a_control_follows_the_control()
    {
        var offset = new Control(0.2, 0, 1, slewSeconds: 0);
        var op = (Constant(1) - offset).Compile(new RenderContext(Rate));
        var block = new StereoFrame[256];
        op.RenderBlock(block);
        Assert.Equal(0.8f, block[^1].Left, 5);
    }

    [Fact]
    public void Negation_flips_the_polarity()
    {
        var a = Mono(-Saw(110));
        var b = Mono(Saw(110));
        for (int i = 0; i < a.Length; i++)
            Assert.Equal(-b[i], a[i]);
    }

    [Fact]
    public void A_sound_used_on_both_sides_is_rendered_once()
    {
        int compiles = 0;
        var counted = Sound.FromOperator(_ =>
        {
            compiles++;
            return Operator.From(block => block.Fill(StereoFrame.Mono(0.5f)));
        }, "Counted");

        AudioAssert.IsSilent(Mono(counted - counted), 0f);
        Assert.Equal(1, compiles);
    }

    // ---- Range over parameters ----

    [Fact]
    public void Range_with_parameters_matches_Range_with_numbers_when_they_are_fixed()
    {
        Assert.Equal(Mono(Sine(3).Range(200, 2000)), Mono(Sine(3).Range((Param)200, (Param)2000)));
    }

    [Fact]
    public void A_control_moves_the_top_of_a_range()
    {
        var top = new Control(1000, 0, 4000, slewSeconds: 0);
        var op = Constant(1).Range(200, top).Compile(new RenderContext(Rate));
        var block = new StereoFrame[256];
        op.RenderBlock(block);
        Assert.Equal(1000f, block[^1].Left, 2);

        top.Value = 3000;
        op.RenderBlock(block);
        Assert.Equal(3000f, block[^1].Left, 2);
    }

    // ---- Mix ----

    [Fact]
    public void Mix_at_its_ends_is_one_sound_or_the_other()
    {
        Assert.All(Mono(Constant(0.4).Mix(Constant(-0.6), 0)), s => Assert.Equal(0.4f, s, 5));
        Assert.All(Mono(Constant(0.4).Mix(Constant(-0.6), 1)), s => Assert.Equal(-0.6f, s, 5));
    }

    [Fact]
    public void Mix_halfway_is_equal_power()
    {
        Assert.All(Mono(Constant(1).Mix(Constant(0))), s => Assert.Equal(MathF.Sqrt(0.5f), s, 5));
    }

    // ---- Slew ----

    [Fact]
    public void Slew_turns_a_jump_into_a_glide_that_arrives()
    {
        // A slewed signal starts where its input is, instead of gliding up from zero.
        Assert.Equal(0.7f, Mono(Constant(0.7).Slew(0.1), 0.01)[0], 5);
        var jump = Mono(Square(4).Slew(0.01), 0.2);
        int flip = Rate / 8;      // a 4 Hz square flips after an eighth of a second
        Assert.True(jump[flip + 10] > -0.5f, "the glide should not have arrived after 10 frames");
        Assert.True(jump[flip + 5 * 480] < -0.98f, "the glide should have nearly arrived after five time constants");
    }

    [Fact]
    public void Slew_can_rise_slowly_and_fall_fast()
    {
        var samples = Mono(Square(4).Range(0, 1).Slew(rise: 0.1, fall: 0.001), 0.5);
        int down = Rate / 8, up = Rate / 4;
        Assert.True(samples[down + 48 * 5] < 0.01f, "falls within a few milliseconds");
        Assert.True(samples[up + 480] < 0.2f, "rises slowly");
    }

    // ---- Hold ----

    [Fact]
    public void Hold_steps_at_its_rate()
    {
        var samples = Mono(Noise().Hold(10), 1.0);
        int changes = 0;
        for (int i = 1; i < samples.Length; i++)
            if (samples[i] != samples[i - 1])
                changes++;
        Assert.InRange(changes, 9, 10);
    }

    [Fact]
    public void Hold_with_a_host_clock_steps_where_it_would_have_since_the_clock_started()
    {
        var clock = new HostClock(Rate);
        clock.Advance(Rate / 20 + 100);          // half a step and a bit past the first 10 Hz tick
        var op = Saw(1).Hold(10).Compile(new RenderContext(Rate, 4800) { Clock = clock });
        var block = new StereoFrame[4800];
        op.RenderBlock(block);
        // The next tick is at 0.1 s on the clock, which is 0.1 - (0.05 + 100 / 48000) seconds into this block.
        int expected = (int)Math.Round(0.1 * Rate) - (Rate / 20 + 100);
        Assert.Equal(block[0].Left, block[expected - 2].Left);
        Assert.NotEqual(block[0].Left, block[expected + 2].Left);
    }

    // ---- Gate ----

    [Fact]
    public void Gate_silences_what_is_below_the_threshold_and_passes_what_is_above()
    {
        var quiet = Mono((Sine(440) * 0.005).Gate(-30), 0.5);    // -46 dB
        var loud = Mono((Sine(440) * 0.5).Gate(-30), 0.5);       // -6 dB
        Assert.True(SignalAnalysis.Peak(quiet) < 1e-4f);
        Assert.True(SignalAnalysis.Peak(loud.AsSpan(Rate / 10)) > 0.49f);
    }

    [Fact]
    public void A_sidechain_opens_the_gate()
    {
        var pad = Sine(220) * 0.3;
        var key = Square(2).Range(0, 1) * Sine(1000);
        var samples = Mono(pad.Gate(-20, sidechain: key), 1.0);
        int open = (int)(0.1 * Rate), shut = (int)(0.4 * Rate);
        Assert.True(SignalAnalysis.Peak(samples.AsSpan(open, 2000)) > 0.25f);
        Assert.True(SignalAnalysis.Peak(samples.AsSpan(shut, 2000)) < 0.01f);
    }

    // ---- DcBlock ----

    [Fact]
    public void DcBlock_recentres_a_narrow_pulse()
    {
        var pulse = Mono(Square(110, width: 0.1), 1.0);
        var blocked = Mono(Square(110, width: 0.1).DcBlock(), 2.0)[Rate..];
        Assert.True(Mean(pulse) < -0.7);
        Assert.InRange(Mean(blocked), -0.01, 0.01);
    }

    [Fact]
    public void DcBlock_passes_the_audible_range()
    {
        var samples = Mono(Sine(440).DcBlock(), 1.0)[(Rate / 2)..];
        Assert.InRange(SignalAnalysis.Rms(samples), 0.70, 0.71);
    }

    // ---- Noise colours ----

    [Fact]
    public void Pink_and_brown_noise_are_darker_than_white()
    {
        static double HighShare(Sound noise)
        {
            var samples = noise.Render(2, Rate).ToMono();
            double all = SignalAnalysis.Rms(samples);
            double high = SignalAnalysis.Rms(noise.HighPass(4000).Render(2, Rate).ToMono());
            return high / all;
        }

        double white = HighShare(Noise()), pink = HighShare(PinkNoise()), brown = HighShare(BrownNoise());
        Assert.True(pink < white * 0.6, $"pink {pink} white {white}");
        Assert.True(brown < pink * 0.5, $"brown {brown} pink {pink}");
    }

    [Fact]
    public void Noise_colours_stay_within_full_scale_and_repeat_with_the_seed()
    {
        foreach (var noise in new[] { PinkNoise(), BrownNoise() })
        {
            var samples = noise.Render(10, Rate).ToMono();
            Assert.InRange(SignalAnalysis.Peak(samples), 0.3f, 1.2f);
            Assert.InRange(Mean(samples), -0.05, 0.05);
        }

        Assert.Equal(Mono(PinkNoise(3)), Mono(PinkNoise(3)));
        Assert.NotEqual(Mono(PinkNoise(3)), Mono(PinkNoise(4)));
    }

    // ---- What every new operator owes ----

    public static TheoryData<string> Operators() => new(Cases().Select(c => c.Name));

    private static IEnumerable<(string Name, Func<Sound> Build)> Cases()
    {
        var lfo = Sine(3);
        yield return ("a - b", () => Saw(110) - Square(165));
        yield return ("a - p", () => Saw(110) - lfo);
        yield return ("p - a", () => lfo - Saw(110));
        yield return ("-a", () => -Saw(110));
        yield return ("Range(Param, Param)", () => lfo.Range(Sine(0.5).Range(100, 200), 2000));
        yield return ("Mix", () => Saw(110).Mix(Square(220), Sine(0.5).Range(0, 1)));
        yield return ("Slew", () => Square(3).Range(200, 800).Slew(0.02));
        yield return ("Hold", () => Saw(0.7).Hold(8));
        yield return ("Gate", () => (Noise() * Square(3).Range(0.02, 1)).Gate(-20));
        yield return ("DcBlock", () => Square(110, width: 0.1).DcBlock());
        yield return ("PinkNoise", () => PinkNoise());
        yield return ("BrownNoise", () => BrownNoise());
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void It_sounds_the_same_at_every_block_size(string name)
    {
        var build = Cases().Single(c => c.Name == name).Build;
        var reference = Mono(build(), 0.5, 256);
        foreach (int blockSize in new[] { 1, 37 })
        {
            var other = Mono(build(), 0.5, blockSize);
            for (int i = 0; i < reference.Length; i++)
                Assert.Equal(reference[i], other[i], 5);
        }
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void It_sounds_alike_at_every_sample_rate(string name)
    {
        var build = Cases().Single(c => c.Name == name).Build;
        double At(int rate)
        {
            var samples = build().Render(2, rate).ToMono();
            return SignalAnalysis.Rms(samples.AsSpan(rate / 2));
        }

        double reference = At(48_000);
        foreach (int rate in new[] { 44_100, 96_000 })
            Assert.InRange(At(rate), reference * 0.85, reference * 1.15);
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void It_allocates_nothing_while_it_renders(string name)
    {
        var op = Cases().Single(c => c.Name == name).Build().Compile(new RenderContext(Rate));
        var block = new StereoFrame[256];
        op.RenderBlock(block);
        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 20; i++)
                op.RenderBlock(block);
        });
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void It_describes_itself(string name)
    {
        var text = Cases().Single(c => c.Name == name).Build().ToString();
        Assert.False(string.IsNullOrWhiteSpace(text));
    }
}
