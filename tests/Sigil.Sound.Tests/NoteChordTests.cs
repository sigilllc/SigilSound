namespace Sigil.Sound.Tests;

/// <summary>
/// The bridge from live notes to patterns: <see cref="NoteStream.Chord"/> and what is written against it. The event traces are the ones
/// the Workbench sources produce: the Tonnetz releases the old chord and presses the new one voice by voice (all at once, or strummed over
/// a few milliseconds), a keyboard may press the new chord before the old one is up.
/// </summary>
public class NoteChordTests
{
    private static readonly int[] C = [60, 64, 67];
    private static readonly int[] D = [62, 65, 69];

    /// <summary>A live chord pattern on a clock the test advances block by block, queried once per block like a player would.</summary>
    private sealed class ChordRig : IDisposable
    {
        private readonly NoteRig _rig;

        public ChordRig(Func<NoteStream, Pattern<Chord>>? make = null, double bpm = 120)
        {
            _rig = new NoteRig(bpm);
            Chord = (make ?? (s => s.Chord(switchOn: ChordSwitch.Instant)))(_rig.Keys.Stream);
        }

        public Pattern<Chord> Chord { get; }

        public NoteInput Keys => _rig.Keys;

        public HostClock Clock => _rig.Clock;

        /// <summary>Every chord seen at the end of a block, in order (null when there was none).</summary>
        public List<int[]?> Seen { get; } = [];

        /// <summary>The chord pitches for cycle <paramref name="cycle"/> right now, or null.</summary>
        public int[]? Cycle(long cycle)
        {
            var events = new List<PatternEvent<Chord>>();
            Chord.Query(new Arc(new Fraction(cycle), new Fraction(cycle + 1)), events);
            return events.Count == 0 ? null : [.. events[0].Value.Pitches];
        }

        public int[]? Now() => Cycle((long)Math.Floor(Clock.Beats / Pat.BeatsPerCycle));

        public void Advance(int blocks = 1)
        {
            for (int i = 0; i < blocks; i++)
            {
                Clock.Advance(NoteRig.Block);
                Seen.Add(Now());
            }
        }

        public void AdvanceBeats(double beats) => Advance((int)Math.Ceiling(beats / _rig.BlockBeats));

        public void Dispose() => _rig.Dispose();
    }

    // --- What counts as a chord ------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_a_chord_until_the_keys_have_settled_and_then_it_is_the_whole_chord()
    {
        using var rig = new ChordRig();

        Assert.Null(rig.Now());
        rig.Keys.NoteOn(67);
        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(64);
        rig.Advance(3); // 16 ms: inside the 35 ms window
        Assert.Null(rig.Now());

        rig.Advance(6);
        Assert.Equal(C, rig.Now()); // sorted, whatever order the keys came in
    }

    [Fact]
    public void A_strum_over_several_blocks_is_never_seen_half_made()
    {
        using var rig = new ChordRig();

        rig.Keys.NoteOn(60);
        rig.Advance(2);
        rig.Keys.NoteOn(64);
        rig.Advance(2);
        rig.Keys.NoteOn(67);
        rig.Advance(12);

        Assert.All(rig.Seen.Where(c => c is not null), chord => Assert.Equal(C, chord));
        Assert.Equal(C, rig.Now());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void A_chord_that_changes_voice_by_voice_is_never_seen_mixed(int blocksBetweenVoices)
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);
        rig.Seen.Clear();

        // The Tonnetz: each voice releases its old note and presses its new one.
        int[] from = C, to = D;
        for (int voice = 0; voice < 3; voice++)
        {
            rig.Keys.NoteOff(from[voice]);
            rig.Keys.NoteOn(to[voice]);
            rig.Advance(blocksBetweenVoices);
        }

        rig.Advance(12);

        Assert.All(rig.Seen.Where(c => c is not null), chord => Assert.True(chord!.SequenceEqual(C) || chord.SequenceEqual(D), string.Join(",", chord!)));
        Assert.Equal(D, rig.Now());
    }

    [Fact]
    public void A_new_chord_pressed_before_the_old_one_is_up_replaces_it_when_the_old_one_comes_up_in_time()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        rig.Keys.NoteOn("d4 f4 a4");
        rig.Advance(2);
        rig.Keys.NoteOff("c4 e4 g4"); // legato: up within the settle window
        rig.Advance(12);

        Assert.Equal(D, rig.Now());
    }

    [Fact]
    public void Letting_go_of_a_chord_key_by_key_keeps_its_shape()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        rig.Keys.NoteOff(60);
        rig.Advance(10);
        rig.Keys.NoteOff(64);
        rig.Advance(10);
        rig.Keys.NoteOff(67);
        rig.Advance(10);

        Assert.Equal(C, rig.Now());
    }

    [Fact]
    public void A_gated_chord_is_gone_when_the_keys_are()
    {
        using var rig = new ChordRig(s => s.Chord(latch: ChordLatch.Gated, switchOn: ChordSwitch.Instant));
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);
        Assert.Equal(C, rig.Now());

        rig.Keys.NoteOff("c4 e4 g4");
        rig.Advance(10);

        Assert.Null(rig.Now());
    }

    [Fact]
    public void A_clock_given_to_the_input_after_the_chord_was_built_is_followed()
    {
        // A host may build the script before it has the clock to give: the chord must still keep the clock's time, not the wall's.
        var keys = new NoteInput();
        var chord = keys.Stream.Chord(switchOn: ChordSwitch.Cycle);
        var clock = new HostClock(NoteRig.SampleRate);
        keys.Clock = clock;
        keys.NoteOn("c4 e4 g4");

        for (int i = 0; i < 12; i++) // 64 ms of the clock's time, a few microseconds of the wall's
        {
            clock.Advance(NoteRig.Block);
            chord.Cycle(0);
        }

        var events = chord.Cycle(0);
        Assert.Equal(C, Assert.Single(events).Value.Pitches);
    }

    // --- When a new chord takes over -------------------------------------------------------------------------------------

    [Fact]
    public void Instant_switches_as_soon_as_the_chord_has_settled()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.AdvanceBeats(1.0);

        rig.Keys.NoteOff("c4 e4 g4");
        rig.Keys.NoteOn("d4 f4 a4");
        rig.Advance(4);
        Assert.Equal(C, rig.Now());
        rig.Advance(6);
        Assert.Equal(D, rig.Now());
    }

    [Fact]
    public void Beat_waits_for_the_next_beat()
    {
        using var rig = new ChordRig(s => s.Chord(switchOn: ChordSwitch.Beat));
        rig.Keys.NoteOn("c4 e4 g4");
        rig.AdvanceBeats(1.2);

        rig.Keys.NoteOff("c4 e4 g4");
        rig.Keys.NoteOn("d4 f4 a4"); // at beat 1.2
        rig.AdvanceBeats(0.5); // 1.7: settled, boundary at 2
        Assert.Equal(C, rig.Now());

        rig.AdvanceBeats(0.4); // 2.1
        Assert.Equal(D, rig.Now());
    }

    [Fact]
    public void Cycle_holds_the_old_chord_for_the_rest_of_the_bar_and_a_bar_long_event_uses_one_chord()
    {
        using var rig = new ChordRig(s => s.Chord(switchOn: ChordSwitch.Cycle));
        rig.Keys.NoteOn("c4 e4 g4");
        rig.AdvanceBeats(1.0);

        rig.Keys.NoteOff("c4 e4 g4");
        rig.Keys.NoteOn("d4 f4 a4"); // at beat 1.0
        rig.AdvanceBeats(2.0); // 3.0: settled long ago, bar line at 4

        Assert.Equal(C, rig.Cycle(0)); // the bar in progress keeps its chord from start to finish...
        Assert.Equal(D, rig.Cycle(1)); // ...and the next bar is already written against the new one

        rig.AdvanceBeats(1.2); // 4.2
        Assert.Equal(D, rig.Now());
    }

    [Fact]
    public void While_the_transport_is_stopped_a_new_chord_takes_over_at_once()
    {
        using var rig = new ChordRig(s => s.Chord(switchOn: ChordSwitch.Cycle));
        rig.Keys.NoteOn("c4 e4 g4");
        rig.AdvanceBeats(0.5);
        rig.Clock.Running = false;

        rig.Keys.NoteOff("c4 e4 g4");
        rig.Keys.NoteOn("d4 f4 a4");
        rig.Advance(12);

        Assert.Equal(D, rig.Now());
    }

    // --- Written against the chord ---------------------------------------------------------------------------------------

    private static int[] Pitches(Pattern<Note> pattern, long cycle) => [.. pattern.Cycle(cycle).Select(e => e.Value.Pitch)];

    [Fact]
    public void Steps_climb_through_the_chord_wrap_into_octaves_and_fall_below_the_root()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        Assert.Equal([60, 64, 67, 72, 76, 79], Pitches(rig.Chord.Notes("0 1 2 3 4 5"), 0));
        Assert.Equal([79, 67, 55, 52], Pitches(rig.Chord.Notes("5 2 -1 -2"), 0));
    }

    [Fact]
    public void Steps_keep_the_rhythm_of_the_steps_and_leave_out_rests_and_out_of_range_notes()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        var events = rig.Chord.Notes("0 ~ [1 2]").Cycle(0);

        Assert.Equal([60, 64, 67], events.Select(e => e.Value.Pitch));
        Assert.Equal([new Fraction(0), new Fraction(2, 3), new Fraction(5, 6)], events.Select(e => e.Whole.Begin));
        Assert.Empty(rig.Chord.Notes("100").Cycle(0));
    }

    [Fact]
    public void A_run_written_against_a_cycle_chord_uses_one_chord_even_when_the_chord_changes_in_the_bar()
    {
        using var rig = new ChordRig(s => s.Chord(switchOn: ChordSwitch.Cycle));
        var run = rig.Chord.Notes("7 6 5 4 3 2 1 0");
        rig.Keys.NoteOn("c4 e4 g4");
        rig.AdvanceBeats(0.5);

        var seen = new List<int[]>();
        for (int i = 0; i < 4; i++)
        {
            if (i == 1)
            {
                rig.Keys.NoteOff("c4 e4 g4");
                rig.Keys.NoteOn("d4 f4 a4");
            }

            seen.Add(Pitches(run, 0));
            rig.AdvanceBeats(0.7);
        }

        // Every time the bar is asked for, it is the same run on the same chord.
        Assert.All(seen, pitches => Assert.Equal([88, 84, 79, 76, 72, 67, 64, 60], pitches));
        Assert.Equal([89, 86, 81, 77, 74, 69, 65, 62], Pitches(run, 1));
    }

    [Fact]
    public void A_chord_pattern_arpeggiates_like_any_other()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        Assert.Equal([67, 64, 60], rig.Chord.Arp(ArpMode.Down).Cycle(0).Select(e => e.Value.Pitch));
        Assert.Equal([60, 64, 67], rig.Chord.Notes().Cycle(0).Select(e => e.Value.Pitch));
    }

    [Fact]
    public void Lowest_and_Highest_follow_the_chord()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        Assert.Equal(60, Assert.Single(rig.Chord.Lowest().Cycle(0)).Value);
        Assert.Equal(67, Assert.Single(rig.Chord.Highest().Cycle(0)).Value);
    }

    [Fact]
    public void A_fixed_chord_and_a_written_progression_take_steps_too()
    {
        Assert.Equal([60, 64, 67, 72, 55], Pitches(Chord.Major(60).Steps("0 1 2 3 -1"), 0));

        var progression = Mini.Chords("<c3:min7 f3:7>").Notes(Mini.Integers("0 2 4"));
        Assert.Equal([48, 55, 60], Pitches(progression, 0));
        Assert.Equal([53, 60, 65], Pitches(progression, 1)); // F7 is F A C Eb: step 2 is C, step 4 is the F above
    }

    [Fact]
    public void A_live_pattern_cannot_be_written_out_as_a_score()
    {
        using var rig = new ChordRig();
        rig.Keys.NoteOn("c4 e4 g4");
        rig.Advance(10);

        var ex = Assert.Throws<InvalidOperationException>(() => rig.Chord.Notes("0 1 2").ToPattern());
        Assert.Contains("live notes", ex.Message);

        // Checking it for runaway density is not writing it out, and leaves it as it was.
        rig.Chord.Validate();
        Assert.Equal(C, rig.Now());
    }

    // --- Fold ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Fold_reduces_the_stream_to_a_live_pattern()
    {
        var rig = new NoteRig();
        var count = rig.Keys.Stream.Fold(0, (n, e) => e.IsOn ? n + 1 : n - 1);
        int Now()
        {
            rig.Clock.Advance(NoteRig.Block);
            var events = new List<PatternEvent<int>>();
            count.Query(new Arc(new Fraction(0), new Fraction(1)), events);
            return events.Single().Value;
        }

        Assert.Equal(0, Now());
        rig.Keys.NoteOn("c4 e4 g4");
        Assert.Equal(3, Now());
        rig.Keys.NoteOff(64);
        Assert.Equal(2, Now());
        rig.Dispose();
    }
}
