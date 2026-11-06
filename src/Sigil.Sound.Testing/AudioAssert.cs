using System.Globalization;

namespace Sigil.Sound.Testing;

/// <summary>Thrown when an <see cref="AudioAssert"/> check fails. Test frameworks report it as a failure.</summary>
public sealed class AudioAssertionException(string message) : Exception(message);

/// <summary>
/// Tolerance-based assertions for rendered audio. Compare with tolerances, never exact bytes,
/// so tests survive harmless numerical changes.
/// </summary>
public static class AudioAssert
{
    /// <summary>Checks the frequency of a clean periodic signal, estimated from its zero crossings. For chords or noisy signals use <see cref="PeakFrequencyNear"/>.</summary>
    /// <param name="audio">The audio to measure.</param>
    /// <param name="expectedHz">The frequency it should have.</param>
    /// <param name="toleranceHz">How far from it is still accepted.</param>
    /// <exception cref="AudioAssertionException">The measured frequency is outside the tolerance.</exception>
    public static void FrequencyNear(AudioBuffer audio, double expectedHz, double toleranceHz)
    {
        double actual = SignalAnalysis.EstimateFrequency(audio.ToMono(), audio.SampleRate);
        if (Math.Abs(actual - expectedHz) > toleranceHz)
            Fail($"Expected frequency {expectedHz} Hz ± {toleranceHz}, measured {actual:0.###} Hz.");
    }

    /// <summary>
    /// Checks the strongest spectral component, which unlike <see cref="FrequencyNear"/> works for chords, noisy signals and
    /// anything with a dominant partial, and is exact to a fraction of an FFT bin.
    /// </summary>
    public static void PeakFrequencyNear(AudioBuffer audio, double expectedHz, double toleranceHz)
    {
        double actual = SignalAnalysis.PeakFrequency(audio.ToMono(), audio.SampleRate);
        if (Math.Abs(actual - expectedHz) > toleranceHz)
            Fail($"Expected the spectral peak at {expectedHz} Hz ± {toleranceHz}, measured {actual:0.###} Hz.");
    }

    /// <summary>Checks the largest absolute sample of either channel.</summary>
    /// <param name="audio">The audio to measure.</param>
    /// <param name="expected">The peak it should have.</param>
    /// <param name="tolerance">How far from it is still accepted.</param>
    /// <exception cref="AudioAssertionException">The measured peak is outside the tolerance.</exception>
    public static void PeakNear(AudioBuffer audio, float expected, float tolerance)
    {
        float actual = Math.Max(SignalAnalysis.Peak(audio.Left()), SignalAnalysis.Peak(audio.Right()));
        if (Math.Abs(actual - expected) > tolerance)
            Fail($"Expected peak {expected} ± {tolerance}, measured {actual:0.####}.");
    }

    /// <summary>Checks the RMS level of the mono mix.</summary>
    /// <param name="audio">The audio to measure.</param>
    /// <param name="expected">The RMS it should have.</param>
    /// <param name="tolerance">How far from it is still accepted.</param>
    /// <exception cref="AudioAssertionException">The measured level is outside the tolerance.</exception>
    public static void RmsNear(AudioBuffer audio, double expected, double tolerance)
    {
        double actual = SignalAnalysis.Rms(audio.ToMono());
        if (Math.Abs(actual - expected) > tolerance)
            Fail($"Expected RMS {expected} ± {tolerance}, measured {actual:0.####}.");
    }

    /// <summary>Checks that no sample is louder than <paramref name="threshold"/>.</summary>
    /// <param name="samples">The samples to check.</param>
    /// <param name="threshold">The largest absolute value still counted as silence.</param>
    /// <exception cref="AudioAssertionException">A sample is louder than the threshold.</exception>
    public static void IsSilent(ReadOnlySpan<float> samples, float threshold = 1e-4f)
    {
        float peak = SignalAnalysis.Peak(samples);
        if (peak > threshold)
            Fail($"Expected silence (peak <= {threshold}), measured peak {peak:0.####}.");
    }

    /// <summary>Checks that no sample of either channel is NaN or infinite.</summary>
    /// <param name="audio">The audio to check.</param>
    /// <exception cref="AudioAssertionException">A sample is not finite.</exception>
    public static void IsFinite(AudioBuffer audio)
    {
        if (SignalAnalysis.HasNonFinite(audio.Left()) || SignalAnalysis.HasNonFinite(audio.Right()))
            Fail("Audio contains NaN or infinite samples.");
    }

    private static void Fail(FormattableString message) => Fail(message.ToString(CultureInfo.InvariantCulture));

    private static void Fail(string message) => throw new AudioAssertionException(message);
}
