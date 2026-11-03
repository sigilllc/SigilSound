namespace Sigil.Sound.Operators;

/// <summary>
/// A Schroeder reverberator: four damped feedback combs in parallel build the decaying echoes, then two
/// all-pass filters smooth them. Each channel has its own chain, with slightly longer delays on the right
/// so the two decorrelate into a stereo tail.
/// </summary>
internal sealed class ReverbOperator : IOperator, IResettable
{
    private static readonly float[] CombMilliseconds = [29.7f, 37.1f, 41.1f, 43.7f];
    private static readonly float[] AllPassMilliseconds = [5.0f, 1.7f];
    private const float RightSpread = 1.0157f;
    private const float AllPassGain = 0.5f;
    private const float WetGain = 0.3f;

    private readonly IOperator _input;
    private readonly ParamSource _mix;
    private readonly Chain _left;
    private readonly Chain _right;

    public ReverbOperator(IOperator input, ParamSource mix, double size, double decay, double damping,
        RenderContext context)
    {
        _input = input;
        _mix = mix;
        float dampingCoefficient = (float)Math.Clamp(damping, 0, 0.99);
        _left = new Chain(context.SampleRate, (float)size, 1f, (float)decay, dampingCoefficient);
        _right = new Chain(context.SampleRate, (float)size, RightSpread, (float)decay, dampingCoefficient);
    }

    public void Reset()
    {
        _left.Clear();
        _right.Clear();
        _input.ResetState();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var mix = _mix.Next(block.Length);

        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];
            var wet = new StereoFrame(_left.Process(dry.Left), _right.Process(dry.Right));
            block[i] = Dsp.Blend(dry, wet, mix[i]);
        }
    }

    private sealed class Chain
    {
        private readonly float[][] _combs;
        private readonly int[] _combPosition;
        private readonly float[] _combFeedback;
        private readonly float[] _combDamped;
        private readonly float _damping;

        private readonly float[][] _allPasses;
        private readonly int[] _allPassPosition;

        public Chain(int sampleRate, float size, float spread, float decay, float damping)
        {
            _damping = damping;
            decay = Math.Max(0.05f, decay);

            _combs = new float[CombMilliseconds.Length][];
            _combPosition = new int[_combs.Length];
            _combFeedback = new float[_combs.Length];
            _combDamped = new float[_combs.Length];
            for (int c = 0; c < _combs.Length; c++)
            {
                float seconds = CombMilliseconds[c] / 1000f * size * spread;
                _combs[c] = new float[Math.Max(2, (int)(seconds * sampleRate))];
                // Feedback that makes the loop lose 60 dB over the decay time.
                _combFeedback[c] = MathF.Pow(10f, -3f * seconds / decay);
            }

            _allPasses = new float[AllPassMilliseconds.Length][];
            _allPassPosition = new int[_allPasses.Length];
            for (int a = 0; a < _allPasses.Length; a++)
                _allPasses[a] = new float[Math.Max(2, (int)(AllPassMilliseconds[a] / 1000f * size * spread * sampleRate))];
        }

        public void Clear()
        {
            foreach (var buffer in _combs) Array.Clear(buffer);
            foreach (var buffer in _allPasses) Array.Clear(buffer);
            Array.Clear(_combPosition);
            Array.Clear(_combDamped);
            Array.Clear(_allPassPosition);
        }

        public float Process(float input)
        {
            input = Dsp.Safe(input);

            float sum = 0;
            for (int c = 0; c < _combs.Length; c++)
            {
                var buffer = _combs[c];
                float delayed = buffer[_combPosition[c]];
                sum += delayed;

                _combDamped[c] = delayed * (1f - _damping) + _combDamped[c] * _damping;
                buffer[_combPosition[c]] = input + _combDamped[c] * _combFeedback[c];
                if (++_combPosition[c] == buffer.Length) _combPosition[c] = 0;
            }

            float signal = sum * WetGain;
            for (int a = 0; a < _allPasses.Length; a++)
            {
                var buffer = _allPasses[a];
                float delayed = buffer[_allPassPosition[a]];
                buffer[_allPassPosition[a]] = signal + delayed * AllPassGain;
                signal = delayed - signal;
                if (++_allPassPosition[a] == buffer.Length) _allPassPosition[a] = 0;
            }

            return signal;
        }
    }
}
