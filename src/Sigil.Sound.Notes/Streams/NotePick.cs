namespace Sigil.Sound;

/// <summary>
/// A test for notes, described as data: what <see cref="NoteStream.Keep"/> and <see cref="NoteStream.Drop"/> use to choose. Make one with
/// <see cref="Pick"/> and combine with <see cref="And"/>, <see cref="Or"/> and <see cref="Not"/>.
/// </summary>
/// <remarks>
/// A pick is a value, not a function of yours, which is why a script that may not run code of its own can still filter notes.
/// It is decided once, when the note starts; the note's end follows it.
/// </remarks>
public abstract class NotePick
{
    internal NotePick()
    {
    }

    internal abstract bool Matches(in NoteEvent note);

    /// <summary>Notes that match both this pick and <paramref name="other"/>.</summary>
    /// <param name="other">The second test.</param>
    public NotePick And(NotePick other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Combined(this, other, all: true);
    }

    /// <summary>Notes that match this pick or <paramref name="other"/> (or both).</summary>
    /// <param name="other">The second test.</param>
    public NotePick Or(NotePick other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Combined(this, other, all: false);
    }

    /// <summary>The notes this pick does not match.</summary>
    public NotePick Not() => new Negated(this);

    private sealed class Combined(NotePick a, NotePick b, bool all) : NotePick
    {
        internal override bool Matches(in NoteEvent note) => all ? a.Matches(note) && b.Matches(note) : a.Matches(note) || b.Matches(note);
    }

    private sealed class Negated(NotePick pick) : NotePick
    {
        internal override bool Matches(in NoteEvent note) => !pick.Matches(note);
    }
}

/// <summary>The ways to choose notes: <c>stream.Keep(Pick.Pitch(60, 127))</c>.</summary>
public static class Pick
{
    /// <summary>Every note.</summary>
    public static NotePick All { get; } = new Predicate(static (in NoteEvent _) => true);

    /// <summary>Notes with a pitch from <paramref name="low"/> to <paramref name="high"/>, both included.</summary>
    /// <param name="low">The lowest MIDI pitch, 0 to 127.</param>
    /// <param name="high">The highest MIDI pitch, at least <paramref name="low"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 127, or <paramref name="high"/> below <paramref name="low"/>.</exception>
    public static NotePick Pitch(int low, int high)
    {
        if (low < 0 || high > 127 || low > high) throw new ArgumentOutOfRangeException(nameof(low), low, "The range must lie inside 0 to 127 and end after it starts.");
        return new Predicate((in NoteEvent n) => n.Pitch >= low && n.Pitch <= high);
    }

    /// <summary>Notes whose pitch class belongs to <paramref name="scale"/>, in any octave.</summary>
    /// <param name="scale">The scale, such as <c>Scale.Parse("c minor")</c>.</param>
    public static NotePick InScale(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        var allowed = new bool[12];
        for (int degree = 0; degree < scale.Count; degree++)
            allowed[((scale[degree] % 12) + 12) % 12] = true;
        return new Predicate((in NoteEvent n) => allowed[n.Pitch % 12]);
    }

    /// <summary>Notes with a velocity from <paramref name="low"/> to <paramref name="high"/>, both included.</summary>
    /// <param name="low">The softest, 0 to 1.</param>
    /// <param name="high">The hardest, at least <paramref name="low"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 1, or <paramref name="high"/> below <paramref name="low"/>.</exception>
    public static NotePick Velocity(double low, double high)
    {
        if (!(low >= 0 && high <= 1 && low <= high)) throw new ArgumentOutOfRangeException(nameof(low), low, "The range must lie inside 0 to 1 and end after it starts.");
        return new Predicate((in NoteEvent n) => n.Velocity >= low && n.Velocity <= high);
    }

    /// <summary>
    /// A random share of the notes. Which ones depends only on the seed and the note, so the same performance picks the same notes.
    /// </summary>
    /// <param name="probability">The share to match, 0 to 1.</param>
    /// <param name="seed">Another seed picks other notes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 1.</exception>
    public static NotePick Chance(double probability, int seed = 0)
    {
        if (!(probability is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(probability), probability, "The probability must be between 0 and 1.");
        return new Predicate((in NoteEvent n) => NoteIds.Unit(seed, n.Id) < probability);
    }

    /// <summary>Notes tagged with <paramref name="channel"/> (see <see cref="NoteStream.Channel"/>).</summary>
    /// <param name="channel">The channel, 0 or more.</param>
    public static NotePick Channel(int channel) => new Predicate((in NoteEvent n) => n.Channel == channel);

    private delegate bool Test(in NoteEvent note);

    private sealed class Predicate(Test test) : NotePick
    {
        internal override bool Matches(in NoteEvent note) => test(note);
    }
}
