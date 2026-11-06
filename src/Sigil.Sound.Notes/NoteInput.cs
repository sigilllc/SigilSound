namespace Sigil.Sound;

/// <summary>
/// Notes played live, from a keyboard, a game or a UI, as opposed to a <see cref="Pattern"/> written in advance.
/// Create one, build a sound from it with <see cref="Play"/>, and call <see cref="NoteOn"/> and <see cref="NoteOff"/>
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
/// keys.NoteOn(60);   // middle C sounds until...
/// keys.NoteOff(60);  // ...it is released and its envelope rings out
/// </code>
/// </example>
public sealed class NoteInput
{
    private readonly object _gate = new();
    private readonly List<WeakReference<LiveVoiceOperator>> _listeners = [];
    private readonly Dictionary<int, float> _held = [];

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

    /// <summary>Releases every held note.</summary>
    public void AllNotesOff()
    {
        lock (_gate)
        {
            _held.Clear();
            Post(new NoteEvent(NoteEventKind.AllOff, 0, 0f));
        }
    }

    /// <summary>True while <paramref name="pitch"/> is held, that is between its <see cref="NoteOn"/> and <see cref="NoteOff"/>.</summary>
    /// <param name="pitch">The MIDI pitch, 0 to 127.</param>
    public bool IsHeld(int pitch)
    {
        ValidatePitch(pitch);
        lock (_gate)
            return _held.ContainsKey(pitch);
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
}

internal readonly record struct NoteEvent(NoteEventKind Kind, int Pitch, float Velocity);
