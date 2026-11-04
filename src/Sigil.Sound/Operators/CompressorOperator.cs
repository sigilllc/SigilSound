namespace Sigil.Sound.Operators;

/// <summary>
/// A feed-forward dynamic range compressor. A peak detector follows the larger channel of the key signal, which is the
/// sidechain when one is given and the input otherwise, with separate attack and release time constants. A soft-knee
/// gain computer turns the level above the threshold into gain reduction, and makeup gain then lifts the result.
/// With lookahead, the detector runs ahead of the audio by a fixed number of frames, so the gain is already falling
/// when a transient reaches the output.
/// </summary>
internal sealed class CompressorOperator : IOperator, IResettable
{
    // The detector never reads below -120 dB, so the logarithm stays finite on silence.
    private const float MinLevel = 1e-6f;
    private const float DenormalFloor = 1e-15f;
    private const float DecibelsPerNeper = 8.6858896f;   // 20 / ln(10)
    private const float NepersPerDecibel = 0.11512925f;  // ln(10) / 20

    private readonly IOperator _input;
    private readonly IOperator? _sidechain;
    private readonly StereoFrame[] _sidechainBuffer;
    private readonly ParamSource _threshold;
    private readonly ParamSource _ratio;
    private readonly ParamSource _attack;
    private readonly ParamSource _release;
    private readonly ParamSource _knee;
    private readonly ParamSource _makeup;
    private readonly SmoothingCoefficient _attackCoefficient;
    private readonly SmoothingCoefficient _releaseCoefficient;

    // A power-of-two ring holds the audio while the detector looks ahead; the mask wraps indices without a branch.
    private readonly StereoFrame[] _ring;
    private readonly int _mask;
    private readonly int _lookaheadFrames;
    private int _writeIndex;

    private float _envelope;

    public CompressorOperator(IOperator input, IOperator? sidechain, ParamSource threshold, ParamSource ratio,
        ParamSource attack, ParamSource release, ParamSource knee, ParamSource makeup, int lookaheadFrames,
        RenderContext context)
    {
        _input = input;
        _sidechain = sidechain;
        _sidechainBuffer = sidechain is null ? Array.Empty<StereoFrame>() : new StereoFrame[context.BlockSize];
        _threshold = threshold;
        _ratio = ratio;
        _attack = attack;
        _release = release;
        _knee = knee;
        _makeup = makeup;
        _attackCoefficient = new SmoothingCoefficient(context.SampleRate);
        _releaseCoefficient = new SmoothingCoefficient(context.SampleRate);

        int capacity = 1;
        while (capacity <= lookaheadFrames)
            capacity <<= 1;
        _ring = new StereoFrame[capacity];
        _mask = capacity - 1;
        _lookaheadFrames = lookaheadFrames;
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _writeIndex = 0;
        _envelope = 0;
        _input.ResetState();
        _sidechain?.ResetState();
        _threshold.Reset();
        _ratio.Reset();
        _attack.Reset();
        _release.Reset();
        _knee.Reset();
        _makeup.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        Span<StereoFrame> key = default;
        if (_sidechain is not null)
        {
            key = _sidechainBuffer.AsSpan(0, block.Length);
            _sidechain.RenderBlock(key);
        }

        var threshold = _threshold.Next(block.Length);
        var ratio = _ratio.Next(block.Length);
        var attack = _attack.Next(block.Length);
        var release = _release.Next(block.Length);
        var knee = _knee.Next(block.Length);
        var makeup = _makeup.Next(block.Length);

        float envelope = _envelope;
        int writeIndex = _writeIndex;
        int mask = _mask;
        int lookahead = _lookaheadFrames;
        bool sidechained = _sidechain is not null;

        for (int i = 0; i < block.Length; i++)
        {
            var audio = new StereoFrame(Dsp.Safe(block[i].Left), Dsp.Safe(block[i].Right));

            // The detector listens to the key signal now, while the audio it controls is still `lookahead` frames behind.
            var detect = sidechained
                ? new StereoFrame(Dsp.Safe(key[i].Left), Dsp.Safe(key[i].Right))
                : audio;
            float level = MathF.Max(MathF.Abs(detect.Left), MathF.Abs(detect.Right));

            // Rising input is followed with the attack time, falling input with the release time.
            float timeCoefficient = level > envelope
                ? _attackCoefficient.For(Dsp.Finite(attack[i], 0.01f))
                : _releaseCoefficient.For(Dsp.Finite(release[i], 0.1f));
            envelope = level + timeCoefficient * (envelope - level);
            if (envelope < DenormalFloor)
                envelope = 0f;

            float levelDb = DecibelsPerNeper * MathF.Log(MathF.Max(envelope, MinLevel));
            float gainReductionDb = GainReduction(levelDb - Dsp.Finite(threshold[i], -18f),
                Math.Max(1f, Dsp.Finite(ratio[i], 4f)), Math.Max(0f, Dsp.Finite(knee[i], 0f)));
            float gain = MathF.Exp(NepersPerDecibel * (gainReductionDb + Dsp.Finite(makeup[i], 0f)));

            // Write the audio into the ring, then read the frame written `lookahead` frames ago and apply the gain now.
            _ring[writeIndex] = audio;
            var delayed = _ring[(writeIndex - lookahead) & mask];
            writeIndex = (writeIndex + 1) & mask;
            block[i] = delayed * gain;
        }

        _envelope = envelope;
        _writeIndex = writeIndex;
    }

    // The gain computer, in decibels. `over` is how far the level sits above the threshold. Below the knee nothing happens,
    // above it the excess is divided by the ratio, and inside the knee the reduction eases in along a quadratic, so the
    // curve has no corner. The result is zero or negative.
    private static float GainReduction(float over, float ratio, float kneeDb)
    {
        float slope = 1f / ratio - 1f;
        float twiceOver = 2f * over;
        if (twiceOver <= -kneeDb)
            return 0f;
        if (twiceOver < kneeDb)
        {
            float x = over + 0.5f * kneeDb;
            return slope * x * x / (2f * kneeDb);
        }
        return slope * over;
    }
}
