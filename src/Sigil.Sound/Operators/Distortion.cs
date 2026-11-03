namespace Sigil.Sound.Operators;

/// <summary>Waveshaping: pushes the signal through a nonlinear curve, adding harmonics.</summary>
internal sealed class DistortOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _drive;
    private readonly ParamSource _level;
    private readonly DistortionMode _mode;

    // The fuzz tone filters are one-pole smoothers tuned at 48 kHz, restated for the actual sample rate.
    private readonly float _dcCoefficient;
    private readonly float _lowCoefficient;
    private readonly float _highCoefficient;

    // Fuzz keeps a little filter state per channel.
    private float _dcLeft, _dcRight, _lowLeft, _lowRight, _highLeft, _highRight;

    public DistortOperator(IOperator input, ParamSource drive, ParamSource level, DistortionMode mode, RenderContext context)
    {
        _input = input;
        _drive = drive;
        _level = level;
        _mode = mode;
        _dcCoefficient = Dsp.ScaleCoefficient(0.01f, context.SampleRate);
        _lowCoefficient = Dsp.ScaleCoefficient(0.05f, context.SampleRate);
        _highCoefficient = Dsp.ScaleCoefficient(0.4f, context.SampleRate);
    }

    public void Reset()
    {
        _dcLeft = _dcRight = _lowLeft = _lowRight = _highLeft = _highRight = 0;
        _input.ResetState();
        _drive.Reset();
        _level.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var drive = _drive.Next(block.Length);
        var level = _level.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            float d = Dsp.Finite(drive[i], 1f);
            float l = Dsp.Finite(level[i], 1f);
            var frame = block[i];
            block[i] = new StereoFrame(
                Shape(frame.Left * d, ref _dcLeft, ref _lowLeft, ref _highLeft) * l,
                Shape(frame.Right * d, ref _dcRight, ref _lowRight, ref _highRight) * l);
        }
    }

    private float Shape(float x, ref float dc, ref float low, ref float high)
    {
        switch (_mode)
        {
            case DistortionMode.Hard:
                return Math.Clamp(x, -1f, 1f);

            case DistortionMode.Fold:
                // A sine folds the waveform back on itself instead of flattening it: bright and metallic.
                return MathF.Sin(x * MathF.PI * 0.5f);

            case DistortionMode.Fuzz:
            {
                // Half rectification (octave-up grit), DC removal, heavy clipping, then a scooped tone.
                float mixed = x * 0.4f + MathF.Abs(x) * 0.6f;
                dc += (mixed - dc) * _dcCoefficient;
                float clipped = MathF.Tanh((mixed - dc) * 2f);
                low += (clipped - low) * _lowCoefficient;
                high += (clipped - high) * _highCoefficient;
                return Dsp.Safe(low * 1.5f + (clipped - high) * 0.8f);
            }

            default:
                return MathF.Tanh(x);
        }
    }
}

/// <summary>Reduces bit depth and, optionally, sample rate, for a lo-fi, aliased sound.</summary>
internal sealed class BitCrushOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _bits;
    private readonly ParamSource _rate;
    private readonly double _sampleRate;

    // Starts at a full cycle so the very first frame is taken as a sample rather than held as silence.
    private double _phase = 1.0;
    private StereoFrame _held;
    private float _lastBits = float.NaN;
    private float _steps;

    public BitCrushOperator(IOperator input, ParamSource bits, ParamSource rate, RenderContext context)
    {
        _input = input;
        _bits = bits;
        _rate = rate;
        _sampleRate = context.SampleRate;
    }

    public void Reset()
    {
        _phase = 1.0;
        _held = default;
        _lastBits = float.NaN;
        _input.ResetState();
        _bits.Reset();
        _rate.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var bits = _bits.Next(block.Length);
        var rate = _rate.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            // Sample and hold at the reduced rate: take a new input frame each time the phase wraps.
            _phase += Math.Clamp(Dsp.Finite(rate[i], (float)_sampleRate), 1f, (float)_sampleRate) / _sampleRate;
            if (_phase >= 1.0)
            {
                _phase -= Math.Floor(_phase);
                _held = block[i];
            }

            // One bit of the depth is the sign, so n bits leave 2^(n-1) steps on each side of zero.
            // The power is only recomputed when the depth moves.
            float depth = Math.Max(1f, Dsp.Finite(bits[i], 16f));
            if (depth != _lastBits)
            {
                _lastBits = depth;
                _steps = MathF.Pow(2f, depth - 1f);
            }

            block[i] = new StereoFrame(
                Math.Clamp(MathF.Round(_held.Left * _steps) / _steps, -1f, 1f),
                Math.Clamp(MathF.Round(_held.Right * _steps) / _steps, -1f, 1f));
        }
    }
}
