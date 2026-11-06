namespace Sigil.Sound;

/// <summary>
/// A fixed set of pre-built voices that notes are started on, mixed, released and recycled. Shared by the operators
/// that play notes: <c>PatternOperator</c> (notes known in advance, so each one's length is known when it starts) and
/// <c>LiveVoiceOperator</c> (notes from a keyboard, ended later by a note-off).
/// </summary>
/// <remarks>
/// Every voice is compiled once, when the pool is, and then retriggered for each note: the note's pitch, velocity and
/// length go into the voice's <see cref="VoiceControl"/>, the voice is reset, and it renders. Nothing is allocated while
/// rendering. Time is a frame counter the owner keeps; the pool only compares positions, so each owner chooses what
/// frame zero means.
/// </remarks>
internal sealed class VoicePool : IDisposable
{
    /// <summary>The release position of a note that has not ended yet.</summary>
    public const long Held = long.MaxValue;

    private const float Silence = 1e-5f;
    private const int StealFadeFrames = 128;

    private readonly VoiceSlot[] _voices;
    private readonly int _polyphony;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly bool _stealReleasedFirst;
    private readonly StereoFrame[] _scratch;
    private readonly long _quietFrames;

    /// <param name="factory">Describes the sound of one voice; called once per pooled voice while the pool is built.</param>
    /// <param name="polyphony">The most notes sounding at once; the pool keeps twice as many voices so cut ones can fade.</param>
    /// <param name="context">The render context every voice is compiled with.</param>
    /// <param name="stealReleasedFirst">
    /// At the polyphony limit, cut a note that is already dying away before one a player is still holding.
    /// Live playing wants this; a pattern's notes all have fixed lengths, so it keeps cutting the oldest.
    /// </param>
    public VoicePool(Func<Voice, Sound> factory, int polyphony, RenderContext context, bool stealReleasedFirst = false)
    {
        _polyphony = polyphony;
        _sampleRate = context.SampleRate;
        _blockSize = context.BlockSize;
        _stealReleasedFirst = stealReleasedFirst;
        _scratch = new StereoFrame[context.BlockSize];
        _quietFrames = context.SampleRate / 4;

        // Room for the voices that are fading out while their replacements start.
        _voices = new VoiceSlot[polyphony * 2];
        for (int i = 0; i < _voices.Length; i++)
        {
            var control = new VoiceControl();
            // Each voice is its own compile root, so nothing is shared between voices, and it knows its own note.
            var op = factory(Voice.Instance).Compile(context with { Scope = null, Voice = control });
            _voices[i] = new VoiceSlot(control, op);
        }
    }

    public void Reset()
    {
        foreach (var voice in _voices)
            voice.Active = false;
    }

    public void Dispose()
    {
        foreach (var voice in _voices)
            (voice.Operator as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Starts a note. <paramref name="holdSeconds"/> is how long the note is held, or infinity for a note that a later
    /// <see cref="Release"/> will end.
    /// </summary>
    // A note started with a handle (source) follows that note's bend, pressure and slide for as long as it sounds, and a
    // release by pitch leaves it alone: only ReleaseNote ends it. A note with no handle is released by its pitch.
    public VoiceSlot Start(double frequency, int pitch, float velocity, double holdSeconds, long position, PlayingNote? source = null)
    {
        // At the polyphony limit one sounding voice is cut, with a very short fade so it doesn't click.
        VoiceSlot? victim = null;
        int sounding = 0;
        foreach (var other in _voices)
        {
            if (!other.Active || other.FadeRemaining > 0)
                continue;
            sounding++;
            if (victim is null || IsBetterVictim(other, victim))
                victim = other;
        }

        if (sounding >= _polyphony && victim is not null)
            victim.FadeRemaining = StealFadeFrames;

        var slot = FreeSlot();
        slot.Control.Set(frequency, pitch, velocity, holdSeconds);
        slot.Source = source;
        // Before the reset, so the voice starts where the player's hand already is instead of gliding there.
        CopyExpression(slot);
        slot.Operator.ResetState();
        slot.Velocity = velocity;
        slot.StartedAt = position;
        slot.NoteOffAt = double.IsPositiveInfinity(holdSeconds) ? Held : position + (long)Math.Round(holdSeconds * _sampleRate);
        slot.QuietFrames = 0;
        slot.FadeRemaining = 0;
        slot.Active = true;
        return slot;
    }

    /// <summary>
    /// Ends every held note of <paramref name="pitch"/> at <paramref name="position"/>, which must be the frame the next
    /// render starts at. The voice's hold becomes what it has already rendered, so an envelope or gate inside it releases
    /// from where it is. A note lasts at least one block, so a quick tap still plays its attack.
    /// </summary>
    public void Release(int pitch, long position)
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.NoteOffAt == Held && voice.Source is null && voice.Control.Pitch == pitch)
                ReleaseSlot(voice, position);
        }
    }

    /// <summary>Ends the voice playing <paramref name="note"/>, and only that one, however many notes share its pitch.</summary>
    public void ReleaseNote(PlayingNote note, long position)
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.NoteOffAt == Held && ReferenceEquals(voice.Source, note))
                ReleaseSlot(voice, position);
        }
    }

    /// <summary>Ends every held note; see <see cref="Release"/>.</summary>
    public void ReleaseAll(long position)
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.NoteOffAt == Held)
                ReleaseSlot(voice, position);
        }
    }

    /// <summary>Adds the sounding voices to <paramref name="segment"/>, which starts at frame <paramref name="position"/>.</summary>
    public void Render(Span<StereoFrame> segment, long position)
    {
        var scratch = _scratch.AsSpan(0, segment.Length);
        foreach (var voice in _voices)
        {
            if (!voice.Active)
                continue;

            // The player's hand, once per block; the voice's signals glide between these values.
            CopyExpression(voice);
            voice.Operator.RenderBlock(scratch);

            float peak = voice.FadeRemaining > 0 ? MixFading(voice, scratch, segment) : MixSteady(voice.Velocity, scratch, segment);

            if (voice.FadeRemaining > 0)
            {
                voice.FadeRemaining -= segment.Length;
                if (voice.FadeRemaining <= 0)
                {
                    voice.Active = false;
                    continue;
                }
            }

            // A note is finished once it has been released and then stayed quiet for a moment.
            if (position >= voice.NoteOffAt && peak < Silence)
                voice.QuietFrames += segment.Length;
            else
                voice.QuietFrames = 0;

            if (voice.QuietFrames >= _quietFrames)
                voice.Active = false;
        }
    }

    // The common case, one pass over the interleaved floats. Every voice runs this for every frame, and in the browser
    // the .NET interpreter pays for each call and struct copy, so it does plain float arithmetic with no helper calls.
    private static float MixSteady(float velocity, Span<StereoFrame> source, Span<StereoFrame> segment)
    {
        var from = System.Runtime.InteropServices.MemoryMarshal.Cast<StereoFrame, float>(source);
        var to = System.Runtime.InteropServices.MemoryMarshal.Cast<StereoFrame, float>(segment);
        float peak = 0;
        for (int i = 0; i < to.Length; i++)
        {
            float value = from[i] * velocity;
            to[i] += value;
            float magnitude = value < 0 ? -value : value;
            if (magnitude > peak)
                peak = magnitude;
        }

        return peak;
    }

    private static float MixFading(VoiceSlot voice, Span<StereoFrame> source, Span<StereoFrame> segment)
    {
        float peak = 0;
        for (int i = 0; i < segment.Length; i++)
        {
            float gain = voice.Velocity * Math.Max(0f, (float)(voice.FadeRemaining - i) / StealFadeFrames);
            var frame = source[i] * gain;
            segment[i] += frame;
            peak = Math.Max(peak, Math.Max(MathF.Abs(frame.Left), MathF.Abs(frame.Right)));
        }

        return peak;
    }

    private static void CopyExpression(VoiceSlot voice)
    {
        if (voice.Source is not { } note)
            return;
        voice.Control.Bend = (float)note.Bend;
        voice.Control.Pressure = (float)note.Pressure;
        voice.Control.Slide = (float)note.Slide;
    }

    private void ReleaseSlot(VoiceSlot voice, long position)
    {
        long heldFrames = Math.Max(position - voice.StartedAt, _blockSize);
        voice.NoteOffAt = voice.StartedAt + heldFrames;
        voice.Control.HoldSeconds = (double)heldFrames / _sampleRate;
    }

    private bool IsBetterVictim(VoiceSlot candidate, VoiceSlot current)
    {
        if (_stealReleasedFirst)
        {
            bool candidateReleased = candidate.NoteOffAt != Held;
            bool currentReleased = current.NoteOffAt != Held;
            if (candidateReleased != currentReleased)
                return candidateReleased;
        }

        return candidate.StartedAt < current.StartedAt;
    }

    private VoiceSlot FreeSlot()
    {
        foreach (var voice in _voices)
            if (!voice.Active)
                return voice;

        // Even the fading voices fill the pool (a burst of simultaneous notes): replace the oldest outright.
        var oldest = _voices[0];
        for (int i = 1; i < _voices.Length; i++)
            if (_voices[i].StartedAt < oldest.StartedAt)
                oldest = _voices[i];
        return oldest;
    }

    internal sealed class VoiceSlot(VoiceControl control, IOperator op)
    {
        public VoiceControl Control { get; } = control;
        public IOperator Operator { get; } = op;
        public bool Active { get; set; }
        public float Velocity { get; set; }
        public long StartedAt { get; set; }
        public long NoteOffAt { get; set; }
        public long QuietFrames { get; set; }

        /// <summary>The note whose bend, pressure and slide this voice follows; null for a note without a handle.</summary>
        public PlayingNote? Source { get; set; }

        /// <summary>Frames left of the fade-out after the voice was cut; zero while it sounds normally.</summary>
        public int FadeRemaining { get; set; }
    }
}
