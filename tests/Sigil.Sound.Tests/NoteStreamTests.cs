using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>The declarative operators of <see cref="NoteStream"/>: what comes out for what goes in, and that every note ends.</summary>
public class NoteStreamTests
{
    private static NoteRig Rig(Func<NoteStream, NoteStream>? build = null, double bpm = 120)
    {
        var rig = new NoteRig(bpm);
        rig.Run(build?.Invoke(rig.Keys.Stream) ?? rig.Keys.Stream);
        return rig;
    }

    // --- The source ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_key_down_is_a_start_and_the_key_up_is_the_matching_end()
    {
        using var rig = Rig();

        rig.Keys.NoteOn(60, 0.7f);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        var start = Assert.Single(rig.Starts).Event;
        var end = Assert.Single(rig.Ends).Event;
        Assert.Equal((60, 0.7f), (start.Pitch, start.Velocity));
        Assert.Equal(start.Id, end.Id);
        Assert.Equal(60, end.Pitch);
    }

    [Fact]
    public void Cents_travel_with_the_note_and_set_its_frequency()
    {
        using var rig = Rig();

        rig.Keys.NoteOn(69, 0.8f, 50.0);
        rig.Render();

        var start = Assert.Single(rig.Starts).Event;
        Assert.Equal(50f, start.Cents);
        Assert.InRange(start.Frequency, 452.8, 452.9); // a quarter tone above A4
    }

    [Fact]
    public void Pressing_a_held_key_again_ends_the_first_note_before_the_second_starts()
    {
        using var rig = Rig();

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.Equal([true, false, true, false], rig.Events.Select(s => s.Event.IsOn));
        rig.AssertAllEnded();
    }

    [Fact]
    public void Notes_started_with_a_handle_can_share_a_pitch_and_each_end_alone()
    {
        using var rig = Rig();

        var first = rig.Keys.Start(60);
        var second = rig.Keys.Start(60);
        rig.Render();
        first.Release();
        rig.Render();

        Assert.Equal(2, rig.Starts.Count());
        var end = Assert.Single(rig.Ends).Event;
        Assert.Equal(rig.Starts.First().Event.Id, end.Id);
        second.Release();
        rig.Render();
        rig.AssertAllEnded();
    }

    [Fact]
    public void AllNotesOff_ends_everything_sounding()
    {
        using var rig = Rig(s => s.Harmonize(0, 7));

        rig.Keys.NoteOn("c3 e3 g3");
        rig.Render();
        rig.Keys.AllNotesOff();
        rig.Render();

        Assert.Equal(6, rig.Starts.Count());
        rig.AssertAllEnded();
    }

    [Fact]
    public void A_stream_built_while_keys_are_down_starts_with_them_already_sounding()
    {
        var rig = new NoteRig();
        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(64);
        rig.Run(rig.Keys.Stream);

        rig.Render();

        Assert.Equal([60, 64], rig.StartPitches.Order());
        rig.Dispose();
    }

    // --- Pitch -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Transpose_moves_notes_and_the_end_follows_the_start_not_the_function()
    {
        using var rig = Rig(s => s.Transpose(7));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.Equal([67], rig.StartPitches);
        Assert.Equal(67, Assert.Single(rig.Ends).Event.Pitch);
    }

    [Fact]
    public void A_note_moved_out_of_range_is_dropped_and_so_is_its_end()
    {
        using var rig = Rig(s => s.Transpose(12));

        rig.Keys.NoteOn(120);
        rig.Render();
        rig.Keys.NoteOff(120);
        rig.Render();

        Assert.Empty(rig.Events);
    }

    [Fact]
    public void Detune_carries_whole_semitones_into_the_pitch()
    {
        using var rig = Rig(s => s.Detune(130));

        rig.Keys.NoteOn(60, 0.8f, 10.0);
        rig.Render();

        var start = Assert.Single(rig.Starts).Event;
        Assert.Equal(61, start.Pitch);
        Assert.Equal(40f, start.Cents, 0.001f);
    }

    [Theory]
    [InlineData(RangeMode.Drop, 76, -1)]
    [InlineData(RangeMode.Clamp, 76, 72)]
    [InlineData(RangeMode.Wrap, 76, 64)]
    [InlineData(RangeMode.Fold, 76, 68)]
    [InlineData(RangeMode.Fold, 40, 56)]
    [InlineData(RangeMode.Wrap, 40, 52)]
    [InlineData(RangeMode.Drop, 60, 60)]
    public void Range_moves_or_drops_a_note_outside_it(RangeMode mode, int pitch, int expected)
    {
        using var rig = Rig(s => s.Range(48, 72, mode));

        rig.Keys.NoteOn(pitch);
        rig.Render();

        Assert.Equal(expected < 0 ? Array.Empty<int>() : new[] { expected }, rig.StartPitches);
    }

    [Theory]
    [InlineData(QuantizeMode.Nearest, 61, 60)]
    [InlineData(QuantizeMode.Nearest, 66, 65)]
    [InlineData(QuantizeMode.Up, 61, 62)]
    [InlineData(QuantizeMode.Down, 61, 60)]
    [InlineData(QuantizeMode.Up, 60, 60)]
    public void Quantize_moves_a_note_to_a_tone_of_the_scale(QuantizeMode mode, int pitch, int expected)
    {
        using var rig = Rig(s => s.Quantize(Scale.Major(60), mode));

        rig.Keys.NoteOn(pitch, 0.8f, 30.0);
        rig.Render();

        var start = Assert.Single(rig.Starts).Event;
        Assert.Equal(expected, start.Pitch);
        if (expected != pitch)
            Assert.Equal(0f, start.Cents); // a snapped note has no cents left
    }

    // --- Dynamics --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Velocity_operators_set_scale_clamp_and_curve()
    {
        using var set = Rig(s => s.Velocity(0.5));
        set.Keys.NoteOn(60, 0.9f);
        set.Render();
        Assert.Equal(0.5f, set.Starts.Single().Event.Velocity);

        using var scale = Rig(s => s.ScaleVelocity(0.5));
        scale.Keys.NoteOn(60, 0.8f);
        scale.Render();
        Assert.Equal(0.4f, scale.Starts.Single().Event.Velocity, 0.001f);

        using var clamp = Rig(s => s.ClampVelocity(0.3, 0.6));
        clamp.Keys.NoteOn(60, 0.9f);
        clamp.Keys.NoteOn(62, 0.1f);
        clamp.Render();
        Assert.Equal([0.6f, 0.3f], clamp.Starts.Select(e => e.Event.Velocity).ToArray());

        using var curve = Rig(s => s.VelocityCurve(2));
        curve.Keys.NoteOn(60, 0.5f);
        curve.Render();
        Assert.Equal(0.25f, curve.Starts.Single().Event.Velocity, 0.001f);
    }

    // --- Filter and routing ----------------------------------------------------------------------------------------------

    [Fact]
    public void Keep_and_Drop_choose_by_a_pick_and_a_dropped_note_has_no_end()
    {
        using var rig = Rig(s => s.Keep(Pick.Pitch(60, 71)));

        rig.Keys.NoteOn("c3 c4 c5");
        rig.Render();
        rig.Keys.AllNotesOff();
        rig.Render();

        Assert.Equal([60], rig.StartPitches);
        rig.AssertAllEnded();

        using var drop = Rig(s => s.Drop(Pick.Pitch(60, 71)));
        drop.Keys.NoteOn("c3 c4 c5");
        drop.Render();
        Assert.Equal([48, 72], drop.StartPitches.Order());
    }

    [Fact]
    public void Picks_combine()
    {
        var inScale = Pick.InScale(Scale.Major(0));
        using var rig = Rig(s => s.Keep(inScale.And(Pick.Pitch(60, 71)).Or(Pick.Velocity(0.95, 1))));

        rig.Keys.NoteOn(61, 0.5f); // not in C major
        rig.Keys.NoteOn(62, 0.5f); // in C major, in range
        rig.Keys.NoteOn(80, 0.5f); // in C major, out of range, soft
        rig.Keys.NoteOn(81, 1.0f); // loud
        rig.Render();

        Assert.Equal([62, 81], rig.StartPitches);

        using var not = Rig(s => s.Keep(Pick.Pitch(60, 71).Not()));
        not.Keys.NoteOn(62);
        not.Keys.NoteOn(80);
        not.Render();
        Assert.Equal([80], not.StartPitches);
    }

    [Fact]
    public void Chance_picks_the_same_notes_every_time_for_a_seed()
    {
        int[] Run(int seed)
        {
            using var rig = Rig(s => s.Keep(Pick.Chance(0.5, seed)));
            for (int p = 20; p < 120; p++)
                rig.Keys.NoteOn(p);
            rig.Render();
            return rig.StartPitches;
        }

        var first = Run(1);
        Assert.Equal(first, Run(1));
        Assert.NotEqual(first, Run(2));
        Assert.InRange(first.Length, 25, 75);

        using var none = Rig(s => s.Keep(Pick.Chance(0, 1)));
        none.Keys.NoteOn(60);
        none.Render();
        Assert.Empty(none.Events);
    }

    [Fact]
    public void Channel_tags_notes_and_Pick_Channel_finds_them()
    {
        using var rig = Rig(s => s.Channel(3).Keep(Pick.Channel(3)));

        rig.Keys.NoteOn(60);
        rig.Render();

        Assert.Equal(3, Assert.Single(rig.Starts).Event.Channel);
    }

    [Fact]
    public void Merge_keeps_both_streams_and_gives_the_same_pitch_two_notes()
    {
        using var rig = Rig(s => s.Merge(s.Transpose(0)));

        rig.Keys.NoteOn(60);
        rig.Render();

        Assert.Equal(2, rig.Starts.Count());
        Assert.Equal(2, rig.Starts.Select(s => s.Event.Id).Distinct().Count());
        rig.Keys.NoteOff(60);
        rig.Render();
        rig.AssertAllEnded();
    }

    [Fact]
    public void A_stream_used_twice_is_computed_once()
    {
        int calls = 0;
        using var rig = new NoteRig();
        var shared = rig.Keys.Stream.Select(n =>
        {
            calls++;
            return n;
        });
        rig.Run(shared.Merge(shared.Transpose(7)));

        rig.Keys.NoteOn(60);
        rig.Render();

        Assert.Equal(1, calls);
        Assert.Equal([60, 67], rig.StartPitches);
    }

    // --- One into several ------------------------------------------------------------------------------------------------

    [Fact]
    public void Harmonize_starts_a_note_per_interval_and_each_ends_with_the_key()
    {
        using var rig = Rig(s => s.Harmonize(0, 7, 12));

        rig.Keys.NoteOn(60);
        rig.Render();
        Assert.Equal([60, 67, 72], rig.StartPitches);
        Assert.Equal(3, rig.Starts.Select(s => s.Event.Id).Distinct().Count());

        rig.Keys.NoteOff(60);
        rig.Render();
        Assert.Equal([60, 67, 72], rig.Ends.Select(s => s.Event.Pitch).Order());
        rig.AssertAllEnded();
    }

    [Theory]
    [InlineData(60, new[] { 60, 64 })]
    [InlineData(62, new[] { 62, 65 })]
    [InlineData(71, new[] { 71, 74 })]
    [InlineData(61, new[] { 61, 65 })] // between C and D: keeps its distance from the C
    public void Harmonize_in_a_scale_adds_the_diatonic_third(int pitch, int[] expected)
    {
        using var rig = Rig(s => s.Harmonize(Scale.Major(60), 0, 2));

        rig.Keys.NoteOn(pitch);
        rig.Render();

        Assert.Equal(expected, rig.StartPitches.Order());
    }

    [Fact]
    public void Chordify_builds_a_chord_of_the_given_shape_on_each_note()
    {
        using var rig = Rig(s => s.Chordify(Chord.Minor(0)));

        rig.Keys.NoteOn(62);
        rig.Render();

        Assert.Equal([62, 65, 69], rig.StartPitches.Order());
    }

    // --- Time ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Delay_moves_both_ends_of_a_note_so_it_keeps_its_length()
    {
        using var rig = Rig(s => s.Delay(1.0));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.RenderBeats(0.5);
        rig.Keys.NoteOff(60);
        rig.RenderBeats(2);

        var start = Assert.Single(rig.Starts);
        var end = Assert.Single(rig.Ends);
        Assert.InRange(start.Beat, 1.0 - 0.02, 1.0 + 0.02);
        Assert.InRange(end.Beat - start.Beat, 0.5 - 0.03, 0.5 + 0.03);
    }

    [Fact]
    public void Delay_follows_the_tempo()
    {
        using var rig = Rig(s => s.Delay(1.0), bpm: 60);

        rig.Keys.NoteOn(60);
        rig.RenderBeats(2);

        Assert.InRange(Assert.Single(rig.Starts).Beat, 1.0 - 0.02, 1.0 + 0.02);
    }

    [Fact]
    public void Echo_repeats_each_note_softer_and_as_long()
    {
        using var rig = Rig(s => s.Echo(2, 1.0, 0.5));

        rig.Keys.NoteOn(60, 0.8f);
        rig.RenderBeats(0.5);
        rig.Keys.NoteOff(60);
        rig.RenderBeats(3);

        var starts = rig.Starts.ToArray();
        Assert.Equal([0.8f, 0.4f, 0.2f], starts.Select(s => s.Event.Velocity).ToArray());
        Assert.Equal(3, starts.Select(s => s.Event.Id).Distinct().Count());
        for (int k = 0; k < 3; k++)
        {
            Assert.InRange(starts[k].Beat, k - 0.02, k + 0.02);
            double end = rig.Ends.Single(e => e.Event.Id == starts[k].Event.Id).Beat;
            Assert.InRange(end - starts[k].Beat, 0.5 - 0.03, 0.5 + 0.03);
        }

        rig.AssertAllEnded();
    }

    [Fact]
    public void An_echo_of_a_note_released_at_once_still_ends_every_repeat()
    {
        using var rig = Rig(s => s.Echo(4, 0.25, 0.9));

        rig.Keys.NoteOn(60);
        rig.Keys.NoteOff(60);
        rig.RenderBeats(3);

        Assert.Equal(5, rig.Starts.Count());
        rig.AssertAllEnded();
    }

    [Fact]
    public void Strum_spreads_a_chord_in_the_order_it_came_and_each_note_keeps_its_length()
    {
        using var rig = Rig(s => s.Strum(0.25));

        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(64);
        rig.Keys.NoteOn(67);
        rig.RenderBeats(1);
        rig.Keys.NoteOff(60);
        rig.Keys.NoteOff(64);
        rig.Keys.NoteOff(67);
        rig.RenderBeats(2);

        Assert.Equal([60, 64, 67], rig.StartPitches);
        var beats = rig.Starts.Select(s => s.Beat).ToArray();
        Assert.InRange(beats[1] - beats[0], 0.25 - 0.02, 0.25 + 0.02);
        Assert.InRange(beats[2] - beats[1], 0.25 - 0.02, 0.25 + 0.02);
        Assert.Equal([60, 64, 67], rig.Ends.Select(s => s.Event.Pitch));
        rig.AssertAllEnded();
    }

    [Fact]
    public void Strum_does_not_leave_a_note_stuck_when_the_key_is_tapped()
    {
        using var rig = Rig(s => s.Strum(0.5));

        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(64);
        rig.Keys.NoteOff(60);
        rig.Keys.NoteOff(64);
        rig.RenderBeats(3);

        Assert.Equal(2, rig.Starts.Count());
        rig.AssertAllEnded();
    }

    [Fact]
    public void Length_ends_each_note_after_a_fixed_time_whatever_the_key_does()
    {
        using var rig = Rig(s => s.Length(0.5));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOff(60); // tapped
        rig.RenderBeats(1);
        rig.Keys.NoteOn(62);
        rig.RenderBeats(3); // held far longer

        var ends = rig.Ends.ToArray();
        Assert.Equal(2, ends.Length);
        Assert.InRange(ends[0].Beat, 0.5 - 0.02, 0.5 + 0.02);
        Assert.InRange(ends[1].Beat - rig.Starts.Last().Beat, 0.5 - 0.03, 0.5 + 0.03);
    }

    [Fact]
    public void Humanize_is_late_by_at_most_the_timing_and_repeats_for_a_seed()
    {
        List<NoteRig.Seen> Run(int seed)
        {
            using var rig = Rig(s => s.Humanize(0.2, 0.1, seed));
            for (int p = 40; p < 60; p++)
                rig.Keys.NoteOn(p, 0.5f);
            rig.RenderBeats(1);
            rig.Keys.AllNotesOff();
            rig.RenderBeats(1);
            rig.AssertAllEnded();
            return rig.Starts.ToList();
        }

        var first = Run(1);
        Assert.Equal(first, Run(1));
        Assert.NotEqual(first, Run(2));
        Assert.All(first, s => Assert.InRange(s.Beat, 0, 0.2 + 0.02));
        Assert.All(first, s => Assert.InRange(s.Event.Velocity, 0.4f, 0.6f));
        Assert.True(first.Select(s => s.Beat).Distinct().Count() > 5);
    }

    // --- Voicing ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Mono_with_lowest_priority_plays_the_lowest_held_key()
    {
        using var rig = Rig(s => s.Mono(NotePriority.Lowest));

        rig.Keys.NoteOn(64);
        rig.Render();
        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOn(67);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();
        rig.Keys.AllNotesOff();
        rig.Render();

        Assert.Equal([64, 60, 64], rig.StartPitches);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Mono_with_last_priority_returns_to_the_earlier_key_when_the_last_comes_up()
    {
        using var rig = Rig(s => s.Mono());

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOn(64);
        rig.Render();
        rig.Keys.NoteOff(64);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.Equal([60, 64, 60], rig.StartPitches);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Latch_holds_notes_until_the_next_group_of_keys_goes_down()
    {
        using var rig = Rig(s => s.Latch());

        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(64);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Keys.NoteOff(64);
        rig.Render(3);
        Assert.Empty(rig.Ends);

        rig.Keys.NoteOn(62);
        rig.Render();

        Assert.Equal([60, 64], rig.Ends.Select(s => s.Event.Pitch).Order());
        Assert.Equal([60, 64, 62], rig.StartPitches);
    }

    // --- A pattern as a stream -------------------------------------------------------------------------------------------

    [Fact]
    public void A_pattern_becomes_a_stream_on_the_beat_grid()
    {
        using var rig = new NoteRig();
        rig.Run(Mini.Notes("c4 e4").ToStream());

        rig.RenderBeats(4.1);

        Assert.Equal([60, 64], rig.StartPitches.Take(2));
        Assert.InRange(rig.Starts.ElementAt(0).Beat, 0, 0.02);
        Assert.InRange(rig.Starts.ElementAt(1).Beat, 2 - 0.02, 2 + 0.02);
        Assert.InRange(rig.Ends.ElementAt(0).Beat, 2 - 0.02, 2 + 0.02);
        Assert.InRange(rig.Ends.ElementAt(1).Beat, 4 - 0.02, 4 + 0.02);
    }

    [Fact]
    public void A_pattern_stream_goes_through_the_operators_and_merges_with_live_keys()
    {
        using var rig = new NoteRig();
        rig.Run(Mini.Notes("c4").ToStream().Transpose(12).Merge(rig.Keys.Stream));

        rig.Keys.NoteOn(50);
        rig.RenderBeats(1);

        Assert.Equal([50, 72], rig.StartPitches.Order());
    }

    [Fact]
    public void A_pattern_stream_plays_nothing_while_the_transport_is_stopped()
    {
        using var rig = new NoteRig();
        rig.Run(Mini.Notes("c4*4").ToStream());
        rig.Clock.Running = false;

        rig.RenderBeats(2);

        Assert.Empty(rig.Events);
    }

    // --- Reset and robustness --------------------------------------------------------------------------------------------

    [Fact]
    public void Reset_returns_the_ends_of_what_sounds_and_held_keys_start_again()
    {
        using var rig = Rig(s => s.Harmonize(0, 7).Delay(0.5));

        rig.Keys.NoteOn(60);
        rig.RenderBeats(1);
        Assert.Equal(2, rig.Runner!.Sounding);

        var ends = rig.Runner.Reset();

        Assert.Equal([60, 67], ends.ToArray().Select(e => e.Pitch).Order());
        Assert.All(ends.ToArray(), e => Assert.False(e.IsOn));
        Assert.Equal(0, rig.Runner.Sounding);

        rig.Forget();
        rig.RenderBeats(1);
        Assert.Equal([60, 67], rig.StartPitches.Order()); // the key is still down
    }

    [Fact]
    public void A_flood_of_events_while_nothing_renders_cannot_leave_a_note_stuck()
    {
        using var rig = Rig(s => s.Harmonize(0, 12));

        for (int i = 0; i < 6000; i++)
        {
            rig.Keys.NoteOn(40 + i % 40);
            rig.Keys.NoteOff(40 + i % 40);
        }

        rig.Keys.NoteOn(72); // the one key left down
        rig.Render(2);

        Assert.Equal(2, rig.Runner!.Sounding);
        rig.Keys.AllNotesOff();
        rig.Render();
        rig.AssertAllEnded();
    }

    [Fact]
    public void A_full_stage_drops_starts_and_never_ends()
    {
        using var rig = Rig(s => s.Harmonize(0, 1, 2, 3, 4, 5, 6, 7));

        for (int p = 20; p < 120; p++)
            rig.Keys.NoteOn(p);
        rig.Render(2);
        Assert.True(rig.Runner!.Sounding <= NoteStageCapacity);
        Assert.True(rig.Runner.Dropped > 0);

        rig.Keys.AllNotesOff();
        rig.Render(2);
        rig.AssertAllEnded();
    }

    private const int NoteStageCapacity = 128;

    // --- Every operator, every storm -------------------------------------------------------------------------------------

    public static TheoryData<string> Chains => new()
    {
        "transpose-harmonize",
        "echo-delay",
        "strum-humanize",
        "length-echo",
        "mono-lowest",
        "latch-harmonize",
        "merge",
        "pattern-merge",
        "everything",
    };

    private static NoteStream Chain(string name, NoteInput keys)
    {
        var s = keys.Stream;
        return name switch
        {
            "transpose-harmonize" => s.Transpose(5).Harmonize(0, 4, 7),
            "echo-delay" => s.Echo(3, 0.25, 0.7).Delay(0.1),
            "strum-humanize" => s.Strum(0.05).Humanize(0.1, 0.2, 3),
            "length-echo" => s.Length(0.3).Echo(2, 0.5),
            "mono-lowest" => s.Mono(NotePriority.Lowest).Echo(1, 0.2),
            "latch-harmonize" => s.Latch().Harmonize(Scale.Minor(60), 0, 2, 4),
            "merge" => s.Merge(s.Octave(1), s.Delay(0.3).Keep(Pick.Chance(0.5, 4))),
            "pattern-merge" => Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>").ToStream().Merge(s.Strum(0.02)),
            _ => s.Detune(30).Range(36, 96, RangeMode.Fold).Quantize(Scale.Major(60)).Harmonize(0, 7).Strum(0.03).Echo(2, 0.25).Humanize(0.05).Length(0.4),
        };
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void However_the_keys_are_played_every_note_that_starts_ends(string chain)
    {
        var random = new Random(chain.GetHashCode() ^ 12345);
        using var rig = new NoteRig();
        rig.Run(Chain(chain, rig.Keys));
        var started = new List<PlayingNote>();

        for (int block = 0; block < 1500; block++)
        {
            for (int i = random.Next(3); i > 0; i--)
            {
                int pitch = random.Next(30, 100);
                switch (random.Next(7))
                {
                    case 0 or 1 or 2:
                        rig.Keys.NoteOn(pitch, (float)(0.1 + random.NextDouble() * 0.9), random.Next(-40, 40));
                        break;
                    case 3 or 4:
                        rig.Keys.NoteOff(pitch);
                        break;
                    case 5:
                        started.Add(rig.Keys.Start(pitch));
                        break;
                    default:
                        if (started.Count > 0)
                        {
                            started[random.Next(started.Count)].Release();
                            started.RemoveAll(n => !n.IsHeld);
                        }

                        if (random.Next(40) == 0)
                            rig.Keys.AllNotesOff();
                        break;
                }
            }

            rig.Render();
            if (block == 700)
                rig.Events.AddRange(rig.Runner!.Reset().ToArray().Select(e => new NoteRig.Seen(rig.Clock.Beats, e)));
        }

        rig.Keys.AllNotesOff();
        rig.RenderBeats(8); // long enough for every echo and delay to have played out
        if (chain.StartsWith("latch", StringComparison.Ordinal) || chain.StartsWith("pattern", StringComparison.Ordinal))
            rig.Events.AddRange(rig.Runner!.Reset().ToArray().Select(e => new NoteRig.Seen(rig.Clock.Beats, e))); // a latch holds notes past their keys and a pattern plays on by itself, on purpose; a stop ends them
        Assert.Equal(0, rig.Runner!.Sounding);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Running_a_block_allocates_nothing_once_the_stream_is_built()
    {
        using var rig = Rig(s => s.Transpose(2).Harmonize(0, 7).Keep(Pick.Pitch(30, 100)).Echo(2, 0.25).Strum(0.01).Humanize(0.05).Delay(0.1));
        rig.Keys.NoteOn("c3 e3 g3");
        rig.Render(300); // warm up: jitting, first echoes

        AllocationAssert.NoneOf(() =>
        {
            rig.Keys.NoteOn("c4 e4"); // a key press itself belongs to the control thread; only the blocks are measured
            rig.Keys.NoteOff("c4 e4");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
            {
                _ = rig.Runner!.Process(NoteRig.Block).Length;
                rig.Clock.Advance(NoteRig.Block);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }

    // --- Sound -----------------------------------------------------------------------------------------------------------

    private static float[] Render(SoundContext output, double seconds)
    {
        var frames = new StereoFrame[(int)(seconds * NoteRig.SampleRate)];
        output.Render(frames);
        return frames.Select(f => f.ToMono()).ToArray();
    }

    [Fact]
    public void Play_sounds_the_notes_that_came_out_of_the_stream()
    {
        var clock = new HostClock(NoteRig.SampleRate);
        var keys = new NoteInput { Clock = clock };
        using var output = new SoundContext(NoteRig.SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(keys.Stream.Transpose(12).Play(v => Sine(v.Frequency)), fadeSeconds: 0);

        keys.NoteOn(69, 1f); // A4, an octave up is 880
        var samples = Render(output, 0.5);

        Assert.InRange(SignalAnalysis.EstimateFrequency(samples.AsSpan(4800), NoteRig.SampleRate), 878, 882);
    }

    [Fact]
    public void Play_releases_the_voice_when_the_key_comes_up()
    {
        var clock = new HostClock(NoteRig.SampleRate);
        var keys = new NoteInput { Clock = clock };
        using var output = new SoundContext(NoteRig.SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(keys.Stream.Play(v => Constant(1).Adsr(0, 0, 1, 0.05)), fadeSeconds: 0);

        keys.NoteOn(60, 1f);
        Render(output, 0.2);
        keys.NoteOff(60);
        Render(output, 0.1); // release, then quiet
        var after = Render(output, 0.2);

        Assert.True(SignalAnalysis.Peak(after) < 0.001f);
    }

    [Fact]
    public void Gate_is_one_while_a_key_is_down()
    {
        var clock = new HostClock(NoteRig.SampleRate);
        var keys = new NoteInput { Clock = clock };
        using var output = new SoundContext(NoteRig.SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(keys.Stream.Gate(), fadeSeconds: 0);

        Assert.Equal(0f, Render(output, 0.1).Max());
        keys.NoteOn(60);
        Assert.Equal(1f, Render(output, 0.1).Last());
        keys.NoteOff(60);
        Assert.Equal(0f, Render(output, 0.1).Last());
    }

    // --- Arguments -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Arguments_are_checked_when_the_stream_is_built()
    {
        var s = NoteStream.Silence;
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Range(70, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Velocity(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Echo(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Echo(17, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Echo(2, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Delay(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Strum(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Length(0));
        Assert.Throws<ArgumentException>(() => s.Harmonize());
        Assert.Throws<ArgumentException>(() => s.Harmonize(0, 1, 2, 3, 4, 5, 6, 7, 8));
        Assert.Throws<ArgumentException>(() => s.Merge());
        Assert.Throws<ArgumentOutOfRangeException>(() => Pick.Pitch(80, 60));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pick.Chance(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoteInput().NoteOn(60, 0.5f, 150.0));
    }
}
