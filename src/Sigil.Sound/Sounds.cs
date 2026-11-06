using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>How a <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/> plays once it reaches its loop end, or the end of the sample.</summary>
public enum PlaybackMode
{
    /// <summary>Plays the sample once, from its first frame to its last, then falls silent.</summary>
    OneShot,

    /// <summary>Plays the loop region over and over: at the loop end it jumps back to the loop start.</summary>
    Loop,

    /// <summary>Plays the loop region forward, then backward, then forward again, without a jump at either end.</summary>
    PingPong,
}


/// <summary>
/// Sources: the starting points of a sound. Designed for <c>using static Sigil.Sound.Sounds;</c>.
/// </summary>
public static class Sounds
{
    /// <summary>A sine wave. <paramref name="frequency"/> is in Hz.</summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> for vibrato and sweeps. Also used as an LFO below 20 Hz. Defaults to 440 (A4).</param>
    /// <example><code>Sine(440)</code></example>
    public static Sound Sine(Param? frequency = null)
    {
        var pitch = frequency ?? 440f;
        return Sound.FromOperator(ctx => new SineOperator(pitch.Compile(ctx), ctx), $"Sine({pitch})");
    }

    /// <summary>An anti-aliased (PolyBLEP) sawtooth wave. <paramref name="frequency"/> is in Hz.</summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> to modulate it. Defaults to 440 (A4).</param>
    /// <example><code>Saw(110).LowPass(800)</code></example>
    public static Sound Saw(Param? frequency = null)
    {
        var pitch = frequency ?? 440f;
        return Sound.FromOperator(ctx => new SawOperator(pitch.Compile(ctx), ctx), $"Saw({pitch})");
    }

    /// <summary>
    /// An anti-aliased (PolyBLEP) pulse wave. <paramref name="width"/> is the duty cycle in (0, 1); 0.5 is a square.
    /// </summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> to modulate it. Defaults to 440 (A4).</param>
    /// <param name="width">Fraction of each cycle spent high, from 0.01 to 0.99. Modulate it for pulse-width movement. Defaults to 0.5.</param>
    /// <example><code>Square(110, Sine(0.3).Range(0.1, 0.9))</code></example>
    public static Sound Square(Param? frequency = null, Param? width = null)
    {
        var pitch = frequency ?? 440f;
        var duty = width ?? 0.5f;
        return Sound.FromOperator(ctx => new SquareOperator(pitch.Compile(ctx), duty.Compile(ctx), ctx),
            $"Square({pitch})");
    }

    /// <summary>White noise, independent per channel. The same <paramref name="seed"/> renders the same noise.</summary>
    /// <param name="seed">Starting value of the generator; change it for different noise. Defaults to 1.</param>
    /// <example><code>Noise().LowPass(1000) * 0.2</code></example>
    public static Sound Noise(int seed = 1) =>
        Sound.FromOperator(_ => new NoiseOperator(seed), "Noise()");

    /// <summary>
    /// Plays a recorded or rendered sample, such as a drum hit or a loop. Speed sets the pitch: 2 is an octave up and 0.5 an
    /// octave down. A negative speed plays backward: a one-shot starts at its last frame and runs back to its first. Blending
    /// between frames keeps fractional speeds smooth.
    /// </summary>
    /// <remarks>
    /// The sample is held in memory and read without allocating, so a single <see cref="AudioBuffer"/> can back many voices.
    /// Its own sample rate is converted to the render rate, so a 44.1 kHz file plays at the right pitch in a 48 kHz render.
    /// For a sampler instrument, pitch each note by its frequency over the sample's root:
    /// <c>Sample(kick, speed: v.Frequency / 261.63)</c> plays a sample recorded at C4 at the pitch of each played note.
    /// </remarks>
    /// <param name="buffer">The audio to play, usually from <see cref="AudioBuffer.FromWav(Stream)"/> or a rendered sound.</param>
    /// <param name="speed">Playback rate: 1 is the original pitch and speed. A number, or a sound or <c>Fn(t => ...)</c> to bend or vibrato it. Defaults to 1.</param>
    /// <param name="mode">How the sample plays: once, looped, or ping-ponged. Defaults to <see cref="PlaybackMode.OneShot"/>.</param>
    /// <param name="loopStart">Where the loop begins, in seconds from the start of the sample. Used only by <see cref="PlaybackMode.Loop"/> and <see cref="PlaybackMode.PingPong"/>. Defaults to 0.</param>
    /// <param name="loopEnd">Where the loop ends, in seconds from the start of the sample. Defaults to null, the end of the sample. Must be after <paramref name="loopStart"/>.</param>
    /// <exception cref="ArgumentException">The buffer has no frames.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The loop markers are outside the sample, or in the wrong order.</exception>
    /// <example>
    /// <code>
    /// // One hit at the recorded pitch.
    /// Sample(Samples.Load("pluck.wav"))
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// // A loop an octave down.
    /// Sample(Samples.Load("pluck.wav"), speed: 0.5, mode: PlaybackMode.Loop) * 0.5
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// // Ping-pong between two points of the sample: forward from 0.05 s to 0.4 s, then backward, and so on.
    /// Sample(Samples.Load("pluck.wav"), mode: PlaybackMode.PingPong, loopStart: 0.05, loopEnd: 0.4) * 0.5
    /// </code>
    /// </example>
    public static Sound Sample(AudioBuffer buffer, Param? speed = null, PlaybackMode mode = PlaybackMode.OneShot,
        double loopStart = 0, double? loopEnd = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.FrameCount == 0)
            throw new ArgumentException("The sample has no frames to play.", nameof(buffer));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown playback mode.");

        double end = loopEnd ?? buffer.Duration;
        if (double.IsNaN(loopStart) || loopStart < 0 || loopStart >= end)
            throw new ArgumentOutOfRangeException(nameof(loopStart), loopStart, "The loop must start at or after 0 and before it ends.");
        if (end > buffer.Duration)
            throw new ArgumentOutOfRangeException(nameof(loopEnd), loopEnd, "The loop cannot end after the sample.");

        var rate = speed ?? 1f;
        var frames = buffer.Storage;
        int fileRate = buffer.SampleRate;
        return Sound.FromOperator(ctx => new SampleOperator(frames, fileRate, rate.Compile(ctx), mode, loopStart, end, ctx),
            $"Sample({buffer.Duration:0.###}s, {mode})");
    }

    /// <summary>
    /// Plays a sample at a pitch: the sample sounds at <paramref name="rootFrequency"/> when played at that frequency, and
    /// every other frequency transposes it by the ratio. Use it to play a sample as an instrument, one note at a time.
    /// </summary>
    /// <remarks>
    /// This is <c>Sample(buffer, speed: frequency / rootFrequency)</c>, written out for the common case. The root frequency is
    /// required so that a call with a single number cannot be mistaken for a speed.
    /// </remarks>
    /// <param name="buffer">The audio to play, usually from <see cref="AudioBuffer.FromWav(string)"/>.</param>
    /// <param name="frequency">The pitch to play, in Hz: a number, or a sound such as a voice's <c>v.Frequency</c>.</param>
    /// <param name="rootFrequency">The pitch the sample was recorded at, in Hz; for example 261.63 for a sample recorded at middle C.</param>
    /// <param name="mode">How the sample plays: once, looped, or ping-ponged. Defaults to <see cref="PlaybackMode.OneShot"/>.</param>
    /// <param name="loopStart">Where the loop begins, in seconds from the start of the sample. Defaults to 0.</param>
    /// <param name="loopEnd">Where the loop ends, in seconds from the start of the sample. Defaults to null, the end of the sample.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rootFrequency"/> is not positive, or the loop markers are invalid.</exception>
    /// <example>
    /// <code>
    /// // Plays the pluck at the pitch of each note; the pluck was recorded at 261.63 Hz (C4).
    /// Notes.Parse("C4 E4 G4 C5").Every(Beats.Quarter).Loop()
    ///     .Play(v => Sample(Samples.Load("pluck.wav"), v.Frequency, 261.63), bpm: 120) * 0.5
    /// </code>
    /// </example>
    public static Sound Sample(AudioBuffer buffer, Param frequency, double rootFrequency, PlaybackMode mode = PlaybackMode.OneShot,
        double loopStart = 0, double? loopEnd = null)
    {
        if (double.IsNaN(rootFrequency) || rootFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(rootFrequency), rootFrequency, "The root frequency must be positive.");

        return Sample(buffer, FromParam(frequency) * (1.0 / rootFrequency), mode, loopStart, loopEnd);
    }

    /// <summary>
    /// A band-limited triangle wave: a straight ramp up and a straight ramp down, from -1 to 1. Its corners are rounded so
    /// high pitches stay clean. Use it as a source, or at low rates as a bipolar modulator for <c>Range</c>.
    /// </summary>
    /// <remarks>
    /// Skew sets where the peak falls in each cycle. Skew 0.5 is symmetric, and a skew of 0.8 gives a slow rise and a quick
    /// fall. Skew 0 and 1 are limited to 0.01 and 0.99. A true saw needs a jump, and a triangle has none, so the extreme
    /// skews are a very steep ramp rather than a saw.
    /// </remarks>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> to modulate it. Defaults to 440 (A4).</param>
    /// <param name="skew">Where the peak falls in the cycle, from 0 to 1. 0.5 is symmetric. Defaults to 0.5.</param>
    /// <exception cref="ArgumentOutOfRangeException">The constant skew is outside 0 to 1.</exception>
    /// <example><code>Triangle(220, skew: 0.5) * 0.3</code></example>
    /// <example><code>Sine(440) * Triangle(4, skew: 0.8).Range(0.9, 1.0)   // a tremolo that ramps up and snaps back</code></example>
    public static Sound Triangle(Param? frequency = null, Param? skew = null)
    {
        var pitch = frequency ?? 440f;
        var curve = skew ?? 0.5f;
        if (curve.IsConstant && curve.ConstantValue is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(skew), curve.ConstantValue, "The skew must be between 0 and 1.");

        return Sound.FromOperator(ctx => new TriangleOperator(pitch.Compile(ctx), curve.Compile(ctx), ctx),
            $"Triangle({pitch}, {curve})");
    }

    /// <summary>
    /// A sound whose value is a function of time in seconds, such as <c>Fn(t => 200 + 100 * t)</c>.
    /// Use it wherever a <see cref="Param"/> is expected; C# cannot convert a bare lambda to <see cref="Param"/>.
    /// The function runs on the audio thread once per frame, so it must not allocate.
    /// </summary>
    /// <param name="function">
    /// Maps time in seconds since the sound started to a value in [-1, 1] when played, or to any number when used as a parameter.
    /// When the render context has a <see cref="HostClock"/>, time is the host's instead, so a replacement sound carries on from where the old one was.
    /// Inside a note voice it is always the time since the note began.
    /// </param>
    /// <example><code>Sine(Fn(t => 200 + 100 * t))   // a rising pitch</code></example>
    public static Sound Fn(Func<double, double> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Sound.FromOperator(ctx => new FunctionOperator(function, ctx), "Fn(t)");
    }

    /// <summary>
    /// Like <see cref="Fn(Func{double, double})"/>, but evaluated at the chosen rate. <see cref="FunctionRate.PerMillisecond"/>
    /// calls the function about once a millisecond and draws a straight line between the answers: far cheaper for slow
    /// movement such as a stepped melody, filter sweep or envelope, and the same at any block size.
    /// </summary>
    /// <param name="function">Maps time in seconds to a value; see <see cref="Fn(Func{double, double})"/>.</param>
    /// <param name="rate">How often the function runs.</param>
    /// <example>
    /// <code>
    /// // A cutoff that is recomputed once per millisecond instead of once per sample: cheaper, and smooth enough for a sweep.
    /// Saw(110).LowPass(Fn(t => 200 + 4000 * Math.Abs(Math.Sin(t)), FunctionRate.PerMillisecond)) * 0.3
    /// </code>
    /// </example>
    public static Sound Fn(Func<double, double> function, FunctionRate rate)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!Enum.IsDefined(rate)) throw new ArgumentOutOfRangeException(nameof(rate), rate, "Unknown rate.");
        return rate == FunctionRate.PerSample
            ? Fn(function)
            : Sound.FromOperator(ctx => new FunctionOperator(function, ctx, Math.Max(1, (int)Math.Round(ctx.SampleRate * 0.001))), "Fn(t, 1 ms)");
    }

    /// <summary>A constant signal (DC). Mostly useful as a building block.</summary>
    /// <param name="value">The level to output.</param>
    public static Sound Constant(double value) =>
        Sound.FromOperator(_ => new ConstantOperator((float)value), $"Constant({value})");

    // A parameter as a sound, for arithmetic such as sound + 12.
    internal static Sound FromParam(Param param) =>
        Sound.FromBackingParam(param, ctx => new ParamOperator(param.Compile(ctx)));

    /// <summary>Silence.</summary>
    public static Sound Silence => Constant(0);
}

/// <summary>How often <see cref="Sounds.Fn(Func{double, double}, FunctionRate)"/> runs its function.</summary>
public enum FunctionRate
{
    /// <summary>Every frame: exact, and the most expensive.</summary>
    PerSample,

    /// <summary>About once a millisecond, with a straight line between the answers.</summary>
    PerMillisecond,
}
