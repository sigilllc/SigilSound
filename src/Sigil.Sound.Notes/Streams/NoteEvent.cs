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
/// <param name="Channel">A routing tag that operators leave alone; a function of yours may set or read it (<c>n with { Channel = 2 }</c>).</param>
public readonly record struct NoteEvent(NoteEventKind Kind, long Id, int Pitch, float Velocity = 0.8f, float Cents = 0f, int Channel = 0)
{
    /// <summary>True for the event that starts the note.</summary>
    public bool IsOn => Kind == NoteEventKind.On;

    /// <summary>The note's frequency in Hz, in 12-tone equal temperament with A4 (MIDI 69) at 440 Hz, including <see cref="Cents"/>.</summary>
    public double Frequency => 440.0 * Math.Pow(2.0, (Pitch - 69 + Cents / 100.0) / 12.0);

    /// <summary>The pitch as a fraction of a MIDI note number: <c>60.5</c> is a quarter tone above middle C.</summary>
    public double PitchWithCents => Pitch + Cents / 100.0;

    /// <summary>This note moved by <paramref name="semitones"/>. A note moved out of the MIDI range is dropped by the stream that gets it.</summary>
    /// <param name="semitones">How far to move, up (positive) or down.</param>
    /// <example>
    /// <code>
    /// // A function of your own that moves a note: loud ones up an octave, soft ones down a fifth.
    /// var keys = new NoteInput();
    /// var sound = keys.Stream.Select(n =&gt; n.Velocity &gt; 0.7f ? n.Transpose(12) : n.Transpose(-5))
    ///     .Play(v =&gt; Saw(v.Frequency).LowPass(1800).Adsr(0.005, 0.2, 0.5, 0.25));
    /// keys.NoteOn("c3:0.9 e3:0.4");
    /// sound * 0.3
    /// </code>
    /// </example>
    public NoteEvent Transpose(int semitones) => this with { Pitch = Pitch + semitones };

    /// <summary>This note moved by <paramref name="cents"/> hundredths of a semitone, carrying whole semitones into the pitch.</summary>
    /// <param name="cents">How far to move, up (positive) or down.</param>
    /// <example>
    /// <code>
    /// // Every note 30 cents sharp, so against an untouched copy of the stream the pair beats slowly.
    /// var keys = new NoteInput();
    /// var sound = (keys.Stream + keys.Stream.Select(n =&gt; n.Detune(30)))
    ///     .Play(v =&gt; Saw(v.Frequency).LowPass(1800).Adsr(0.005, 0.2, 0.5, 0.25));
    /// keys.NoteOn("c3 g3");
    /// sound * 0.3
    /// </code>
    /// </example>
    public NoteEvent Detune(double cents)
    {
        double total = Cents + cents;
        int shift = (int)Math.Round(total / 100.0);
        return this with { Pitch = Pitch + shift, Cents = (float)(total - shift * 100.0) };
    }

    /// <summary>True when the note is a tone of <paramref name="scale"/>, in any octave. <c>Where(n =&gt; n.InScale(scale))</c> keeps those.</summary>
    /// <param name="scale">The scale, such as <c>Scale.Parse("d dorian")</c>.</param>
    /// <example>
    /// <code>
    /// // Only the keys that belong to D dorian get through: the C# and G# are left out.
    /// var keys = new NoteInput();
    /// var dorian = Scale.Parse("d dorian");
    /// var sound = keys.Stream.Where(n =&gt; n.InScale(dorian))
    ///     .Play(v =&gt; Square(v.Frequency).LowPass(1500).Adsr(0.005, 0.2, 0.4, 0.2));
    /// keys.NoteOn("d3 c#3 f3 g#3 a3");
    /// sound * 0.2
    /// </code>
    /// </example>
    public bool InScale(Scale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return scale.Contains(Pitch);
    }

    /// <summary>
    /// This note moved to a tone of <paramref name="scale"/>, in any octave; a note already in the scale is returned as it is. The cents
    /// of a moved note are cleared. <c>Select(n =&gt; n.Snap(scale))</c> quantizes a stream.
    /// </summary>
    /// <param name="scale">The tones allowed.</param>
    /// <param name="mode">Which tone a note between two goes to. Defaults to the closest, and the lower one when two are equally close.</param>
    /// <example>
    /// <code>
    /// // Keys outside D dorian are moved to the nearest tone of it, so whatever is played stays in the scale.
    /// var keys = new NoteInput();
    /// var dorian = Scale.Parse("d dorian");
    /// var sound = keys.Stream.Select(n =&gt; n.Snap(dorian))
    ///     .Play(v =&gt; Triangle(v.Frequency).LowPass(2000).Adsr(0.01, 0.2, 0.6, 0.3));
    /// keys.NoteOn("d3 f#3 a3 c#4");
    /// sound * 0.3
    /// </code>
    /// </example>
    public NoteEvent Snap(Scale scale, QuantizeMode mode = QuantizeMode.Nearest)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (scale.Contains(Pitch))
            return this;

        for (int distance = 1; distance <= 12; distance++)
        {
            if (mode != QuantizeMode.Up && scale.Contains(Pitch - distance))
                return this with { Pitch = Pitch - distance, Cents = 0f };
            if (mode != QuantizeMode.Down && scale.Contains(Pitch + distance))
                return this with { Pitch = Pitch + distance, Cents = 0f };
        }

        return this;
    }

    /// <summary>
    /// True for a random share of notes: <c>Where(n =&gt; n.Chance(0.3))</c> lets 3 in 10 through. Which ones depends only on the
    /// <paramref name="seed"/> and the note's <see cref="Id"/>, so the same performance is picked the same way every time.
    /// </summary>
    /// <param name="probability">The share, 0 to 1.</param>
    /// <param name="seed">Another seed picks other notes.</param>
    /// <example>
    /// <code>
    /// // Seven in ten of the notes get through, the same ones every time.
    /// var keys = new NoteInput();
    /// var sound = keys.Stream.Where(n =&gt; n.Chance(0.7, seed: 4))
    ///     .Play(v =&gt; Saw(v.Frequency).LowPass(1800).Adsr(0.005, 0.2, 0.5, 0.2), polyphony: 12);
    /// keys.NoteOn("c3 d3 e3 f3 g3 a3 b3 c4");
    /// sound * 0.2
    /// </code>
    /// </example>
    public bool Chance(double probability, int seed = 0) => NoteIds.Unit(seed, Id) < probability;

    /// <summary>An event that starts a note.</summary>
    public static NoteEvent Start(long id, int pitch, float velocity = 0.8f, float cents = 0f, int channel = 0) =>
        new(NoteEventKind.On, id, pitch, velocity, cents, channel);

    /// <summary>An event that ends a note.</summary>
    public static NoteEvent Stop(long id, int pitch, float cents = 0f, int channel = 0) =>
        new(NoteEventKind.Off, id, pitch, 0f, cents, channel);
}

/// <summary>Which scale tone <see cref="NoteEvent.Snap"/> picks for a note between two of them.</summary>
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
