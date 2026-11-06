namespace Sigil.Sound.Operators;

/// <summary>
/// Offline Blackman-windowed-sinc sample-rate converter that brings an impulse response to the rate of the render.
/// Output sample j is evaluated independently from the source, so it can be produced in any chunking and at any
/// fractional position.
///
/// Kernels are precomputed per fractional phase. The phase count is the reduced denominator of
/// dst/src (160 for 44.1k -> 48k), so common rates are exact; unusual ratios use a 4096-phase table.
/// </summary>
internal sealed class SincResampler
{
    private const int HalfWidth = 16;        // zero crossings on each side at the cutoff
    private const int MaxPhases = 4096;
    private const double CutoffMargin = 0.98; // keep the transition band inside Nyquist

    private readonly int _srcRate;
    private readonly int _dstRate;
    private readonly long _num;       // source samples per `_den` output samples, reduced
    private readonly int _phases;
    private readonly bool _exact;
    private readonly int _taps;
    private readonly int _tapOffset;  // first tap is source index i0 + _tapOffset
    private readonly float[] _table;  // [phase * _taps + tap]

    public SincResampler(int srcRate, int dstRate)
    {
        if (srcRate <= 0 || dstRate <= 0) throw new ArgumentOutOfRangeException();
        _srcRate = srcRate;
        _dstRate = dstRate;

        int g = Gcd(srcRate, dstRate);
        long den = dstRate / g;
        _num = srcRate / g;
        _exact = den <= MaxPhases;
        _phases = _exact ? (int)den : MaxPhases;

        double cutoff = Math.Min(1.0, (double)dstRate / srcRate) * CutoffMargin;
        int w = (int)Math.Ceiling(HalfWidth / cutoff);
        _taps = 2 * w;
        _tapOffset = -w + 1;
        _table = new float[_phases * _taps];

        for (int ph = 0; ph < _phases; ph++)
        {
            double frac = (double)ph / _phases;
            double sum = 0;
            int row = ph * _taps;
            for (int t = 0; t < _taps; t++)
            {
                double x = (t + _tapOffset) - frac;
                double sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * cutoff * x) / (Math.PI * cutoff * x);
                double u = x / w;
                double window = Math.Abs(u) >= 1.0 ? 0.0
                    : 0.42 + 0.5 * Math.Cos(Math.PI * u) + 0.08 * Math.Cos(2.0 * Math.PI * u);
                double v = cutoff * sinc * window;
                _table[row + t] = (float)v;
                sum += v;
            }
            // Unity gain at DC for every phase so level does not wobble with the fractional position.
            for (int t = 0; t < _taps; t++) _table[row + t] = (float)(_table[row + t] / sum);
        }
    }

    public int OutputLength(int srcLength) => Math.Max(1, (int)Math.Round(srcLength * (double)_dstRate / _srcRate));

    /// <summary>
    /// Renders output samples [from, to) of the converted slice src[srcStart .. srcStart + srcLength).
    /// Source samples outside the slice count as zero, matching the trim the user asked for.
    /// </summary>
    public void Render(float[] src, int srcStart, int srcLength, float[] dst, int from, int to)
    {
        int taps = _taps;
        for (int j = from; j < to; j++)
        {
            long i0;
            int ph;
            if (_exact)
            {
                long n = j * _num;
                i0 = n / _phases;
                ph = (int)(n % _phases);
            }
            else
            {
                double pos = j * (double)_srcRate / _dstRate;
                i0 = (long)Math.Floor(pos);
                ph = (int)Math.Round((pos - i0) * _phases);
                if (ph >= _phases) { ph = 0; i0++; }
            }

            long first = i0 + _tapOffset;
            int row = ph * taps;
            float acc = 0f;
            if (first >= 0 && first + taps <= srcLength)
            {
                int s = srcStart + (int)first;
                for (int t = 0; t < taps; t++) acc += _table[row + t] * src[s + t];
            }
            else
            {
                for (int t = 0; t < taps; t++)
                {
                    long idx = first + t;
                    if (idx < 0 || idx >= srcLength) continue;
                    acc += _table[row + t] * src[srcStart + (int)idx];
                }
            }
            dst[j] = acc;
        }
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
