namespace Sigil.Sound.Tests;

/// <summary>The raw operators: map, filter, expand and fold with functions of your own. Whatever they return, notes end.</summary>
public class NoteStreamRawTests
{
    private static NoteRig Rig(Func<NoteStream, NoteStream> build)
    {
        var rig = new NoteRig();
        rig.Run(build(rig.Keys.Stream));
        return rig;
    }

    [Fact]
    public void Select_changes_each_note_and_its_end_still_follows()
    {
        using var rig = Rig(s => s.Select(n => n with { Pitch = n.Pitch + 7, Velocity = n.Velocity / 2 }));

        rig.Keys.NoteOn(60, 0.8f);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        var start = Assert.Single(rig.Starts).Event;
        Assert.Equal((67, 0.4f), (start.Pitch, start.Velocity));
        Assert.Equal(67, Assert.Single(rig.Ends).Event.Pitch);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Select_cannot_change_the_id_or_turn_a_start_into_an_end()
    {
        using var rig = Rig(s => s.Select(n => n with { Id = 999, Kind = NoteEventKind.Off }));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.NotEqual(999, Assert.Single(rig.Starts).Event.Id);
        Assert.True(rig.Starts.Single().Event.IsOn);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Select_that_leaves_the_midi_range_drops_the_note()
    {
        using var rig = Rig(s => s.Select(n => n with { Pitch = n.Pitch * 3 }));

        rig.Keys.NoteOn(60);
        rig.Keys.NoteOn(20);
        rig.Render();
        rig.Keys.AllNotesOff();
        rig.Render();

        Assert.Equal([60], rig.StartPitches);
        rig.AssertAllEnded();
    }

    [Fact]
    public void Where_keeps_the_notes_the_function_accepts()
    {
        using var rig = Rig(s => s.Where(n => n.Pitch % 2 == 0 && n.Velocity > 0.3f));

        rig.Keys.NoteOn(60, 0.8f);
        rig.Keys.NoteOn(61, 0.8f);
        rig.Keys.NoteOn(62, 0.1f);
        rig.Render();

        Assert.Equal([60], rig.StartPitches);
    }

    [Fact]
    public void SelectMany_makes_several_notes_that_all_end_with_the_key_and_stops_at_the_capacity()
    {
        using var rig = Rig(s => s.SelectMany((n, emit) =>
        {
            emit.Add(n);
            emit.Add(n.Pitch + 12, 0.5f);
            if (n.Pitch == 40)
            {
                for (int i = 0; i < 30; i++)
                    emit.Add(41 + i);
            }
        }));

        rig.Keys.NoteOn(60);
        rig.Render();
        Assert.Equal([60, 72], rig.StartPitches);

        rig.Keys.NoteOn(40);
        rig.Render();
        Assert.Equal(NoteEmitter.Capacity, rig.Starts.Count() - 2);

        rig.Keys.AllNotesOff();
        rig.Render();
        rig.AssertAllEnded();
    }

    [Fact]
    public void SelectMany_can_make_nothing()
    {
        using var rig = Rig(s => s.SelectMany((n, emit) => { }));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.Empty(rig.Events);
    }

    [Fact]
    public void Scan_carries_state_through_starts_and_ends_and_can_start_notes()
    {
        var states = new List<int>();
        using var rig = Rig(s => s.Scan(0, (down, e, emit) =>
        {
            int next = e.IsOn ? down + 1 : down - 1;
            states.Add(next);

            // A second key held on top of another gets a fifth above it.
            if (e.IsOn && down >= 1)
                emit.Add(e.Pitch + 7);
            return next;
        }));

        rig.Keys.NoteOn(60);
        rig.Render();
        rig.Keys.NoteOn(64);
        rig.Render();
        rig.Keys.NoteOff(64);
        rig.Render();
        rig.Keys.NoteOff(60);
        rig.Render();

        Assert.Equal([1, 2, 1, 0], states);
        Assert.Equal([71], rig.Starts.Where(s => s.Event.Pitch != 60 && s.Event.Pitch != 64).Select(s => s.Event.Pitch));
        rig.AssertAllEnded();
    }

    [Fact]
    public void A_function_that_throws_surfaces_to_the_host_instead_of_being_swallowed()
    {
        using var rig = Rig(s => s.Select(n => throw new InvalidOperationException("boom")));

        rig.Keys.NoteOn(60);

        var ex = Assert.Throws<InvalidOperationException>(() => rig.Render());
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void Whatever_the_function_does_every_note_ends()
    {
        var random = new Random(7);
        using var rig = Rig(s => s
            .SelectMany((n, emit) =>
            {
                for (int i = random.Next(4); i > 0; i--)
                    emit.Add(random.Next(-20, 150), (float)random.NextDouble());
            })
            .Select(n => n with { Pitch = n.Pitch + random.Next(-10, 10) })
            .Where(n => random.Next(5) != 0));

        for (int block = 0; block < 600; block++)
        {
            if (random.Next(3) == 0)
                rig.Keys.NoteOn(random.Next(30, 100), 0.8f);
            if (random.Next(3) == 0)
                rig.Keys.NoteOff(random.Next(30, 100));
            if (random.Next(200) == 0)
                rig.Keys.AllNotesOff();
            rig.Render();
        }

        rig.Keys.AllNotesOff();
        rig.Render(2);
        Assert.Equal(0, rig.Runner!.Sounding);
        rig.AssertAllEnded();
    }
}
