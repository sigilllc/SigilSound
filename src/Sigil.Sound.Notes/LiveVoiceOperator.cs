namespace Sigil.Sound;

/// <summary>
/// Plays the notes of a <see cref="NoteInput"/>: events arrive from control threads through a fixed single-producer queue
/// and are applied at the start of each block, then the voices of a <see cref="VoicePool"/> are rendered and mixed.
/// </summary>
/// <remarks>
/// Notes start at the first frame of the block after they were posted, so a live note has no sub-block timing; at the
/// default 256-frame block that is under 6 ms at 48 kHz. The operator's time is its own frame counter: voices compare
/// positions with it, and it has no relation to a host clock (voices keep their own time by design).
/// </remarks>
internal sealed class LiveVoiceOperator : IOperator, IResettable, IDisposable
{
    private const int QueueSize = 4096; // a power of two

    private readonly NoteInput _input;
    private readonly VoicePool _voices;
    private readonly NoteEvent[] _queue = new NoteEvent[QueueSize];

    // Single producer (NoteInput, under its lock) and single consumer (the audio thread).
    private int _head;
    private int _tail;
    private int _overflowed;
    private long _position;

    public LiveVoiceOperator(NoteInput input, Func<Voice, Sound> factory, int polyphony, RenderContext context)
    {
        _input = input;
        _voices = new VoicePool(factory, polyphony, context, stealReleasedFirst: true);
        input.Attach(this);
    }

    /// <summary>Notes that arrived while the queue was full and were dropped; a drop releases every voice rather than risk a stuck note.</summary>
    public int Dropped { get; private set; }

    /// <summary>Queues an event for the next block. Callers must be serialized, which <see cref="NoteInput"/> does.</summary>
    public void Post(NoteEvent noteEvent)
    {
        int tail = _tail;
        int next = (tail + 1) & (QueueSize - 1);
        if (next == Volatile.Read(ref _head))
        {
            Volatile.Write(ref _overflowed, 1);
            return;
        }

        _queue[tail] = noteEvent;
        Volatile.Write(ref _tail, next);
    }

    public void Reset()
    {
        _voices.Reset();
        _position = 0;
        // Anything still queued belongs to the state that was just discarded.
        Volatile.Write(ref _head, Volatile.Read(ref _tail));
    }

    public void Dispose()
    {
        _input.Detach(this);
        _voices.Dispose();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        Drain();
        block.Clear();
        _voices.Render(block, _position);
        _position += block.Length;
    }

    private void Drain()
    {
        int head = _head;
        int tail = Volatile.Read(ref _tail);
        while (head != tail)
        {
            var noteEvent = _queue[head];
            head = (head + 1) & (QueueSize - 1);
            switch (noteEvent.Kind)
            {
                case NoteEventKind.On:
                    _voices.Start(new Note(noteEvent.Pitch).Frequency, noteEvent.Pitch, noteEvent.Velocity, double.PositiveInfinity, _position);
                    break;
                case NoteEventKind.Off:
                    _voices.Release(noteEvent.Pitch, _position);
                    break;
                default:
                    _voices.ReleaseAll(_position);
                    break;
            }
        }

        Volatile.Write(ref _head, head);

        // An event was lost, possibly a note off. Letting every voice go is the one answer that cannot leave a note stuck,
        // and it has to come after the queued events are applied, or a stale note on would hold a voice again.
        if (Interlocked.Exchange(ref _overflowed, 0) == 1)
        {
            Dropped++;
            _voices.ReleaseAll(_position);
        }
    }
}
