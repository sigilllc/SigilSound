namespace Sigil.Sound;

/// <summary>Whether a <see cref="NoteEvent"/> starts a note or ends it.</summary>
public enum NoteEventKind : byte
{
    /// <summary>The note starts.</summary>
    On,

    /// <summary>The note ends. It carries the <see cref="NoteEvent.Id"/> of the note that started.</summary>
    Off,
}

/// <summary>
/// One thing that happens to a note in a <see cref="NoteStream"/>: it starts, or it ends. Unlike a <see cref="Note"/> in a pattern, which
/// knows how long it lasts, a live note has no length: it lasts until its <see cref="NoteEventKind.Off"/> arrives.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is what pairs the two events. Every operator keeps the pairing: an <c>Off</c> always ends the notes that its
/// <c>On</c> produced, whatever the operator would compute for that note now. The pitch of a note is the nearest MIDI pitch and
/// <see cref="Cents"/> the distance from it, so notes from other tunings and bent notes keep their exact pitch through every operator.
/// </remarks>
/// <param name="Kind">Starts or ends the note.</param>
/// <param name="Id">Identifies the note; the same on its <c>On</c> and its <c>Off</c>. Unique among the notes sounding in one stream.</param>
/// <param name="Pitch">The nearest MIDI pitch, 0 to 127; 60 is middle C.</param>
/// <param name="Velocity">How hard the note was played, from 0 to 1. Meaningful for an <c>On</c>.</param>
/// <param name="Cents">Hundredths of a semitone above <paramref name="Pitch"/> (negative for below).</param>
/// <param name="Channel">A routing tag that operators leave alone unless asked (<see cref="NoteStream.Channel"/>, <see cref="Pick.Channel"/>).</param>
public readonly record struct NoteEvent(NoteEventKind Kind, long Id, int Pitch, float Velocity = 0.8f, float Cents = 0f, int Channel = 0)
{
    /// <summary>True for the event that starts the note.</summary>
    public bool IsOn => Kind == NoteEventKind.On;

    /// <summary>The note's frequency in Hz, in 12-tone equal temperament with A4 (MIDI 69) at 440 Hz, including <see cref="Cents"/>.</summary>
    public double Frequency => 440.0 * Math.Pow(2.0, (Pitch - 69 + Cents / 100.0) / 12.0);

    /// <summary>The pitch as a fraction of a MIDI note number: <c>60.5</c> is a quarter tone above middle C.</summary>
    public double PitchWithCents => Pitch + Cents / 100.0;

    /// <summary>An event that starts a note.</summary>
    public static NoteEvent Start(long id, int pitch, float velocity = 0.8f, float cents = 0f, int channel = 0) =>
        new(NoteEventKind.On, id, pitch, velocity, cents, channel);

    /// <summary>An event that ends a note.</summary>
    public static NoteEvent Stop(long id, int pitch, float cents = 0f, int channel = 0) =>
        new(NoteEventKind.Off, id, pitch, 0f, cents, channel);
}

/// <summary>What <see cref="NoteStream.Range"/> does with a note outside the range.</summary>
public enum RangeMode
{
    /// <summary>Reflects it off the edge it passed, as a wavefolder does: a note 3 semitones above the top comes back 3 below it.</summary>
    Fold,

    /// <summary>Moves it by octaves until it is inside, which keeps what note it is. A range narrower than an octave may have no place for it; the note is then dropped.</summary>
    Wrap,

    /// <summary>Drops the note.</summary>
    Drop,

    /// <summary>Moves it to the nearest edge of the range.</summary>
    Clamp,
}

/// <summary>Which scale tone <see cref="NoteStream.Quantize"/> picks for a note between two of them.</summary>
public enum QuantizeMode
{
    /// <summary>The closest tone; the lower one when two are equally close.</summary>
    Nearest,

    /// <summary>The tone at or above the note.</summary>
    Up,

    /// <summary>The tone at or below the note.</summary>
    Down,
}

/// <summary>Which note <see cref="NoteStream.Mono"/> keeps when several are held.</summary>
public enum NotePriority
{
    /// <summary>The note pressed last.</summary>
    Last,

    /// <summary>The lowest held note.</summary>
    Lowest,

    /// <summary>The highest held note.</summary>
    Highest,
}

/// <summary>When a new chord takes over in <see cref="NoteStream.Chord"/>, the same choices as the AutoArp device.</summary>
public enum ChordSwitch
{
    /// <summary>As soon as the chord has settled.</summary>
    Instant,

    /// <summary>At the next beat, so a run keeps its chord until the beat is over.</summary>
    Beat,

    /// <summary>At the next cycle (bar), so a bar-long run or arpeggio is written against one chord from start to finish.</summary>
    Cycle,
}

/// <summary>What <see cref="NoteStream.Chord"/> does when every key is up.</summary>
public enum ChordLatch
{
    /// <summary>Keeps the last chord, so patterns carry on over the gap between two chords.</summary>
    Infinite,

    /// <summary>Has no chord while no key is down, so patterns written against it fall silent.</summary>
    Gated,
}
