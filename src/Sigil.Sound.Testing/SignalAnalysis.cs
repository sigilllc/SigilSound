namespace Sigil.Sound.Testing;

/// <summary>Simple, dependency-free measurements on rendered audio.</summary>
public static class SignalAnalysis
{
    /// <summary>The largest absolute sample value.</summary>
    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0;
        foreach (float s in samples)
            peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    /// <summary>Root mean square level.</summary>
    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return 0;
        double sum = 0;
        foreach (float s in samples)
            sum += (double)s * s;
        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>True if any sample is NaN or infinite.</summary>
    public static bool HasNonFinite(ReadOnlySpan<float> samples)
    {
        foreach (float s in samples)
        {
            if (!float.IsFinite(s))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Estimates the fundamental frequency from rising zero crossings, with linear interpolation
    /// between samples. Good for clean periodic signals; not a pitch tracker.
    /// </summary>
    public static double EstimateFrequency(ReadOnlySpan<float> samples, int sampleRate)
    {
        double first = -1, last = -1;
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            float a = samples[i - 1], b = samples[i];
            if (a < 0 && b >= 0)
            {
                double position = i - 1 + a / (a - b);
                if (first < 0)
                    first = position;
                last = position;
                crossings++;
            }
        }

        return crossings < 2 ? 0 : (crossings - 1) * sampleRate / (last - first);
    }

    /// <summary>
    /// The magnitude of each frequency bin of a signal, from 0 Hz to Nyquist, after a Hann window. The signal is zero-padded to
    /// <paramref name="fftSize"/> (the next power of two if 0). Bin <c>k</c> is at <c>k * sampleRate / fftSize</c> Hz; a sine of
    /// amplitude 1 peaks at about 1.
    /// </summary>
    public static float[] MagnitudeSpectrum(ReadOnlySpan<float> samples, int fftSize = 0)
    {
        if (samples.Length == 0) throw new ArgumentException("There is nothing to analyse.", nameof(samples));
        if (fftSize == 0) fftSize = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(samples.Length, 4));
        if (fftSize < samples.Length) throw new ArgumentOutOfRangeException(nameof(fftSize), fftSize, "The FFT size must hold the whole signal.");

        var plan = new Spectral.FftPlan(fftSize);
        var window = Spectral.Windows.Hann(samples.Length);
        var re = new float[fftSize];
        var im = new float[fftSize];
        for (int i = 0; i < samples.Length; i++)
            re[i] = samples[i] * window[i];

        plan.Forward(re, im);

        // A sine of amplitude A transforms to A * (sum of the window) / 2 = A * length / 4 in its bin, so this reads A back.
        float scale = 4f / samples.Length;
        var magnitude = new float[plan.Bins];
        for (int k = 0; k < magnitude.Length; k++)
            magnitude[k] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) * scale;
        return magnitude;
    }

    /// <summary>
    /// The frequency of the strongest component, found from the spectrum and refined between bins, so it is accurate to a small
    /// fraction of a bin. Unlike <see cref="EstimateFrequency"/> it copes with several partials and with noise, and it
    /// answers for the loudest one.
    /// </summary>
    public static double PeakFrequency(ReadOnlySpan<float> samples, int sampleRate)
    {
        // Padding by 4x gives the interpolation smooth peaks to work on.
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(samples.Length, 4)) * 4;
        var magnitude = MagnitudeSpectrum(samples, size);

        int peak = 1;
        for (int k = 2; k < magnitude.Length - 1; k++)
            if (magnitude[k] > magnitude[peak]) peak = k;

        // A parabola through the log magnitudes of the peak and its neighbours puts the true peak between bins.
        double a = Math.Log(magnitude[peak - 1] + 1e-12), b = Math.Log(magnitude[peak] + 1e-12), c = Math.Log(magnitude[peak + 1] + 1e-12);
        double denominator = a - 2 * b + c;
        double offset = Math.Abs(denominator) < 1e-12 ? 0 : 0.5 * (a - c) / denominator;
        return (peak + offset) * sampleRate / size;
    }
}
