

namespace Sigil.Sound.Tests;

public class NotesTests
{
    [Theory]
    [InlineData("A4", 69)]
    [InlineData("C4", 60)]
    [InlineData("C#4", 61)]
    [InlineData("Eb3", 51)]
    [InlineData("C-1", 0)]
    [InlineData("G9", 127)]
    public void ParsePitch_reads_scientific_pitch_notation(string name, int expected)
    {
        Assert.Equal(expected, Notes.ParsePitch(name));
    }

    [Fact]
    public void Parse_sequence_supports_linq()
    {
        var names = Notes.Parse("C4 E4 G4 B4")
            .Transpose(12)
            .Where(n => n.Pitch < 83)
            .Select(n => n.Name);

        Assert.Equal(["C5", "E5", "G5"], names);
    }

    [Fact]
    public void A4_is_440_hz()
    {
        Assert.Equal(440.0, new Note(69).Frequency, 6);
    }

    [Theory]
    [InlineData("H4")]
    [InlineData("C")]
    [InlineData("C10")]
    public void ParsePitch_rejects_bad_names(string name)
    {
        Assert.Throws<FormatException>(() => Notes.ParsePitch(name));
    }

    [Fact]
    public void Parse_reads_names_as_Mini_does_octave_3_by_default_and_a_velocity()
    {
        var notes = Notes.Parse("C eb4 g4:0.5");

        Assert.Equal([48, 63, 67], notes.Select(n => n.Pitch));
        Assert.Equal(0.5f, notes[2].Velocity);
    }

    [Fact]
    public void Drums_are_notes_on_the_general_midi_map()
    {
        var hits = Mini.Drums("bd ~ SN hh:0.4").Cycle(0).Select(e => e.Value).ToArray();

        Assert.Equal([36, 38, 42], hits.Select(n => n.Pitch));
        Assert.Equal(0.4f, hits[2].Velocity);
    }

    [Fact]
    public void An_unknown_drum_is_reported_with_its_column()
    {
        var error = Assert.Throws<MiniNotationException>(() => Mini.Drums("bd xx"));

        Assert.Equal(3, error.Position);
        Assert.Contains("xx", error.Message);
    }
}
