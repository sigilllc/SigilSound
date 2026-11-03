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

        for (int i = 0; i < block.Length; i++)
        {
            UpdateCoefficients(Dsp.Finite(cutoff[i], 1000f), Dsp.Finite(q[i], 0.7071f));

            var x = block[i];
            block[i] = new StereoFrame(
                Process(Dsp.Safe(x.Left), ref _lIc1, ref _lIc2),
                Process(Dsp.Safe(x.Right), ref _rIc1, ref _rIc2));
        }
    }

    private float Process(float v0, ref float ic1, ref float ic2)
    {
        float v3 = v0 + DenormalOffset - ic2;
        float v1 = _a1 * ic1 + _a2 * v3;
        float v2 = ic2 + _a2 * ic1 + _a3 * v3;
        ic1 = 2f * v1 - ic1;
        ic2 = 2f * v2 - ic2;

        return _mode switch
        {
            FilterMode.LowPass => v2,
            FilterMode.BandPass => v1 * _k,
            FilterMode.HighPass => v0 - _k * v1 - v2,
            _ => v0 - _k * v1,
        };
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

