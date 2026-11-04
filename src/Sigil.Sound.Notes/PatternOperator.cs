namespace Sigil.Sound;

/// <summary>
/// Plays a <see cref="Pattern"/> at a fixed tempo: triggers a voice at each note's frame and mixes the active voices.
/// Time starts when the operator does; see <c>HostPatternOperator</c> for patterns that follow a host's beat grid.
/// </summary>
/// <remarks>
/// The voices, their reuse and their release live in <see cref="VoicePool"/>; this operator only decides when each note
/// starts and how long it is held.
/// </remarks>
internal sealed class PatternOperator : IOperator, IResettable, IDisposable
{
    private readonly TimedNote[] _events;
    private readonly long[] _startFrames;
    private readonly double _secondsPerBeat;
    private readonly long _cycleFrames;
    private readonly VoicePool _voices;

    private long _position;
    private long _cycleStart;
    private int _next;

    public PatternOperator(Pattern pattern, Func<Voice, Sound> factory, double bpm, int polyphony, RenderContext context)
    {
        _events = pattern.Events.ToArray();
        _secondsPerBeat = 60.0 / bpm;
        _startFrames = _events.Select(e => (long)Math.Round(e.Start * _secondsPerBeat * context.SampleRate)).ToArray();
        _cycleFrames = pattern.IsLooping ? (long)Math.Round(pattern.Length * _secondsPerBeat * context.SampleRate) : 0;
        _voices = new VoicePool(factory, polyphony, context);
    }

    public void Reset()
    {
        _position = 0;
        _cycleStart = 0;
        _next = 0;
        _voices.Reset();
    }

    public void Dispose() => _voices.Dispose();

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
            _voices.Render(block.Slice(done, segment), _position);
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
        var note = _events[_next++].Note;
        _voices.Start(note.Frequency, note.Pitch, note.Velocity, note.Beats * _secondsPerBeat, _position);
    }
}
