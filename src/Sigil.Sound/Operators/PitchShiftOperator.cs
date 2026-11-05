using Sigil.Sound.Spectral;

namespace Sigil.Sound.Operators;

/// <summary>
/// Shifts the pitch of a stereo signal in the frequency domain, each channel through its own short-time transform. The
/// frames of both channels fall on the same samples, so the pitch can follow a signal that changes while it plays.
/// </summary>
internal sealed class PitchShiftOperator : IOperator, IResettable
{
    private const float MaxSemitones = 24f;

    private readonly IOperator _input;
    private readonly ParamSource _semitones;
    private readonly ParamSource _mix;
    private readonly SpectralShifter _shifterLeft;
    private readonly SpectralShifter _shifterRight;
    private readonly StftProcessor _left;
    private readonly StftProcessor _right;
    private readonly float[] _dryLeft;
    private readonly float[] _dryRight;
    private readonly float[] _wetLeft;
    private readonly float[] _wetRight;
    private readonly float[] _delayLeft;
    private readonly float[] _delayRight;
    private readonly int _latency;
    private int _delayPosition;

    public PitchShiftOperator(IOperator input, ParamSource semitones, ParamSource mix, RenderContext context)
    {
        _input = input;
        _semitones = semitones;
        _mix = mix;

        int size = FrameSizeFor(context.SampleRate);
        int hop = size / 4;
        _shifterLeft = new SpectralShifter(size, hop);
        _shifterRight = new SpectralShifter(size, hop);
        _left = new StftProcessor(size, hop, _shifterLeft.Process);
        _right = new StftProcessor(size, hop, _shifterRight.Process);
        _latency = _left.Latency;

        _dryLeft = new float[context.BlockSize];
        _dryRight = new float[context.BlockSize];
        _wetLeft = new float[context.BlockSize];
        _wetRight = new float[context.BlockSize];
        // The dry copy is delayed by the same amount as the shifted one, so a mix of the two stays in time.
        _delayLeft = new float[_latency];
        _delayRight = new float[_latency];
    }

    /// <summary>The frame length used at a sample rate: the power of two nearest 40 ms, which is 2048 at 44.1 and 48 kHz.</summary>
    public static int FrameSizeFor(int sampleRate)
    {
        int exponent = (int)Math.Round(Math.Log2(sampleRate * 0.04));
        return 1 << Math.Clamp(exponent, 9, 13);
    }

    /// <summary>How many frames the output lags the input.</summary>
    public int Latency => _latency;

    public void Reset()
    {
        _input.ResetState();
        _semitones.Reset();
        _mix.Reset();
        _left.Reset();
        _right.Reset();
        _shifterLeft.Reset();
        _shifterRight.Reset();
        Array.Clear(_delayLeft);
        Array.Clear(_delayRight);
        _delayPosition = 0;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        int count = block.Length;
        var semitones = _semitones.Next(count);
        var mix = _mix.Next(count);

        for (int i = 0; i < count; i++)
        {
            _dryLeft[i] = Dsp.Safe(block[i].Left);
            _dryRight[i] = Dsp.Safe(block[i].Right);
        }

        // Frames run at fixed points in the stream, so feed up to each one, set the shift for it from the control signal
        // at that moment, and carry on. The frame is processed with the value at its last sample.
        for (int done = 0; done < count;)
        {
            int segment = Math.Min(count - done, _left.SamplesToNextFrame);
            float shift = Math.Clamp(Dsp.Finite(semitones[done + segment - 1], 0f), -MaxSemitones, MaxSemitones);
            float ratio = MathF.Pow(2f, shift / 12f);
            _shifterLeft.Ratio = ratio;
            _shifterRight.Ratio = ratio;

            _left.Process(_dryLeft.AsSpan(done, segment), _wetLeft.AsSpan(done, segment));
            _right.Process(_dryRight.AsSpan(done, segment), _wetRight.AsSpan(done, segment));
            done += segment;
        }

        for (int i = 0; i < count; i++)
        {
            float wet = Math.Clamp(Dsp.Finite(mix[i], 1f), 0f, 1f);
            float dryL = _delayLeft[_delayPosition];
            float dryR = _delayRight[_delayPosition];
            _delayLeft[_delayPosition] = _dryLeft[i];
            _delayRight[_delayPosition] = _dryRight[i];
            if (++_delayPosition == _latency) _delayPosition = 0;

            block[i] = new StereoFrame(
                dryL * (1f - wet) + _wetLeft[i] * wet,
                dryR * (1f - wet) + _wetRight[i] * wet);
        }
    }
}
