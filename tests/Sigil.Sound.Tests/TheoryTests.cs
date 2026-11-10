using Sigil.Sound.Testing;
using static Sigil.Sound.Pitch;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class PitchTests
{
    [Fact]
    public void Names_are_midi_pitches_with_middle_c_at_C4()
    {
        Assert.Equal(60, C4);
        Assert.Equal(69, A4);
        Assert.Equal(63, Eb4);
        Assert.Equal(Cs4, Db4);
        Assert.Equal(12, C0);
        Assert.Equal(119, B8);
        Assert.Equal(Fs3, Notes.ParsePitch("F#3"));
    }

    [Fact]
    public void Every_name_agrees_with_the_note_parser()
    {
        string[] sharps = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        for (int octave = 0; octave <= 8; octave++)
            for (int pc = 0; pc < 12; pc++)
                Assert.Equal((octave + 1) * 12 + pc, Notes.ParsePitch(sharps[pc] + octave));
    }
}

public class ScaleTests
{
    [Fact]
    public void Degrees_climb_the_scale_and_wrap_by_octaves()
    {
        var scale = Scale.Minor(C3);

        Assert.Equal([48, 50, 51, 53, 55, 56, 58, 60, 62], Enumerable.Range(0, 9).Select(d => scale[d]));
        Assert.Equal([46, 43, 36], new[] { scale[-1], scale[-3], scale[-7] });
    }

    [Fact]
    public void A_pentatonic_wraps_after_five_degrees()
    {
        var scale = Scale.MinorPentatonic(A2);

        Assert.Equal(5, scale.Count);
        Assert.Equal(scale[0] + 12, scale[5]);
        Assert.Equal(scale[2] + 24, scale[12]);
    }

    [Theory]
    [InlineData("c3 minor", 48, 51)]
    [InlineData("C3:dorian", 48, 51)]
    [InlineData("f#2 minor pentatonic", 42, 47)]
    [InlineData("d major", 50, 54)]
    [InlineData("a3 Harmonic-Minor", 57, 60)]
    public void Parse_reads_a_root_and_a_name(string text, int root, int third)
    {
        var scale = Scale.Parse(text);

        Assert.Equal(root, scale[0]);
        Assert.Equal(third, scale[2]);
    }

    [Fact]
    public void Every_named_scale_rises_inside_one_octave()
    {
        var scales = new[]
        {
            Scale.Major(C4), Scale.Minor(C4), Scale.HarmonicMinor(C4), Scale.MelodicMinor(C4), Scale.Dorian(C4), Scale.Phrygian(C4),
            Scale.Lydian(C4), Scale.Mixolydian(C4), Scale.Locrian(C4), Scale.MajorPentatonic(C4), Scale.MinorPentatonic(C4),
            Scale.Blues(C4), Scale.WholeTone(C4), Scale.Chromatic(C4),
        };

        foreach (var scale in scales)
            for (int d = 1; d < scale.Count; d++)
                Assert.InRange(scale[d] - scale[d - 1], 1, 4);
    }

    [Fact]
    public void Bad_scales_are_rejected()
    {
        Assert.Throws<FormatException>(() => Scale.Parse("c3 sparkly"));
        Assert.Throws<FormatException>(() => Scale.Parse("minor"));
        Assert.Throws<ArgumentException>(() => new Scale(60, [1, 2]));
        Assert.Throws<ArgumentException>(() => new Scale(60, [0, 4, 2]));
        Assert.Throws<ArgumentException>(() => new Scale(60, [0, 12]));
    }

    [Fact]
    public void Degrees_give_notes_and_a_pattern_of_degrees_gives_a_melody()
    {
        var scale = Scale.Minor(C3);

        Assert.Equal([48, 51, 55, 60], scale.Degrees(0, 2, 4, 7).Select(n => n.Pitch));

        var melody = scale.Notes(Mini.Integers("0 2 <4 7> ~"));
        Assert.Equal([48, 51, 55], melody.Cycle(0).Select(e => e.Value.Pitch));
        Assert.Equal([48, 51, 60], melody.Cycle(1).Select(e => e.Value.Pitch));
    }

    [Fact]
    public void Integers_accept_negatives_and_reject_fractions()
    {
        Assert.Equal([-1, 0, 3], Mini.Integers("-1 0 3").Cycle(0).Select(e => e.Value));
        Assert.Throws<MiniNotationException>(() => Mini.Integers("1.5"));
    }
}

public class ChordTests
{
    [Fact]
    public void Chords_are_built_from_a_root_and_a_quality()
    {
        Assert.Equal([60, 64, 67], Chord.Major(C4).Pitches);
        Assert.Equal([60, 63, 67], Chord.Minor(C4).Pitches);
        Assert.Equal([48, 51, 55, 58], Chord.Minor7(C3).Pitches);
        Assert.Equal([55, 59, 62, 65], Chord.Dominant7(G3).Pitches);
        Assert.Equal([60, 64, 67, 71, 74], Chord.Of(C4, "maj9").Pitches);
    }

    [Theory]
    [InlineData("c3:min7", new[] { 48, 51, 55, 58 })]
    [InlineData("c3", new[] { 48, 52, 55 })]
    [InlineData("G3:7", new[] { 55, 59, 62, 65 })]
    [InlineData("a3:m", new[] { 57, 60, 64 })]
    [InlineData("a3:M", new[] { 57, 61, 64 })]
    [InlineData("a3:M7", new[] { 57, 61, 64, 68 })]
    [InlineData("a3:Maj7", new[] { 57, 61, 64, 68 })]
    [InlineData("eb3:dim7", new[] { 51, 54, 57, 60 })]
    public void Parse_reads_a_root_and_an_optional_quality(string text, int[] pitches) =>
        Assert.Equal(pitches, Chord.Parse(text).Pitches);

    [Fact]
    public void Unknown_chords_are_rejected()
    {
        Assert.Throws<FormatException>(() => Chord.Parse("c3:sparkly"));
        Assert.Throws<FormatException>(() => Chord.Parse("h3"));
        Assert.Throws<MiniNotationException>(() => Mini.Chords("c3 f3:nope"));
    }

    [Fact]
    public void Inversions_move_the_outer_notes_by_octaves()
    {
        var c = Chord.Major(C4);

        Assert.Equal([64, 67, 72], c.Inversion(1).Pitches);
        Assert.Equal([67, 72, 76], c.Inversion(2).Pitches);
        Assert.Equal([55, 60, 64], c.Inversion(-1).Pitches);
        Assert.Equal([62, 66, 69], Chord.Major(C4).Transpose(2).Pitches);
    }

    private static int[] Arp(ArpMode mode, int octaves = 1) =>
        Chord.Major(C4).Arp(mode, octaves).Cycle(0).Select(e => e.Value.Pitch).ToArray();

    [Fact]
    public void Arpeggios_walk_the_chord_in_the_chosen_order()
    {
        Assert.Equal([60, 64, 67], Arp(ArpMode.Up));
        Assert.Equal([67, 64, 60], Arp(ArpMode.Down));
        Assert.Equal([60, 64, 67, 64], Arp(ArpMode.UpDown));
        Assert.Equal([67, 64, 60, 64], Arp(ArpMode.DownUp));
        Assert.Equal([60, 67, 64], Arp(ArpMode.Converge));
        Assert.Equal([60, 64, 67, 72, 76, 79], Arp(ArpMode.Up, octaves: 2));
    }

    [Fact]
    public void An_arpeggio_divides_the_chords_event_equally()
    {
        var events = Mini.Chords("c3:min7 f3:7").Arp().Cycle(0);

        Assert.Equal(8, events.Count);
        Assert.Equal(new Arc(0, new Fraction(1, 8)), events[0].Whole);
        Assert.Equal(new Arc(new Fraction(3, 8), new Fraction(1, 2)), events[3].Whole);
        Assert.Equal(new Arc(new Fraction(1, 2), new Fraction(5, 8)), events[4].Whole);
        Assert.Equal([48, 51, 55, 58, 53, 57, 60, 63], events.Select(e => e.Value.Pitch));
    }

    [Fact]
    public void Block_chords_sound_all_notes_for_the_whole_event()
    {
        var events = Mini.Chords("c3 f3").Notes().Cycle(0);

        Assert.Equal(6, events.Count);
        Assert.All(events.Take(3), e => Assert.Equal(new Arc(0, new Fraction(1, 2)), e.Whole));
        Assert.Equal([48, 52, 55, 53, 57, 60], events.Select(e => e.Value.Pitch));
    }

    [Fact]
    public void Querying_part_of_an_arpeggio_cuts_it_to_the_part()
    {
        var events = new List<PatternEvent<Note>>();

        Chord.Major(C4).Arp().Query(new Arc(new Fraction(1, 6), new Fraction(1, 2)), events);

        // Steps are thirds: the first is cut off from 1/6, the second whole, the third starts after the query's end... only two touch.
        Assert.Equal([60, 64], events.Select(e => e.Value.Pitch));
        Assert.False(events[0].HasOnset);
        Assert.True(events[1].HasOnset);
    }

    [Fact]
    public void Arpeggio_ideas_compose_with_pattern_tools()
    {
        var pattern = Mini.Chords("c3:min7 f3:7").Arp(ArpMode.UpDown).Fast(2).Every(2, p => p.Rev());

        AudioAssert.IsFinite(pattern.Play(v => Saw(v.Frequency).Adsr(0.002, 0.05, 0.4, 0.05), bpm: 120).Render(2.0, 48_000));
    }

    [Fact]
    public void Arpeggios_and_chords_allocate_nothing_when_queried_again()
    {
        var pattern = Pat.Stack(
            Mini.Chords("c3:min7 f3:7 [g3:sus4 g3]").Arp(ArpMode.UpDown, 2),
            Mini.Chords("c2 f2").Notes());
        var buffer = new List<PatternEvent<Note>>(256);
        for (int i = 0; i < 100; i++)
        {
            buffer.Clear();
            pattern.Query(new Arc(i, i + 1), buffer);
        }

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                buffer.Clear();
                pattern.Query(new Arc(new Fraction(i, 4), new Fraction(i + 1, 4)), buffer);
            }
        });
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        var chords = Mini.Chords("c3");

        Assert.Throws<ArgumentOutOfRangeException>(() => chords.Arp(octaves: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => chords.Arp((ArpMode)99));
        Assert.Throws<ArgumentException>(() => new Chord([]));
    }
}

public class ScaleTextTests
{
    [Fact]
    public void Notes_reads_degrees_from_text_like_Mini_Integers()
    {
        var scale = Scale.Parse("c3 minor");

        var fromText = scale.Notes("0 2 <4 7> ~").Cycle(1).Select(e => e.Value.Pitch);
        var fromPattern = scale.Notes(Mini.Integers("0 2 <4 7> ~")).Cycle(1).Select(e => e.Value.Pitch);

        Assert.Equal(fromPattern, fromText);
        Assert.Equal([48, 51, 60], fromText);
    }

    [Fact]
    public void Degrees_reads_whole_numbers_from_text()
    {
        Assert.Equal([48, 50, 51, 55], Scale.Parse("c3 minor").Degrees("0 1 2 4").Select(n => n.Pitch));
    }

    [Fact]
    public void Degrees_rejects_text_that_is_not_whole_numbers()
    {
        Assert.Throws<MiniNotationException>(() => Scale.Parse("c3 minor").Degrees("0 two"));
    }
}
