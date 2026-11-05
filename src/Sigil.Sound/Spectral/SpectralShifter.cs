namespace Sigil.Sound.Spectral;

/// <summary>
/// Moves every frequency in a short-time spectrum by a ratio, one frame at a time: the heart of the spectral pitch shifter.
/// It follows the "identity phase locking" idea of Laroche and Dolson: find the spectral peaks, work out each peak's true
/// frequency from how its phase advanced since the last frame, move the peak's whole neighbourhood to its new place, and
/// rotate that neighbourhood so every bin keeps its phase relation to its peak.
/// </summary>
/// <remarks>
/// <para>
/// Because a peak carries its neighbourhood with it, the shape of the spectrum moves as a whole: formants follow the shift, as
/// they do when a tape is sped up, and nothing is invented to fill the space that opens up. Shifting down therefore lowers the
/// top of the spectrum too (the content ends at Nyquist times the ratio), and shifting up discards what would go past Nyquist.
/// </para>
/// <para>
/// Each peak keeps its own sub-bin accuracy through its phase: its new frequency is the old one times the ratio, carried
/// forward from the previous frame's phase at the bin it lands on, and the landing bin is only the nearest whole bin.
/// </para>
/// <para>
/// One instance handles one channel; its arrays are the state carried from frame to frame.
/// </para>
/// </remarks>
internal sealed class SpectralShifter
{
    // A peak must be this far above the loudest bin to count (-80 dB), or noise floors would be full of peaks.
    private const float PeakFloor = 1e-4f;
    private const float SilenceMagnitude = 1e-9f;

    private readonly int _size;
    private readonly int _bins;
    private readonly float _binsPerRadian; // converts a phase deviation per hop to bins
    private readonly float _hopPhase;      // phase advance of a one-bin frequency over one hop: 2π · hop / size
    private readonly float[] _magnitude;
    private readonly float[] _phase;
    private readonly float[] _previousPhase;
    private readonly float[] _frequency;   // true frequency of each input bin, in bins
    private readonly float[] _outputPhase;
    private readonly float[] _previousOutputPhase;
    private readonly float[] _outputMagnitude;
    private readonly float[] _spectrumRe;
    private readonly float[] _spectrumIm;
    private readonly int[] _peaks;

    /// <param name="size">The frame length of the transform.</param>
    /// <param name="hop">The samples between frames.</param>
    public SpectralShifter(int size, int hop)
    {
        _size = size;
        _bins = size / 2 + 1;
        _hopPhase = MathF.Tau * hop / size;
        _binsPerRadian = size / (MathF.Tau * hop);
        _magnitude = new float[_bins];
        _phase = new float[_bins];
        _previousPhase = new float[_bins];
        _frequency = new float[_bins];
        _outputPhase = new float[_bins];
        _previousOutputPhase = new float[_bins];
        _outputMagnitude = new float[_bins];
        _spectrumRe = new float[size];
        _spectrumIm = new float[size];
        _peaks = new int[_bins / 2 + 1];
    }

    /// <summary>The frequency ratio to apply to the next frame: 2 is an octave up, 0.5 an octave down.</summary>
    public float Ratio { get; set; } = 1f;

    /// <summary>Forgets the previous frames.</summary>
    public void Reset()
    {
        Array.Clear(_previousPhase);
        Array.Clear(_previousOutputPhase);
        Array.Clear(_outputPhase);
    }

    /// <summary>The handler to give an <see cref="StftProcessor"/>: shifts the frame it is handed, in place.</summary>
    public void Process(Span<float> re, Span<float> im)
    {
        int last = _bins - 1;

        // 1. Polar form, and the true frequency of every bin from how far its phase moved against what a bin-centred sine would do.
        float loudest = 0f;
        for (int k = 0; k < _bins; k++)
        {
            float mag = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);
            float phase = MathF.Atan2(im[k], re[k]);
            _magnitude[k] = mag;
            _phase[k] = phase;
            if (mag > loudest) loudest = mag;

            float deviation = SpectralMath.WrapPhase(phase - _previousPhase[k] - k * _hopPhase);
            _frequency[k] = k + deviation * _binsPerRadian;
            _previousPhase[k] = phase;
        }

        _spectrumRe.AsSpan(0, _size).Clear();
        _spectrumIm.AsSpan(0, _size).Clear();
        Array.Clear(_outputMagnitude);
        Array.Clear(_outputPhase);

        if (loudest > SilenceMagnitude)
            ShiftPeaks(re, im, loudest * PeakFloor, last);

        // 2. Hand the result back. The upper half is rebuilt as the mirror image so the inverse transform is real.
        _spectrumRe.AsSpan(0, _bins).CopyTo(re);
        _spectrumIm.AsSpan(0, _bins).CopyTo(im);
        SpectralMath.MirrorLowerHalf(re, im);
        _outputPhase.AsSpan().CopyTo(_previousOutputPhase);
    }

    private void ShiftPeaks(Span<float> re, Span<float> im, float floor, int last)
    {
        float ratio = Ratio;

        int count = 0;
        for (int k = 2; k < last - 1; k++)
        {
            float m = _magnitude[k];
            if (m > floor && m > _magnitude[k - 1] && m >= _magnitude[k + 1] && m > _magnitude[k - 2] && m >= _magnitude[k + 2])
                _peaks[count++] = k;
        }

        // A frame with no clear peak (nothing but DC, say) has nothing to move and comes out silent.
        if (count == 0)
            return;

        int regionStart = 0;
        for (int i = 0; i < count; i++)
        {
            int peak = _peaks[i];
            int regionEnd = i + 1 < count ? (peak + _peaks[i + 1]) / 2 : last;

            float newFrequency = _frequency[peak] * ratio;
            int newPeak = (int)MathF.Round(newFrequency);
            if (newPeak >= 1 && newPeak <= last)
            {
                // The peak's new phase is where the output at that bin had got to, plus a hop at the new frequency.
                float peakPhase = SpectralMath.WrapPhase(_previousOutputPhase[newPeak] + newFrequency * _hopPhase);
                float rotation = peakPhase - _phase[peak];
                var (sin, cos) = MathF.SinCos(rotation);
                int shift = newPeak - peak;

                for (int k = regionStart; k <= regionEnd; k++)
                {
                    int target = k + shift;
                    if (target < 1 || target > last) continue;

                    // Where regions overlap (shifting down squeezes them together) the louder bin wins, rather than
                    // adding two unrelated phases into noise.
                    if (_magnitude[k] <= _outputMagnitude[target]) continue;

                    _outputMagnitude[target] = _magnitude[k];
                    _spectrumRe[target] = re[k] * cos - im[k] * sin;
                    _spectrumIm[target] = re[k] * sin + im[k] * cos;
                    _outputPhase[target] = SpectralMath.WrapPhase(_phase[k] + rotation);
                }
            }

            regionStart = regionEnd + 1;
        }
    }
}
