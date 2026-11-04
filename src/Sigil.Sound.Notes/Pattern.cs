namespace Sigil.Sound;

/// <summary>Common note lengths, in beats (a beat is a quarter note), for <see cref="Patterns.Every"/>.</summary>
public static class Beats
{
    /// <summary>Four beats.</summary>
    public const double Whole = 4;

    /// <summary>Two beats.</summary>
    public const double Half = 2;

    /// <summary>One beat.</summary>
    public const double Quarter = 1;

    /// <summary>Half a beat.</summary>
    public const double Eighth = 0.5;

    /// <summary>A quarter of a beat.</summary>
    public const double Sixteenth = 0.25;
}

/// <summary>A note and when it starts, in beats from the start of the pattern.</summary>
public readonly record struct TimedNote(double Start, Note Note);

/// <summary>
/// The note a voice is playing, as signals you build a sound from. Each one follows the note that triggered the
/// voice, so <c>Saw(v.Frequency)</c> plays the right pitch for every note, and the voice is reused without
/// being rebuilt. See <see cref="Patterns.Play"/>.
/// </summary>
public sealed class Voice
{
    internal static Voice Instance { get; } = new();

    private Voice()
    {
    }

    /// <summary>The pitch in Hz, as a signal: <c>Saw(v.Frequency)</c>, or <c>Saw(v.Frequency * 2)</c> an octave up.</summary>
    public Sound Frequency => VoiceSignals.Frequency;

    /// <summary>The MIDI pitch, 0 to 127, as a signal.</summary>
    public Sound Pitch => VoiceSignals.Pitch;

    /// <summary>
    /// How hard the note is played, from 0 to 1, as a signal. <see cref="Patterns.Play"/> already scales the voice by it;
    /// use this to shape the sound as well, for example to open a filter on harder notes.
    /// </summary>
    public Sound Velocity => VoiceSignals.Velocity;

    /// <summary>
    /// 1 while the note is held and 0 once it ends, as a signal. <c>Adsr</c> inside a voice already releases with the note;
    /// use this for anything custom, such as <c>Noise() * v.Gate</c>.
    /// </summary>
    public Sound Gate => VoiceSignals.Gate;
}

/// <summary>
/// Notes placed in time: an immutable timeline that <see cref="Patterns.Play"/> turns into sound.
/// Create one with <see cref="Patterns.Every"/>, change it with <see cref="Loop"/> and <see cref="Transpose"/>.
/// </summary>
public sealed class Pattern
{
    internal Pattern(IReadOnlyList<TimedNote> events, double length, bool isLooping)
    {
        Events = events;
        Length = length;
        IsLooping = isLooping;
    }

    /// <summary>The notes, sorted by start time.</summary>
    public IReadOnlyList<TimedNote> Events { get; }

    /// <summary>Beats before a looping pattern starts over.</summary>
    public double Length { get; }

    /// <summary>True if the pattern starts over after <see cref="Length"/> beats instead of stopping.</summary>
    public bool IsLooping { get; }

    /// <summary>The same pattern, repeating forever.</summary>
    public Pattern Loop() => new(Events, Length, true);

    /// <summary>The same pattern moved by <paramref name="semitones"/>.</summary>
    public Pattern Transpose(int semitones) =>
        new(Events.Select(e => e with { Note = e.Note.Transpose(semitones) }).ToArray(), Length, IsLooping);
}

/// <summary>Placing notes in time and playing them: <c>notes.Every(Beats.Eighth).Loop().Play(v => ...)</c>.</summary>
public static class Patterns
{
    private const double DefaultBpm = 120;

    /// <summary>
    /// Places the notes one after another, <paramref name="step"/> beats apart.
    /// </summary>
    /// <param name="notes">The notes, in order; any LINQ filtering or transposing you did on them carries over.</param>
    /// <param name="step">Beats from the start of one note to the start of the next; see <see cref="Beats"/>.</param>
    /// <param name="gate">How long each note is held, as a multiple of <paramref name="step"/>. 1 (the default) ends it as the next begins;
    /// above 1 the notes overlap, which is what makes an arpeggio polyphonic.</param>
    /// <example><code>Notes.Parse("C4 E4 G4").Every(Beats.Eighth, gate: 2)</code></example>
    public static Pattern Every(this IEnumerable<Note> notes, double step, double gate = 1)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");
        if (gate <= 0) throw new ArgumentOutOfRangeException(nameof(gate), gate, "The gate must be positive.");

        var events = notes.Select((note, index) => new TimedNote(index * step, note with { Beats = step * gate })).ToArray();
        return new Pattern(events, events.Length * step, false);
    }

    /// <summary>
    /// Turns a pattern into sound: at each note's start, a voice built from <paramref name="voice"/> plays it,
    /// and the voices are mixed. Notes that overlap play together, up to <paramref name="polyphony"/> at once; past that, the oldest is cut.
    /// </summary>
    /// <param name="pattern">The notes to play.</param>
    /// <param name="voice">Describes the sound of one note from its <see cref="Voice"/> signals. Called while the sound is built, not per note.</param>
    /// <param name="bpm">
    /// Beats (quarter notes) per minute; the pattern then keeps its own time, starting when the sound does. Leave it out
    /// to follow the host: with a <see cref="HostClock"/> on the render context, a looping pattern stays on the host's
    /// beat grid at the host's tempo (so replacing the sound keeps the notes in time), and without one the tempo is 120.
    /// </param>
    /// <param name="polyphony">The most notes sounding at once, from 1 to 64. Defaults to 8.</param>
    /// <remarks>
    /// Each voice is scaled by the note's velocity, and an <c>Adsr</c> inside it releases when the note ends.
    /// The voices are built up front and reused for every note, so playing allocates nothing.
    /// </remarks>
    /// <example>
    /// <code>
    /// Notes.Parse("C3 E3 G3 B3").Every(Beats.Sixteenth, gate: 3).Loop()
    ///     .Play(v => Saw(v.Frequency).Adsr(0.003, 0.15, 0.4, 0.3), bpm: 110)
    /// </code>
    /// </example>
    public static Sound Play(this Pattern pattern, Func<Voice, Sound> voice, double? bpm = null, int polyphony = 8)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(voice);
        if (bpm is { } tempo && !(double.IsFinite(tempo) && tempo > 0)) throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "The tempo must be positive.");
        if (polyphony is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(polyphony), polyphony, "Polyphony must be between 1 and 64.");

        return Sound.FromOperator(
            ctx => bpm is null && ctx.TimeClock is not null
                ? new HostPatternOperator(pattern, voice, polyphony, ctx)
                : new PatternOperator(pattern, voice, bpm ?? DefaultBpm, polyphony, ctx),
            $"Play({pattern.Events.Count} notes)");
    }
}
