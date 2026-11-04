namespace Sigil.Sound.Operators;

/// <summary>
/// A feedback delay network reverb. Eight delay lines are fed back through an orthogonal Hadamard matrix, which spreads
/// energy evenly between them and builds a dense, smooth tail rather than separate echoes. Each line has its own damping
/// and a decay gain, so the whole tail falls by 60 dB over the decay time. Input goes through a predelay and a chain of
/// all-pass diffusers first. With sheen, an octave-up copy of the tail is mixed in, so the highs shimmer above the reverb.
/// </summary>
internal sealed class FdnReverbOperator : IOperator, IResettable
{
    // Base line lengths in milliseconds. They are mutually unrelated, so the echoes never line up into a comb.
    private static readonly float[] LineMilliseconds = [29.7f, 37.1f, 41.1f, 43.7f, 47.3f, 53.1f, 59.9f, 61.3f];
    private static readonly float[] DiffuserMilliseconds = [4.77f, 3.59f, 12.73f, 9.31f];
    private const int LineCount = 8;
    private const float DiffuserGain = 0.6f;
    private const float InputGain = 0.35f;
    private const float OutputGain = 0.3f;
    private const float MaxSheen = 0.7f;

    // 1 / sqrt(8): scales the Hadamard matrix so that it preserves energy.
    private const float HadamardScale = 0.35355339f;

    private readonly IOperator _input;
    private readonly ParamSource _sheen;
    private readonly ParamSource _mix;
    private readonly MonoLine _predelay;
    private readonly float _predelayFrames;
    private readonly Diffuser[] _diffusers;
    private readonly MonoLine[] _lines;
    private readonly float[] _lineFrames;
    private readonly float[] _lineGain;
    private readonly float[] _damped;
    private readonly float[] _outputs;
    private readonly float[] _mixed;
    private readonly float[] _tail = new float[2];
    private readonly float[] _shiftedTail = new float[2];
    private readonly float _damping;
    private readonly OctaveShifterBank _shifter = new(channels: 2);

    public FdnReverbOperator(IOperator input, ParamSource sheen, ParamSource mix, double decaySeconds, double size,
        double damping, double predelaySeconds, RenderContext context)
    {
        _input = input;
        _sheen = sheen;
        _mix = mix;
        _damping = (float)damping;

        // Every line length scales with the room size, so the buffers are sized once here for the largest room they will hold.
        // The predelay is an absolute time: it separates the sound from the room whatever the room's size.
        float framesPerMs = context.SampleRate / 1000f * (float)size;
        _predelayFrames = (float)(predelaySeconds * context.SampleRate);
        _predelay = new MonoLine((int)MathF.Ceiling(_predelayFrames) + 4);

        _diffusers = new Diffuser[DiffuserMilliseconds.Length];
        for (int k = 0; k < _diffusers.Length; k++)
            _diffusers[k] = new Diffuser((int)MathF.Ceiling(DiffuserMilliseconds[k] * framesPerMs) + 2, DiffuserGain);

        _lines = new MonoLine[LineCount];
        _lineFrames = new float[LineCount];
        _lineGain = new float[LineCount];
        for (int i = 0; i < LineCount; i++)
        {
            _lineFrames[i] = LineMilliseconds[i] * framesPerMs;
            _lines[i] = new MonoLine((int)MathF.Ceiling(_lineFrames[i]) + 4);
            // The gain that makes this line lose 60 dB over the decay time, for each trip round the loop.
            double lineSeconds = LineMilliseconds[i] / 1000.0 * size;
            _lineGain[i] = (float)Math.Pow(10.0, -3.0 * lineSeconds / decaySeconds);
        }

        _damped = new float[LineCount];
        _outputs = new float[LineCount];
        _mixed = new float[LineCount];
    }

    public void Reset()
    {
        _predelay.Clear();
        foreach (var diffuser in _diffusers)
            diffuser.Clear();
        foreach (var line in _lines)
            line.Clear();
        Array.Clear(_damped);
        Array.Clear(_outputs);
        Array.Clear(_mixed);
        Array.Clear(_tail);
        Array.Clear(_shiftedTail);
        _shifter.Clear();
        _input.ResetState();
        _sheen.Reset();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var sheen = _sheen.Next(block.Length);
        var mix = _mix.Next(block.Length);
        float damping = _damping;

        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];
            float mono = 0.5f * (Dsp.Safe(dry.Left) + Dsp.Safe(dry.Right));

            // Predelay, then diffusion: the first reflections are smeared before they enter the network.
            float input = _predelay.Read(_predelayFrames);
            _predelay.Write(mono);
            foreach (var diffuser in _diffusers)
                input = diffuser.Process(input);

            // Read every line before writing any, so each line is written with values from the same frame.
            for (int j = 0; j < LineCount; j++)
            {
                float delayed = _lines[j].Read(_lineFrames[j]);
                _damped[j] = delayed * (1f - damping) + _damped[j] * damping;
                _outputs[j] = _damped[j] * _lineGain[j];
            }

            // Mix the lines back together and feed them round the loop. The loop itself is the plain network, so the
            // decay time is exact at every sheen setting.
            Array.Copy(_outputs, _mixed, LineCount);
            Hadamard(_mixed);

            // Alternating signs spread the input across the lines, so the two ears start out different.
            for (int j = 0; j < LineCount; j++)
            {
                float signed = (j & 1) == 0 ? input : -input;
                _lines[j].Write(signed * InputGain + _mixed[j]);
            }

            // Even lines feed the left ear and odd lines the right, so the reverb is stereo.
            float left = (_outputs[0] + _outputs[2] + _outputs[4] + _outputs[6]) * OutputGain;
            float right = (_outputs[1] + _outputs[3] + _outputs[5] + _outputs[7]) * OutputGain;

            // Sheen mixes in an octave-up copy of the tail. It is taken from the output, not from inside the loop: a shifter
            // with gain near one inside a loop of gain near one grows without bound, so a feed-forward layer is the stable choice.
            float share = Math.Clamp(Dsp.Finite(sheen[i], 0f), 0f, MaxSheen);
            _tail[0] = left;
            _tail[1] = right;
            _shifter.Process(_tail, _shiftedTail);
            var wet = new StereoFrame(
                left + share * _shiftedTail[0],
                right + share * _shiftedTail[1]);
            block[i] = Dsp.Blend(dry, wet, mix[i]);
        }
    }

    // In-place 8-point Walsh-Hadamard transform, scaled to be orthonormal.
    private static void Hadamard(float[] v)
    {
        for (int span = 1; span < v.Length; span <<= 1)
            for (int start = 0; start < v.Length; start += span << 1)
                for (int j = start; j < start + span; j++)
                {
                    float a = v[j];
                    float b = v[j + span];
                    v[j] = a + b;
                    v[j + span] = a - b;
                }

        for (int j = 0; j < v.Length; j++)
            v[j] *= HadamardScale;
    }

    // A mono delay line with linear interpolation. Read returns the frame written `delay` frames ago, so read before writing.
    private sealed class MonoLine
    {
        private readonly float[] _buffer;
        private int _position;

        public MonoLine(int capacity) => _buffer = new float[Math.Max(4, capacity)];

        public float Read(float delay)
        {
            delay = Math.Clamp(delay, 1f, _buffer.Length - 2);
            int whole = (int)delay;
            float fraction = delay - whole;
            int newer = _position - whole;
            if (newer < 0) newer += _buffer.Length;
            int older = newer == 0 ? _buffer.Length - 1 : newer - 1;
            return _buffer[newer] + (_buffer[older] - _buffer[newer]) * fraction;
        }

        public void Write(float value)
        {
            _buffer[_position] = Dsp.Safe(value);
            if (++_position == _buffer.Length) _position = 0;
        }

        public void Clear()
        {
            Array.Clear(_buffer);
            _position = 0;
        }
    }

    // A Schroeder all-pass: it passes every frequency at the same level but smears their phase, which spreads transients
    // into a soft onset.
    private sealed class Diffuser(int length, float gain)
    {
        private readonly float[] _buffer = new float[Math.Max(2, length)];
        private int _position;

        public float Process(float x)
        {
            float delayed = _buffer[_position];
            float w = x + gain * delayed;
            _buffer[_position] = w;
            float y = delayed - gain * w;
            if (++_position == _buffer.Length) _position = 0;
            return y;
        }

        public void Clear()
        {
            Array.Clear(_buffer);
            _position = 0;
        }
    }

    // An octave-up pitch shifter. Two read heads move through a short delay at twice the write speed, so the signal they
    // read rises an octave. A sin-squared window fades each head in and out at the ends of its sweep, where the jump back
    // happens, so the reset is never heard, and the two windows add to unity gain.
    private sealed class OctaveShifterBank(int channels)
    {
        private const int BufferLength = 4096;
        private const int BufferMask = BufferLength - 1;
        private const int SweepFrames = 1024;
        private const int SweepMask = SweepFrames - 1;
        private const int HalfSweep = SweepFrames / 2;
        private const int MinimumDelay = 64;

        // Each head fades by sin² of its own sweep phase: zero at the wrap where its delay jumps, one at mid-sweep.
        // Head B is half a sweep behind head A, so the two windows sum to exactly 1: a crossfade that can never make a
        // coherent signal louder than its inputs.
        private static readonly float[] Window = BuildWindow();

        // One ring per channel, stored end to end. All channels share one write position and one sweep phase.
        private readonly float[] _buffers = new float[channels * BufferLength];
        private int _write;
        private int _phase;

        // Each read head moves one frame of delay per frame, so it covers two frames for each one written: an octave up.
        // Whole-frame delays keep the reads exact and the phase a counter, so no sine is evaluated per frame.
        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            int phaseB = (_phase + HalfSweep) & SweepMask;
            int delayA = MinimumDelay + (SweepFrames - _phase);
            int delayB = MinimumDelay + (SweepFrames - phaseB);
            float windowA = Window[_phase];
            float windowB = Window[phaseB];

            for (int j = 0; j < channels; j++)
            {
                int ring = j * BufferLength;
                _buffers[ring + _write] = Dsp.Safe(input[j]);
                float a = _buffers[ring + ((_write - delayA) & BufferMask)];
                float b = _buffers[ring + ((_write - delayB) & BufferMask)];
                output[j] = a * windowA + b * windowB;
            }

            _write = (_write + 1) & BufferMask;
            _phase = (_phase + 1) & SweepMask;
        }

        public void Clear()
        {
            Array.Clear(_buffers);
            _write = 0;
            _phase = 0;
        }

        private static float[] BuildWindow()
        {
            var window = new float[SweepFrames];
            for (int p = 0; p < SweepFrames; p++)
            {
                float sine = MathF.Sin(MathF.PI * p / SweepFrames);
                window[p] = sine * sine;
            }
            return window;
        }
    }
}
