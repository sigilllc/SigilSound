namespace Sigil.Sound;

/// <summary>
/// Plays a <see cref="Pattern"/> on a <see cref="HostClock"/>'s beat grid instead of from its own first frame. A looping
/// pattern sits at a fixed place on the grid (its position is the host's beat position modulo its length), so replacing
/// the sound that plays it leaves the notes on the beat; a pattern that does not loop starts at the beat where it began.
/// The tempo is the clock's, read every block.
/// </summary>
/// <remarks>
/// Each block handles the events in a window of pattern time. The window starts where the previous one ended, not where
/// the clock says this block starts, so rounding between the two can neither skip an event nor play one twice; only a
/// jump larger than <see cref="ContinuityBeats"/> (the transport was moved) is treated as a seek.
/// </remarks>
internal sealed class HostPatternOperator : IOperator, IResettable, IDisposable
{
    private const double ContinuityBeats = 0.01;

    // Both edges of every window move down by this much (about half a nanosecond at 120 bpm). A note exactly on a block
    // boundary then belongs to the later block however the arithmetic rounds, instead of to whichever side noise puts it on.
    // Because every window starts where the last one ended, shifting both edges together cannot skip or repeat a note.
    private const double EdgeBeats = 1e-9;

    private readonly HostClock _clock;
    private readonly Note[] _notes;
    private readonly double[] _starts;
    private readonly double _length;
    private readonly bool _looping;
    private readonly int _sampleRate;
    private readonly VoicePool _voices;

    private long _position;
    private double _origin;
    private bool _started;
    private double _handledTo = double.NaN;

    public HostPatternOperator(Pattern pattern, Func<Voice, Sound> factory, int polyphony, RenderContext context)
    {
        _clock = context.TimeClock ?? throw new ArgumentException("A host pattern needs a clock.", nameof(context));
        _notes = pattern.Events.Select(e => e.Note).ToArray();
        _starts = pattern.Events.Select(e => e.Start).ToArray();
        _length = pattern.Length;
        _looping = pattern.IsLooping && pattern.Length > 0;
        _sampleRate = context.SampleRate;
        _voices = new VoicePool(factory, polyphony, context);
    }

    public void Reset()
    {
        _position = 0;
        _started = false;
        _handledTo = double.NaN;
        _voices.Reset();
    }

    public void Dispose() => _voices.Dispose();

    public void RenderBlock(Span<StereoFrame> block)
    {
        block.Clear();

        // A held host clock starts nothing new; the handled-to beat is left alone so the pattern resumes exactly there.
        if (!_clock.Running)
        {
            _voices.Render(block, _position);
            _position += block.Length;
            return;
        }

        double bpm = _clock.Bpm;
        double beatsPerFrame = bpm / 60.0 / _sampleRate;

        if (!_started)
        {
            _started = true;
            // A looping pattern lives on the host's grid; one that plays once begins where it was started.
            _origin = _looping ? 0 : _clock.Beats;
        }

        double blockStart = _clock.Beats - _origin;
        double windowStart = !double.IsNaN(_handledTo) && Math.Abs(blockStart - _handledTo) <= ContinuityBeats
            ? _handledTo
            : blockStart;
        double windowEnd = Math.Max(windowStart, blockStart + block.Length * beatsPerFrame);

        int done = 0;
        if (_starts.Length > 0)
        {
            long firstCycle = _looping ? (long)Math.Floor(windowStart / _length) : 0;
            long lastCycle = _looping ? (long)Math.Floor(windowEnd / _length) : 0;
            for (long cycle = firstCycle; cycle <= lastCycle; cycle++)
            {
                double cycleBase = _looping ? cycle * _length : 0;
                double lo = windowStart - cycleBase - EdgeBeats;
                double hi = windowEnd - cycleBase - EdgeBeats;
                for (int i = LowerBound(lo); i < _starts.Length && _starts[i] < hi; i++)
                    Fire(block, ref done, i, cycleBase + _starts[i], blockStart, beatsPerFrame, bpm);
            }
        }

        if (done < block.Length)
            _voices.Render(block[done..], _position + done);

        _position += block.Length;
        _handledTo = windowEnd;
    }

    // Renders up to the event's frame, then starts its note there.
    private void Fire(Span<StereoFrame> block, ref int done, int index, double at, double blockStart, double beatsPerFrame, double bpm)
    {
        int frame = Math.Clamp((int)Math.Ceiling((at - blockStart) / beatsPerFrame - 1e-9), done, block.Length - 1);
        if (frame > done)
        {
            _voices.Render(block.Slice(done, frame - done), _position + done);
            done = frame;
        }

        var note = _notes[index];
        _voices.Start(note.Frequency, note.Pitch, note.Velocity, note.Beats * 60.0 / bpm, _position + done);
    }

    // The index of the first event starting at or after beat, or the event count if there is none.
    private int LowerBound(double beat)
    {
        int low = 0;
        int high = _starts.Length;
        while (low < high)
        {
            int mid = (low + high) >>> 1;
            if (_starts[mid] < beat)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }
}
