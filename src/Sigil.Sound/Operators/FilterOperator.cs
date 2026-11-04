namespace Sigil.Sound.Operators;

internal enum FilterMode
{
    LowPass,
    HighPass,
    BandPass,
    Notch,
}

/// <summary>
/// A resonant two-pole filter built as a topology-preserving state-variable filter (Zavalishin / Cytomic), one per channel.
/// Unlike a direct-form biquad it stays stable and quiet while cutoff and Q move at audio rate, and one structure
/// gives low-pass, high-pass, band-pass and notch.
/// </summary>
internal sealed class FilterOperator : IOperator, IResettable
{
    private const float FrequencyTolerance = 0.01f;
    private const float ResonanceTolerance = 0.001f;
    private const float MinResonance = 0.0001f;
    private const float MinCutoff = 10f;

    // Tiny offset that keeps the state out of denormal range during silence.
    private const float DenormalOffset = 1e-25f;

    private readonly IOperator _input;
    private readonly FilterMode _mode;
    private readonly ParamSource _cutoff;
    private readonly ParamSource _q;
    private readonly float _piSamplePeriod;
    private readonly float _maxCutoff;

    private float _lastCutoff = float.NaN;
    private float _lastQ = float.NaN;
    private float _k, _a1, _a2, _a3;

    private float _lIc1, _lIc2, _rIc1, _rIc2;

    public FilterOperator(IOperator input, FilterMode mode, ParamSource cutoff, ParamSource q, RenderContext context)
    {
        _input = input;
        _mode = mode;
        _cutoff = cutoff;
        _q = q;
        _piSamplePeriod = (float)(Math.PI / context.SampleRate);
        // Stay below Nyquist so tan() stays finite and the filter stable.
        _maxCutoff = Math.Max(MinCutoff, (float)context.Nyquist * 0.95f);
    }

    public void Reset()
    {
        _lIc1 = _lIc2 = _rIc1 = _rIc2 = 0;
        _lastCutoff = _lastQ = float.NaN;
        _input.ResetState();
        _cutoff.Reset();
        _q.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var cutoff = _cutoff.Next(block.Length);
        var q = _q.Next(block.Length);

        // The sample loop is written out in one piece, with the filter state in locals: a voice runs it for every
        // frame, and where the .NET interpreter runs (the browser) each call and ref argument is a large part of the cost.
        // A fixed cutoff and Q (the usual case) are applied once per block instead of tested for every frame.
        bool fixedCoefficients = _cutoff.IsConstant && _q.IsConstant;
        if (fixedCoefficients)
            UpdateCoefficients(Dsp.Finite(cutoff[0], 1000f), Dsp.Finite(q[0], 0.7071f));

        var mode = _mode;
        float lIc1 = _lIc1, lIc2 = _lIc2, rIc1 = _rIc1, rIc2 = _rIc2;
        float a1 = _a1, a2 = _a2, a3 = _a3, k = _k;

        for (int i = 0; i < block.Length; i++)
        {
            if (!fixedCoefficients)
            {
                UpdateCoefficients(Dsp.Finite(cutoff[i], 1000f), Dsp.Finite(q[i], 0.7071f));
                a1 = _a1;
                a2 = _a2;
                a3 = _a3;
                k = _k;
            }

            var x = block[i];
            // NaN and infinity both fail x - x == 0, and become silence so one bad value cannot poison the state.
            float l = x.Left - x.Left == 0f ? x.Left : 0f;
            float r = x.Right - x.Right == 0f ? x.Right : 0f;

            float v3 = l + DenormalOffset - lIc2;
            float v1 = a1 * lIc1 + a2 * v3;
            float v2 = lIc2 + a2 * lIc1 + a3 * v3;
            lIc1 = 2f * v1 - lIc1;
            lIc2 = 2f * v2 - lIc2;
            float outLeft = mode == FilterMode.LowPass ? v2
                : mode == FilterMode.BandPass ? v1 * k
                : mode == FilterMode.HighPass ? l - k * v1 - v2
                : l - k * v1;

            v3 = r + DenormalOffset - rIc2;
            v1 = a1 * rIc1 + a2 * v3;
            v2 = rIc2 + a2 * rIc1 + a3 * v3;
            rIc1 = 2f * v1 - rIc1;
            rIc2 = 2f * v2 - rIc2;
            float outRight = mode == FilterMode.LowPass ? v2
                : mode == FilterMode.BandPass ? v1 * k
                : mode == FilterMode.HighPass ? r - k * v1 - v2
                : r - k * v1;

            block[i] = new StereoFrame(outLeft, outRight);
        }

        _lIc1 = lIc1;
        _lIc2 = lIc2;
        _rIc1 = rIc1;
        _rIc2 = rIc2;
    }

    private void UpdateCoefficients(float cutoff, float q)
    {
        if (Math.Abs(cutoff - _lastCutoff) < FrequencyTolerance && Math.Abs(q - _lastQ) < ResonanceTolerance)
            return;

        _lastCutoff = cutoff;
        _lastQ = q;

        float g = MathF.Tan(_piSamplePeriod * Math.Clamp(cutoff, MinCutoff, _maxCutoff));
        _k = 1f / Math.Max(q, MinResonance);
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }
}

