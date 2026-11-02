namespace Sigil.Sound.Notes;

/// <summary>
/// Creating note sequences. Sequences are plain <see cref="IEnumerable{T}"/> values, so LINQ
/// (<c>Where</c>, <c>Select</c>, ...) already works on them; timing, playback and MIDI file reading come later.
/// </summary>
public static class Notes
{
    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>
    /// Parses whitespace-separated note names in scientific pitch notation, such as <c>"C4 Eb4 G4"</c>.
    /// Accepts <c>#</c> and <c>b</c> accidentals; octave -1 through 9.
    /// </summary>
    public static IReadOnlyList<Note> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var notes = new Note[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
            notes[i] = new Note(ParsePitch(tokens[i]));
        return notes;
    }

    /// <summary>Parses one note name, such as <c>"A4"</c> or <c>"F#3"</c>, to a MIDI pitch.</summary>
    public static int ParsePitch(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        int semitone = char.ToUpperInvariant(name[0]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => throw new FormatException($"'{name}' is not a note name: it must start with A to G."),
        };

        int index = 1;
        while (index < name.Length && name[index] is '#' or 'b')
        {
            semitone += name[index] == '#' ? 1 : -1;
            index++;
        }

        if (!int.TryParse(name.AsSpan(index), System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out int octave) || octave is < -1 or > 9)
            throw new FormatException($"'{name}' needs an octave from -1 to 9, as in C4.");

        int pitch = (octave + 1) * 12 + semitone;
        if (pitch is < 0 or > 127)
            throw new FormatException($"'{name}' is outside the MIDI range.");
        return pitch;
    }

    /// <summary>The scientific pitch name of a MIDI pitch, using sharps.</summary>
    public static string NameOf(int pitch) => $"{SharpNames[((pitch % 12) + 12) % 12]}{Math.Floor(pitch / 12.0) - 1}";

    /// <summary>Transposes every note in a sequence.</summary>
    public static IEnumerable<Note> Transpose(this IEnumerable<Note> notes, int semitones)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Select(n => n.Transpose(semitones));
    }
}
