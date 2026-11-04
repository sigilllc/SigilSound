using Sigil.Sound.Testing;

namespace Sigil.Sound.Tests;

/// <summary>
/// Measurements shared by the operator tests, so each test states what it checks and not how it measures.
/// </summary>
internal static class Measure
{
    public const int SampleRate = 48_000;

    /// <summary>Renders <paramref name="seconds"/> of a sound at 48 kHz and returns its mono signal.</summary>
    public static float[] Mono(Sound sound, double seconds) => sound.Render(seconds, SampleRate).ToMono();

    /// <summary>
    /// Pulls <paramref name="frames"/> frames from a compiled operator in default-sized blocks, the way the engine does.
    /// A single RenderBlock call may not ask for more than the render context's block size.
    /// </summary>
    public static StereoFrame[] Pull(IOperator op, int frames)
    {
        var output = new StereoFrame[frames];
        for (int offset = 0; offset < frames; offset += RenderContext.DefaultBlockSize)
            op.RenderBlock(output.AsSpan(offset, Math.Min(RenderContext.DefaultBlockSize, frames - offset)));
        return output;
    }

    /// <summary>The samples between two times in seconds, at 48 kHz.</summary>
    public static float[] Slice(float[] samples, double fromSeconds, double toSeconds) =>
        samples[(int)(fromSeconds * SampleRate)..(int)(toSeconds * SampleRate)];

    /// <summary>
    /// The energy at one frequency, from the Goertzel algorithm: a single bin of a Fourier transform, so it costs no more
    /// than one pass over the samples. Compare powers at different frequencies of the same signal; the scale is arbitrary.
    /// </summary>
    public static double Power(ReadOnlySpan<float> samples, double frequency, int sampleRate = SampleRate)
    {
        double omega = 2.0 * Math.PI * frequency / sampleRate;
        double coefficient = 2.0 * Math.Cos(omega);
        double s1 = 0, s2 = 0;
        foreach (float x in samples)
        {
            double s0 = x + coefficient * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        double real = s1 - s2 * Math.Cos(omega);
        double imaginary = s2 * Math.Sin(omega);
        return (real * real + imaginary * imaginary) / ((double)samples.Length * samples.Length);
    }

    /// <summary>The root-mean-square level of a slice.</summary>
    public static double Rms(ReadOnlySpan<float> samples) => SignalAnalysis.Rms(samples);

    /// <summary>The largest absolute sample of a slice.</summary>
    public static float Peak(ReadOnlySpan<float> samples) => SignalAnalysis.Peak(samples);

    /// <summary>A stereo sound with a fixed level in each channel.</summary>
    public static Sound Stereo(float left, float right) =>
        Sound.FromOperator(_ => Operator.From(block =>
        {
            for (int i = 0; i < block.Length; i++)
                block[i] = new StereoFrame(left, right);
        }), $"Stereo({left}, {right})");
}
