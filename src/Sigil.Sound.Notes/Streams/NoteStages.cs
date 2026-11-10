namespace Sigil.Sound;

/// <summary>Changes a note as it starts. Returns false to drop it.</summary>
internal delegate bool NoteTransform(ref NoteEvent note);

/// <summary>Gathers the notes a raw function makes from one note; see <see cref="NoteStream.SelectMany"/>.</summary>
/// <remarks>One emitter is reused for every note, so it is only valid during the call that receives it.</remarks>
public sealed class NoteEmitter
{
    /// <summary>The most notes one note can turn into. Notes past this are dropped.</summary>
    public const int Capacity = 16;

    private readonly NoteEvent[] _notes = new NoteEvent[Capacity];

    internal int Count { get; private set; }

    internal NoteEvent this[int index] => _notes[index];

    internal void Reset() => Count = 0;

    /// <summary>
    /// Adds a note to the output. Only the pitch, velocity, cents and channel are used: the kind is always a start, and the id is the
    /// engine's, which is what lets it end the note when the note it came from ends.
    /// </summary>
    public void Add(NoteEvent note)
    {
        if (Count < Capacity)
            _notes[Count++] = note;
    }

    /// <summary>Adds a note by pitch.</summary>
    public void Add(int pitch, float velocity = 0.8f, float cents = 0f, int channel = 0) =>
        Add(NoteEvent.Start(0, pitch, velocity, cents, channel));
}

/// <summary>The source of a stream: the key events of a <see cref="NoteInput"/>.</summary>
/// <remarks>
/// Events arrive from control threads through a fixed single-producer queue and are read at the start of the block, so a note begins
/// within one block (under 6 ms at 256 frames and 48 kHz) of being pressed.
/// </remarks>
internal sealed class InputStage : NoteStage, IKeyListener
{
    private const int QueueSize = 4096; // a power of two
    private const byte PitchKeyed = 1;

    private readonly NoteInput _input;
    private readonly KeyEvent[] _queue = new KeyEvent[QueueSize];
    private int _head;
    private int _tail;
    private int _overflowed;

    public InputStage(NoteInput input)
    {
        _input = input;
        input.Attach(this);
    }

    public void Post(KeyEvent keyEvent)
    {
        int tail = _tail;
        int next = (tail + 1) & (QueueSize - 1);
        if (next == Volatile.Read(ref _head))
        {
            Volatile.Write(ref _overflowed, 1);
            return;
        }

        _queue[tail] = keyEvent;
        Volatile.Write(ref _tail, next);
    }

    protected override void Compute(in BlockTime time)
    {
        int head = _head;
        int tail = Volatile.Read(ref _tail);
        while (head != tail)
        {
            var key = _queue[head];
            head = (head + 1) & (QueueSize - 1);
            switch (key.Kind)
            {
                case KeyEventKind.On:
                    // A key pressed again while it is down ends the earlier press, so one pitch is never two stuck notes.
                    ReleaseTagged(PitchKeyed, key.Pitch);
                    Emit(key.Id, NoteEvent.Start(key.Id, key.Pitch, key.Velocity, key.Cents), PitchKeyed);
                    break;
                case KeyEventKind.OnNote:
                    Emit(key.Id, NoteEvent.Start(key.Id, key.Pitch, key.Velocity));
                    break;
                case KeyEventKind.Off:
                case KeyEventKind.OffNote:
                    ReleaseOwner(key.Id);
                    break;
                default:
                    ReleaseEverything();
                    break;
            }
        }

        Volatile.Write(ref _head, head);

        // An event was lost, possibly an end. Ending everything and asking the input what is down now is the one answer that cannot
        // leave a note stuck, and it has to come after the queued events, or a stale start would hold a note again.
        if (Interlocked.Exchange(ref _overflowed, 0) == 1)
        {
            ReleaseEverything();
            Volatile.Write(ref _head, Volatile.Read(ref _tail));
            _input.ReplayHeld(this);
        }
    }

    protected override void ClearState()
    {
        Volatile.Write(ref _head, Volatile.Read(ref _tail));
        _input.ReplayHeld(this);
    }

    protected override void Release() => _input.Detach(this);
}

/// <summary>A stage that changes each note as it starts, or drops it; its end follows through the ledger.</summary>
internal sealed class TransformStage(NoteStage input, NoteTransform transform) : NoteStage
{
    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.Off)
            {
                ReleaseOwner(e.Id);
                continue;
            }

            var note = e;
            if (transform(ref note) && note.Pitch is >= 0 and <= 127)
                Emit(e.Id, note with { Kind = NoteEventKind.On, Id = e.Id });
        }
    }
}

/// <summary>A stage that turns each note into several (a harmony, a chord, a raw function's choice).</summary>
internal sealed class FanOutStage(NoteStage input, Action<NoteEvent, NoteEmitter> expand) : NoteStage
{
    private readonly NoteEmitter _emitter = new();

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.Off)
            {
                ReleaseOwner(e.Id);
                continue;
            }

            _emitter.Reset();
            expand(e, _emitter);
            for (int i = 0; i < _emitter.Count; i++)
            {
                var made = _emitter[i];
                if (made.Pitch is < 0 or > 127)
                    continue;
                Emit(e.Id, made with { Kind = NoteEventKind.On, Id = NoteIds.Derive(e.Id, i, Salt) });
            }
        }
    }
}

/// <summary>A stage with state that can start notes of its own (a raw fold over the stream).</summary>
internal sealed class ScanStage<TState> : NoteStage
{
    private readonly NoteStage _input;
    private readonly TState _seed;
    private readonly Func<TState, NoteEvent, NoteEmitter, TState> _step;
    private readonly NoteEmitter _emitter = new();
    private TState _state;

    public ScanStage(NoteStage input, TState seed, Func<TState, NoteEvent, NoteEmitter, TState> step)
    {
        _input = input;
        _seed = seed;
        _step = step;
        _state = seed;
    }

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in _input.Run(time))
        {
            _emitter.Reset();
            _state = _step(_state, e, _emitter);
            if (e.Kind == NoteEventKind.Off)
            {
                // The end of the note ends what the function made from it, and the function may not start anything on an end.
                ReleaseOwner(e.Id);
                continue;
            }

            for (int i = 0; i < _emitter.Count; i++)
            {
                var made = _emitter[i];
                if (made.Pitch is >= 0 and <= 127)
                    Emit(e.Id, made with { Kind = NoteEventKind.On, Id = NoteIds.Derive(e.Id, i, Salt) });
            }
        }
    }

    protected override void ClearState() => _state = _seed;
}

/// <summary>Several streams as one. The notes of each keep their ids, made distinct from the others'.</summary>
internal sealed class MergeStage(NoteStage[] inputs) : NoteStage
{
    protected override void Compute(in BlockTime time)
    {
        for (int branch = 0; branch < inputs.Length; branch++)
        {
            foreach (var e in inputs[branch].Run(time))
            {
                long id = NoteIds.Derive(e.Id, branch, Salt);
                if (e.Kind == NoteEventKind.Off)
                    ReleaseOwner(id);
                else
                    Emit(id, e with { Id = id });
            }
        }
    }
}

/// <summary>Delays every note, start and end alike, so each keeps its length.</summary>
internal sealed class DelayStage(NoteStage input, double beats) : NoteStage
{
    private readonly ScheduledQueue _queue = new();

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.On)
                _queue.Add(time.Elapsed + beats, ScheduledQueue.Op.Start, e.Id, e);
            else if (!_queue.Add(time.Elapsed + beats, ScheduledQueue.Op.End, e.Id, e))
                ReleaseOwner(e.Id); // no room to schedule the end: end it early rather than never
        }

        Drain(_queue, time.ElapsedEnd);
    }

    private void Drain(ScheduledQueue queue, double before)
    {
        while (queue.TryTake(before, out var item))
        {
            if (item.Op == ScheduledQueue.Op.Start)
                Emit(item.Owner, item.Event);
            else
                ReleaseOwner(item.Owner);
        }
    }

    protected override void ClearState() => _queue.Clear();
}

/// <summary>Repeats each note, softer each time, one <c>beats</c> apart; every repeat is as long as the note.</summary>
internal sealed class EchoStage(NoteStage input, int repeats, double beats, double decay) : NoteStage
{
    private readonly ScheduledQueue _queue = new();

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.On)
            {
                Emit(e.Id, e);
                float velocity = e.Velocity;
                for (int k = 1; k <= repeats; k++)
                {
                    velocity *= (float)decay;
                    if (velocity < 0.01f)
                        break;
                    long id = NoteIds.Derive(e.Id, k, Salt);
                    _queue.Add(time.Elapsed + k * beats, ScheduledQueue.Op.Start, id, e with { Id = id, Velocity = velocity });
                }
            }
            else
            {
                ReleaseOwner(e.Id);
                for (int k = 1; k <= repeats; k++)
                {
                    long id = NoteIds.Derive(e.Id, k, Salt);
                    if (!_queue.Add(time.Elapsed + k * beats, ScheduledQueue.Op.End, id, e with { Id = id }))
                        ReleaseOwner(id);
                }
            }
        }

        while (_queue.TryTake(time.ElapsedEnd, out var item))
        {
            if (item.Op == ScheduledQueue.Op.Start)
                Emit(item.Owner, item.Event);
            else
                ReleaseOwner(item.Owner);
        }
    }

    protected override void ClearState() => _queue.Clear();
}

/// <summary>Spreads the notes that start together over time, lowest first as they came, each <c>beats</c> after the one before.</summary>
internal sealed class StrumStage(NoteStage input, double beats) : NoteStage
{
    private const int MemoSize = 128;

    private readonly ScheduledQueue _queue = new();
    private readonly long[] _ids = new long[MemoSize];
    private readonly double[] _delays = new double[MemoSize];
    private int _memoCount;

    protected override void Compute(in BlockTime time)
    {
        int index = 0;
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.On)
            {
                double delay = index++ * beats;
                if (_memoCount < MemoSize)
                {
                    _ids[_memoCount] = e.Id;
                    _delays[_memoCount++] = delay;
                }
                else
                {
                    delay = 0;
                }

                _queue.Add(time.Elapsed + delay, ScheduledQueue.Op.Start, e.Id, e);
            }
            else
            {
                double delay = Forget(e.Id);
                if (!_queue.Add(time.Elapsed + delay, ScheduledQueue.Op.End, e.Id, e))
                    ReleaseOwner(e.Id);
            }
        }

        while (_queue.TryTake(time.ElapsedEnd, out var item))
        {
            if (item.Op == ScheduledQueue.Op.Start)
                Emit(item.Owner, item.Event);
            else
                ReleaseOwner(item.Owner);
        }
    }

    private double Forget(long id)
    {
        for (int i = 0; i < _memoCount; i++)
        {
            if (_ids[i] != id)
                continue;
            double delay = _delays[i];
            _memoCount--;
            _ids[i] = _ids[_memoCount];
            _delays[i] = _delays[_memoCount];
            return delay;
        }

        return 0;
    }

    protected override void ClearState()
    {
        _queue.Clear();
        _memoCount = 0;
    }
}

/// <summary>Gives every note the same length, whatever the key does.</summary>
internal sealed class LengthStage(NoteStage input, double beats) : NoteStage
{
    private readonly ScheduledQueue _queue = new();

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            // The key's own end is ignored; the note ends when its length is up.
            if (e.Kind == NoteEventKind.On && Emit(e.Id, e) && !_queue.Add(time.Elapsed + beats, ScheduledQueue.Op.End, e.Id, e))
                ReleaseOwner(e.Id);
        }

        while (_queue.TryTake(time.ElapsedEnd, out var item))
            ReleaseOwner(item.Owner);
    }

    protected override void ClearState() => _queue.Clear();
}

/// <summary>Moves each note a little later and changes its velocity a little, by amounts that depend only on the seed and the note.</summary>
internal sealed class HumanizeStage(NoteStage input, double timing, double velocity, int seed) : NoteStage
{
    private readonly ScheduledQueue _queue = new();

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            double delay = NoteIds.Unit(seed, e.Id) * timing;
            if (e.Kind == NoteEventKind.On)
            {
                float v = (float)Math.Clamp(e.Velocity + (NoteIds.Unit(seed, e.Id, 1) * 2 - 1) * velocity, 1.0 / 127, 1.0);
                _queue.Add(time.Elapsed + delay, ScheduledQueue.Op.Start, e.Id, e with { Velocity = v });
            }
            else if (!_queue.Add(time.Elapsed + delay, ScheduledQueue.Op.End, e.Id, e))
            {
                ReleaseOwner(e.Id);
            }
        }

        while (_queue.TryTake(time.ElapsedEnd, out var item))
        {
            if (item.Op == ScheduledQueue.Op.Start)
                Emit(item.Owner, item.Event);
            else
                ReleaseOwner(item.Owner);
        }
    }

    protected override void ClearState() => _queue.Clear();
}

/// <summary>One note at a time: of the keys that are down, only the one the priority picks sounds.</summary>
internal sealed class MonoStage(NoteStage input, NotePriority priority) : NoteStage
{
    private const int MaxHeld = 64;

    private readonly NoteEvent[] _held = new NoteEvent[MaxHeld];
    private int _count;

    protected override void Compute(in BlockTime time)
    {
        bool changed = false;
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.On)
            {
                if (_count < MaxHeld)
                {
                    _held[_count++] = e;
                    changed = true;
                }
            }
            else
            {
                for (int i = 0; i < _count; i++)
                {
                    if (_held[i].Id != e.Id)
                        continue;
                    Array.Copy(_held, i + 1, _held, i, --_count - i);
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
            Settle();
    }

    private void Settle()
    {
        if (_count == 0)
        {
            ReleaseEverything();
            return;
        }

        var winner = _held[0];
        for (int i = 1; i < _count; i++)
        {
            var candidate = _held[i];
            bool better = priority switch
            {
                NotePriority.Lowest => candidate.Pitch <= winner.Pitch,
                NotePriority.Highest => candidate.Pitch >= winner.Pitch,
                _ => true, // later presses sit later in the list
            };
            if (better)
                winner = candidate;
        }

        if (IsSounding(winner.Id))
            return;

        ReleaseEverything();
        Emit(winner.Id, winner);
    }

    protected override void ClearState() => _count = 0;
}

/// <summary>Keeps notes sounding after their keys are released, until a new group of keys is pressed.</summary>
internal sealed class LatchStage(NoteStage input) : NoteStage
{
    private const int MaxDown = 128;

    private readonly long[] _down = new long[MaxDown];
    private int _downCount;

    protected override void Compute(in BlockTime time)
    {
        foreach (var e in input.Run(time))
        {
            if (e.Kind == NoteEventKind.On)
            {
                // The first key of a new group lets the last group go.
                if (_downCount == 0)
                    ReleaseEverything();
                if (_downCount < MaxDown)
                    _down[_downCount++] = e.Id;
                Emit(e.Id, e);
            }
            else
            {
                for (int i = 0; i < _downCount; i++)
                {
                    if (_down[i] != e.Id)
                        continue;
                    _down[i] = _down[--_downCount];
                    break;
                }
            }
        }
    }

    protected override void ClearState() => _downCount = 0;
}

/// <summary>A pattern of notes as a stream: its notes start on the beat grid and end when their events do.</summary>
internal sealed class PatternSourceStage(Pattern<Note> pattern) : NoteStage
{
    private const double Epsilon = 1e-9;
    private const double ContinuityBeats = 0.01;

    private readonly ScheduledQueue _queue = new();
    private readonly List<PatternEvent<Note>> _events = new(64);
    private Fraction _end;
    private double _endBeats = double.NaN;
    private bool _failed;
    private long _nextId = 1L << 62;

    protected override void Compute(in BlockTime time)
    {
        // Ends first, so a note that starts again on the same pitch is heard again.
        while (_queue.TryTake(time.GridEnd - Epsilon, out var item))
            ReleaseOwner(item.Owner);

        if (!time.GridRunning || _failed)
            return;

        // A window starts exactly where the last one ended (as a fraction, so nothing is lost to rounding): every note plays once.
        var begin = Math.Abs(time.Grid - _endBeats) <= ContinuityBeats ? _end : Fraction.FromDouble(time.Grid / Pat.BeatsPerCycle);
        var end = Fraction.Max(begin, Fraction.FromDouble(time.GridEnd / Pat.BeatsPerCycle));
        _end = end;
        _endBeats = time.GridEnd;

        _events.Clear();
        if (!pattern.TryQuery(new Arc(begin, end), _events))
        {
            // It was valid when built, but a later cycle is too dense: stop sending notes (those sounding still end).
            _failed = true;
            return;
        }

        foreach (var e in _events)
        {
            if (!e.HasOnset)
                continue;
            long id = ++_nextId;
            var note = NoteEvent.Start(id, Math.Clamp(e.Value.Pitch, 0, 127), e.Value.Velocity);
            if (Emit(id, note))
            {
                double length = e.Whole.Length.ToDouble() * Pat.BeatsPerCycle;
                double start = e.Whole.Begin.ToDouble() * Pat.BeatsPerCycle;
                if (!_queue.Add(start + length, ScheduledQueue.Op.End, id, note))
                    ReleaseOwner(id);
            }
        }
    }

    protected override void ClearState()
    {
        _queue.Clear();
        _endBeats = double.NaN;
        _failed = false;
    }
}
