namespace Sigil.Sound.Operators;

/// <summary>
/// A vocal-tract resonator: four band-pass resonators per channel, tuned to the first four formants of a vowel. The vowel
/// position morphs continuously through A, E, I, O and U. Formant frequencies, bandwidths and levels are interpolated
/// between the neighbouring vowels, and the coefficients are updated once per block.
/// </summary>
internal sealed class VowelFilterOperator : IOperator, IResettable
{
    // The number of formants and of vowel rows in the tables below.
    private const int Formants = 4;
    private const int VowelCount = 5;
    private const float DenormalOffset = 1e-25f;
    private const float MinQ = 0.5f;

    // Formant centres in Hz for A, E, I, O and U (one row per vowel). These are the commonly used averages for an adult voice.
    private static readonly float[,] FormantFrequencies =
    {
        { 800f, 1150f, 2900f, 3900f },
        { 350f, 2000f, 2800f, 3600f },
        { 270f, 2140f, 2950f, 3900f },
        { 450f, 800f, 2830f, 3800f },
        { 325f, 700f, 2700f, 3800f },
    };

    // Bandwidths in Hz, the width of each formant's peak.
    private static readonly float[,] FormantBandwidths =
    {
        { 80f, 90f, 120f, 130f },
        { 60f, 100f, 120f, 150f },
        { 60f, 90f, 100f, 120f },
        { 70f, 80f, 100f, 130f },
        { 50f, 60f, 170f, 180f },
    };

    // Relative levels of the four formants: the first is the strongest and the others fall off.
    private static readonly float[,] FormantLevels =
    {
        { 1.00f, 0.50f, 0.25f, 0.12f },
        { 1.00f, 0.60f, 0.20f, 0.10f },
        { 1.00f, 0.70f, 0.30f, 0.10f },
        { 1.00f, 0.40f, 0.20f, 0.10f },
        { 1.00f, 0.25f, 0.10f, 0.05f },
    };

    private readonly IOperator _input;
    private readonly ParamSource _vowel;
    private readonly ParamSource _resonance;
    private readonly int _sampleRate;

    // Per formant: the normalised band-pass coefficients (b1 is zero, so b2 is minus b0) and the level.
    private readonly float[] _b0 = new float[Formants];
    private readonly float[] _a1 = new float[Formants];
    private readonly float[] _a2 = new float[Formants];
    private readonly float[] _level = new float[Formants];

    // Direct form II transposed state: two values per formant, per channel.
    private readonly float[] _stateLeft = new float[Formants * 2];
    private readonly float[] _stateRight = new float[Formants * 2];

    private float _lastVowel = float.NaN;
    private float _lastResonance = float.NaN;

    public VowelFilterOperator(IOperator input, ParamSource vowel, ParamSource resonance, RenderContext context)
    {
        _input = input;
        _vowel = vowel;
        _resonance = resonance;
        _sampleRate = context.SampleRate;
    }

    public void Reset()
    {
        Array.Clear(_stateLeft);
        Array.Clear(_stateRight);
        _input.ResetState();
        _vowel.Reset();
        _resonance.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var vowel = _vowel.Next(block.Length);
        var resonance = _resonance.Next(block.Length);

        // A fixed vowel is set up once per block. A moving one is followed frame by frame, so a morph sounds continuous.
        bool fixedCoefficients = _vowel.IsConstant && _resonance.IsConstant;
        if (fixedCoefficients)
            UpdateCoefficients(Dsp.Finite(vowel[0], 0f), Dsp.Finite(resonance[0], 0.5f));

        for (int i = 0; i < block.Length; i++)
        {
            if (!fixedCoefficients)
                UpdateCoefficients(Dsp.Finite(vowel[i], 0f), Dsp.Finite(resonance[i], 0.5f));

            var x = block[i];
            float left = Dsp.Safe(x.Left);
            float right = Dsp.Safe(x.Right);
            float outLeft = 0f, outRight = 0f;

            for (int f = 0; f < Formants; f++)
            {
                int s = 2 * f;
                // A band-pass in transposed direct form II, with b1 = 0 and b2 = -b0.
                float yl = _b0[f] * left + _stateLeft[s];
                _stateLeft[s] = -_a1[f] * yl + _stateLeft[s + 1] + DenormalOffset;
                _stateLeft[s + 1] = -_b0[f] * left - _a2[f] * yl;
                outLeft += _level[f] * yl;

                float yr = _b0[f] * right + _stateRight[s];
                _stateRight[s] = -_a1[f] * yr + _stateRight[s + 1] + DenormalOffset;
                _stateRight[s + 1] = -_b0[f] * right - _a2[f] * yr;
                outRight += _level[f] * yr;
            }

            block[i] = new StereoFrame(outLeft, outRight);
        }
    }

    // Maps the 0..1 vowel position onto the five vowel rows and interpolates each formant between the two nearest.
    // Resonance scales the Q of every formant: 0 gives broad, soft formants and 1 gives sharp, ringing ones.
    private void UpdateCoefficients(float vowel, float resonance)
    {
        if (vowel == _lastVowel && resonance == _lastResonance)
            return;
        _lastVowel = vowel;
        _lastResonance = resonance;

        float position = Math.Clamp(vowel, 0f, 1f) * (VowelCount - 1);
        int row = Math.Min((int)position, VowelCount - 1);
        float fraction = position - row;
        int next = Math.Min(row + 1, VowelCount - 1);
        float q = (0.5f + Math.Clamp(resonance, 0f, 1f));

        for (int f = 0; f < Formants; f++)
        {
            float frequency = Lerp(FormantFrequencies[row, f], FormantFrequencies[next, f], fraction);
            float bandwidth = Lerp(FormantBandwidths[row, f], FormantBandwidths[next, f], fraction);
            float level = Lerp(FormantLevels[row, f], FormantLevels[next, f], fraction);

            // A narrower bandwidth means a higher Q: the peak is the frequency divided by the bandwidth, scaled by resonance.
            double centre = Math.Clamp(frequency, 20.0, _sampleRate * 0.45);
            double formantQ = Math.Max(MinQ, centre / Math.Max(1f, bandwidth) * q);
            double w0 = 2.0 * Math.PI * centre / _sampleRate;
            double alpha = Math.Sin(w0) / (2.0 * formantQ);
            double a0 = 1.0 + alpha;

            _b0[f] = (float)(alpha / a0);
            _a1[f] = (float)(-2.0 * Math.Cos(w0) / a0);
            _a2[f] = (float)((1.0 - alpha) / a0);
            _level[f] = level;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
