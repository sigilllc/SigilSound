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
}
