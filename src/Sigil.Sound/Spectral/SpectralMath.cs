namespace Sigil.Sound.Spectral;

/// <summary>Analysis windows for short-time transforms.</summary>
internal static class Windows
{
    /// <summary>
    /// A periodic Hann window: <c>sin²(π·n/N)</c>. Periodic (not symmetric) is the right one for overlap-add, because copies
    /// shifted by a quarter of the length add up to a constant.
    /// </summary>
    public static float[] Hann(int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size), size, "A window needs at least 2 samples.");
        var window = new float[size];
        for (int n = 0; n < size; n++)
        {
            double s = Math.Sin(Math.PI * n / size);
            window[n] = (float)(s * s);
        }
        return window;
    }
}

/// <summary>Small helpers for working on spectra held as separate real and imaginary arrays.</summary>
internal static class SpectralMath
{
    /// <summary>Wraps an angle to the range [-π, π].</summary>
    public static float WrapPhase(float phase) => MathF.IEEERemainder(phase, MathF.Tau);

    /// <summary>Splits complex values into magnitude and phase.</summary>
    public static void ToPolar(ReadOnlySpan<float> re, ReadOnlySpan<float> im, Span<float> magnitude, Span<float> phase)
    {
        for (int i = 0; i < re.Length; i++)
        {
            magnitude[i] = MathF.Sqrt(re[i] * re[i] + im[i] * im[i]);
            phase[i] = MathF.Atan2(im[i], re[i]);
        }
    }

    /// <summary>Builds complex values from magnitude and phase.</summary>
    public static void FromPolar(ReadOnlySpan<float> magnitude, ReadOnlySpan<float> phase, Span<float> re, Span<float> im)
    {
        for (int i = 0; i < magnitude.Length; i++)
        {
            var (sin, cos) = MathF.SinCos(phase[i]);
            re[i] = magnitude[i] * cos;
            im[i] = magnitude[i] * sin;
        }
    }

    /// <summary>
    /// Rebuilds the upper half of a spectrum of a real signal from its lower half (bins 0 to N/2), as complex conjugates.
    /// Frame handlers that only edit the lower half call this when done, so the inverse transform has a real result.
    /// </summary>
    public static void MirrorLowerHalf(Span<float> re, Span<float> im)
    {
        int n = re.Length;
        for (int k = 1; k < n / 2; k++)
        {
            re[n - k] = re[k];
            im[n - k] = -im[k];
        }
        im[0] = 0;
        im[n / 2] = 0;
    }
}
