using Sigil.Sound.Notes;

// Inside the Sigil.Sound namespace, "Notes" means the namespace; user code outside it can write Notes.Parse.
using NoteSeq = Sigil.Sound.Notes.Notes;

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
        Assert.Equal(expected, NoteSeq.ParsePitch(name));
    }

    [Fact]
    public void Parse_sequence_supports_linq()
    {
        var names = NoteSeq.Parse("C4 E4 G4 B4")
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
        Assert.Throws<FormatException>(() => NoteSeq.ParsePitch(name));
    }
}
