using System.Globalization;

namespace Sigil.Sound;

/// <summary>
/// One note a player is holding, which the player can keep shaping while it sounds: bend its pitch, press harder, slide.
/// Get one from <see cref="NoteInput.Start(int, float)"/>. Two notes at the same pitch are two notes, each with its own bend,
/// pressure and slide, which is how an MPE controller plays: one note per channel.
/// </summary>
/// <remarks>
/// <para>
/// The setters are safe from any thread and take no lock. The sound reads them once per block and glides to the new value
/// (about 10 ms), so a controller sending a few hundred updates a second is heard as a smooth curve, not as steps. The library
/// knows nothing about MIDI: a host that speaks MIDI, a touch screen or a game maps its own gestures onto these three numbers.
/// </para>
/// <para>
/// A sound shapes itself from the note with the <see cref="Voice"/> signals <c>Bend</c>, <c>Pressure</c> and <c>Slide</c>; its
/// <c>Frequency</c> and <c>Pitch</c> already follow the bend. A released note keeps listening while it rings out, so a bend
/// can carry on into the release.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Two notes on the same pitch. One is bent a third of a semitone sharp, so the pair beats slowly.
/// var keys = new NoteInput();
/// var synth = keys.Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.01, 0.1, 0.8, 0.3), polyphony: 4);
/// var first = keys.Start(57);
/// var second = keys.Start(57);
/// second.Bend = 0.33;
/// synth * 0.3
/// </code>
/// </example>
public sealed class PlayingNote
{
    /// <summary>The most a note can be bent either way, in semitones.</summary>
    public const double MaxBend = 96;

    private readonly NoteInput _owner;
    private float _bend;
    private float _pressure;
    private float _slide;
    private int _held = 1;

    internal PlayingNote(NoteInput owner, int pitch, float velocity, long id)
    {
        _owner = owner;
        Pitch = pitch;
        Velocity = velocity;
        Id = id;
    }

    /// <summary>The identity the note has in a <see cref="NoteStream"/>.</summary>
    internal long Id { get; }

    /// <summary>The MIDI pitch the note was started on, 0 to 127.</summary>
    public int Pitch { get; }

    /// <summary>How hard the note was started, from 0 to 1.</summary>
    public float Velocity { get; }

    /// <summary>True from <see cref="NoteInput.Start(int, float)"/> until <see cref="Release"/> (or <see cref="NoteInput.AllNotesOff"/>).</summary>
    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>
    /// How far the note's pitch is moved, in semitones; 0 is the note itself and 12 is an octave up. Clamped to plus or minus
    /// <see cref="MaxBend"/>. Seen by a voice as <c>v.Bend</c>, and already part of <c>v.Frequency</c> and <c>v.Pitch</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to NaN or infinity.</exception>
    public double Bend
    {
        get => Volatile.Read(ref _bend);
        set => Volatile.Write(ref _bend, (float)Math.Clamp(Check(value, nameof(value)), -MaxBend, MaxBend));
    }

    /// <summary>How hard the note is pressed after it started, from 0 to 1. Seen by a voice as <c>v.Pressure</c>. Clamped.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to NaN or infinity.</exception>
    public double Pressure
    {
        get => Volatile.Read(ref _pressure);
        set => Volatile.Write(ref _pressure, (float)Math.Clamp(Check(value, nameof(value)), 0.0, 1.0));
    }

    /// <summary>
    /// A third dimension the player moves the finger along, such as up and down the key, from 0 to 1. Seen by a voice as
    /// <c>v.Slide</c>. What it does is up to the sound: brightness is the usual choice. Clamped.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to NaN or infinity.</exception>
    public double Slide
    {
        get => Volatile.Read(ref _slide);
        set => Volatile.Write(ref _slide, (float)Math.Clamp(Check(value, nameof(value)), 0.0, 1.0));
    }

    /// <summary>Ends this note only; its voice releases and rings out. Does nothing if the note has already ended.</summary>
    public void Release() => _owner.Release(this);

    // Called by the owner, under its lock, when the note ends for any reason.
    internal bool MarkReleased() => Interlocked.Exchange(ref _held, 0) == 1;

    private static double Check(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "The value must be a number.");
        return value;
    }

    /// <summary>A short description of the note and what the player is doing to it.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"PlayingNote({Pitch}, bend {Bend:0.##}, pressure {Pressure:0.##}, slide {Slide:0.##}{(IsHeld ? "" : ", released")})");
}
