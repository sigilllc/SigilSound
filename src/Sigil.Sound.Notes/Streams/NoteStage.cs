namespace Sigil.Sound;

/// <summary>
/// Where one block of a stream is in time. <see cref="Elapsed"/> counts beats of the stream's own, which keep running when the
/// host transport is stopped (a delay must still be a delay); <see cref="Grid"/> is the host's beat position, the one patterns and
/// bar lines are measured on.
/// </summary>
internal readonly record struct BlockTime(
    long Block,
    int Frames,
    double Seconds,
    double Elapsed,
    double ElapsedEnd,
    double Grid,
    double GridEnd,
    bool GridRunning,
    double Bpm);

/// <summary>
/// One compiled step of a <see cref="NoteStream"/>: reads the events of the stages before it for a block and writes its own.
/// </summary>
/// <remarks>
/// <para>
/// The base class does the part that has to be right every time, so no operator can get it wrong. Every note a stage starts goes into
/// a ledger with the <i>owner</i> it came from (usually the id of the input note), and ending the owner ends exactly those notes. That
/// is the pairing rule: an operator decides what to start, never how to end it, so a raw function cannot leave a note stuck and a
/// note whose start was dropped has no end to send. The ledger and the output buffer have fixed sizes and always leave room for the
/// ends of everything sounding, so a flood of notes drops starts, never ends.
/// </para>
/// <para>A stage runs once per block however many stages read it (the output is kept), and nothing here allocates after construction.</para>
/// </remarks>
internal abstract class NoteStage : IDisposable
{
    /// <summary>The most notes one stage keeps sounding at once. A start past this is dropped.</summary>
    public const int MaxSounding = 128;

    /// <summary>The most events one stage writes in a block.</summary>
    public const int OutputCapacity = 512;

    private readonly NoteEvent[] _output = new NoteEvent[OutputCapacity];
    private readonly Sounding[] _ledger = new Sounding[MaxSounding];
    private int _count;
    private int _ledgerCount;
    private long _block = long.MinValue;

    /// <summary>Starts that were dropped because the stage was full.</summary>
    public int Dropped { get; private set; }

    /// <summary>A number unique to this stage in its stream, given when it is built; it keeps the ids this stage makes apart from every other stage's.</summary>
    public int Salt { get; set; }

    /// <summary>The events of this stage for the block, computed on the first call for a block and kept for the others.</summary>
    public ReadOnlySpan<NoteEvent> Run(scoped in BlockTime time)
    {
        if (time.Block != _block)
        {
            _block = time.Block;
            _count = 0;
            Compute(time);
        }

        return _output.AsSpan(0, _count);
    }

    /// <summary>Writes this stage's events for the block with <see cref="Emit"/> and <see cref="ReleaseOwner"/>.</summary>
    protected abstract void Compute(in BlockTime time);

    /// <summary>Forgets the state the subclass keeps (queues, memory of notes, random state). The ledger is cleared by the caller.</summary>
    protected virtual void ClearState()
    {
    }

    /// <summary>Releases what the subclass holds that needs it.</summary>
    protected virtual void Release()
    {
    }

    public void Dispose() => Release();

    /// <summary>
    /// Starts a note owned by <paramref name="owner"/>. Returns false, and writes nothing, when the stage is full; the note's end will
    /// then find nothing to end, which is what keeps the pairing.
    /// </summary>
    protected bool Emit(long owner, in NoteEvent start, byte tag = 0)
    {
        // The start takes a slot now and reserves one for its end, as does every note already sounding.
        if (_ledgerCount >= MaxSounding || _count + _ledgerCount + 2 > OutputCapacity)
        {
            Dropped++;
            return false;
        }

        _ledger[_ledgerCount++] = new Sounding(owner, start, tag);
        _output[_count++] = start;
        return true;
    }

    /// <summary>Ends every note this stage started for <paramref name="owner"/>. Does nothing if there is none.</summary>
    protected void ReleaseOwner(long owner)
    {
        int write = 0;
        for (int read = 0; read < _ledgerCount; read++)
        {
            var entry = _ledger[read];
            if (entry.Owner == owner)
                WriteOff(entry.Event);
            else
                _ledger[write++] = entry;
        }

        _ledgerCount = write;
    }

    /// <summary>Ends the note this stage started with the tag <paramref name="tag"/> on <paramref name="pitch"/>, if any (a key pressed twice).</summary>
    protected void ReleaseTagged(byte tag, int pitch)
    {
        int write = 0;
        for (int read = 0; read < _ledgerCount; read++)
        {
            var entry = _ledger[read];
            if (entry.Tag == tag && entry.Event.Pitch == pitch)
                WriteOff(entry.Event);
            else
                _ledger[write++] = entry;
        }

        _ledgerCount = write;
    }

    /// <summary>Ends every note this stage has sounding, in the order they started.</summary>
    protected void ReleaseEverything()
    {
        for (int i = 0; i < _ledgerCount; i++)
            WriteOff(_ledger[i].Event);
        _ledgerCount = 0;
    }

    /// <summary>True while this stage has a note sounding that <paramref name="owner"/> started.</summary>
    protected bool IsSounding(long owner)
    {
        for (int i = 0; i < _ledgerCount; i++)
            if (_ledger[i].Owner == owner)
                return true;
        return false;
    }

    /// <summary>How many notes this stage has sounding.</summary>
    public int SoundingCount => _ledgerCount;

    /// <summary>
    /// Copies the ends of everything sounding into <paramref name="into"/> (up to its length) and forgets all state, as for a stop or a
    /// locate. Returns how many were written.
    /// </summary>
    public int TakeEnds(Span<NoteEvent> into)
    {
        int written = 0;
        for (int i = 0; i < _ledgerCount && written < into.Length; i++)
            into[written++] = _ledger[i].Event with { Kind = NoteEventKind.Off, Velocity = 0f };
        return written;
    }

    /// <summary>Forgets everything: the notes sounding, the output of the last block and the subclass's state.</summary>
    public void Clear()
    {
        _ledgerCount = 0;
        _count = 0;
        _block = long.MinValue;
        ClearState();
    }

    private void WriteOff(in NoteEvent start)
    {
        // Room was kept for this when the note started.
        _output[_count++] = start with { Kind = NoteEventKind.Off, Velocity = 0f };
    }

    private readonly record struct Sounding(long Owner, NoteEvent Event, byte Tag);
}

/// <summary>Spreads one note id into several, and several notes from different branches into distinct ones.</summary>
internal static class NoteIds
{
    /// <summary>
    /// The id of the <paramref name="index"/>th note made from <paramref name="id"/> by the stage numbered <paramref name="salt"/>; the first
    /// one keeps the id itself. The salt keeps two stages apart that make notes from the same note (a harmonizer, then an echo): without
    /// it, the echo's first repeat would be the harmonizer's second note.
    /// </summary>
    public static long Derive(long id, int index, int salt)
    {
        if (index == 0)
            return id;
        return Mix(id + (index + (long)salt * 64) * unchecked((long)0x9E3779B97F4A7C15UL));
    }

    /// <summary>A well mixed 64-bit value from <paramref name="value"/> (SplitMix64's finalizer).</summary>
    public static long Mix(long value)
    {
        unchecked
        {
            ulong z = (ulong)value;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (long)(z ^ (z >> 31));
        }
    }

    /// <summary>A repeatable number in [0, 1) from a seed and a note id: the dice of the chance and humanize operators.</summary>
    public static double Unit(int seed, long id, int salt = 0) =>
        (Mix(id ^ Mix(seed + salt * 7919L)) >>> 11) / (double)(1L << 53);
}

/// <summary>A fixed-size list of things to do at a time, kept in order of time and then of arrival.</summary>
internal sealed class ScheduledQueue
{
    public const int Capacity = 512;

    private readonly Item[] _items = new Item[Capacity];
    private int _count;
    private long _sequence;

    public enum Op : byte
    {
        Start,
        End,
    }

    public readonly record struct Item(double Due, long Sequence, Op Op, long Owner, NoteEvent Event);

    public int Count => _count;

    /// <summary>Adds an item; false (and nothing added) when the queue is full.</summary>
    public bool Add(double due, Op op, long owner, in NoteEvent noteEvent)
    {
        if (_count == Capacity)
            return false;

        var item = new Item(due, _sequence++, op, owner, noteEvent);
        int index = _count++;
        while (index > 0 && _items[index - 1].Due > due)
        {
            _items[index] = _items[index - 1];
            index--;
        }

        _items[index] = item;
        return true;
    }

    /// <summary>Takes the earliest item due before <paramref name="before"/>.</summary>
    public bool TryTake(double before, out Item item)
    {
        if (_count > 0 && _items[0].Due < before)
        {
            item = _items[0];
            Array.Copy(_items, 1, _items, 0, --_count);
            return true;
        }

        item = default;
        return false;
    }

    public void Clear() => _count = 0;
}
