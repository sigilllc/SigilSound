namespace Sigil.Sound.Operators;

/// <summary>
/// Resonant two-pole low-pass (RBJ Audio EQ Cookbook biquad, Direct Form I), one filter per channel.
/// Cutoff and Q may be modulated; coefficients are recomputed only when they move past a small tolerance.
/// </summary>
public sealed class LowPassOperator : IOperator
{
    private const float FrequencyTolerance = 0.01f;
    private const float ResonanceTolerance = 0.001f;
    private const float MinResonance = 0.0001f;
    private const float MinCutoff = 10f;

    // Tiny offset that keeps the feedback path out of denormal range during silence.
    private const float DenormalOffset = 1e-25f;

    private readonly IOperator _input;
    private readonly ParamSource _cutoff;
    private readonly ParamSource _q;
    private readonly float _twoPiSamplePeriod;
    private readonly float _maxCutoff;

    private float _lastCutoff = float.NaN;
    private float _lastQ = float.NaN;
    private float _b0, _b1, _b2, _a1, _a2;

    private float _lx1, _lx2, _ly1, _ly2;
    private float _rx1, _rx2, _ry1, _ry2;

    public LowPassOperator(IOperator input, ParamSource cutoff, ParamSource q, RenderContext context)
    {
        _input = input;
        _cutoff = cutoff;
        _q = q;
        _twoPiSamplePeriod = (float)(2.0 * Math.PI / context.SampleRate);
        // Stay safely below Nyquist so sin(omega) > 0 and the poles stay stable.
        _maxCutoff = Math.Max(MinCutoff, (float)context.Nyquist - 20f);
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var cutoff = _cutoff.Next(block.Length);
        var q = _q.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            UpdateCoefficients(cutoff[i], q[i]);

            var x = block[i];
            float left = _b0 * x.Left + _b1 * _lx1 + _b2 * _lx2 - _a1 * _ly1 - _a2 * _ly2 + DenormalOffset;
            float right = _b0 * x.Right + _b1 * _rx1 + _b2 * _rx2 - _a1 * _ry1 - _a2 * _ry2 + DenormalOffset;

            _lx2 = _lx1; _lx1 = x.Left; _ly2 = _ly1; _ly1 = left;
            _rx2 = _rx1; _rx1 = x.Right; _ry2 = _ry1; _ry1 = right;

            block[i] = new StereoFrame(left, right);
        }
    }

    private void UpdateCoefficients(float cutoff, float q)
    {
        if (Math.Abs(cutoff - _lastCutoff) < FrequencyTolerance && Math.Abs(q - _lastQ) < ResonanceTolerance)
            return;

        _lastCutoff = cutoff;
        _lastQ = q;

        float omega = _twoPiSamplePeriod * Math.Clamp(cutoff, MinCutoff, _maxCutoff);
        (float sin, float cos) = MathF.SinCos(omega);
        float alpha = sin / (2f * Math.Max(q, MinResonance));

        float a0 = 1f + alpha;
        _b0 = (1f - cos) / 2f / a0;
        _b1 = (1f - cos) / a0;
        _b2 = _b0;
        _a1 = -2f * cos / a0;
        _a2 = (1f - alpha) / a0;
    }
}
