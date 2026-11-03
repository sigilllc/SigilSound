namespace Sigil.Sound.Operators;

/// <summary>A feedback delay (echo). The delay time may be modulated.</summary>
internal sealed class DelayOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _time;
    private readonly ParamSource _feedback;
    private readonly ParamSource _mix;
    private readonly DelayLine _line;
    private readonly int _sampleRate;

    public DelayOperator(IOperator input, ParamSource time, ParamSource feedback, ParamSource mix,
        double maxSeconds, RenderContext context)
    {
        _input = input;
        _time = time;
        _feedback = feedback;
        _mix = mix;
        _sampleRate = context.SampleRate;
        _line = new DelayLine((int)Math.Ceiling(maxSeconds * context.SampleRate) + 2);
    }

    public void Reset()
    {
        _line.Clear();
        _input.ResetState();
        _time.Reset();
        _feedback.Reset();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var time = _time.Next(block.Length);
        var feedback = _feedback.Next(block.Length);
        var mix = _mix.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];
            var wet = _line.Read(Dsp.Finite(time[i], 0f) * _sampleRate);
            float fb = Math.Clamp(Dsp.Finite(feedback[i], 0f), -0.99f, 0.99f);
            _line.Write(dry + wet * fb);
            block[i] = Dsp.Blend(dry, wet, mix[i]);
        }
    }
}

/// <summary>
/// A feedback comb filter with a damped loop: a resonator tuned to a frequency. Fed a short burst,
/// it rings at that pitch, which is the Karplus-Strong plucked string.
/// </summary>
internal sealed class CombOperator : IOperator, IResettable
{
    private const float MinFrequency = 20f;

    private readonly IOperator _input;
    private readonly ParamSource _frequency;
    private readonly ParamSource _feedback;
    private readonly ParamSource _damping;
    private readonly DelayLine _line;
    private readonly int _sampleRate;
    private StereoFrame _damped;

    public CombOperator(IOperator input, ParamSource frequency, ParamSource feedback, ParamSource damping,
        RenderContext context)
    {
        _input = input;
        _frequency = frequency;
        _feedback = feedback;
        _damping = damping;
        _sampleRate = context.SampleRate;
        _line = new DelayLine((int)Math.Ceiling(context.SampleRate / MinFrequency) + 2);
    }

    public void Reset()
    {
        _line.Clear();
        _damped = default;
        _input.ResetState();
        _frequency.Reset();
        _feedback.Reset();
        _damping.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var frequency = _frequency.Next(block.Length);
        var feedback = _feedback.Next(block.Length);
        var damping = _damping.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            float hz = Math.Max(MinFrequency, Dsp.Finite(frequency[i], 440f));
            var delayed = _line.Read(_sampleRate / hz);

            // One-pole low-pass in the loop: high partials die faster than low ones, like a real string.
            float damp = Math.Clamp(Dsp.Finite(damping[i], 0f), 0f, 0.99f);
            _damped = delayed * (1f - damp) + _damped * damp;

            float fb = Math.Clamp(Dsp.Finite(feedback[i], 0f), -0.999f, 0.999f);
            var output = block[i] + _damped * fb;
            _line.Write(output);
            block[i] = output;
        }
    }
}

/// <summary>Several short, slowly modulated delays spread across the stereo field.</summary>
internal sealed class ChorusOperator : IOperator, IResettable
{
    private const double BaseDelaySeconds = 0.018;
    private const double MaxDepthSeconds = 0.012;

    private readonly IOperator _input;
    private readonly ParamSource _rate;
    private readonly ParamSource _depth;
    private readonly ParamSource _mix;
    private readonly DelayLine _line;
    private readonly double[] _phase;
    private readonly float[] _panLeft;
    private readonly float[] _panRight;
    private readonly float _voiceGain;
    private readonly int _sampleRate;

    public ChorusOperator(IOperator input, ParamSource rate, ParamSource depth, ParamSource mix, int voices,
        RenderContext context)
    {
        _input = input;
        _rate = rate;
        _depth = depth;
        _mix = mix;
        _sampleRate = context.SampleRate;
        _line = new DelayLine((int)Math.Ceiling((BaseDelaySeconds + MaxDepthSeconds) * context.SampleRate) + 4);

        _phase = new double[voices];
        _panLeft = new float[voices];
        _panRight = new float[voices];
        for (int v = 0; v < voices; v++)
        {
            float pan = voices == 1 ? 0f : -1f + 2f * v / (voices - 1);
            // Equal-power spread, so one voice sits in the middle and many fan out.
            _panLeft[v] = MathF.Cos((pan + 1f) * MathF.PI / 4f);
            _panRight[v] = MathF.Sin((pan + 1f) * MathF.PI / 4f);
        }
        _voiceGain = 1.4142135f / MathF.Sqrt(voices);
        StartPhases();
    }

    public void Reset()
    {
        _line.Clear();
        StartPhases();
        _input.ResetState();
        _rate.Reset();
        _depth.Reset();
        _mix.Reset();
    }

    private void StartPhases()
    {
        for (int v = 0; v < _phase.Length; v++)
            _phase[v] = (double)v / _phase.Length;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var rate = _rate.Next(block.Length);
        var depth = _depth.Next(block.Length);
        var mix = _mix.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];
            _line.Write(dry);

            double depthSeconds = Math.Clamp(Dsp.Finite(depth[i], 0f), 0f, 1f) * MaxDepthSeconds;
            float left = 0, right = 0;
            for (int v = 0; v < _phase.Length; v++)
            {
                // Each voice drifts at a slightly different speed, so the voices never lock together.
                double step = Math.Max(0, Dsp.Finite(rate[i], 0f)) * (1.0 + 0.09 * v) / _sampleRate;
                _phase[v] += step;
                if (_phase[v] >= 1.0) _phase[v] -= Math.Floor(_phase[v]);

                double seconds = BaseDelaySeconds + depthSeconds * Math.Sin(2.0 * Math.PI * _phase[v]);
                var voice = _line.Read((float)(seconds * _sampleRate));
                float mono = voice.ToMono();
                left += mono * _panLeft[v];
                right += mono * _panRight[v];
            }

            var wet = new StereoFrame(left, right) * _voiceGain;
            block[i] = Dsp.Blend(dry, wet, mix[i]);
        }
    }
}

/// <summary>A swept chain of all-pass filters; mixed with the dry signal it carves moving notches.</summary>
internal sealed class PhaserOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _rate;
    private readonly ParamSource _feedback;
    private readonly ParamSource _mix;
    private readonly int _stages;
    private readonly float _logMin;
    private readonly float _logSpan;
    private readonly float _piOverRate;
    private readonly double _samplePeriod;
    private readonly float[] _leftState;
    private readonly float[] _rightState;
    private double _phase;
    private StereoFrame _previous;

    public PhaserOperator(IOperator input, ParamSource rate, ParamSource feedback, ParamSource mix, int stages,
        double minFrequency, double maxFrequency, RenderContext context)
    {
        _input = input;
        _rate = rate;
        _feedback = feedback;
        _mix = mix;
        _stages = stages;
        _logMin = MathF.Log((float)minFrequency);
        _logSpan = MathF.Log((float)maxFrequency) - _logMin;
        _piOverRate = MathF.PI / context.SampleRate;
        _samplePeriod = context.SamplePeriod;
        _leftState = new float[stages];
        _rightState = new float[stages];
    }

    public void Reset()
    {
        Array.Clear(_leftState);
        Array.Clear(_rightState);
        _phase = 0;
        _previous = default;
        _input.ResetState();
        _rate.Reset();
        _feedback.Reset();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var rate = _rate.Next(block.Length);
        var feedback = _feedback.Next(block.Length);
        var mix = _mix.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            _phase += Math.Max(0, Dsp.Finite(rate[i], 0f)) * _samplePeriod;
            if (_phase >= 1.0) _phase -= Math.Floor(_phase);

            // The right channel sweeps a quarter cycle behind, which widens the sound.
            float lfoLeft = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * (float)_phase);
            float lfoRight = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * (float)(_phase + 0.25));
            float coefLeft = Coefficient(lfoLeft);
            float coefRight = Coefficient(lfoRight);

            float fb = Math.Clamp(Dsp.Finite(feedback[i], 0f), -0.95f, 0.95f);
            var dry = block[i];
            float left = dry.Left + _previous.Left * fb;
            float right = dry.Right + _previous.Right * fb;
            for (int s = 0; s < _stages; s++)
            {
                left = AllPass(left, coefLeft, ref _leftState[s]);
                right = AllPass(right, coefRight, ref _rightState[s]);
            }

            _previous = new StereoFrame(Dsp.Safe(left), Dsp.Safe(right));
            block[i] = Dsp.Blend(dry, _previous, mix[i]);
        }
    }

    private float Coefficient(float lfo)
    {
        float hz = MathF.Exp(_logMin + _logSpan * lfo);
        float t = MathF.Tan(_piOverRate * hz);
        return (t - 1f) / (t + 1f);
    }

    // First-order all-pass: unity gain at every frequency, with a phase shift that sweeps with the coefficient.
    private static float AllPass(float x, float a, ref float state)
    {
        float y = a * x + state;
        state = x - a * y;
        return y;
    }
}
