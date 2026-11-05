namespace Sigil.Sound.Operators;

/// <summary>
/// A peak limiter: the gain drops quickly when the signal would pass the ceiling and recovers slowly.
/// A final clamp guarantees the output never exceeds the ceiling, even in the first sample of a sudden peak.
/// </summary>
internal sealed class LimiterOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly float _ceiling;
    private readonly float _attack;
    private readonly float _release;
    private float _gain = 1f;

    public LimiterOperator(IOperator input, double ceiling, double releaseSeconds, RenderContext context)
    {
        _input = input;
        _ceiling = (float)ceiling;
        _attack = Coefficient(0.0005, context.SampleRate);
        _release = Coefficient(releaseSeconds, context.SampleRate);
    }

    public void Reset()
    {
        _gain = 1f;
        _input.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        for (int i = 0; i < block.Length; i++)
        {
            var frame = block[i];
            float peak = Math.Max(MathF.Abs(frame.Left), MathF.Abs(frame.Right));
            // NaN or infinity would otherwise poison the gain for good.
            if (!float.IsFinite(peak)) peak = 0f;
            float needed = peak > _ceiling ? _ceiling / peak : 1f;

            _gain += (needed - _gain) * (needed < _gain ? _attack : _release);
            var limited = frame * _gain;
            block[i] = new StereoFrame(
                Math.Clamp(Dsp.Safe(limited.Left), -_ceiling, _ceiling),
                Math.Clamp(Dsp.Safe(limited.Right), -_ceiling, _ceiling));
        }
    }

    // One-pole coefficient that covers about 63% of the distance in the given time.
    private static float Coefficient(double seconds, int sampleRate) =>
        seconds <= 0 ? 1f : 1f - MathF.Exp((float)(-1.0 / (seconds * sampleRate)));
}

/// <summary>Balances the channels: the side you pan away from is turned down, the other is left alone.</summary>
internal sealed class BalanceOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _position;

    // The cosines are only recomputed when the position moves, so a fixed pan costs nothing per frame.
    private float _lastPosition = float.NaN;
    private float _left = 1f, _right = 1f;

    public BalanceOperator(IOperator input, ParamSource position)
    {
        _input = input;
        _position = position;
    }

    public void Reset()
    {
        _input.ResetState();
        _position.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var position = _position.Next(block.Length);
        for (int i = 0; i < block.Length; i++)
        {
            float p = Math.Clamp(Dsp.Finite(position[i], 0f), -1f, 1f);
            if (p != _lastPosition)
            {
                _lastPosition = p;
                // A quarter-cosine turns the far side down smoothly to silence at the extreme.
                _left = p > 0 ? MathF.Cos(p * MathF.PI * 0.5f) : 1f;
                _right = p < 0 ? MathF.Cos(-p * MathF.PI * 0.5f) : 1f;
                // cos(pi/2) in single precision is about -4e-8, which would leak a faint inverted signal at a hard balance.
                if (p >= 1f) _left = 0f;
                if (p <= -1f) _right = 0f;
            }

            block[i] = new StereoFrame(block[i].Left * _left, block[i].Right * _right);
        }
    }
}

/// <summary>
/// Places a source in the stereo field with an equal-power law: the left and right gains are sqrt(2) times the cosine and sine
/// of the position angle, so the total power is the same everywhere and a centered mono source keeps its level.
/// Stereo input is folded to mono first.
/// </summary>
internal sealed class PanOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _position;

    // The trigonometry is only recomputed when the position moves, so a fixed pan costs nothing per frame.
    private float _lastPosition = float.NaN;
    private float _left = 1f, _right = 1f;

    public PanOperator(IOperator input, ParamSource position)
    {
        _input = input;
        _position = position;
    }

    public void Reset()
    {
        _input.ResetState();
        _position.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var position = _position.Next(block.Length);
        for (int i = 0; i < block.Length; i++)
        {
            float p = Math.Clamp(Dsp.Finite(position[i], 0f), -1f, 1f);
            if (p != _lastPosition)
            {
                _lastPosition = p;
                float angle = (p + 1f) * MathF.PI * 0.25f;
                _left = MathF.Sqrt(2f) * MathF.Cos(angle);
                _right = MathF.Sqrt(2f) * MathF.Sin(angle);
                // Exactly silent at the extremes rather than 1e-8, so a hard pan is a clean zero.
                if (p >= 1f) _left = 0f;
                if (p <= -1f) _right = 0f;
            }

            float mono = block[i].ToMono();
            block[i] = new StereoFrame(mono * _left, mono * _right);
        }
    }
}
