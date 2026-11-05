using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class FractionTests
{
    [Fact]
    public void Fractions_reduce_and_compare_exactly()
    {
        Assert.Equal(new Fraction(1, 2), new Fraction(3, 6));
        Assert.Equal(new Fraction(1, 2), new Fraction(-1, -2));
        Assert.True(new Fraction(1, 3) < new Fraction(1, 2));
        Assert.Equal(new Fraction(5, 6), new Fraction(1, 2) + new Fraction(1, 3));
        Assert.Equal(new Fraction(1, 6), new Fraction(1, 2) * new Fraction(1, 3));
        Assert.Equal(Fraction.Zero, new Fraction(0, 7));
        Assert.Equal(default, Fraction.Zero);
    }

    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(-7, 2, -4)]
    [InlineData(-4, 2, -2)]
    [InlineData(0, 5, 0)]
    public void Floor_rounds_toward_negative_infinity(long n, long d, long expected) =>
        Assert.Equal(expected, new Fraction(n, d).Floor());

    [Theory]
    [InlineData(0.5, 1, 2)]
    [InlineData(0.25, 1, 4)]
    [InlineData(1.0 / 3, 1, 3)]
    [InlineData(0.1, 1, 10)]
    [InlineData(2.0, 2, 1)]
    [InlineData(1.5, 3, 2)]
    public void FromDouble_finds_the_simple_fraction(double value, long n, long d) =>
        Assert.Equal(new Fraction(n, d), Fraction.FromDouble(value));

    [Fact]
    public void A_zero_denominator_is_rejected() =>
        Assert.Throws<DivideByZeroException>(() => new Fraction(1, 0));
}

public class PatternAlgebraTests
{
    private static Pattern<string> P(string text) => Mini.Parse(text, s => s);

    // "begin-end:value" for each event that starts in the cycle, so expectations read like the notation.
    private static string[] Cycle(Pattern<string> pattern, long cycle = 0) =>
        pattern.Cycle(cycle).Where(e => e.HasOnset).Select(Show).ToArray();

    private static string Show(PatternEvent<string> e) => $"{e.Whole.Begin}-{e.Whole.End}:{e.Value}";

    [Fact]
    public void A_sequence_shares_the_cycle_equally() =>
        Assert.Equal(["0-1/4:a", "1/4-1/2:b", "1/2-3/4:c", "3/4-1:d"], Cycle(P("a b c d")));

    [Fact]
    public void Groups_share_one_step() =>
        Assert.Equal(["0-1/2:a", "1/2-3/4:b", "3/4-1:c"], Cycle(P("a [b c]")));

    [Fact]
    public void Rests_leave_a_gap() =>
        Assert.Equal(["0-1/3:a", "2/3-1:b"], Cycle(P("a ~ b")));

    [Fact]
    public void A_dash_on_its_own_is_a_rest_but_a_dash_before_a_digit_is_part_of_the_word() =>
        Assert.Equal(["0-1/2:-1"], Cycle(P("-1 -")));

    [Fact]
    public void Angle_brackets_play_one_per_cycle()
    {
        var pattern = P("<a b c>");

        Assert.Equal(["a", "b", "c", "a", "b"], Enumerable.Range(0, 5).Select(c => pattern.Cycle(c)[0].Value));
    }

    [Fact]
    public void Alternation_inside_a_sequence_keeps_its_own_count()
    {
        var pattern = P("x <a b>");

        Assert.Equal(["0-1/2:x", "1/2-1:a"], Cycle(pattern, 0));
        Assert.Equal(["1-3/2:x", "3/2-2:b"], Cycle(pattern, 1));
    }

    [Fact]
    public void Star_and_slash_change_speed()
    {
        Assert.Equal(["0-1/4:a", "1/4-1/2:a", "1/2-1:b"], Cycle(P("a*2 b")));
        Assert.Equal(["0-2:a"], Cycle(P("a/2"), 0));
        Assert.Empty(Cycle(P("a/2"), 1));
    }

    [Fact]
    public void Commas_layer_patterns_and_make_chords()
    {
        Assert.Equal(["0-1:a", "0-1:b"], Cycle(P("[a,b]")).Order());
        Assert.Equal(["0-1:a", "0-1:b"], Cycle(P("a,b")).Order());
    }

    [Fact]
    public void Weights_and_underscores_stretch_a_step()
    {
        Assert.Equal(["0-3/4:a", "3/4-1:b"], Cycle(P("a@3 b")));
        Assert.Equal(["0-3/4:a", "3/4-1:b"], Cycle(P("a _ _ b")));
    }

    [Fact]
    public void Bang_repeats_a_step()
    {
        Assert.Equal(["0-1/3:a", "1/3-2/3:a", "2/3-1:a"], Cycle(P("a!3")));
        Assert.Equal(["0-1/3:a", "1/3-2/3:b", "2/3-1:b"], Cycle(P("a b !")));
    }

    [Theory]
    [InlineData(3, 8, 0, "x..x..x.")]
    [InlineData(5, 8, 0, "x.xx.xx.")]
    [InlineData(2, 5, 0, "x.x..")]
    [InlineData(4, 9, 0, "x.x.x.x..")]
    [InlineData(3, 8, 2, ".x..x.x.")]
    [InlineData(0, 4, 0, "....")]
    [InlineData(4, 4, 0, "xxxx")]
    public void Euclid_matches_the_known_rhythms(int pulses, int steps, int rotation, string expected)
    {
        var pattern = P("a").Euclid(pulses, steps, rotation);
        var hits = pattern.Cycle(0).Where(e => e.HasOnset).Select(e => (int)(e.Whole.Begin * steps).Floor()).ToHashSet();

        Assert.Equal(expected, new string(Enumerable.Range(0, steps).Select(i => hits.Contains(i) ? 'x' : '.').ToArray()));
    }

    [Fact]
    public void Euclid_in_the_notation_matches_the_method() =>
        Assert.Equal(Cycle(P("a").Euclid(3, 8, 2)), Cycle(P("a(3,8,2)")));

    [Fact]
    public void Euclid_takes_the_structure_from_the_rhythm_and_the_values_from_the_pattern()
    {
        // Hits at 0, 3/8 and 6/8 sample "[a b]": a until 1/2, then b.
        Assert.Equal(["0-1/8:a", "3/8-1/2:a", "3/4-7/8:b"], Cycle(P("[a b]").Euclid(3, 8)));
        // A rest at the start of a hit means no event for it.
        Assert.Equal(["0-1/8:a"], Cycle(P("a ~").Euclid(3, 8)).Take(1));
    }

    [Fact]
    public void Rev_reverses_each_cycle()
    {
        Assert.Equal(["0-1/3:c", "1/3-2/3:b", "2/3-1:a"], Cycle(P("a b c").Rev()));
        Assert.Equal(["1-4/3:c", "4/3-5/3:b", "5/3-2:a"], Cycle(P("a b c").Rev(), 1));
    }

    [Fact]
    public void Every_changes_only_the_chosen_cycles()
    {
        var pattern = P("a b").Every(2, p => p.Rev());

        Assert.Equal(["0-1/2:b", "1/2-1:a"], Cycle(pattern, 0));
        Assert.Equal(["1-3/2:a", "3/2-2:b"], Cycle(pattern, 1));
        Assert.Equal(["2-5/2:b", "5/2-3:a"], Cycle(pattern, 2));
    }

    [Fact]
    public void Shift_moves_the_pattern_later()
    {
        var pattern = P("a b").Shift(new Fraction(1, 4));

        Assert.Equal(["1/4-3/4:a", "3/4-5/4:b"], pattern.Cycle(0).Where(e => e.HasOnset).Select(Show));
    }

    [Fact]
    public void Cat_gives_each_pattern_its_own_clock()
    {
        var pattern = Pat.Cat(P("a b"), P("c"));

        Assert.Equal(["0-1/2:a", "1/2-1:b"], Cycle(pattern, 0));
        Assert.Equal(["1-2:c"], Cycle(pattern, 1));
        Assert.Equal(["2-5/2:a", "5/2-3:b"], Cycle(pattern, 2));
    }

    [Fact]
    public void A_query_across_a_cycle_boundary_cuts_events_in_two()
    {
        var events = new List<PatternEvent<string>>();

        P("a").Query(new Arc(new Fraction(1, 2), new Fraction(3, 2)), events);

        Assert.Equal(2, events.Count);
        Assert.False(events[0].HasOnset);
        Assert.Equal(new Arc(0, 1), events[0].Whole);
        Assert.Equal(new Arc(new Fraction(1, 2), 1), events[0].Part);
        Assert.True(events[1].HasOnset);
    }

    [Fact]
    public void A_point_query_finds_the_event_playing_then()
    {
        var events = new List<PatternEvent<string>>();

        P("a b c d").Query(new Arc(new Fraction(1, 4), new Fraction(1, 4)), events);

        Assert.Equal(["b"], events.Select(e => e.Value));
    }

    [Fact]
    public void Results_come_back_in_time_order()
    {
        var events = new List<PatternEvent<string>>();

        P("a b, c d e, f").Query(new Arc(0, 1), events);

        Assert.Equal(events.OrderBy(e => e.Part.Begin).Select(e => e.Value), events.Select(e => e.Value));
    }

    public static IEnumerable<object[]> Shapes() =>
        new[] { "a b c d", "a [b c] ~ <d e>", "a*3 b(3,8)", "<a b> <c d e>", "a? b? c? d?", "[a,b] c@3 d!2" }
            .Select(text => new object[] { text });

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Rev_twice_is_the_pattern_itself(string text)
    {
        var pattern = P(text);
        var twice = pattern.Rev().Rev();

        for (int cycle = -2; cycle < 6; cycle++)
            Assert.Equal(pattern.Cycle(cycle), twice.Cycle(cycle));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Fast_then_slow_is_the_pattern_itself(string text)
    {
        var pattern = P(text);

        foreach (int factor in new[] { 2, 3, 5 })
            for (int cycle = 0; cycle < 4; cycle++)
                Assert.Equal(pattern.Cycle(cycle), pattern.Fast(factor).Slow(factor).Cycle(cycle));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Shifting_there_and_back_is_the_pattern_itself(string text)
    {
        var pattern = P(text);
        var there = pattern.Shift(new Fraction(3, 7)).Shift(new Fraction(-3, 7));

        for (int cycle = 0; cycle < 4; cycle++)
            Assert.Equal(pattern.Cycle(cycle), there.Cycle(cycle));
    }

    [Fact]
    public void Querying_two_halves_matches_querying_the_whole()
    {
        var pattern = P("a*3 [b c]/2 <d e>(3,8)");
        var whole = new List<PatternEvent<string>>();
        var halves = new List<PatternEvent<string>>();

        pattern.Query(new Arc(0, 2), whole);
        pattern.Query(new Arc(0, 1), halves);
        pattern.Query(new Arc(1, 2), halves);

        Assert.Equal(whole.Where(e => e.HasOnset).Select(Show).Order(), halves.Where(e => e.HasOnset).Select(Show).Order());
    }

    [Fact]
    public void Degrade_is_the_same_every_time_and_respects_the_amount()
    {
        var pattern = P("a*8").Degrade(0.5, seed: 3);

        Assert.Equal(Cycle(pattern, 5), Cycle(pattern, 5));
        int kept = Enumerable.Range(0, 200).Sum(c => Cycle(pattern, c).Length);
        Assert.InRange(kept, 700, 900);
        Assert.Empty(Cycle(P("a*8").Degrade(1), 0));
        Assert.Equal(8, Cycle(P("a*8").Degrade(0), 0).Length);
    }

    [Fact]
    public void Different_seeds_drop_different_events()
    {
        var a = Enumerable.Range(0, 20).SelectMany(c => Cycle(P("a*8").Degrade(0.5, 1), c));
        var b = Enumerable.Range(0, 20).SelectMany(c => Cycle(P("a*8").Degrade(0.5, 2), c));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Sometimes_transforms_only_the_chosen_events()
    {
        var pattern = P("a*8").Sometimes(0.5, p => p.Select(_ => "X"), seed: 1);

        var all = Enumerable.Range(0, 50).SelectMany(c => Cycle(pattern, c)).ToArray();

        Assert.Equal(400, all.Length);
        Assert.InRange(all.Count(s => s.EndsWith('X')), 140, 260);
    }

    [Fact]
    public void Select_and_Where_work_on_values()
    {
        var pattern = P("1 2 3 4").Select(int.Parse).Where(n => n % 2 == 0);

        Assert.Equal([2, 4], pattern.Cycle(0).Select(e => e.Value));
    }

    [Fact]
    public void Mapping_the_same_pattern_in_two_places_at_once_is_safe()
    {
        var mapped = P("a b c d").Select(s => s.ToUpperInvariant());
        var both = Pat.Stack(mapped, mapped.Fast(2));

        Assert.Equal(12, both.Cycle(0).Count);
    }

    [Fact]
    public void Querying_a_busy_pattern_again_allocates_nothing()
    {
        var pattern = Pat.Stack(
            P("a*3 [b c]/2 <d e>(3,8)").Every(3, p => p.Rev()),
            P("a? b? c? d?").Fast(2),
            P("a b").Select(s => s).Degrade(0.3));
        var buffer = new List<PatternEvent<string>>(256);
        for (int i = 0; i < 100; i++)
        {
            buffer.Clear();
            pattern.Query(new Arc(i, i + 1), buffer);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            buffer.Clear();
            pattern.Query(new Arc(new Fraction(i, 4), new Fraction(i + 1, 4)), buffer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Large_cycle_numbers_stay_exact()
    {
        long cycle = 5_000_000;
        var events = P("a b c").Cycle(cycle);

        Assert.Equal(new Fraction(cycle * 3 + 1, 3), events[1].Whole.Begin);
    }

    [Theory]
    [InlineData("a [b c", "Missing ']'", 6)]
    [InlineData("a b)", "Unexpected ')'", 3)]
    [InlineData("a b]", "Unexpected ']'", 3)]
    [InlineData("<a b", "Missing '>'", 4)]
    [InlineData("a*", "Expected a number", 2)]
    [InlineData("a(9,8)", "not a valid rhythm", 1)]
    [InlineData("a(3 8)", "Expected ','", 4)]
    [InlineData("_ a", "'_' needs a step", 0)]
    [InlineData("a*0", "above zero", 1)]
    [InlineData("a?2", "between 0 and 1", 1)]
    [InlineData("a @", "Unexpected '@'", 2)]
    public void Bad_notation_names_the_problem_and_the_column(string text, string message, int column)
    {
        var error = Assert.Throws<MiniNotationException>(() => P(text));

        Assert.Contains(message, error.Message);
        Assert.Equal(column, error.Position);
    }

    [Fact]
    public void An_atom_the_caller_rejects_is_reported_at_its_column()
    {
        var error = Assert.Throws<MiniNotationException>(() => Mini.Notes("c3 e3 h3"));

        Assert.Equal(6, error.Position);
    }
}

public class MiniNotesTests
{
    [Fact]
    public void Notes_parse_names_accidentals_octaves_and_velocity()
    {
        var notes = Mini.Notes("c4 Eb4 f#2 bb3 c c4:0.5").Cycle(0).Select(e => e.Value).ToArray();

        Assert.Equal([60, 63, 42, 58, 48, 60], notes.Select(n => n.Pitch));
        Assert.Equal(0.5f, notes[5].Velocity);
    }

    [Fact]
    public void Chords_and_rests_work_with_notes()
    {
        var events = Mini.Notes("[c3,e3,g3] ~").Cycle(0);

        Assert.Equal([48, 52, 55], events.Select(e => e.Value.Pitch).Order());
        Assert.All(events, e => Assert.Equal(new Arc(0, new Fraction(1, 2)), e.Whole));
    }

    [Fact]
    public void Numbers_parse_as_doubles() =>
        Assert.Equal([400.0, 800.0, 1600.5], Mini.Numbers("400 800 1600.5").Cycle(0).Select(e => e.Value));

    [Fact]
    public void Transpose_and_Velocity_change_every_note()
    {
        var pattern = Mini.Notes("c3 e3").Transpose(12).Velocity(0.25f);

        Assert.Equal([60, 64], pattern.Cycle(0).Select(e => e.Value.Pitch));
        Assert.All(pattern.Cycle(0), e => Assert.Equal(0.25f, e.Value.Velocity));
    }

    [Fact]
    public void ToPattern_writes_beats_and_lengths()
    {
        var pattern = Mini.Notes("c3 [e3 g3]").ToPattern();

        Assert.True(pattern.IsLooping);
        Assert.Equal(4.0, pattern.Length);
        Assert.Equal([0.0, 2.0, 3.0], pattern.Events.Select(e => e.Start));
        Assert.Equal([2.0, 1.0, 1.0], pattern.Events.Select(e => e.Note.Beats));
    }

    [Fact]
    public void ToPattern_over_several_cycles_keeps_the_variation()
    {
        var pattern = Mini.Notes("<c3 e3>").ToPattern(cycles: 2);

        Assert.Equal([48, 52], pattern.Events.Select(e => e.Note.Pitch));
        Assert.Equal([0.0, 4.0], pattern.Events.Select(e => e.Start));
        Assert.Equal(8.0, pattern.Length);
    }

    [Fact]
    public void A_notation_pattern_plays_through_the_voice_pool()
    {
        var sound = Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>").ToPattern(cycles: 2)
            .Play(v => Saw(v.Frequency).Adsr(0.003, 0.1, 0.5, 0.1), bpm: 240);

        var audio = sound.Render(2.0, 48_000);

        AudioAssert.IsFinite(audio);
        Assert.True(Measure.Rms(audio.ToMono()) > 0.01);
    }
}

public class PatternLimitTests
{
    [Fact]
    public void Absurd_counts_in_the_notation_are_rejected()
    {
        Assert.Throws<MiniNotationException>(() => Mini.Notes("c3*100000000"));
        Assert.Throws<MiniNotationException>(() => Mini.Notes("c3!99999"));
        Assert.Throws<MiniNotationException>(() => Mini.Notes("c3(3,99999)"));
    }

    [Fact]
    public void A_runaway_combination_stops_with_a_clear_error()
    {
        var pattern = Mini.Notes("[c3*1024]*1024");

        var error = Assert.Throws<InvalidOperationException>(() => pattern.Cycle(0));

        Assert.Contains("too dense", error.Message);
    }
}
