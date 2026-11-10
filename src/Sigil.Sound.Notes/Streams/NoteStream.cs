namespace Sigil.Sound;

/// <summary>Turns one note into several; see <see cref="NoteStream.SelectMany"/>.</summary>
/// <param name="note">The note that started.</param>
/// <param name="emit">Where to add the notes it becomes. Valid only during the call.</param>
public delegate void NoteExpander(NoteEvent note, NoteEmitter emit);

/// <summary>
/// Notes as they happen: a description of live notes (from a keyboard, a device, a pattern) and what is done to them on the way to
/// an instrument or the next device. Where a <see cref="Pattern{T}"/> is a score, a function of time, a stream is a performance:
/// its notes start when a key goes down and end when it comes up, and each has an identity that every operator keeps.
/// </summary>
/// <remarks>
/// <para>
/// A stream is immutable, like a <see cref="Sound"/>: every operator returns a new one, and nothing runs until something plays it
/// (<see cref="Play"/>), sends it on (<see cref="Compile"/>) or reads it (<see cref="Chord"/>, <see cref="Gate"/>). Start from
/// <see cref="NoteInput.Stream"/>; <c>pattern.ToStream()</c> turns a score into one.
/// </para>
/// <para>
/// <b>Notes always end.</b> Whatever an operator does, the end of a note ends exactly the notes it made from it, even a raw function
/// of your own (<see cref="Select"/>, <see cref="SelectMany"/>), and everything still sounding is ended when the stream is reset,
/// replaced or disposed. Capacities are fixed (128 notes sounding per step, 512 events a block); past them a note's start is dropped
/// and its end never sent, never the other way round.
/// </para>
/// <para>
/// <b>Time.</b> Operators that work in beats (<see cref="Delay"/>, <see cref="Echo"/>, <see cref="Strum"/>) use the tempo of the
/// <see cref="HostClock"/> of the input (or 120 bpm without one) and keep running while the transport is stopped. Events are quantized
/// to the block they fall in, under 6 ms at the default block size.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var keys = new NoteInput();
/// // Every key you press sounds with a fifth above it, and twice more a beat later, softer.
/// var plucks = keys.Stream.Harmonize(0, 7).Echo(2, Beats.Quarter, 0.5).Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.003, 0.2, 0, 0.2));
/// keys.NoteOn("c3 e3");
/// plucks * 0.3
/// </code>
/// </example>
public sealed partial class NoteStream
{
    private readonly Func<StageBuilder, NoteStage> _build;
    private readonly Func<HostClock?>? _clock;

    internal NoteStream(string name, Func<StageBuilder, NoteStage> build, Func<HostClock?>? clock = null)
    {
        Name = name;
        _build = build;
        _clock = clock;
    }

    internal string Name { get; }

    /// <summary>The clock of the <see cref="NoteInput"/> this stream comes from, if it has one. Read when needed, so a host may set it after the stream is built.</summary>
    internal HostClock? Clock => _clock?.Invoke();

    /// <summary>A stream with no notes.</summary>
    public static NoteStream Silence { get; } = new("Silence", _ => new SilenceStage());

    internal static NoteStream FromInput(NoteInput input) => new("Note input", _ => new InputStage(input), () => input.Clock);

    internal NoteStage Build(StageBuilder builder) => builder.For(this);

    internal NoteStage Make(StageBuilder builder) => _build(builder);

    // The common shape of an operator: a new stream whose stage reads this one's.
    private NoteStream Then(string name, Func<NoteStage, NoteStage> make) => new(name, b => make(b.For(this)), _clock);

    private NoteStream Transform(string name, NoteTransform transform) => Then(name, input => new TransformStage(input, transform));

    /// <summary>
    /// Builds the stream so a host can run it block by block and read the notes that come out: the way to send a stream to a device's
    /// Notes out jack. Dispose the runner when done; it stops listening to the input.
    /// </summary>
    /// <param name="clock">The clock the host advances, which gives beats their tempo and the grid its position. Null uses the <see cref="NoteInput.Clock"/> of the input, and without one 120 bpm and a clock of the runner's own.</param>
    /// <param name="sampleRate">Frames per second of the blocks passed to <see cref="NoteStreamRunner.Process"/>.</param>
    public NoteStreamRunner Compile(HostClock? clock = null, int sampleRate = RenderContext.DefaultSampleRate) =>
        new(this, () => clock ?? Clock, sampleRate);

    // --- Pitch -----------------------------------------------------------------------------------------------------------

    /// <summary>Moves every note by <paramref name="semitones"/>. A note that would leave the MIDI range is dropped.</summary>
    /// <param name="semitones">How far to move, up (positive) or down.</param>
    public NoteStream Transpose(int semitones) => Transform("Transpose", (ref NoteEvent n) =>
    {
        n = n with { Pitch = n.Pitch + semitones };
        return true;
    });

    /// <summary>Moves every note by <paramref name="octaves"/> octaves. A note that would leave the MIDI range is dropped.</summary>
    /// <param name="octaves">How many octaves up (positive) or down.</param>
    public NoteStream Octave(int octaves) => Transpose(octaves * 12);

    /// <summary>Moves every note by <paramref name="cents"/> hundredths of a semitone, on top of any detune it already has.</summary>
    /// <param name="cents">How far to move, up (positive) or down.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not a number.</exception>
    public NoteStream Detune(double cents)
    {
        if (!double.IsFinite(cents)) throw new ArgumentOutOfRangeException(nameof(cents), cents, "Cents must be a number.");
        return Transform("Detune", (ref NoteEvent n) =>
        {
            double total = n.Cents + cents;
            int shift = (int)Math.Round(total / 100.0);
            n = n with { Pitch = n.Pitch + shift, Cents = (float)(total - shift * 100.0) };
            return true;
        });
    }

    /// <summary>Keeps every note between <paramref name="low"/> and <paramref name="high"/>, moving or dropping the ones outside as <paramref name="mode"/> says.</summary>
    /// <param name="low">The lowest MIDI pitch allowed, 0 to 127.</param>
    /// <param name="high">The highest MIDI pitch allowed, at least <paramref name="low"/>.</param>
    /// <param name="mode">What happens to a note outside. Defaults to dropping it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside 0 to 127, or ends before it starts.</exception>
    public NoteStream Range(int low, int high, RangeMode mode = RangeMode.Drop)
    {
        if (low < 0 || high > 127 || low > high) throw new ArgumentOutOfRangeException(nameof(low), low, "The range must lie inside 0 to 127 and end after it starts.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown range mode.");
        return Transform("Range", (ref NoteEvent n) =>
        {
            int p = n.Pitch;
            if (p >= low && p <= high)
                return true;

            switch (mode)
            {
                case RangeMode.Clamp:
                    p = Math.Clamp(p, low, high);
                    break;
                case RangeMode.Wrap:
                    while (p < low) p += 12;
                    while (p > high) p -= 12;
                    break;
                case RangeMode.Fold:
                    for (int i = 0; i < 16 && (p < low || p > high); i++)
                        p = p > high ? 2 * high - p : 2 * low - p;
                    break;
            }

            if (p < low || p > high)
                return false;
            n = n with { Pitch = p, Cents = mode == RangeMode.Clamp || mode == RangeMode.Fold ? 0f : n.Cents };
            return true;
        });
    }

    /// <summary>Moves every note to a tone of <paramref name="scale"/>, in any octave. A note already in the scale is untouched; the cents of a moved note are cleared.</summary>
    /// <param name="scale">The tones allowed, such as <c>Scale.Parse("d dorian")</c>.</param>
    /// <param name="mode">Which tone a note between two goes to. Defaults to the closest.</param>
    public NoteStream Quantize(Scale scale, QuantizeMode mode = QuantizeMode.Nearest)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown quantize mode.");
        var allowed = PitchClasses(scale);
        return Transform("Quantize", (ref NoteEvent n) =>
        {
            if (allowed[Class(n.Pitch)])
                return true;

            for (int distance = 1; distance <= 12; distance++)
            {
                if (mode != QuantizeMode.Up && allowed[Class(n.Pitch - distance)])
                {
                    n = n with { Pitch = n.Pitch - distance, Cents = 0f };
                    return true;
                }

                if (mode != QuantizeMode.Down && allowed[Class(n.Pitch + distance)])
                {
                    n = n with { Pitch = n.Pitch + distance, Cents = 0f };
                    return true;
                }
            }

            return false;
        });
    }

    // --- Dynamics --------------------------------------------------------------------------------------------------------

    /// <summary>Plays every note at the same velocity.</summary>
    /// <param name="velocity">Above 0 up to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not above 0 and at most 1.</exception>
    public NoteStream Velocity(double velocity)
    {
        if (!(velocity > 0 && velocity <= 1)) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity must be above 0 and at most 1.");
        float value = (float)velocity;
        return Transform("Velocity", (ref NoteEvent n) =>
        {
            n = n with { Velocity = value };
            return true;
        });
    }

    /// <summary>Multiplies every velocity by <paramref name="factor"/>; the result stays between 1/127 and 1 so a note never turns silent.</summary>
    /// <param name="factor">0 or more; 0.5 halves the velocity.</param>
    /// <exception cref="ArgumentOutOfRangeException">Negative or not a number.</exception>
    public NoteStream ScaleVelocity(double factor)
    {
        if (!(factor >= 0) || !double.IsFinite(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be 0 or more.");
        return Transform("ScaleVelocity", (ref NoteEvent n) =>
        {
            n = n with { Velocity = (float)Math.Clamp(n.Velocity * factor, 1.0 / 127, 1.0) };
            return true;
        });
    }

    /// <summary>Keeps every velocity between <paramref name="low"/> and <paramref name="high"/>.</summary>
    /// <param name="low">The softest velocity allowed, above 0.</param>
    /// <param name="high">The hardest velocity allowed, at most 1 and at least <paramref name="low"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside 0 (exclusive) to 1, or ends before it starts.</exception>
    public NoteStream ClampVelocity(double low, double high)
    {
        if (!(low > 0 && high <= 1 && low <= high)) throw new ArgumentOutOfRangeException(nameof(low), low, "The range must lie inside 0 (exclusive) to 1 and end after it starts.");
        return Transform("ClampVelocity", (ref NoteEvent n) =>
        {
            n = n with { Velocity = (float)Math.Clamp(n.Velocity, low, high) };
            return true;
        });
    }

    /// <summary>Reshapes the velocity response: velocity raised to <paramref name="exponent"/>, so above 1 favors loud playing and below 1 evens it out.</summary>
    /// <param name="exponent">Above 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">Not above 0.</exception>
    public NoteStream VelocityCurve(double exponent)
    {
        if (!(exponent > 0) || !double.IsFinite(exponent)) throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "The exponent must be above 0.");
        return Transform("VelocityCurve", (ref NoteEvent n) =>
        {
            n = n with { Velocity = (float)Math.Clamp(Math.Pow(n.Velocity, exponent), 1.0 / 127, 1.0) };
            return true;
        });
    }

    // --- Filter and routing ----------------------------------------------------------------------------------------------

    /// <summary>Lets through only the notes that match <paramref name="pick"/>.</summary>
    /// <param name="pick">What to keep, such as <c>Pick.Pitch(60, 127)</c>.</param>
    public NoteStream Keep(NotePick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        return Transform("Keep", (ref NoteEvent n) => pick.Matches(n));
    }

    /// <summary>Removes the notes that match <paramref name="pick"/>.</summary>
    /// <param name="pick">What to remove, such as <c>Pick.Velocity(0, 0.2)</c>.</param>
    public NoteStream Drop(NotePick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        return Transform("Drop", (ref NoteEvent n) => !pick.Matches(n));
    }

    /// <summary>Tags every note with a channel, for <see cref="Pick.Channel"/> further on.</summary>
    /// <param name="channel">0 or more.</param>
    /// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
    public NoteStream Channel(int channel)
    {
        if (channel < 0) throw new ArgumentOutOfRangeException(nameof(channel), channel, "The channel must be 0 or more.");
        return Transform("Channel", (ref NoteEvent n) =>
        {
            n = n with { Channel = channel };
            return true;
        });
    }

    /// <summary>
    /// This stream and the others as one. Each keeps its notes; their ids are made distinct so two streams can sound the same pitch at
    /// once and each ends its own.
    /// </summary>
    /// <param name="others">The streams to add, at least one.</param>
    public NoteStream Merge(params NoteStream[] others)
    {
        ArgumentNullException.ThrowIfNull(others);
        if (others.Length == 0) throw new ArgumentException("Merge needs at least one other stream.", nameof(others));
        if (others.Any(s => s is null)) throw new ArgumentException("A stream is null.", nameof(others));
        var all = new NoteStream[others.Length + 1];
        all[0] = this;
        others.CopyTo(all, 1);
        return new("Merge", b => new MergeStage([.. all.Select(s => b.For(s))]), () => all.Select(s => s.Clock).FirstOrDefault(c => c is not null));
    }

    // --- One note into several -------------------------------------------------------------------------------------------

    /// <summary>
    /// Plays each note and the notes <paramref name="semitones"/> away from it: <c>Harmonize(0, 7)</c> is the note and its fifth,
    /// <c>Harmonize(-12)</c> only the note an octave down. Include 0 to keep the original.
    /// </summary>
    /// <param name="semitones">One to eight distances from the note.</param>
    /// <exception cref="ArgumentException">None, or more than eight.</exception>
    public NoteStream Harmonize(params int[] semitones)
    {
        ArgumentNullException.ThrowIfNull(semitones);
        if (semitones.Length is < 1 or > 8) throw new ArgumentException("Give one to eight intervals.", nameof(semitones));
        var intervals = (int[])semitones.Clone();
        return Then("Harmonize", input => new FanOutStage(input, (n, emit) =>
        {
            foreach (int interval in intervals)
                emit.Add(n with { Pitch = n.Pitch + interval });
        }));
    }

    /// <summary>
    /// Plays each note and the tones of <paramref name="scale"/> <paramref name="degrees"/> steps away from it (a diatonic harmony):
    /// in C major, <c>Harmonize(scale, 0, 2)</c> adds a third above every note. A note between two scale tones keeps its distance
    /// from the one below.
    /// </summary>
    /// <param name="scale">The scale the steps are counted in.</param>
    /// <param name="degrees">One to eight steps from the note's own degree; 0 is the note, 2 a third above, -3 a fourth below.</param>
    /// <exception cref="ArgumentException">None, or more than eight.</exception>
    public NoteStream Harmonize(Scale scale, params int[] degrees)
    {
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(degrees);
        if (degrees.Length is < 1 or > 8) throw new ArgumentException("Give one to eight degrees.", nameof(degrees));
        var steps = (int[])degrees.Clone();
        return Then("Harmonize", input => new FanOutStage(input, (n, emit) =>
        {
            int degree = DegreeAtOrBelow(scale, n.Pitch);
            int offset = n.Pitch - scale[degree];
            foreach (int step in steps)
                emit.Add(n with { Pitch = scale[degree + step] + offset });
        }));
    }

    /// <summary>Plays each note as the bottom of a chord of this shape: <c>Chordify(Chord.Major(0))</c> makes every note a major triad.</summary>
    /// <param name="shape">The chord, taken as the distances of its tones from its lowest tone.</param>
    public NoteStream Chordify(Chord shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var intervals = shape.Pitches.Select(p => p - shape.Pitches[0]).Take(NoteEmitter.Capacity).ToArray();
        return Then("Chordify", input => new FanOutStage(input, (n, emit) =>
        {
            foreach (int interval in intervals)
                emit.Add(n with { Pitch = n.Pitch + interval });
        }));
    }

    // --- Time ------------------------------------------------------------------------------------------------------------

    /// <summary>Plays every note later, start and end alike, so each keeps its length.</summary>
    /// <param name="beats">How much later, 0 to 64 beats (<see cref="Beats"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside 0 to 64.</exception>
    public NoteStream Delay(double beats)
    {
        CheckBeats(beats, nameof(beats), allowZero: true);
        return Then("Delay", input => new DelayStage(input, beats));
    }

    /// <summary>Repeats every note, each repeat softer and as long as the note itself.</summary>
    /// <param name="repeats">How many repeats, 1 to 16.</param>
    /// <param name="beats">The time between repeats, above 0 up to 8 beats.</param>
    /// <param name="decay">The velocity of each repeat as a fraction of the one before, 0 to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public NoteStream Echo(int repeats, double beats, double decay = 0.6)
    {
        if (repeats is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(repeats), repeats, "Repeats must be 1 to 16.");
        CheckBeats(beats, nameof(beats), allowZero: false, max: 8);
        if (!(decay is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(decay), decay, "Decay must be between 0 and 1.");
        return Then("Echo", input => new EchoStage(input, repeats, beats, decay));
    }

    /// <summary>
    /// Spreads notes that start together (a chord) one after the other, in the order they came, each <paramref name="beats"/> after the
    /// one before. Each keeps its length.
    /// </summary>
    /// <param name="beats">The gap between notes, above 0 up to 1 beat.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside the range.</exception>
    public NoteStream Strum(double beats)
    {
        CheckBeats(beats, nameof(beats), allowZero: false, max: 1);
        return Then("Strum", input => new StrumStage(input, beats));
    }

    /// <summary>Gives every note the same length, whatever the key does: it ends after <paramref name="beats"/>, held or not.</summary>
    /// <param name="beats">The length, above 0 up to 64 beats.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside the range.</exception>
    public NoteStream Length(double beats)
    {
        CheckBeats(beats, nameof(beats), allowZero: false);
        return Then("Length", input => new LengthStage(input, beats));
    }

    /// <summary>
    /// Makes the playing a little less exact: each note starts later by up to <paramref name="timing"/> beats and its velocity moves by up
    /// to <paramref name="velocity"/>. The amounts depend only on the seed and the note, so the same performance humanizes the same way.
    /// </summary>
    /// <param name="timing">The most a note is late, 0 to 1 beat.</param>
    /// <param name="velocity">The most a velocity changes either way, 0 to 1.</param>
    /// <param name="seed">Another seed gives another, equally repeatable, feel.</param>
    /// <exception cref="ArgumentOutOfRangeException">Outside the range.</exception>
    public NoteStream Humanize(double timing, double velocity = 0.1, int seed = 0)
    {
        CheckBeats(timing, nameof(timing), allowZero: true, max: 1);
        if (!(velocity is >= 0 and <= 1)) throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "Velocity must be between 0 and 1.");
        return Then("Humanize", input => new HumanizeStage(input, timing, velocity, seed));
    }

    // --- Voicing ---------------------------------------------------------------------------------------------------------

    /// <summary>One note at a time: of the keys held, only the one <paramref name="priority"/> picks sounds, and it changes as keys go down and up.</summary>
    /// <param name="priority">Which held key wins. Defaults to the last pressed.</param>
    public NoteStream Mono(NotePriority priority = NotePriority.Last)
    {
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        return Then("Mono", input => new MonoStage(input, priority));
    }

    /// <summary>Keeps notes sounding after their keys come up, until the next group of keys goes down.</summary>
    public NoteStream Latch() => Then("Latch", input => new LatchStage(input));

    // --- Raw: your own functions (Code mode) -----------------------------------------------------------------------------

    /// <summary>
    /// Changes each note with a function of your own. The function sees the note as it starts and returns the note to play; its
    /// <see cref="NoteEvent.Id"/> and kind are ignored, so the engine still ends the note when the key comes up.
    /// </summary>
    /// <param name="map">Called once per note, on the audio thread: it must not allocate, lock, or take long.</param>
    /// <remarks>A function that throws stops the stream; the host decides what that means. Graph-mode scripts cannot call this.</remarks>
    public NoteStream Select(Func<NoteEvent, NoteEvent> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Transform("Select", (ref NoteEvent n) =>
        {
            n = map(n);
            return true;
        });
    }

    /// <summary>Keeps the notes a function of your own says yes to. See <see cref="Select"/> for what the function may do.</summary>
    /// <param name="keep">Called once per note, on the audio thread.</param>
    public NoteStream Where(Func<NoteEvent, bool> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        return Transform("Where", (ref NoteEvent n) => keep(n));
    }

    /// <summary>Turns each note into none, one or several with a function of your own: add the notes to make to <c>emit</c>. See <see cref="Select"/>.</summary>
    /// <param name="expand">Called once per note, on the audio thread. At most <see cref="NoteEmitter.Capacity"/> notes are taken.</param>
    public NoteStream SelectMany(NoteExpander expand)
    {
        ArgumentNullException.ThrowIfNull(expand);
        return Then("SelectMany", input => new FanOutStage(input, (n, emit) => expand(n, emit)));
    }

    /// <summary>
    /// Carries a state through the stream's events, with a function of your own that sees every start and every end and may start notes.
    /// The notes it makes end when the note they came from ends.
    /// </summary>
    /// <typeparam name="TState">What the function remembers.</typeparam>
    /// <param name="seed">The state before the first event.</param>
    /// <param name="step">Called for each event with the state so far; returns the new state. Add notes to make to <c>emit</c> (only on a start).</param>
    public NoteStream Scan<TState>(TState seed, Func<TState, NoteEvent, NoteEmitter, TState> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Then("Scan", input => new ScanStage<TState>(input, seed, step));
    }

    // --- Output ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Plays the notes of the stream: each start sounds a voice built from <paramref name="voice"/> until its end. Like
    /// <see cref="NoteInput.Play"/>, but for notes that have been through operators. Its notes carry no bend, pressure or slide.
    /// </summary>
    /// <param name="voice">Describes the sound of one note from its <see cref="Voice"/> signals. Called while the sound is built, not per note.</param>
    /// <param name="polyphony">The most notes sounding at once, from 1 to 64. Defaults to 8.</param>
    public Sound Play(Func<Voice, Sound> voice, int polyphony = 8)
    {
        ArgumentNullException.ThrowIfNull(voice);
        if (polyphony is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(polyphony), polyphony, "Polyphony must be between 1 and 64.");
        return Sound.FromOperator(ctx => new StreamPlayOperator(this, voice, polyphony, ctx), "Note stream");
    }

    /// <summary>
    /// A control signal that is 1 while any note of the stream sounds and 0 otherwise: it lets a held key open a filter, gate an
    /// effect or trigger an envelope elsewhere. It steps; smooth it with a lowpass if you need a ramp.
    /// </summary>
    public Sound Gate() => Sound.FromOperator(ctx => new StreamGateOperator(this, ctx), "Note stream gate");

    // --- Helpers ---------------------------------------------------------------------------------------------------------

    private static void CheckBeats(double beats, string name, bool allowZero, double max = 64)
    {
        bool ok = double.IsFinite(beats) && beats <= max && (allowZero ? beats >= 0 : beats > 0);
        if (!ok)
            throw new ArgumentOutOfRangeException(name, beats, $"Beats must be {(allowZero ? "0" : "above 0")} up to {max}.");
    }

    private static bool[] PitchClasses(Scale scale)
    {
        var allowed = new bool[12];
        for (int degree = 0; degree < scale.Count; degree++)
            allowed[Class(scale[degree])] = true;
        return allowed;
    }

    private static int Class(int pitch) => ((pitch % 12) + 12) % 12;

    // The highest degree of the scale whose tone is at or below the pitch.
    private static int DegreeAtOrBelow(Scale scale, int pitch)
    {
        int degree = (int)Math.Floor((pitch - scale.Root) * (double)scale.Count / 12.0);
        while (scale[degree] > pitch) degree--;
        while (scale[degree + 1] <= pitch) degree++;
        return degree;
    }
}

/// <summary>The compile-time plumbing of a stream: which stage stands for which stream, so one stream used twice is one stage.</summary>
internal sealed class StageBuilder(HostClock? clock)
{
    private readonly Dictionary<NoteStream, NoteStage> _stages = new(ReferenceEqualityComparer.Instance);

    public HostClock? Clock { get; } = clock;

    /// <summary>Every stage built, inputs before the stages that read them.</summary>
    public List<NoteStage> Stages { get; } = [];

    public NoteStage For(NoteStream stream)
    {
        if (_stages.TryGetValue(stream, out var existing))
            return existing;

        var stage = stream.Make(this);
        stage.Salt = Stages.Count + 1;
        _stages[stream] = stage;
        Stages.Add(stage);
        return stage;
    }
}

/// <summary>A stage with no notes.</summary>
internal sealed class SilenceStage : NoteStage
{
    protected override void Compute(in BlockTime time)
    {
    }
}
