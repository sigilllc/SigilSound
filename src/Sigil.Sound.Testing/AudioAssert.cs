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
    public static void FrequencyNear(AudioBuffer audio, double expectedHz, double toleranceHz)
    {
        double actual = SignalAnalysis.EstimateFrequency(audio.ToMono(), audio.SampleRate);
        if (Math.Abs(actual - expectedHz) > toleranceHz)
            Fail($"Expected frequency {expectedHz} Hz ± {toleranceHz}, measured {actual:0.###} Hz.");
    }

    public static void PeakNear(AudioBuffer audio, float expected, float tolerance)
    {
        float actual = Math.Max(SignalAnalysis.Peak(audio.Left()), SignalAnalysis.Peak(audio.Right()));
        if (Math.Abs(actual - expected) > tolerance)
            Fail($"Expected peak {expected} ± {tolerance}, measured {actual:0.####}.");
    }

    public static void RmsNear(AudioBuffer audio, double expected, double tolerance)
    {
        double actual = SignalAnalysis.Rms(audio.ToMono());
        if (Math.Abs(actual - expected) > tolerance)
            Fail($"Expected RMS {expected} ± {tolerance}, measured {actual:0.####}.");
    }

    public static void IsSilent(ReadOnlySpan<float> samples, float threshold = 1e-4f)
    {
        float peak = SignalAnalysis.Peak(samples);
        if (peak > threshold)
            Fail($"Expected silence (peak <= {threshold}), measured peak {peak:0.####}.");
    }

    public static void IsFinite(AudioBuffer audio)
    {
        if (SignalAnalysis.HasNonFinite(audio.Left()) || SignalAnalysis.HasNonFinite(audio.Right()))
            Fail("Audio contains NaN or infinite samples.");
    }

    private static void Fail(FormattableString message) => Fail(message.ToString(CultureInfo.InvariantCulture));

    private static void Fail(string message) => throw new AudioAssertionException(message);
}
