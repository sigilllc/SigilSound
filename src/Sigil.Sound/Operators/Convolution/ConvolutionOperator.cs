namespace Sigil.Sound.Operators;

/// <summary>
/// An impulse response as <see cref="Sound.Convolve"/> uses it: prepared once for each sample rate it is rendered at,
/// then shared by every compile of the sound. Preparing means trimming to <see cref="MaxSeconds"/>, converting the sample
/// rate, and scaling, and then transforming into a <see cref="ConvolutionKernel"/>, which is the expensive part.
/// </summary>
internal sealed class ImpulseResponseSource
{
    /// <summary>The longest impulse response used. A longer one is cut here, with a fade-out so the cut cannot click.</summary>
    public const double MaxSeconds = 10;

    /// <summary>The largest boost normalisation may apply, so a near-silent response does not turn into noise.</summary>
    private const double MaxNormalizeBoostDb = 24;

    private readonly float[] _left;
    private readonly float[] _right;
    private readonly bool _mono;
    private readonly int _sampleRate;
    private readonly IrNormalize _normalize;
    private readonly Dictionary<int, ConvolutionKernel> _kernels = [];

    public ImpulseResponseSource(AudioBuffer impulse, IrNormalize normalize)
    {
        // A copy, so the sound stays what it was described as even if the buffer's owner reuses its frames.
        _left = impulse.Left();
        _right = impulse.Right();
        _mono = _left.AsSpan().SequenceEqual(_right);
        _sampleRate = impulse.SampleRate;
        _normalize = normalize;
    }

    /// <summary>The kernel for a render at <paramref name="sampleRate"/>; built on first use, then reused.</summary>
    public ConvolutionKernel KernelFor(int sampleRate)
    {
        lock (_kernels)
        {
            if (!_kernels.TryGetValue(sampleRate, out var kernel))
                _kernels[sampleRate] = kernel = Prepare(sampleRate);
            return kernel;
        }
    }

    private ConvolutionKernel Prepare(int rate)
    {
        long cap = (long)Math.Floor(MaxSeconds * _sampleRate);
        int length = (int)Math.Min(_left.Length, cap);
        bool truncated = length < _left.Length;

        float[] left, right;
        if (rate == _sampleRate)
        {
            left = _left.AsSpan(0, length).ToArray();
            right = _mono ? left : _right.AsSpan(0, length).ToArray();
        }
        else
        {
            var resampler = new SincResampler(_sampleRate, rate);
            int converted = resampler.OutputLength(length);
            left = new float[converted];
            resampler.Render(_left, 0, length, left, 0, converted);
            right = left;
            if (!_mono)
            {
                right = new float[converted];
                resampler.Render(_right, 0, length, right, 0, converted);
            }
        }

        if (truncated)
            FadeOut(left, right, rate);
        Scale(left, right, _normalize);
        return new ConvolutionKernel(left, right);
    }

    /// <summary>Cosine fade over the last 5% (at least 5 ms) so the cut leaves no click.</summary>
    private static void FadeOut(float[] left, float[] right, int rate)
    {
        int fade = Math.Max((int)Math.Round(left.Length * 0.05), (int)Math.Round(rate * 0.005));
        fade = Math.Min(fade, left.Length / 2);
        for (int i = 0; i < fade; i++)
        {
            float gain = (float)(0.5 * (1 + Math.Cos(Math.PI * (i + 1) / fade)));
            int index = left.Length - fade + i;
            left[index] *= gain;
            if (!ReferenceEquals(left, right)) right[index] *= gain;
        }
    }

    private static void Scale(float[] left, float[] right, IrNormalize normalize)
    {
        if (normalize == IrNormalize.Off)
            return;

        bool mono = ReferenceEquals(left, right);
        double peak = 0, energy = 0;
        for (int i = 0; i < left.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(left[i]));
            energy += (double)left[i] * left[i];
            if (!mono)
            {
                peak = Math.Max(peak, Math.Abs(right[i]));
                energy += (double)right[i] * right[i];
            }
        }

        double scale = 1;
        if (normalize == IrNormalize.Peak)
        {
            if (peak > 1e-12) scale = 1 / peak;
        }
        else
        {
            // Mean power across the channels, so a stereo response keeps its level relative to a mono one.
            double power = mono ? energy : energy * 0.5;
            if (power > 1e-24) scale = 1 / Math.Sqrt(power);
        }

        float gain = (float)Math.Min(scale, Math.Pow(10, MaxNormalizeBoostDb / 20));
        for (int i = 0; i < left.Length; i++) left[i] *= gain;
        if (!mono)
            for (int i = 0; i < right.Length; i++) right[i] *= gain;
    }
}

/// <summary>
/// Convolves its input with an impulse response, then blends the result with the dry input. The convolver adds no latency,
/// so the dry path needs no compensation.
/// </summary>
internal sealed class ConvolutionOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _mix;
    private readonly ConvolutionEngine _engine;
    private readonly float[] _inLeft;
    private readonly float[] _inRight;
    private readonly float[] _wetLeft;
    private readonly float[] _wetRight;

    public ConvolutionOperator(IOperator input, ParamSource mix, ConvolutionKernel kernel, RenderContext context)
    {
        _input = input;
        _mix = mix;
        _engine = new ConvolutionEngine(kernel);
        _inLeft = new float[context.BlockSize];
        _inRight = new float[context.BlockSize];
        _wetLeft = new float[context.BlockSize];
        _wetRight = new float[context.BlockSize];
    }

    public void Reset()
    {
        _engine.Reset();
        _input.ResetState();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        int n = block.Length;
        var mix = _mix.Next(n);

        // A NaN in the input would sit in the delay lines for the length of the response, and a subnormal would slow every
        // multiply-accumulate that touches it, so both are removed on the way in, from the dry path as well.
        for (int i = 0; i < n; i++)
        {
            _inLeft[i] = Clean(block[i].Left);
            _inRight[i] = Clean(block[i].Right);
            block[i] = new StereoFrame(_inLeft[i], _inRight[i]);
        }

        _engine.Process(_inLeft.AsSpan(0, n), _inRight.AsSpan(0, n), _wetLeft.AsSpan(0, n), _wetRight.AsSpan(0, n));

        for (int i = 0; i < n; i++)
            block[i] = Dsp.Blend(block[i], new StereoFrame(_wetLeft[i], _wetRight[i]), mix[i]);
    }

    private static float Clean(float value) => float.IsFinite(value) && MathF.Abs(value) >= 1e-20f ? value : 0f;
}
