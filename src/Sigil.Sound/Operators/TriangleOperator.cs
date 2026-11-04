namespace Sigil.Sound.Operators;

/// <summary>
/// A band-limited triangle wave with a variable skew. It rises from -1 to +1 over the first <c>skew</c> of each cycle and
/// falls back over the rest. The two slope corners are rounded with PolyBLAMP residuals (the integral of the PolyBLEP
/// correction that the saw uses), so high-pitched triangles alias far less than a naive one. The value is computed from the
/// phase each sample rather than accumulated, so the wave cannot drift out of step with its pitch.
/// </summary>
internal sealed class TriangleOperator : IOperator, IResettable
{
    // A skew at 0 or 1 would need an infinitely steep slope, so it is kept a little inside the range.
    private const double MinSkew = 0.01;
    private const double MaxSkew = 0.99;

    // Above half the sample rate a cycle has no meaning; the phase step is capped there.
    private const double MaxCyclesPerFrame = 0.5;

    private readonly ParamSource _frequency;
    private readonly ParamSource _skew;
    private readonly int _sampleRate;
    private readonly HostClock? _clock;
    private double _phase;
    private bool _synced;

    public TriangleOperator(ParamSource frequency, ParamSource skew, RenderContext context)
    {
        _frequency = frequency;
        _skew = skew;
        _sampleRate = context.SampleRate;
        _clock = context.TimeClock;
    }

    public void Reset()
    {
        _phase = 0;
        _synced = false;
        _frequency.Reset();
        _skew.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        if (block.Length == 0)
            return;

        var frequency = _frequency.Next(block.Length);
        var skew = _skew.Next(block.Length);

        // With a host clock and a fixed pitch, the wave picks up the phase it would have had, as the sine does.
        if (!_synced)
        {
            _synced = true;
            if (_clock is not null && _frequency.IsConstant)
                _phase = HostTime.Cycles(Math.Max(0f, Dsp.Finite(frequency[0], 0f)), _clock);
        }

        for (int i = 0; i < block.Length; i++)
        {
            double s = Math.Clamp(Dsp.Finite(skew[i], 0.5f), MinSkew, MaxSkew);
            double cyclesPerFrame = Math.Clamp(Math.Max(0f, Dsp.Finite(frequency[i], 0f)) / _sampleRate, 0.0, MaxCyclesPerFrame);
            block[i] = StereoFrame.Mono((float)Value(_phase, s, cyclesPerFrame));

            _phase += cyclesPerFrame;
            if (_phase >= 1.0)
                _phase -= Math.Floor(_phase);
        }
    }

    // The sample at a phase, with both corners rounded. The bottom corner (phase 0) turns upward by `change`; the top corner
    // (phase skew) turns downward by the same amount. Each corner adds half its slope change, times one frame, times the
    // integrated PolyBLEP residual, which is what rounds the corner without changing the average level.
    private static double Value(double phase, double skew, double cyclesPerFrame)
    {
        double value = Naive(phase, skew);
        if (cyclesPerFrame <= 0)
            return value;

        double change = 2.0 / skew + 2.0 / (1.0 - skew);

        // Signed distances to each corner, in cycles, wrapped to the nearest occurrence of the corner.
        double toBottom = phase - Math.Round(phase);
        double toTop = phase - skew;
        toTop -= Math.Round(toTop);

        value += 0.5 * change * cyclesPerFrame * Blamp(toBottom / cyclesPerFrame);
        value -= 0.5 * change * cyclesPerFrame * Blamp(toTop / cyclesPerFrame);
        return value;
    }

    // The unrounded triangle: two straight lines meeting at the bottom (phase 0) and the top (phase skew).
    private static double Naive(double phase, double skew) =>
        phase < skew
            ? -1.0 + 2.0 * phase / skew
            : 1.0 - 2.0 * (phase - skew) / (1.0 - skew);

    // The integral of the PolyBLEP residual, in samples: a bump that peaks at 1/3 on the corner and is zero more than one
    // sample away. Its area is 1/6, and the two corners of a triangle have opposite signs, so together they add no average level.
    private static double Blamp(double x)
    {
        if (x <= -1 || x >= 1)
            return 0;
        return x < 0 ? (x + 1) * (x + 1) * (x + 1) / 3.0 : (1 - x) * (1 - x) * (1 - x) / 3.0;
    }
}
