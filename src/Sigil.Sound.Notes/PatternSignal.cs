using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>Turning a pattern of numbers into a signal that drives a parameter.</summary>
public static class PatternSignals
{
    /// <summary>
    /// A pattern of numbers as a signal: each value holds for the length of its step, in time with the music. Use it wherever a
    /// <see cref="Param"/> goes, which replaces stepping through an array inside <c>Fn(t => ...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rest (<c>~</c>) holds the previous value rather than dropping to zero, so a filter stays where it was.
    /// Time is bound to the music the same way as <see cref="Patterns.Play"/>: with a <see cref="HostClock"/> on the render context and no
    /// <paramref name="bpm"/>, the pattern follows the host's beat position and tempo (and stays on the grid when the sound is
    /// replaced); otherwise it counts from when it starts, at <paramref name="bpm"/> (120 if not given). One cycle is
    /// <see cref="Pat.BeatsPerCycle"/> beats.
    /// </para>
    /// <para>The pattern is asked once per block and the answer does not depend on the block size.</para>
    /// </remarks>
    /// <param name="pattern">The numbers, such as <c>Mini.Numbers("400 800 1600 3200")</c>.</param>
    /// <param name="bpm">The tempo, or null to follow the host clock (or 120 without one).</param>
    /// <param name="smoothSeconds">
    /// Glide time between values; after this long a jump is about 63% done. 0 steps instantly, which can click on gain,
    /// pitch and cutoff. Defaults to 0.
    /// </param>
    /// <param name="initial">The value before the pattern has produced any. Defaults to 0.</param>
    /// <example><code>Saw(110).LowPass(Mini.Numbers("400 800 1600 3200").ToSignal(smoothSeconds: 0.02), q: 4)</code></example>
    public static Sound ToSignal(this Pattern<double> pattern, double? bpm = null, double smoothSeconds = 0, double initial = 0)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        pattern.Validate();
        if (bpm is { } tempo && !(double.IsFinite(tempo) && tempo > 0)) throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "The tempo must be positive.");
        if (!double.IsFinite(smoothSeconds) || smoothSeconds < 0) throw new ArgumentOutOfRangeException(nameof(smoothSeconds), smoothSeconds, "The glide time must be zero or more.");
        if (!double.IsFinite(initial)) throw new ArgumentOutOfRangeException(nameof(initial), initial, "The starting value must be a number.");

        return Sound.FromOperator(ctx => new PatternSignalOperator(pattern, bpm, (float)smoothSeconds, initial, ctx), "PatternSignal");
    }
}

internal sealed class PatternSignalOperator : IOperator, IResettable
{
    private const double DefaultBpm = 120;

    private readonly Pattern<double> _pattern;
    private readonly double? _bpm;
    private readonly HostClock? _clock;
    private readonly int _sampleRate;
    private readonly float _smoothSeconds;
    private readonly double _initial;
    private readonly SmoothingCoefficient _coefficient;
    private readonly List<PatternEvent<double>> _events = new(64);

    private long _position;
    private bool _failed;
    private double _held;
    private double _smoothed;

    // Where the last query ended. The next one starts exactly there when the clock is continuous, so no instant is asked
    // twice or skipped because two floating-point routes to the same moment differ in the last digit.
    private Fraction _lastEnd;
    private double _lastEndCycles = double.NaN;

    public PatternSignalOperator(Pattern<double> pattern, double? bpm, float smoothSeconds, double initial, RenderContext context)
    {
        _pattern = pattern;
        _bpm = bpm;
        _clock = bpm is null ? context.TimeClock : null;
        _sampleRate = context.SampleRate;
        _smoothSeconds = smoothSeconds;
        _initial = initial;
        _coefficient = new SmoothingCoefficient(context.SampleRate);
        Reset();
    }

    public void Reset()
    {
        _position = 0;
        _held = _initial;
        _smoothed = _initial;
        _lastEndCycles = double.NaN;
        _failed = false;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        double bpm = _bpm ?? _clock?.Bpm ?? DefaultBpm;
        double cyclesPerFrame = bpm / 60.0 / _sampleRate / Pat.BeatsPerCycle;
        double blockStart = _clock is { } clock ? clock.Beats / Pat.BeatsPerCycle : _position * cyclesPerFrame;
        double blockEnd = blockStart + block.Length * cyclesPerFrame;

        // Mark every frame as "no value yet", let the events claim theirs, then carry the last value over the gaps.
        for (int i = 0; i < block.Length; i++)
            block[i] = new StereoFrame(float.NaN, float.NaN);

        _events.Clear();
        var begin = Math.Abs(blockStart - _lastEndCycles) < 1e-9 ? _lastEnd : Fraction.FromDouble(blockStart);
        var end = Fraction.Max(begin, Fraction.FromDouble(blockEnd));
        _lastEnd = end;
        _lastEndCycles = blockEnd;
        if (!_failed && !_pattern.TryQuery(new Arc(begin, end), _events))
            _failed = true; // too dense to query: hold the last value until the sound is reset
        foreach (var e in _events)
        {
            int from = FrameOf(e.Part.Begin.ToDouble(), blockStart, cyclesPerFrame, block.Length);
            int to = FrameOf(e.Part.End.ToDouble(), blockStart, cyclesPerFrame, block.Length);
            var value = StereoFrame.Mono((float)e.Value);
            for (int i = from; i < to; i++)
                block[i] = value;
        }

        float retain = _coefficient.For(_smoothSeconds);
        for (int i = 0; i < block.Length; i++)
        {
            float raw = block[i].Left;
            if (!float.IsNaN(raw))
                _held = raw;

            _smoothed = retain == 0f ? _held : _held + (_smoothed - _held) * retain;
            block[i] = StereoFrame.Mono((float)_smoothed);
        }

        _position += block.Length;
    }

    // The first frame at or after the cycle position; the small allowance keeps an event on a frame boundary on that frame.
    private static int FrameOf(double cycle, double blockStart, double cyclesPerFrame, int frames) =>
        (int)Math.Clamp(Math.Ceiling((cycle - blockStart) / cyclesPerFrame - 1e-6), 0, frames);
}
