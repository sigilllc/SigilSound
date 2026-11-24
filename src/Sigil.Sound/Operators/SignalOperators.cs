namespace Sigil.Sound.Operators;

/// <summary>The difference of two inputs, channel by channel: <c>a - b</c>.</summary>
internal sealed class SubtractOperator : IOperator, IResettable
{
    private readonly IOperator _a;
    private readonly IOperator _b;
    private readonly StereoFrame[] _scratch;

    public SubtractOperator(IOperator a, IOperator b, RenderContext context)
    {
        _a = a;
        _b = b;
        _scratch = new StereoFrame[context.BlockSize];
    }

    public void Reset()
    {
        _a.ResetState();
        _b.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _a.RenderBlock(block);
        var other = _scratch.AsSpan(0, block.Length);
        _b.RenderBlock(other);
        Vectors.Subtract(block, other);
    }
}

/// <summary>Maps a bipolar input in [-1, 1] to [min, max] on each channel, where both ends are parameters.</summary>
internal sealed class RangeParamOperator(IOperator input, ParamSource min, ParamSource max) : IOperator, IResettable
{
    public void Reset()
    {
        input.ResetState();
        min.Reset();
        max.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        input.RenderBlock(block);
        if (block.Length == 0)
            return;
        var lo = min.Next(block.Length);
        var hi = max.Next(block.Length);
        if (min.IsFlat && max.IsFlat)
        {
            Vectors.Range(block, Dsp.Safe(lo[0]), 0.5f * (Dsp.Safe(hi[0]) - Dsp.Safe(lo[0])));
            return;
        }

        for (int i = 0; i < block.Length; i++)
        {
            float l = Dsp.Safe(lo[i]);
            float half = 0.5f * (Dsp.Safe(hi[i]) - l);
            var f = block[i];
            block[i] = new StereoFrame(l + (f.Left + 1.0f) * half, l + (f.Right + 1.0f) * half);
        }
    }
}

/// <summary>An equal-power crossfade from one input (amount 0) to another (amount 1).</summary>
internal sealed class CrossfadeOperator : IOperator, IResettable
{
    private readonly IOperator _a;
    private readonly IOperator _b;
    private readonly ParamSource _amount;
    private readonly StereoFrame[] _scratch;

    public CrossfadeOperator(IOperator a, IOperator b, ParamSource amount, RenderContext context)
    {
        _a = a;
        _b = b;
        _amount = amount;
        _scratch = new StereoFrame[context.BlockSize];
    }

    public void Reset()
    {
        _a.ResetState();
        _b.ResetState();
        _amount.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _a.RenderBlock(block);
        var other = _scratch.AsSpan(0, block.Length);
        _b.RenderBlock(other);
        if (block.Length == 0)
            return;

        var amount = _amount.Next(block.Length);
        if (_amount.IsFlat)
        {
            (float ga, float gb) = Gains(amount[0]);
            for (int i = 0; i < block.Length; i++)
                block[i] = block[i] * ga + other[i] * gb;
            return;
        }

        for (int i = 0; i < block.Length; i++)
        {
            (float ga, float gb) = Gains(amount[i]);
            block[i] = block[i] * ga + other[i] * gb;
        }
    }

    private static (float A, float B) Gains(float amount)
    {
        double t = Math.Clamp(Dsp.Finite(amount, 0.5f), 0f, 1f) * Math.PI * 0.5;
        (double sin, double cos) = Math.SinCos(t);
        return ((float)cos, (float)sin);
    }
}

/// <summary>
/// A one-pole lag on each channel with separate times for rising and falling: a jump in the input becomes a glide, which covers
/// about 63% of the way in the given time and about 99% in five times that.
/// </summary>
internal sealed class SlewOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _rise;
    private readonly ParamSource _fall;
    private readonly SmoothingCoefficient _up;
    private readonly SmoothingCoefficient _down;
    private float _left, _right;
    private bool _started;

    public SlewOperator(IOperator input, ParamSource rise, ParamSource fall, RenderContext context)
    {
        _input = input;
        _rise = rise;
        _fall = fall;
        _up = new SmoothingCoefficient(context.SampleRate);
        _down = new SmoothingCoefficient(context.SampleRate);
    }

    public void Reset()
    {
        _input.ResetState();
        _rise.Reset();
        _fall.Reset();
        _started = false;
        _left = _right = 0;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var rise = _rise.Next(block.Length);
        var fall = _fall.Next(block.Length);

        // The first value is taken as it is, so a slewed control starts where it is instead of gliding up from zero.
        if (!_started)
        {
            _started = true;
            _left = Dsp.Safe(block[0].Left);
            _right = Dsp.Safe(block[0].Right);
        }

        float left = _left, right = _right;
        for (int i = 0; i < block.Length; i++)
        {
            float up = _up.For(Math.Max(0f, Dsp.Finite(rise[i], 0f)));
            float down = _down.For(Math.Max(0f, Dsp.Finite(fall[i], 0f)));
            var x = block[i];
            float l = Dsp.Safe(x.Left), r = Dsp.Safe(x.Right);
            left = l + (left - l) * (l > left ? up : down);
            right = r + (right - r) * (r > right ? up : down);
            block[i] = new StereoFrame(left, right);
        }

        _left = left;
        _right = right;
    }
}

/// <summary>
/// Sample and hold: takes the input's value at each tick of a clock running at the given rate and holds it until the next.
/// With a host clock and a fixed rate the ticks fall where they would had it been running since the clock started.
/// </summary>
internal sealed class HoldOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _rate;
    private readonly double _samplePeriod;
    private readonly HostClock? _clock;
    private double _phase;
    private bool _started;
    private StereoFrame _held;

    public HoldOperator(IOperator input, ParamSource rate, RenderContext context)
    {
        _input = input;
        _rate = rate;
        _samplePeriod = context.SamplePeriod;
        _clock = context.TimeClock;
    }

    public void Reset()
    {
        _input.ResetState();
        _rate.Reset();
        _phase = 0;
        _started = false;
        _held = default;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var rate = _rate.Next(block.Length);
        if (!_started)
        {
            _started = true;
            _held = Safe(block[0]);
            if (_clock is not null && _rate.IsConstant)
                _phase = HostTime.Cycles(Math.Max(0f, Dsp.Finite(rate[0], 0f)), _clock);
        }

        double phase = _phase;
        var held = _held;
        for (int i = 0; i < block.Length; i++)
        {
            phase += Math.Max(0f, Dsp.Finite(rate[i], 0f)) * _samplePeriod;
            if (phase >= 1.0)
            {
                phase -= Math.Floor(phase);
                held = Safe(block[i]);
            }
            block[i] = held;
        }

        _phase = phase;
        _held = held;
    }

    private static StereoFrame Safe(StereoFrame frame) => new(Dsp.Safe(frame.Left), Dsp.Safe(frame.Right));
}

/// <summary>
/// A noise gate: silences the input while its level (or a sidechain's) is below a threshold. The level is a peak follower; the
/// gate opens above the threshold and closes 4 dB below it, so a level hovering at the threshold does not chatter, and its gain
/// moves with the attack and release times. Each is the time to open or close all but 1%.
/// </summary>
internal sealed class GateOperator : IOperator, IResettable
{
    private const float HysteresisDb = 4f;
    private const float DetectorReleaseSeconds = 0.01f;

    // The attack and release are the times to open and close all but 1%: a one-pole of time constant t / 4.6 gets there in t.
    private const float SettleFraction = 1f / 4.6f;

    private readonly IOperator _input;
    private readonly IOperator? _sidechain;
    private readonly StereoFrame[] _key;
    private readonly ParamSource _threshold;
    private readonly ParamSource _attack;
    private readonly ParamSource _release;
    private readonly SmoothingCoefficient _opening;
    private readonly SmoothingCoefficient _closing;
    private readonly float _detectorRetain;
    private float _level;
    private float _gain;
    private bool _open;

    public GateOperator(IOperator input, IOperator? sidechain, ParamSource threshold, ParamSource attack, ParamSource release,
        RenderContext context)
    {
        _input = input;
        _sidechain = sidechain;
        _key = sidechain is null ? [] : new StereoFrame[context.BlockSize];
        _threshold = threshold;
        _attack = attack;
        _release = release;
        _opening = new SmoothingCoefficient(context.SampleRate);
        _closing = new SmoothingCoefficient(context.SampleRate);
        _detectorRetain = MathF.Exp(-1f / (DetectorReleaseSeconds * context.SampleRate));
    }

    public void Reset()
    {
        _input.ResetState();
        _sidechain?.ResetState();
        _threshold.Reset();
        _attack.Reset();
        _release.Reset();
        _level = _gain = 0;
        _open = false;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        Span<StereoFrame> key = block;
        if (_sidechain is not null)
        {
            key = _key.AsSpan(0, block.Length);
            _sidechain.RenderBlock(key);
        }
        if (block.Length == 0)
            return;

        var threshold = _threshold.Next(block.Length);
        var attack = _attack.Next(block.Length);
        var release = _release.Next(block.Length);

        float level = _level, gain = _gain;
        bool open = _open;
        for (int i = 0; i < block.Length; i++)
        {
            var k = key[i];
            float peak = Math.Max(Math.Abs(Dsp.Safe(k.Left)), Math.Abs(Dsp.Safe(k.Right)));
            level = peak > level ? peak : peak + (level - peak) * _detectorRetain;

            float openAt = Db(Dsp.Finite(threshold[i], -40f));
            float closeAt = openAt * Db(-HysteresisDb);
            if (!open && level > openAt)
                open = true;
            else if (open && level < closeAt)
                open = false;

            float target = open ? 1f : 0f;
            float retain = open
                ? _opening.For(Math.Max(0f, Dsp.Finite(attack[i], 0f)) * SettleFraction)
                : _closing.For(Math.Max(0f, Dsp.Finite(release[i], 0f)) * SettleFraction);
            gain = target + (gain - target) * retain;
            block[i] *= gain;
        }

        _level = level;
        _gain = gain;
        _open = open;
    }

    private static float Db(float db) => MathF.Pow(10f, db / 20f);
}

/// <summary>Removes the constant offset (DC) with a one-pole high-pass on each channel: <c>y = x - x[n-1] + r * y[n-1]</c>.</summary>
internal sealed class DcBlockOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _cutoff;
    private readonly double _twoPiOverRate;
    private readonly float _nyquist;
    private float _lastCutoff = float.NaN;
    private float _r;
    private float _lx, _ly, _rx, _ry;

    public DcBlockOperator(IOperator input, ParamSource cutoff, RenderContext context)
    {
        _input = input;
        _cutoff = cutoff;
        _twoPiOverRate = 2.0 * Math.PI / context.SampleRate;
        _nyquist = (float)context.Nyquist;
    }

    public void Reset()
    {
        _input.ResetState();
        _cutoff.Reset();
        _lx = _ly = _rx = _ry = 0;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var cutoff = _cutoff.Next(block.Length);
        bool flat = _cutoff.IsFlat;
        float r = Coefficient(cutoff[0]);
        float lx = _lx, ly = _ly, rx = _rx, ry = _ry;
        for (int i = 0; i < block.Length; i++)
        {
            if (!flat)
                r = Coefficient(cutoff[i]);
            var x = block[i];
            float l = Dsp.Safe(x.Left), rr = Dsp.Safe(x.Right);
            ly = l - lx + r * ly;
            ry = rr - rx + r * ry;
            lx = l;
            rx = rr;
            block[i] = new StereoFrame(ly, ry);
        }

        _lx = lx;
        _ly = ly;
        _rx = rx;
        _ry = ry;
    }

    private float Coefficient(float cutoff)
    {
        cutoff = Math.Clamp(Dsp.Finite(cutoff, 20f), 1f, _nyquist * 0.5f);
        if (cutoff != _lastCutoff)
        {
            _lastCutoff = cutoff;
            _r = (float)Math.Exp(-_twoPiOverRate * cutoff);
        }
        return _r;
    }
}

/// <summary>
/// Pink noise, equal energy per octave, from a seeded white source through Paul Kellett's filter (accurate to about 0.05 dB above
/// 9.2 Hz at 44.1 kHz). Each channel has its own generator.
/// </summary>
internal sealed class PinkNoiseOperator : IOperator, IResettable
{
    // Brings the filter's peaks close to full scale, as white noise does.
    private const float Scale = 0.11f;

    private readonly NoiseOperator _white;
    private readonly float[] _left = new float[7];
    private readonly float[] _right = new float[7];

    public PinkNoiseOperator(int seed) => _white = new NoiseOperator(seed);

    public void Reset()
    {
        _white.Reset();
        Array.Clear(_left);
        Array.Clear(_right);
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _white.RenderBlock(block);
        for (int i = 0; i < block.Length; i++)
            block[i] = new StereoFrame(Pink(_left, block[i].Left), Pink(_right, block[i].Right));
    }

    private static float Pink(float[] b, float white)
    {
        b[0] = 0.99886f * b[0] + white * 0.0555179f;
        b[1] = 0.99332f * b[1] + white * 0.0750759f;
        b[2] = 0.96900f * b[2] + white * 0.1538520f;
        b[3] = 0.86650f * b[3] + white * 0.3104856f;
        b[4] = 0.55000f * b[4] + white * 0.5329522f;
        b[5] = -0.7616f * b[5] - white * 0.0168980f;
        float pink = b[0] + b[1] + b[2] + b[3] + b[4] + b[5] + b[6] + white * 0.5362f;
        b[6] = white * 0.115926f;
        return pink * Scale;
    }
}

/// <summary>
/// Brown (red) noise, energy falling 6 dB per octave: a seeded white source, integrated with a slight leak so it does not
/// wander off, scaled so its peaks come close to full scale, as white noise does. Each channel has its own generator.
/// </summary>
internal sealed class BrownNoiseOperator : IOperator, IResettable
{
    private const float Leak = 0.998f;
    private const float Step = 0.0625f;
    private const float Scale = 0.4f;

    private readonly NoiseOperator _white;
    private float _left, _right;

    public BrownNoiseOperator(int seed) => _white = new NoiseOperator(seed);

    public void Reset()
    {
        _white.Reset();
        _left = _right = 0;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _white.RenderBlock(block);
        float left = _left, right = _right;
        for (int i = 0; i < block.Length; i++)
        {
            left = left * Leak + block[i].Left * Step;
            right = right * Leak + block[i].Right * Step;
            block[i] = new StereoFrame(left * Scale, right * Scale);
        }
        _left = left;
        _right = right;
    }
}
