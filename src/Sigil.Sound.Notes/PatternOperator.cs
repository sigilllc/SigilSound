namespace Sigil.Sound;

/// <summary>
/// Plays a <see cref="Pattern"/>: triggers a voice at each note's frame, mixes the active voices,
/// and frees a voice once its note is over and it has gone quiet.
/// </summary>
/// <remarks>
/// Every voice is compiled once, when this operator is, and then retriggered for each note: the note's pitch,
/// velocity and length go into the voice's <see cref="VoiceControl"/>, the voice is reset, and it renders. Nothing is
/// allocated while rendering.
/// </remarks>
internal sealed class PatternOperator : IOperator, IResettable, IDisposable
{
    private const float Silence = 1e-5f;
    private const int StealFadeFrames = 128;

    private readonly RenderContext _context;
    private readonly TimedNote[] _events;
    private readonly long[] _startFrames;
    private readonly double _secondsPerBeat;
    private readonly long _cycleFrames;
    private readonly VoiceSlot[] _voices;
    private readonly int _polyphony;
    private readonly StereoFrame[] _scratch;
    private readonly long _quietFrames;

    private long _position;
    private long _cycleStart;
    private int _next;

    public PatternOperator(Pattern pattern, Func<Voice, Sound> factory, double bpm, int polyphony, RenderContext context)
    {
        _context = context;
        _events = pattern.Events.ToArray();
        _secondsPerBeat = 60.0 / bpm;
        _startFrames = _events.Select(e => (long)Math.Round(e.Start * _secondsPerBeat * context.SampleRate)).ToArray();
        _cycleFrames = pattern.IsLooping ? (long)Math.Round(pattern.Length * _secondsPerBeat * context.SampleRate) : 0;
        _polyphony = polyphony;
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
        _position = 0;
        _cycleStart = 0;
        _next = 0;
        foreach (var voice in _voices)
            voice.Active = false;
    }

    public void Dispose()
    {
        foreach (var voice in _voices)
            (voice.Operator as IDisposable)?.Dispose();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        block.Clear();
        int done = 0;
        while (done < block.Length)
        {
            long nextEvent = NextEventFrame();
            if (nextEvent <= _position)
            {
                StartVoice();
                continue;
            }

            int segment = (int)Math.Min(block.Length - done, nextEvent - _position);
            RenderVoices(block.Slice(done, segment));
            _position += segment;
            done += segment;
        }
    }

    // The frame of the next note to start, or long.MaxValue when the pattern has played out.
    private long NextEventFrame()
    {
        if (_events.Length == 0)
            return long.MaxValue;

        if (_next >= _events.Length)
        {
            if (_cycleFrames <= 0)
                return long.MaxValue;
            _cycleStart += _cycleFrames;
            _next = 0;
        }

        return _cycleStart + _startFrames[_next];
    }

    private void StartVoice()
    {
        var timed = _events[_next++];
        double holdSeconds = timed.Note.Beats * _secondsPerBeat;

        // At the polyphony limit the oldest sounding voice is cut, with a very short fade so it doesn't click.
        VoiceSlot? oldest = null;
        int sounding = 0;
        foreach (var other in _voices)
        {
            if (!other.Active || other.FadeRemaining > 0)
                continue;
            sounding++;
            if (oldest is null || other.StartedAt < oldest.StartedAt)
                oldest = other;
        }

        if (sounding >= _polyphony && oldest is not null)
            oldest.FadeRemaining = StealFadeFrames;

        var slot = FreeSlot();
        slot.Control.Set(timed.Note.Frequency, timed.Note.Pitch, timed.Note.Velocity, holdSeconds);
        slot.Operator.ResetState();
        slot.Velocity = timed.Note.Velocity;
        slot.StartedAt = _position;
        slot.NoteOffAt = _position + (long)Math.Round(holdSeconds * _context.SampleRate);
        slot.QuietFrames = 0;
        slot.FadeRemaining = 0;
        slot.Active = true;
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

    private void RenderVoices(Span<StereoFrame> segment)
    {
        var scratch = _scratch.AsSpan(0, segment.Length);
        foreach (var voice in _voices)
        {
            if (!voice.Active)
                continue;

            voice.Operator.RenderBlock(scratch);

            float peak = 0;
            for (int i = 0; i < segment.Length; i++)
            {
                float gain = voice.Velocity;
                if (voice.FadeRemaining > 0)
                    gain *= Math.Max(0f, (float)(voice.FadeRemaining - i) / StealFadeFrames);
                var frame = scratch[i] * gain;
                segment[i] += frame;
                peak = Math.Max(peak, Math.Max(MathF.Abs(frame.Left), MathF.Abs(frame.Right)));
            }

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
            if (_position >= voice.NoteOffAt && peak < Silence)
                voice.QuietFrames += segment.Length;
            else
                voice.QuietFrames = 0;

            if (voice.QuietFrames >= _quietFrames)
                voice.Active = false;
        }
    }

    private sealed class VoiceSlot(VoiceControl control, IOperator op)
    {
        public VoiceControl Control { get; } = control;
        public IOperator Operator { get; } = op;
        public bool Active { get; set; }
        public float Velocity { get; set; }
        public long StartedAt { get; set; }
        public long NoteOffAt { get; set; }
        public long QuietFrames { get; set; }

        /// <summary>Frames left of the fade-out after the voice was cut; zero while it sounds normally.</summary>
        public int FadeRemaining { get; set; }
    }
}
