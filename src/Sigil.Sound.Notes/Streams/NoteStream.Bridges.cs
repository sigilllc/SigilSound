namespace Sigil.Sound;

public sealed partial class NoteStream
{
    /// <summary>
    /// The chord the stream is playing, as a <see cref="Pattern{T}"/> to write against: <c>stream.Chord().Arp(ArpMode.Down)</c> arpeggiates
    /// whatever chord arrives, and <c>stream.Chord().Steps(Mini.Integers("7 6 5 4"))</c> plays steps of it. The same choices as the
    /// AutoArp device decide what counts as a chord and when a new one takes over.
    /// </summary>
    /// <param name="settle">
    /// Notes that start within this many seconds of each other are one chord, so a strum or a source that releases and presses voice by
    /// voice never shows a half-changed chord. 0 to 1; defaults to 0.035 (35 ms), as the AutoArp device.
    /// </param>
    /// <param name="latch">Whether the chord outlives its keys. Defaults to keeping the last chord.</param>
    /// <param name="switchOn">
    /// When a new chord takes over. <see cref="ChordSwitch.Cycle"/> makes every bar-long event (a run, an arpeggio) use one chord from
    /// start to finish; <see cref="ChordSwitch.Beat"/> (the default) lets it change on the beat; <see cref="ChordSwitch.Instant"/> as
    /// soon as it settles. The first chord is taken at once, and so is every chord while the transport is stopped.
    /// </param>
    /// <returns>
    /// A live pattern: one event per cycle holding the chord, answering for the present whatever time it is asked about. It is empty
    /// before the first chord, and (with <see cref="ChordLatch.Gated"/>) while no key is down. It cannot be written out with
    /// <see cref="NotePatterns.ToPattern"/>, which throws.
    /// </returns>
    /// <remarks>
    /// The chord is the set of keys down when the last note of a group has settled, sorted from low to high. Letting go of keys never
    /// changes it (with the default latch), so a chord released key by key keeps its shape. The pattern is caught up whenever it is
    /// queried, once per block by whatever plays it, and reads the time from <see cref="NoteInput.Clock"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The settle time is outside 0 to 1, or an option is not defined.</exception>
    public Pattern<Chord> Chord(double settle = 0.035, ChordLatch latch = ChordLatch.Infinite, ChordSwitch switchOn = ChordSwitch.Beat)
    {
        if (!(settle is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(settle), settle, "The settle time must be between 0 and 1 second.");
        if (!Enum.IsDefined(latch)) throw new ArgumentOutOfRangeException(nameof(latch), latch, "Unknown latch mode.");
        if (!Enum.IsDefined(switchOn)) throw new ArgumentOutOfRangeException(nameof(switchOn), switchOn, "Unknown switch mode.");

        var follower = new ChordFollower(this, settle, latch, switchOn);
        return new((arc, output) => follower.Query(arc, output));
    }

    /// <summary>
    /// Folds the stream's events into a state with a function of your own and exposes it as a live pattern: one event per cycle holding
    /// the state as it is now. This is the reduce of the stream (a count of keys down, the last velocity, a chord of your own making).
    /// </summary>
    /// <typeparam name="TState">The state.</typeparam>
    /// <param name="seed">The state before the first event.</param>
    /// <param name="step">Called for every start and end with the state so far; returns the new state. Runs on the thread that queries the pattern.</param>
    /// <remarks>Like <see cref="Chord"/>, a live pattern that cannot be written out. Graph-mode scripts cannot call this.</remarks>
    public Pattern<TState> Fold<TState>(TState seed, Func<TState, NoteEvent, TState> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var follower = new FoldFollower<TState>(this, seed, step);
        return new((arc, output) => follower.Query(arc, output));
    }
}

public static partial class NotePatterns
{
    /// <summary>
    /// Plays a pattern of notes as a stream, so it can go through the same operators as live notes (<c>Transpose</c>, <c>Echo</c>,
    /// <c>Merge</c> with a keyboard) and out of the same jack. The notes start on the host's beat grid and end when their events do.
    /// </summary>
    /// <param name="pattern">The notes. Needs a <see cref="HostClock"/> on the runner that plays the stream, and plays nothing while it is stopped.</param>
    public static NoteStream ToStream(this Pattern<Note> pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        pattern.Validate();
        return new("Pattern", _ => new PatternSourceStage(pattern));
    }
}

/// <summary>Which patterns follow live notes: such a pattern answers for now, so it cannot be written out as a score.</summary>
internal static class LiveQuery
{
    [ThreadStatic]
    private static bool s_touched;

    /// <summary>Called by a pattern that follows live notes whenever it is queried.</summary>
    public static void Touch() => s_touched = true;

    /// <summary>Starts watching for live patterns on this thread.</summary>
    public static void Begin() => s_touched = false;

    /// <summary>True if a live pattern was queried since <see cref="Begin"/>.</summary>
    public static bool Touched => s_touched;
}

/// <summary>Follows a stream's notes and publishes the chord they make; see <see cref="NoteStream.Chord"/>.</summary>
internal sealed class ChordFollower
{
    private const int MaxHeld = NoteStage.MaxSounding;

    private readonly object _gate = new();
    private readonly NoteStreamRunner _runner;
    private readonly double _settle;
    private readonly ChordLatch _latch;
    private readonly ChordSwitch _switch;
    private readonly long[] _ids = new long[MaxHeld];
    private readonly int[] _pitches = new int[MaxHeld];
    private int _count;
    private double _lastStart = double.NegativeInfinity;
    private bool _dirty;
    private volatile State _state = State.Empty;

    public ChordFollower(NoteStream stream, double settle, ChordLatch latch, ChordSwitch switchOn)
    {
        // The runner follows the input's clock, whenever the host gives it one; without one it counts for itself.
        _runner = stream.Compile();
        _settle = settle;
        _latch = latch;
        _switch = switchOn;
    }

    /// <summary>What is known about the chord at a time: the one in force, the one before it, and one waiting for its boundary.</summary>
    private sealed record State(Chord? Published, Chord? Previous, double PublishedAt, bool HasPending, Chord? Pending, double PendingAt)
    {
        public static State Empty { get; } = new(null, null, 0, false, null, 0);

        /// <summary>The chord in force at <paramref name="beat"/> on the host grid.</summary>
        public Chord? At(double beat)
        {
            if (HasPending && PendingAt <= beat)
                return Pending;
            return beat >= PublishedAt ? Published : Previous ?? Published;
        }
    }

    public void Query(Arc arc, List<PatternEvent<Chord>> output)
    {
        LiveQuery.Touch();
        Poll();
        var state = _state;
        double now = Now();

        foreach (var piece in arc.SplitCycles())
        {
            var whole = new Arc(new Fraction(piece.Begin.Floor()), new Fraction(piece.Begin.Floor() + 1));
            // A bar-long event is written against the chord in force when the bar began; the others against the one in force now.
            double beat = _switch == ChordSwitch.Cycle ? whole.Begin.ToDouble() * Pat.BeatsPerCycle : now;
            var chord = state.At(beat);
            if (chord is not null)
                output.Add(new PatternEvent<Chord>(whole, piece, chord));
        }
    }

    private double Now() => _runner.CurrentClock?.Beats ?? _runner.GridBeats;

    private bool Running => _runner.CurrentClock?.Running ?? true;

    // Catches up with the notes that arrived. If another thread is already doing it, the state it publishes is current enough.
    private void Poll()
    {
        if (!Monitor.TryEnter(_gate))
            return;
        try
        {
            foreach (var e in _runner.Poll())
            {
                if (e.Kind == NoteEventKind.On)
                    Add(e);
                else
                    Remove(e.Id);
            }

            double seconds = _runner.Seconds;
            if (_dirty && seconds - _lastStart >= _settle)
            {
                _dirty = false;
                Settle();
            }

            Apply();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private void Add(in NoteEvent e)
    {
        _lastStart = _runner.Seconds;
        _dirty = true;
        if (_count < MaxHeld)
        {
            _ids[_count] = e.Id;
            _pitches[_count++] = e.Pitch;
        }
    }

    private void Remove(long id)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_ids[i] != id)
                continue;
            _count--;
            _ids[i] = _ids[_count];
            _pitches[i] = _pitches[_count];
            // Only a gated chord cares that keys came up.
            if (_latch == ChordLatch.Gated)
                _dirty = true;
            return;
        }
    }

    // The keys down once a group of presses is over become the chord to switch to.
    private void Settle()
    {
        var state = _state;
        Chord? candidate = _count == 0 ? null : new Chord(_pitches.Take(_count), "held");
        if (candidate is null && _latch == ChordLatch.Infinite)
            return;

        var target = state.HasPending ? state.Pending : state.Published;
        if (candidate is null ? target is null : target is not null && candidate.Pitches.SequenceEqual(target.Pitches))
            return;

        double grid = Now();
        bool immediate = _switch == ChordSwitch.Instant || (state.Published is null && !state.HasPending) || !Running;
        double at = immediate ? grid : _switch == ChordSwitch.Beat ? Math.Floor(grid) + 1 : (Math.Floor(grid / Pat.BeatsPerCycle) + 1) * Pat.BeatsPerCycle;
        _state = state with { HasPending = true, Pending = candidate, PendingAt = at };
    }

    // A waiting chord takes over once its boundary has passed, or at once while the transport is stopped.
    private void Apply()
    {
        var state = _state;
        if (!state.HasPending)
            return;

        double grid = Now();
        bool stopped = !Running;
        if (state.PendingAt <= grid || stopped)
        {
            // While the transport is stopped there is no bar line to wait for, and the bar the clock sits in takes the new chord whole.
            double at = stopped ? double.NegativeInfinity : state.PendingAt;
            _state = new State(state.Pending, state.Published, at, false, null, 0);
        }
    }
}

/// <summary>Follows a stream and folds its events into a state; see <see cref="NoteStream.Fold"/>.</summary>
internal sealed class FoldFollower<TState>
{
    private readonly object _gate = new();
    private readonly NoteStreamRunner _runner;
    private readonly Func<TState, NoteEvent, TState> _step;
    private volatile object? _state;

    public FoldFollower(NoteStream stream, TState seed, Func<TState, NoteEvent, TState> step)
    {
        _runner = stream.Compile(null);
        _step = step;
        _state = seed;
    }

    public void Query(Arc arc, List<PatternEvent<TState>> output)
    {
        LiveQuery.Touch();
        if (Monitor.TryEnter(_gate))
        {
            try
            {
                var state = (TState)_state!;
                foreach (var e in _runner.Poll())
                    state = _step(state, e);
                _state = state;
            }
            finally
            {
                Monitor.Exit(_gate);
            }
        }

        var current = (TState)_state!;
        foreach (var piece in arc.SplitCycles())
        {
            long cycle = piece.Begin.Floor();
            output.Add(new PatternEvent<TState>(new Arc(new Fraction(cycle), new Fraction(cycle + 1)), piece, current));
        }
    }
}
