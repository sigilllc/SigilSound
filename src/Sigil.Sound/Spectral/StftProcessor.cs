namespace Sigil.Sound.Spectral;

/// <summary>
/// Edits one frame's spectrum in place. <paramref name="re"/> and <paramref name="im"/> hold all <c>Size</c> bins of the windowed
/// frame; for a real result the spectrum must stay symmetric (see <see cref="SpectralMath.MirrorLowerHalf"/>), and anything left in
/// the imaginary part after the inverse transform is discarded.
/// </summary>
internal delegate void StftFrameHandler(Span<float> re, Span<float> im);

/// <summary>
/// Streams one channel of audio through a short-time Fourier transform: it collects samples, runs your handler on the spectrum of
/// every overlapping frame, and overlap-adds the results back into a continuous signal. It owns the part of spectral processing
/// that is easy to get subtly wrong (frame alignment, window gain, latency), so operators only write the spectral edit.
/// </summary>
/// <remarks>
/// <para>
/// Works with any call size, including one sample at a time: the output is the same however the input is chopped up. Output lags
/// the input by exactly <see cref="Latency"/> frames, because a sample is only final once the last frame that covers it has been
/// added. With a handler that does nothing the output is the input delayed by <see cref="Latency"/>.
/// </para>
/// <para>
/// Nothing allocates after construction. The handler runs inside <see cref="Process"/> on every <c>hop</c>th sample, so one call
/// may carry a burst of frame work; size the frame for the latency and CPU you can afford.
/// </para>
/// <para>
/// Use one processor per channel. Create it where you build the operator, since it holds the audio state.
/// </para>
/// </remarks>
internal sealed class StftProcessor
{
    private readonly FftPlan _plan;
    private readonly StftFrameHandler _handler;
    private readonly float[] _window;
    private readonly float[] _input;
    private readonly float[] _accumulator;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly int _size;
    private readonly int _hop;
    private readonly int _inputMask;
    private readonly int _accumulatorMask;
    private readonly float _gain;
    private long _written;

    /// <param name="fftSize">The frame length: a power of two, at least 4.</param>
    /// <param name="hopSize">Samples between frames. With the default window this must be <paramref name="fftSize"/> / 4 or smaller (more overlap).</param>
    /// <param name="handler">Edits each frame's spectrum.</param>
    /// <param name="window">
    /// The window applied before and after the handler, <paramref name="fftSize"/> long; a periodic Hann window if null.
    /// The squares of its shifted copies must add to a constant at this hop, which is checked.
    /// </param>
    /// <exception cref="ArgumentException">The window and hop do not overlap-add to a constant, so the output would wobble in level.</exception>
    public StftProcessor(int fftSize, int hopSize, StftFrameHandler handler, float[]? window = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (fftSize < 4 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fftSize), fftSize, "The frame size must be a power of two, at least 4.");
        if (hopSize < 1 || hopSize > fftSize)
            throw new ArgumentOutOfRangeException(nameof(hopSize), hopSize, "The hop must be between 1 and the frame size.");
        if (fftSize % hopSize != 0)
            throw new ArgumentException("The frame size must be a whole number of hops.", nameof(hopSize));
        if (window is not null && window.Length != fftSize)
            throw new ArgumentException("The window must be as long as the frame.", nameof(window));

        _plan = new FftPlan(fftSize);
        _handler = handler;
        _size = fftSize;
        _hop = hopSize;
        _window = window is null ? Windows.Hann(fftSize) : (float[])window.Clone();
        _input = new float[fftSize];
        _accumulator = new float[fftSize * 2];
        _re = new float[fftSize];
        _im = new float[fftSize];
        _inputMask = fftSize - 1;
        _accumulatorMask = fftSize * 2 - 1;
        _gain = 1f / OverlapSum(_window, hopSize);
    }

    /// <summary>The frame length.</summary>
    public int FrameSize => _size;

    /// <summary>The samples between frames.</summary>
    public int Hop => _hop;

    /// <summary>How many frames later than the input the output is: the frame size.</summary>
    public int Latency => _size;

    /// <summary>
    /// How many more samples can be fed before the next frame is run (and its handler called). A caller that needs to set
    /// something up for each frame, such as a value that changes over time, feeds exactly this many and then sets it up.
    /// </summary>
    public int SamplesToNextFrame => _hop - (int)(_written % _hop);

    /// <summary>
    /// Feeds <paramref name="input"/> in and writes the same number of samples to <paramref name="output"/>. The two may be the same
    /// span, to process in place.
    /// </summary>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("The output must be at least as long as the input.", nameof(output));

        for (int i = 0; i < input.Length; i++)
        {
            _input[(int)(_written & _inputMask)] = input[i];
            _written++;
            if (_written % _hop == 0)
                RunFrame();

            // The sample one frame length back is complete: every frame that covers it has been added.
            int slot = (int)((_written - 1 - _size) & _accumulatorMask);
            output[i] = _accumulator[slot];
            _accumulator[slot] = 0f;
        }
    }

    /// <summary>Forgets all audio, as if the processor had just been made.</summary>
    public void Reset()
    {
        Array.Clear(_input);
        Array.Clear(_accumulator);
        _written = 0;
    }

    private void RunFrame()
    {
        long first = _written - _size;
        for (int k = 0; k < _size; k++)
        {
            _re[k] = _input[(int)((first + k) & _inputMask)] * _window[k];
            _im[k] = 0f;
        }

        _plan.Forward(_re, _im);
        _handler(_re, _im);
        _plan.Inverse(_re, _im);

        for (int k = 0; k < _size; k++)
            _accumulator[(int)((first + k) & _accumulatorMask)] += _re[k] * _window[k] * _gain;
    }

    // What the squared window adds up to where its copies overlap. It must be the same at every sample position, or the output
    // level would ripple with the hop.
    private static float OverlapSum(float[] window, int hop)
    {
        double reference = 0;
        for (int i = 0; i < hop; i++)
        {
            double sum = 0;
            for (int j = i; j < window.Length; j += hop)
                sum += (double)window[j] * window[j];
            if (i == 0)
                reference = sum;
            else if (Math.Abs(sum - reference) > 1e-3 * Math.Max(1.0, reference))
                throw new ArgumentException("The window and hop do not overlap-add to a constant; use more overlap (a smaller hop).", nameof(hop));
        }
        return (float)reference;
    }
}
