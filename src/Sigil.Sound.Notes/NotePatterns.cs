namespace Sigil.Sound;

/// <summary>Working with patterns of notes.</summary>
public static partial class NotePatterns
{
    /// <summary>Moves every note by <paramref name="semitones"/>.</summary>
    public static Pattern<Note> Transpose(this Pattern<Note> pattern, int semitones) =>
        pattern.Select(n => n.Transpose(semitones));

    /// <summary>Sets how hard every note is played, from 0 to 1.</summary>
    public static Pattern<Note> Velocity(this Pattern<Note> pattern, double velocity)
    {
        if (!(velocity is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity must be between 0 and 1.");
        float value = (float)velocity;
        return pattern.Select(n => n with { Velocity = value });
    }

    /// <summary>
    /// Writes the first <paramref name="cycles"/> cycles out as a looping <see cref="Pattern"/> that
    /// <see cref="Patterns.Play"/> can play. One cycle is <see cref="Pat.BeatsPerCycle"/> beats, and each note lasts as long as its event.
    /// Anything that changes from cycle to cycle (<c>&lt;a b&gt;</c>, <c>Every</c>) repeats after this many cycles, so pass the length of the whole variation.
    /// </summary>
    /// <param name="pattern">The notes to write out.</param>
    /// <param name="cycles">How many cycles to write out before the loop starts over.</param>
    public static Pattern ToPattern(this Pattern<Note> pattern, int cycles = 1)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (cycles < 1) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "Write out at least one cycle.");

        LiveQuery.Begin();
        pattern.Validate(cycles);
        var found = new List<PatternEvent<Note>>();
        pattern.Query(new Arc(Fraction.Zero, new Fraction(cycles)), found);
        if (LiveQuery.Touched)
            throw new InvalidOperationException("This pattern follows live notes (NoteStream.Chord or Fold), so it has no fixed cycles to write out. Play it, or send it out with Out.Notes.");

        var events = new List<TimedNote>();
        foreach (var e in found)
        {
            if (!e.HasOnset) continue;
            double start = e.Whole.Begin.ToDouble() * Pat.BeatsPerCycle;
            double length = e.Whole.Length.ToDouble() * Pat.BeatsPerCycle;
            events.Add(new TimedNote(start, e.Value with { Beats = length }));
        }

        return new Pattern(events, cycles * Pat.BeatsPerCycle, true);
    }
}
