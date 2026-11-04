namespace Sigil.Sound.Operators;

/// <summary>The three band shapes an <see cref="EqualizerOperator"/> can take.</summary>
internal enum EqualizerShape
{
    Peak,
    LowShelf,
    HighShelf,
}

/// <summary>
/// One band of an equalizer: a peaking (bell) filter or a low or high shelf, built from the Robert Bristow-Johnson
/// cookbook biquad and run in transposed direct form II. At 0 dB gain the band is an exact pass-through.
/// Frequency, Q and gain are followed frame by frame when they are modulated; fixed settings cost one update per block.
/// </summary>
internal sealed class EqualizerOperator : IOperator, IResettable
{
    private const float MinFrequency = 10f;
    private const float MinQ = 0.05f;
    private const double MaxGainDb = 40.0;

    // Keeps the filter state out of the denormal range during silence, where the arithmetic slows down a lot.
    private const float DenormalOffset = 1e-25f;

    private readonly IOperator _input;
    private readonly EqualizerShape _shape;
    private readonly ParamSource _frequency;
    private readonly ParamSource _q;
    private readonly ParamSource _gainDb;
    private readonly int _sampleRate;

    // Normalised coefficients (a0 is divided out, so there is no a0 term).
    private float _b0, _b1, _b2, _a1, _a2;
    private float _lastFrequency = float.NaN;
    private float _lastQ = float.NaN;
    private float _lastGainDb = float.NaN;

    // Transposed direct form II state, one pair per channel.
    private float _l1, _l2, _r1, _r2;

    public EqualizerOperator(IOperator input, EqualizerShape shape, ParamSource frequency, ParamSource q,
        ParamSource gainDb, RenderContext context)
    {
        _input = input;
        _shape = shape;
        _frequency = frequency;
        _q = q;
        _gainDb = gainDb;
        _sampleRate = context.SampleRate;
    }

    public void Reset()
    {
        _l1 = _l2 = _r1 = _r2 = 0;
        _input.ResetState();
        _frequency.Reset();
        _q.Reset();
        _gainDb.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var frequency = _frequency.Next(block.Length);
        var q = _q.Next(block.Length);
        var gainDb = _gainDb.Next(block.Length);

        // Fixed settings need their coefficients worked out once per block. A modulated setting is followed frame by
        // frame, so a sweep of the band is heard as a sweep rather than as steps one block long.
        bool fixedCoefficients = _frequency.IsConstant && _q.IsConstant && _gainDb.IsConstant;
        if (fixedCoefficients)
            UpdateCoefficients(Dsp.Finite(frequency[0], 1000f), Dsp.Finite(q[0], 0.7071f), Dsp.Finite(gainDb[0], 0f));

        float b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
        float l1 = _l1, l2 = _l2, r1 = _r1, r2 = _r2;
        for (int i = 0; i < block.Length; i++)
        {
            if (!fixedCoefficients)
            {
                UpdateCoefficients(Dsp.Finite(frequency[i], 1000f), Dsp.Finite(q[i], 0.7071f), Dsp.Finite(gainDb[i], 0f));
                b0 = _b0;
                b1 = _b1;
                b2 = _b2;
                a1 = _a1;
                a2 = _a2;
            }

            var x = block[i];
            float left = Dsp.Safe(x.Left);
            float right = Dsp.Safe(x.Right);

            float yl = b0 * left + l1;
            l1 = b1 * left - a1 * yl + l2;
            l2 = b2 * left - a2 * yl + DenormalOffset;

            float yr = b0 * right + r1;
            r1 = b1 * right - a1 * yr + r2;
            r2 = b2 * right - a2 * yr + DenormalOffset;

            block[i] = new StereoFrame(yl, yr);
        }

        _l1 = l1;
        _l2 = l2;
        _r1 = r1;
        _r2 = r2;
    }

    // The cookbook formulas. Q sets the bandwidth of the bell and the steepness of the shelves. At 0 dB the bell and
    // shelves reduce to the identity, so an unused band costs only its arithmetic.
    private void UpdateCoefficients(float frequency, float q, float gainDb)
    {
        if (frequency == _lastFrequency && q == _lastQ && gainDb == _lastGainDb)
            return;
        _lastFrequency = frequency;
        _lastQ = q;
        _lastGainDb = gainDb;

        double f = Math.Clamp(frequency, MinFrequency, _sampleRate * 0.45);
        double qq = Math.Max(q, MinQ);
        double g = Math.Clamp(gainDb, -MaxGainDb, MaxGainDb);
        double a = Math.Pow(10.0, g / 40.0);
        double w0 = 2.0 * Math.PI * f / _sampleRate;
        double cosW = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * qq);

        double b0, b1, b2, a0, a1, a2;
        switch (_shape)
        {
            case EqualizerShape.LowShelf:
            {
                double twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cosW + twoSqrtAAlpha);
                b1 = 2.0 * a * ((a - 1) - (a + 1) * cosW);
                b2 = a * ((a + 1) - (a - 1) * cosW - twoSqrtAAlpha);
                a0 = (a + 1) + (a - 1) * cosW + twoSqrtAAlpha;
                a1 = -2.0 * ((a - 1) + (a + 1) * cosW);
                a2 = (a + 1) + (a - 1) * cosW - twoSqrtAAlpha;
                break;
            }
            case EqualizerShape.HighShelf:
            {
                double twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cosW + twoSqrtAAlpha);
                b1 = -2.0 * a * ((a - 1) + (a + 1) * cosW);
                b2 = a * ((a + 1) + (a - 1) * cosW - twoSqrtAAlpha);
                a0 = (a + 1) - (a - 1) * cosW + twoSqrtAAlpha;
                a1 = 2.0 * ((a - 1) - (a + 1) * cosW);
                a2 = (a + 1) - (a - 1) * cosW - twoSqrtAAlpha;
                break;
            }
            default:
            {
                b0 = 1 + alpha * a;
                b1 = -2.0 * cosW;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2.0 * cosW;
                a2 = 1 - alpha / a;
                break;
            }
        }

        _b0 = (float)(b0 / a0);
        _b1 = (float)(b1 / a0);
        _b2 = (float)(b2 / a0);
        _a1 = (float)(a1 / a0);
        _a2 = (float)(a2 / a0);
    }
}
