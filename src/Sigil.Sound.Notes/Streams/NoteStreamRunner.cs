using System.Diagnostics;

namespace Sigil.Sound;

/// <summary>
/// A <see cref="NoteStream"/> built to run: a host calls <see cref="Process"/> once per block and reads the notes that come out, to send
/// them to a device or an instrument of its own. This is the other half of <see cref="NoteStream.Play"/>, which plays the notes itself.
/// </summary>
/// <remarks>
/// <para>
/// Not thread safe: one thread (the audio thread, normally) calls <see cref="Process"/>, <see cref="Poll"/> and <see cref="Reset"/>.
/// The input side takes its notes from control threads through a lock-free queue and needs nothing from you.
/// </para>
/// <para>
/// Call <see cref="Process"/> before the host clock advances for the block, as the clock then still reads the block's first beat.
/// The events it returns are valid until the next call.
/// </para>
/// </remarks>
public sealed class NoteStreamRunner : IDisposable
{
    private readonly NoteStage _terminal;
    private readonly List<NoteStage> _stages;
    private readonly Func<HostClock?> _clockSource;
    private readonly int _sampleRate;
    private readonly NoteEvent[] _ends = new NoteEvent[NoteStage.MaxSounding];
    private long _block;
    private double _seconds;
    private double _elapsed;
    private double _grid;
    private HostClock? _lastClock;
    private long _lastSample;
    private long _lastTicks;

    // The clock is looked up every block, not kept from the build: a host may give the input its clock after a script has built its
    // stream, and a stream built before the clock existed must follow it from then on.
    internal NoteStreamRunner(NoteStream stream, Func<HostClock?> clockSource, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        _clockSource = clockSource;
        _sampleRate = sampleRate;
        var builder = new StageBuilder(_clockSource());
        _terminal = builder.For(stream);
        _stages = builder.Stages;
        _lastClock = _clockSource();
        _lastSample = _lastClock?.SamplePosition ?? 0;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>The clock the stream follows right now, or null.</summary>
    internal HostClock? CurrentClock => _clockSource();

    /// <summary>The time, in seconds, that the blocks passed to <see cref="Process"/> and <see cref="Poll"/> add up to.</summary>
    public double Seconds => _seconds;

    /// <summary>Notes the stream has sounding now.</summary>
    public int Sounding => _terminal.SoundingCount;

    /// <summary>Starts that were dropped because a step of the stream was full. Zero in normal use.</summary>
    public int Dropped => _stages.Sum(s => s.Dropped);

    internal double GridBeats => _grid;

    /// <summary>
    /// Runs the stream for the next block of <paramref name="frames"/> frames and returns the events that fall in it, in order.
    /// </summary>
    /// <param name="frames">The block length, 0 or more. A host may pass 0 to read what has arrived without letting time pass.</param>
    /// <returns>The starts and ends of the block, valid until the next call.</returns>
    public ReadOnlySpan<NoteEvent> Process(int frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames), frames, "Frames must not be negative.");

        var clock = Track();
        double bpm = clock?.Bpm ?? 120.0;
        double duration = frames / (double)_sampleRate * bpm / 60.0;
        double grid = clock?.Beats ?? _grid;
        bool running = clock?.Running ?? true;
        var time = new BlockTime(++_block, frames, _seconds, _elapsed, _elapsed + duration, grid, running ? grid + duration : grid, running, bpm);

        var events = _terminal.Run(time);

        _seconds += frames / (double)_sampleRate;
        _elapsed += duration;
        if (clock is null)
            _grid += duration;
        return events;
    }

    /// <summary>
    /// Runs the stream for the time that has passed since the last call to this method or <see cref="Process"/>, as the clock (or, without
    /// one, the wall clock) tells. This is for readers that are asked at no fixed rate: a pattern that follows the stream is queried
    /// once per block by whoever plays it, and each query catches the stream up.
    /// </summary>
    /// <returns>The events of the elapsed time, valid until the next call.</returns>
    public ReadOnlySpan<NoteEvent> Poll()
    {
        int frames;
        var clock = Track();
        if (clock is not null)
        {
            long position = clock.SamplePosition;
            frames = (int)Math.Clamp(position - _lastSample, 0, int.MaxValue / 4);
            _lastSample = position;
        }
        else
        {
            long ticks = Stopwatch.GetTimestamp();
            frames = (int)Math.Clamp((ticks - _lastTicks) / (double)Stopwatch.Frequency * _sampleRate, 0, int.MaxValue / 4);
            _lastTicks = ticks;
        }

        return Process(frames);
    }

    // Notices a clock that arrived or changed, and starts counting elapsed frames from where it stands.
    private HostClock? Track()
    {
        var clock = _clockSource();
        if (!ReferenceEquals(clock, _lastClock))
        {
            _lastClock = clock;
            _lastSample = clock?.SamplePosition ?? 0;
        }

        return clock;
    }

    /// <summary>
    /// Forgets all state, as for a stop, a bypass or a transport locate, and returns the ends of the notes that were sounding so the
    /// host can pass them on. Keys still held start again with the next block.
    /// </summary>
    /// <returns>The ends of every note the stream had sounding, valid until the next call.</returns>
    public ReadOnlySpan<NoteEvent> Reset()
    {
        int count = _terminal.TakeEnds(_ends);
        foreach (var stage in _stages)
            stage.Clear();
        return _ends.AsSpan(0, count);
    }

    /// <summary>Stops listening to the input. The notes still sounding are not ended; call <see cref="Reset"/> first for that.</summary>
    public void Dispose()
    {
        foreach (var stage in _stages)
            stage.Dispose();
    }
}
