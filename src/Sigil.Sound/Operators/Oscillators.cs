namespace Sigil.Sound.Operators;

/// <summary>Sine oscillator with an audio-rate frequency.</summary>
internal sealed class SineOperator : IOperator, IResettable
{
    private const double TwoPi = 2.0 * Math.PI;

    private readonly ParamSource _frequency;
    private readonly double _twoPiSamplePeriod;
    private readonly HostClock? _clock;
    private double _phase;
    private bool _synced;

    public SineOperator(ParamSource frequency, RenderContext context)
    {
        _frequency = frequency;
        _twoPiSamplePeriod = TwoPi / context.SampleRate;
        _clock = context.TimeClock;
    }

    public void Reset()
    {
        _phase = 0;
        _synced = false;
        _frequency.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        var frequency = _frequency.Next(block.Length);
        if (!_synced)
        {
            _synced = true;
            if (_clock is not null && _frequency.IsConstant)
                _phase = TwoPi * HostTime.Cycles(Dsp.Finite(frequency[0], 0f), _clock);
        }

        for (int i = 0; i < block.Length; i++)
        {
            block[i] = StereoFrame.Mono((float)Math.Sin(_phase));

            _phase += _twoPiSamplePeriod * Dsp.Finite(frequency[i], 0f);
            // Wrap in both directions so negative or very high frequencies stay stable on long runs.
            if (_phase >= TwoPi)
                _phase %= TwoPi;
            else if (_phase < 0)
                _phase = (_phase % TwoPi) + TwoPi;
        }
    }
}

/// <summary>Sawtooth oscillator, band-limited with PolyBLEP to keep aliasing down at high frequencies.</summary>
internal sealed class SawOperator : IOperator, IResettable
{
    private readonly ParamSource _frequency;
    private readonly double _samplePeriod;
    private readonly HostClock? _clock;
    private double _phase;
    private bool _synced;

    public SawOperator(ParamSource frequency, RenderContext context)
    {
        _frequency = frequency;
        _samplePeriod = context.SamplePeriod;
        _clock = context.TimeClock;
    }

    public void Reset()
    {
        _phase = 0;
        _synced = false;
        _frequency.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        var frequency = _frequency.Next(block.Length);
        if (!_synced)
        {
            _synced = true;
            if (_clock is not null && _frequency.IsConstant)
                _phase = HostTime.Cycles(Dsp.Finite(frequency[0], 0f), _clock);
        }

        // PolyBlep.Correction and Advance are written out here: one voice runs this loop for every frame, and where
        // the .NET interpreter runs (the browser) each call and its struct copies are a large part of the cost.
        double phase = _phase;
        double samplePeriod = _samplePeriod;
        for (int i = 0; i < block.Length; i++)
        {
            float hz = frequency[i];
            // NaN and infinity both fail this (x - x is NaN for them) and are treated as a frequency of zero.
            if (hz - hz != 0f)
                hz = 0f;
            double step = hz * samplePeriod;
            // Past Nyquist the band-limiting correction has nothing sensible to do; cap it.
            float increment = (float)(step < 0 ? -step : step);
            if (increment > PolyBlep.MaxIncrement)
                increment = PolyBlep.MaxIncrement;
            float p = (float)phase;

            // Naive ramp from -1 to +1, with the discontinuity at the wrap smoothed out.
            float correction = 0f;
            if (increment > 0)
            {
                if (p < increment)
                {
                    float t = p / increment;
                    correction = t + t - t * t - 1.0f;
                }
                else if (p > 1.0f - increment)
                {
                    float t = (p - 1.0f) / increment;
                    correction = t * t + t + t + 1.0f;
                }
            }

            float value = 2.0f * p - 1.0f - correction;
            block[i] = new StereoFrame(value, value);

            phase += step;
            if (phase >= 1.0 || phase < 0)
                phase -= Math.Floor(phase);
        }

        _phase = phase;
    }
}

/// <summary>Pulse oscillator with a variable duty cycle, band-limited with PolyBLEP on both edges.</summary>
internal sealed class SquareOperator : IOperator, IResettable
{
    private const float MinWidth = 0.01f;
    private const float MaxWidth = 0.99f;

    private readonly ParamSource _frequency;
    private readonly ParamSource _width;
    private readonly double _samplePeriod;
    private readonly HostClock? _clock;
    private double _phase;
    private bool _synced;

    public SquareOperator(ParamSource frequency, ParamSource width, RenderContext context)
    {
        _frequency = frequency;
        _width = width;
        _samplePeriod = context.SamplePeriod;
        _clock = context.TimeClock;
    }

    public void Reset()
    {
        _phase = 0;
        _synced = false;
        _frequency.Reset();
        _width.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        var frequency = _frequency.Next(block.Length);
        var width = _width.Next(block.Length);
        if (!_synced)
        {
            _synced = true;
            if (_clock is not null && _frequency.IsConstant)
                _phase = HostTime.Cycles(Dsp.Finite(frequency[0], 0f), _clock);
        }

        for (int i = 0; i < block.Length; i++)
        {
            double step = Dsp.Finite(frequency[i], 0f) * _samplePeriod;
            float increment = (float)Math.Min(Math.Abs(step), PolyBlep.MaxIncrement);
            float duty = Math.Clamp(Dsp.Finite(width[i], 0.5f), MinWidth, MaxWidth);
            float phase = (float)_phase;

            float value = phase < duty ? 1.0f : -1.0f;
            // Rising edge at phase 0, falling edge at phase == duty.
            value += PolyBlep.Correction(phase, increment);
            float shifted = phase - duty;
            if (shifted < 0) shifted += 1.0f;
            value -= PolyBlep.Correction(shifted, increment);

            block[i] = StereoFrame.Mono(value);
            _phase = PolyBlep.Advance(_phase, step);
        }
    }
}

/// <summary>White noise from a seeded xorshift generator: allocation-free and reproducible.</summary>
internal sealed class NoiseOperator : IOperator, IResettable
{
    private readonly uint _seed;
    private uint _state;

    public NoiseOperator(int seed)
    {
        _seed = seed == 0 ? 0x9E3779B9u : (uint)seed;
        _state = _seed;
    }

    public void Reset() => _state = _seed;

    public void RenderBlock(Span<StereoFrame> block)
    {
        for (int i = 0; i < block.Length; i++)
            block[i] = new StereoFrame(NextBipolar(), NextBipolar());
    }

    private float NextBipolar()
    {
        uint x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        // Top 24 bits give a uniform float in [0, 1).
        return (x >> 8) * (2.0f / (1 << 24)) - 1.0f;
    }
}

/// <summary>Outputs the same value on both channels forever.</summary>
internal sealed class ConstantOperator(float value) : IOperator
{
    public void RenderBlock(Span<StereoFrame> block) => block.Fill(StereoFrame.Mono(value));
}

/// <summary>Outputs <c>function(t)</c> on both channels, where t is the time in seconds since the first frame.</summary>
internal sealed class FunctionOperator : IOperator, IResettable
{
    private readonly Func<double, double> _function;
    private readonly double _samplePeriod;
    private readonly HostClock? _clock;
    private long _position;

    public FunctionOperator(Func<double, double> function, RenderContext context)
    {
        _function = function;
        _samplePeriod = context.SamplePeriod;
        _clock = context.TimeClock;
    }

    public void Reset() => _position = 0;

    public void RenderBlock(Span<StereoFrame> block)
    {
        // With a host clock, time is the host's, so a swapped-in function carries on where the old one was.
        long start = _clock?.SamplePosition ?? _position;
        for (int i = 0; i < block.Length; i++)
            block[i] = StereoFrame.Mono((float)_function((start + i) * _samplePeriod));
        _position += block.Length;
    }
}

internal static class PolyBlep
{
    /// <summary>The largest per-sample phase step the correction handles: Nyquist.</summary>
    public const float MaxIncrement = 0.5f;

    /// <summary>
    /// Polynomial band-limited step correction for a discontinuity at phase 0 (wrapping from 1).
    /// <paramref name="increment"/> is the per-sample phase increment.
    /// </summary>
    public static float Correction(float phase, float increment)
    {
        if (increment <= 0)
            return 0;

        if (phase < increment)
        {
            float t = phase / increment;
            return t + t - t * t - 1.0f;
        }

        if (phase > 1.0f - increment)
        {
            float t = (phase - 1.0f) / increment;
            return t * t + t + t + 1.0f;
        }

        return 0;
    }

    /// <summary>Advances a normalized phase and wraps it into [0, 1).</summary>
    public static double Advance(double phase, double increment)
    {
        phase += increment;
        if (phase >= 1.0 || phase < 0)
            phase -= Math.Floor(phase);
        return phase;
    }
}
