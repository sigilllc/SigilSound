using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Patterns can come from a script of unknown origin, so a pattern that is too big or too deep must be refused when it is built,
/// and one that goes wrong later must never throw out of the audio render.
/// </summary>
public class PatternHardeningTests
{
    private const int Rate = 48_000;

    private static Pattern<Note> Dense() => Mini.Notes("[c3*1024]*1024");

    private static Pattern<T> Deep<T>(Pattern<T> pattern, int links)
    {
        // Alternating keeps the time scale where it started, so only the depth grows.
        for (int i = 0; i < links; i++)
            pattern = i % 2 == 0 ? pattern.Fast(2) : pattern.Slow(2);
        return pattern;
    }

    [Fact]
    public void A_too_dense_pattern_is_refused_when_it_is_played_not_when_it_renders()
    {
        Assert.Throws<InvalidOperationException>(() => Dense().Play(v => Sine(v.Frequency)));
        Assert.Throws<InvalidOperationException>(() => Mini.Numbers("[1*1024]*1024").ToSignal());
        Assert.Throws<InvalidOperationException>(() => Dense().ToPattern());
        Assert.Throws<InvalidOperationException>(() => Dense().Jux(p => p, v => Sine(v.Frequency)));
    }

    [Fact]
    public void A_pattern_chained_thousands_deep_is_refused_instead_of_overflowing_the_stack()
    {
        var pattern = Deep(Mini.Notes("c3"), 30_000);

        var error = Assert.Throws<InvalidOperationException>(() => pattern.Validate());

        Assert.Contains("nested too deeply", error.Message);
    }

    [Fact]
    public void Speed_factors_that_multiply_out_of_range_are_refused()
    {
        var pattern = Mini.Notes("c3").Fast(1_000_000_000).Fast(1_000_000_000).Fast(1_000_000_000);

        Assert.Throws<InvalidOperationException>(() => pattern.Validate());
    }

    [Fact]
    public void A_reasonably_deep_pattern_is_fine()
    {
        Deep(Mini.Notes("c3 e3"), 40).Validate();
        Mini.Notes("[[[[[[[[c3 e3]*2]*2]*2]*2]*2]*2]*2]*2").Validate();
    }

    [Fact]
    public void Validate_checks_the_cycles_it_is_given_and_rejects_nonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mini.Notes("c3").Validate(0));
        Mini.Notes("<c3 e3>").Validate(1);
    }

    [Fact]
    public void A_cycle_that_turns_out_too_dense_later_silences_the_pattern_without_throwing()
    {
        // Fine for the first 100 cycles, which is all that is checked; cycle 100 is far too dense.
        var plain = Mini.Notes("c3:1");
        var turns = Enumerable.Repeat(plain, 100).Append(Mini.Notes("[[c3*1024]*1024]*1024")).ToArray();
        var pattern = Pat.Cat(turns);
        var sound = pattern.Play(v => Constant(1).Adsr(0, 0, 1, 0), bpm: 240);

        var clock = new HostClock(Rate, bpm: 240);
        var op = Mini.Notes("c3:1").Play(v => Constant(1).Adsr(0, 0, 1, 0)).Compile(new RenderContext(Rate) { Clock = clock });
        var block = new StereoFrame[256];
        op.RenderBlock(block);

        var late = pattern.Play(v => Constant(1).Adsr(0, 0, 1, 0)).Compile(new RenderContext(Rate) { Clock = clock });
        clock.Seek(0, beats: 400); // cycle 100
        late.RenderBlock(block);
        late.RenderBlock(block);

        Assert.All(block, f => Assert.True(float.IsFinite(f.Left)));
        Assert.NotNull(sound);
    }

    [Fact]
    public void A_signal_pattern_that_fails_later_holds_its_last_value()
    {
        var plain = Mini.Numbers("5");
        var pattern = Pat.Cat(Enumerable.Repeat(plain, 100).Append(Mini.Numbers("[[1*1024]*1024]*1024")).ToArray());
        var clock = new HostClock(Rate, bpm: 240);
        var op = pattern.ToSignal().Compile(new RenderContext(Rate) { Clock = clock });
        var block = new StereoFrame[256];

        op.RenderBlock(block);
        Assert.Equal(5f, block[^1].Left);

        clock.Seek(0, beats: 400);
        op.RenderBlock(block);

        Assert.Equal(5f, block[^1].Left);
    }

    [Theory]
    [InlineData("[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[c3]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]")]
    [InlineData("<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<c3>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>")]
    public void Brackets_nested_absurdly_deep_are_refused(string text) =>
        Assert.Throws<MiniNotationException>(() => Mini.Notes(text));

    [Fact]
    public void Euclid_steps_and_inversions_are_capped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mini.Notes("c3").Euclid(3, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chord.Major(60).Inversion(1_000_000_000));
        Mini.Notes("c3").Euclid(3, 1024).Validate(1);
    }

    [Fact]
    public void Shift_takes_a_plain_number_of_cycles()
    {
        var shifted = Mini.Notes("c3 e3").Shift(0.25);

        Assert.Equal(new Fraction(1, 4), shifted.Cycle(0).First(e => e.HasOnset).Whole.Begin);
    }
}
