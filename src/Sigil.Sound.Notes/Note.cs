using System.Globalization;

namespace Sigil.Sound;

/// <summary>An immutable note: a MIDI pitch, a velocity in [0, 1] and a length in beats.</summary>
public readonly record struct Note(int Pitch, float Velocity = 0.8f, double Beats = 1.0)
{
    /// <summary>Frequency in Hz in 12-tone equal temperament with A4 (MIDI 69) at 440 Hz.</summary>
    public double Frequency => 440.0 * Math.Pow(2.0, (Pitch - 69) / 12.0);

    /// <summary>A copy of this note moved by <paramref name="semitones"/>.</summary>
    public Note Transpose(int semitones) => this with { Pitch = Pitch + semitones };

    /// <summary>Scientific pitch notation, for example <c>C#4</c>.</summary>
    public string Name => Notes.NameOf(Pitch);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Name} (v={Velocity:0.##}, {Beats} beats)");
}
