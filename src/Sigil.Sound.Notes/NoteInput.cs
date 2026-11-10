namespace Sigil.Sound;

/// <summary>
/// Notes played live, from a keyboard, a game or a UI, as opposed to a <see cref="Pattern"/> written in advance.
/// Create one, build a sound from it with <see cref="Play"/>, and call <see cref="NoteOn(int, float)"/> and <see cref="NoteOff(int)"/>
/// from any thread while that sound renders.
/// </summary>
/// <remarks>
/// <para>
/// The input outlives the sounds built from it. When a sound is replaced (a <see cref="Slot"/> swap, a new
/// <see cref="SoundContext.Play"/>) the replacement starts with the keys that are held right now already sounding,
/// so editing a patch while holding a chord does not drop the chord.
/// </para>
/// <para>
/// Calls take a short lock, so they are for control threads (MIDI, UI, game logic), not for the audio callback.
/// The audio side never locks and never allocates: events pass through a fixed queue and are applied at the start of
/// the next block, so a note begins within one block (a few milliseconds) of the call.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using Sigil.Sound;
/// using static Sigil.Sound.Sounds;
///
/// var keys = new NoteInput();
/// var synth = keys.Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.01, 0.2, 0.6, 0.4), polyphony: 6);
/// keys.NoteOn("c4");   // middle C sounds until...
/// keys.NoteOff("c4");  // ...it is released and its envelope rings out
/// </code>
/// </example>
public sealed class NoteInput
{
    private readonly object _gate = new();
    private readonly List<WeakReference<LiveVoiceOperator>> _listeners = [];
    private readonly Dictionary<int, float> _held = [];
    private readonly List<PlayingNote> _notes = [];

    /// <summary>
    /// Starts a note you can keep shaping while it sounds, and returns it. Unlike <see cref="NoteOn(int, float)"/>, which names a note by
    /// its pitch, the note is its own object: two started on the same pitch are two notes, and ending or bending one leaves the
    /// other alone. This is the entry for a player with per-note expression, such as an MPE controller.
    /// </summary>
    /// <param name="pitch">The MIDI pitch, 0 to 127; 60 is middle C.</param>
    /// <param name="velocity">How hard the note is played, above 0 up to 1. Defaults to 0.8.</param>
    /// <returns>The note. Set its <see cref="PlayingNote.Bend"/>, <see cref="PlayingNote.Pressure"/> and <see cref="PlayingNote.Slide"/>; end it with <see cref="PlayingNote.Release"/>.</returns>
    /// <remarks>
    /// <see cref="NoteOff(int)"/> does not end a note started here; call <see cref="PlayingNote.Release"/> (or <see cref="AllNotesOff"/>).
    /// A sound built later still starts with the notes held now, with their current bend, pressure and slide.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The pitch is not 0 to 127, or the velocity is not above 0 and at most 1.</exception>
    /// <example>
    /// <code>
    /// // A note bent up a whole tone while it is held, as a finger would on an MPE keyboard.
    /// var keys = new NoteInput();
    /// var synth = keys.Play(v => Sine(v.Frequency).Adsr(0.01, 0.1, 0.9, 0.3));
    /// var note = keys.Start(60);
    /// note.Bend = 2;
    /// synth * 0.4
    /// </code>
    /// </example>
    public PlayingNote Start(int pitch, float velocity = 0.8f)
    {
        ValidatePitch(pitch);
        if (!(velocity is > 0f and <= 1f))
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity must be above 0 and at most 1.");

        var note = new PlayingNote(this, pitch, velocity);
        lock (_gate)
        {
            _notes.Add(note);
            Post(new NoteEvent(NoteEventKind.OnNote, pitch, velocity, note));
        }

        return note;
    }

    // The note's own Release lands here.
    internal void Release(PlayingNote note)
    {
        lock (_gate)
        {
            if (_notes.Remove(note) && note.MarkReleased())
                Post(new NoteEvent(NoteEventKind.OffNote, note.Pitch, 0f, note));
        }
    }

    /// <summary>Starts a note. A velocity of 0 ends it instead, as in MIDI.</summary>
    /// <param name="pitch">The MIDI pitch, 0 to 127; 60 is middle C.</param>
    /// <param name="velocity">How hard the note is played, from 0 to 1. Defaults to 0.8.</param>
    public void NoteOn(int pitch, float velocity = 0.8f)
    {
        ValidatePitch(pitch);
        if (!(velocity is >= 0f and <= 1f))
            throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity must be between 0 and 1.");
        if (velocity == 0f)
        {
            NoteOff(pitch);
            return;
        }

        lock (_gate)
        {
            _held[pitch] = velocity;
            Post(new NoteEvent(NoteEventKind.On, pitch, velocity));
        }
    }

    /// <summary>
    /// Starts the notes written in <see cref="Mini"/> notation, all at once: <c>"a2 g3 e4"</c> is the same as three calls to
    /// <see cref="NoteOn(int, float)"/>. This is the way to name notes in a score's own terms (<c>c4</c>, <c>eb3</c>) instead of
    /// MIDI numbers, and a chord can be written as <c>"[c3,e3,g3]"</c> or <c>"c3,e3,g3"</c>.
    /// </summary>
    /// <param name="notes">
    /// Mini-notation for notes, as in <see cref="Mini.Notes"/>: <c>c4:0.6</c> sets the velocity, a name without an octave is in
    /// octave 3, and <c>~</c> is a rest. Time has no meaning for a live input, so the notes of the first cycle all start now;
    /// <c>"c3 e3 g3"</c> is a chord, not an arpeggio. A velocity of 0 (<c>c4:0</c>) ends the note, as in MIDI.
    /// </param>
    /// <exception cref="MiniNotationException">The text is not valid. No note is started.</exception>
    /// <example>
    /// <code>
    /// var keys = new NoteInput();
    /// var synth = keys.Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.01, 0.2, 0.7, 0.4), polyphony: 6);
    /// keys.NoteOn("a2 g3 e4:0.6");
    /// synth * 0.25
    /// </code>
    /// </example>
    public void NoteOn(string notes)
    {
        foreach (var note in ParseNotes(notes))
            NoteOn(note.Pitch, note.Velocity);
    }

    /// <summary>
    /// Ends the notes written in <see cref="Mini"/> notation, the counterpart of <see cref="NoteOn(string)"/>. Velocities in the
    /// text are ignored. A note that is not held is skipped.
    /// </summary>
    /// <param name="notes">Mini-notation for notes, such as <c>"a2 g3 e4"</c>.</param>
    /// <exception cref="MiniNotationException">The text is not valid. No note is ended.</exception>
    public void NoteOff(string notes)
    {
        foreach (var note in ParseNotes(notes))
            NoteOff(note.Pitch);
    }

    /// <summary>
    /// Starts the notes written in <see cref="Mini"/> notation and returns them, one <see cref="PlayingNote"/> each, in the order
    /// written: <c>keys.Start("c4 e4")</c>. See <see cref="Start(int, float)"/> for what a started note can do.
    /// </summary>
    /// <param name="notes">Mini-notation for notes, read as in <see cref="NoteOn(string)"/>. A velocity of 0 is not allowed here.</param>
    /// <exception cref="MiniNotationException">The text is not valid. No note is started.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A velocity is 0.</exception>
    public IReadOnlyList<PlayingNote> Start(string notes)
    {
        var parsed = ParseNotes(notes);
        if (parsed.Any(n => n.Velocity <= 0f))
            throw new ArgumentOutOfRangeException(nameof(notes), notes, "A started note needs a velocity above 0.");
        return parsed.Select(n => Start(n.Pitch, n.Velocity)).ToArray();
    }

    /// <summary>True while every note written in <see cref="Mini"/> notation is held, as <see cref="IsHeld(int)"/> reports it. False for text with no notes.</summary>
    /// <param name="notes">Mini-notation for notes, such as <c>"c4 e4 g4"</c>.</param>
    /// <exception cref="MiniNotationException">The text is not valid.</exception>
    public bool IsHeld(string notes)
    {
        var parsed = ParseNotes(notes);
        return parsed.Count > 0 && parsed.All(n => IsHeld(n.Pitch));
    }

    // The whole text is parsed before any note is touched, so a typo in the last note does not leave the first ones sounding.
    private static List<Note> ParseNotes(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return Mini.Notes(notes).Cycle(0).Select(e => e.Value).ToList();
    }

    /// <summary>Ends every held note of <paramref name="pitch"/>; its voice releases and rings out. Does nothing if none is held.</summary>
    /// <param name="pitch">The MIDI pitch, 0 to 127.</param>
    public void NoteOff(int pitch)
    {
        ValidatePitch(pitch);
        lock (_gate)
        {
            if (_held.Remove(pitch))
                Post(new NoteEvent(NoteEventKind.Off, pitch, 0f));
        }
    }

    /// <summary>Releases every held note, including the ones started with <see cref="Start(int, float)"/>.</summary>
    public void AllNotesOff()
    {
        lock (_gate)
        {
            _held.Clear();
            foreach (var note in _notes)
                note.MarkReleased();
            _notes.Clear();
            Post(new NoteEvent(NoteEventKind.AllOff, 0, 0f));
        }
    }

    /// <summary>
    /// True while <paramref name="pitch"/> is held: between its <see cref="NoteOn(int, float)"/> and <see cref="NoteOff(int)"/>, or while a note
    /// started on it with <see cref="Start(int, float)"/> has not been released.
    /// </summary>
    /// <param name="pitch">The MIDI pitch, 0 to 127.</param>
    public bool IsHeld(int pitch)
    {
        ValidatePitch(pitch);
        lock (_gate)
            return _held.ContainsKey(pitch) || _notes.Exists(n => n.Pitch == pitch);
    }

    /// <summary>
    /// Turns this input into sound: each note on starts a voice built from <paramref name="voice"/>, which sounds until
    /// the matching note off and then releases. Voices are mixed, up to <paramref name="polyphony"/> at once; past that, a
    /// voice that is already releasing is cut first, then the oldest held one.
    /// </summary>
    /// <param name="voice">Describes the sound of one note from its <see cref="Voice"/> signals. Called while the sound is built, not per note.</param>
    /// <param name="polyphony">The most notes sounding at once, from 1 to 64. Defaults to 8.</param>
    /// <remarks>
    /// Each voice is scaled by the note's velocity. An <c>Adsr</c> inside it holds its sustain level for as long as the key
    /// is down and releases at the note off; <c>v.Gate</c> is 1 until then and 0 after. The voices are built up front and
    /// reused, so playing allocates nothing. The same input can feed several sounds at once.
    /// </remarks>
    /// <example>
    /// <code>
    /// using Sigil.Sound;
    /// using static Sigil.Sound.Sounds;
    ///
    /// var keys = new NoteInput();
    /// var synth = keys.Play(v => Square(v.Frequency).Adsr(0.005, 0.1, 0.7, 0.3), polyphony: 4);
    /// </code>
    /// </example>
    public Sound Play(Func<Voice, Sound> voice, int polyphony = 8)
    {
        ArgumentNullException.ThrowIfNull(voice);
        if (polyphony is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(polyphony), polyphony, "Polyphony must be between 1 and 64.");

        return Sound.FromOperator(ctx => new LiveVoiceOperator(this, voice, polyphony, ctx), "Live notes");
    }

    // Called while a sound is compiled. The keys already down start sounding in it straight away.
    internal void Attach(LiveVoiceOperator listener)
    {
        lock (_gate)
        {
            _listeners.RemoveAll(static l => !l.TryGetTarget(out _));
            _listeners.Add(new WeakReference<LiveVoiceOperator>(listener));
            foreach (var (pitch, velocity) in _held)
                listener.Post(new NoteEvent(NoteEventKind.On, pitch, velocity));
            foreach (var note in _notes)
                listener.Post(new NoteEvent(NoteEventKind.OnNote, note.Pitch, note.Velocity, note));
        }
    }

    internal void Detach(LiveVoiceOperator listener)
    {
        lock (_gate)
            _listeners.RemoveAll(l => !l.TryGetTarget(out var target) || ReferenceEquals(target, listener));
    }

    // Always called with the gate held, which keeps the queues single-producer.
    private void Post(NoteEvent noteEvent)
    {
        foreach (var listener in _listeners)
        {
            if (listener.TryGetTarget(out var target))
                target.Post(noteEvent);
        }
    }

    private static void ValidatePitch(int pitch)
    {
        if (pitch is < 0 or > 127)
            throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "Pitch must be between 0 and 127.");
    }
}

internal enum NoteEventKind : byte
{
    On,
    Off,
    AllOff,

    /// <summary>A note started with <see cref="NoteInput.Start(int, float)"/>; the event carries the note itself, whose state the voice reads.</summary>
    OnNote,

    /// <summary>That note's release. Matches the voice by the note, not by pitch.</summary>
    OffNote,
}

internal readonly record struct NoteEvent(NoteEventKind Kind, int Pitch, float Velocity, PlayingNote? Note = null);
