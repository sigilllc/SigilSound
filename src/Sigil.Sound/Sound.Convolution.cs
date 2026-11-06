using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>How <see cref="Sound.Convolve"/> scales an impulse response before using it.</summary>
public enum IrNormalize
{
    /// <summary>Use the impulse response as it is. Its level decides how loud the result is.</summary>
    Off,

    /// <summary>The largest tap becomes 1.</summary>
    Peak,

    /// <summary>
    /// Scale by the energy of the response, so noise passes through at the level it came in. Different files then sit at about the same
    /// loudness, whatever level they were recorded at. This is the default.
    /// </summary>
    Energy,
}

public sealed partial class Sound
{
    /// <summary>
    /// Convolves the sound with an impulse response: a recording of how a space, a speaker cabinet or any other linear system
    /// answers a single click. The result is the sound as if it were played through that system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The convolver adds no latency, and its cost grows slowly with the length of the response (the first 128 taps run directly,
    /// the rest in partitions of an FFT), so a ten second cathedral is practical. A response longer than 10 seconds is cut there with
    /// a short fade-out. A response recorded at another sample rate is converted to the rate of the render when the sound is compiled.
    /// </para>
    /// <para>
    /// A stereo response convolves the left channel with its left side and the right channel with its right side, so a stereo room keeps its
    /// image. A response whose two channels are identical (a mono file, for instance) is applied to both channels at half the cost.
    /// </para>
    /// <para>
    /// The response is prepared when the sound is compiled, not while it plays, and the preparation is shared by every voice and every
    /// recompile of the same sound. Memory: the prepared response takes about 32 bytes per tap for a stereo response (16 for mono),
    /// and each running instance, such as each voice of a <c>Play</c>, adds the same again for its own signal state. A ten second stereo
    /// response is about 16 MB shared plus 16 MB per instance, so use shorter responses where many voices run at once.
    /// Pre-delay, a low or high cut and the width are not parameters here: compose them
    /// (<see cref="Delay"/>, <see cref="HighPass"/>, <see cref="Spread"/>) before or after.
    /// </para>
    /// </remarks>
    /// <param name="impulse">The impulse response, such as <c>AudioBuffer.FromWav(...)</c> or <c>Samples.Load("cab.wav")</c>. It is copied, so changing the buffer afterwards has no effect.</param>
    /// <param name="mix">0 is only the dry sound, 1 only the convolved sound. A number, or a sound or <c>Fn(t => ...)</c> to move it. Defaults to 1, for a cabinet. Use about 0.3 for a reverb.</param>
    /// <param name="normalize">How the response is scaled before use. Defaults to <see cref="IrNormalize.Energy"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="impulse"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="impulse"/> has no frames.</exception>
    /// <example>
    /// <code>
    /// // A guitar cabinet, as it would load from a file: a short response whose resonance colours the sound.
    /// // This one is built in code, a 1.2 kHz ring that fades in 12 ms. Listen for the saw going from buzzy to boxy.
    /// const int rate = 48_000;
    /// var frames = new StereoFrame[rate * 12 / 1000];
    /// for (int i = 0; i &lt; frames.Length; i++)
    ///     frames[i] = StereoFrame.Mono((float)(Sin(2 * PI * 1200 * i / rate) * Exp(-i / 90.0)));
    /// var cabinet = new AudioBuffer(frames, rate);
    ///
    /// Saw(110).Distort(drive: 4).Convolve(cabinet) * 0.4
    /// </code>
    /// </example>
    /// <example>
    /// <code>
    /// // A room from noise: a response that starts loud and dies away over 1.5 s is what a reverb tail is.
    /// // Left and right get different noise, so the room is wide. Plucks go in, the room comes back with them.
    /// const int rate = 48_000;
    /// var random = new Random(7);
    /// var frames = new StereoFrame[rate * 3 / 2];
    /// for (int i = 0; i &lt; frames.Length; i++)
    /// {
    ///     double fade = Exp(-4.6 * i / frames.Length);
    ///     frames[i] = new StereoFrame((float)((random.NextDouble() * 2 - 1) * fade), (float)((random.NextDouble() * 2 - 1) * fade));
    /// }
    /// var room = new AudioBuffer(frames, rate);
    ///
    /// var pluck = Saw(220).LowPass(2000).Adsr(0.002, 0.2, 0, 0.1) * Fn(t => t % 1 &lt; 0.5 ? 1 : 0);
    /// pluck.Convolve(room, mix: 0.4) * 0.5
    /// </code>
    /// </example>
    public Sound Convolve(AudioBuffer impulse, Param? mix = null, IrNormalize normalize = IrNormalize.Energy)
    {
        ArgumentNullException.ThrowIfNull(impulse);
        if (impulse.FrameCount == 0)
            throw new ArgumentException("The impulse response has no frames.", nameof(impulse));
        if (!Enum.IsDefined(normalize))
            throw new ArgumentOutOfRangeException(nameof(normalize), normalize, "Unknown normalisation.");

        var wet = mix ?? 1f;
        var source = new ImpulseResponseSource(impulse, normalize);
        return new(ctx => new ConvolutionOperator(Compile(ctx), wet.Compile(ctx), source.KernelFor(ctx.SampleRate), ctx),
            $"{this}.Convolve({impulse.FrameCount} frames at {impulse.SampleRate} Hz)");
    }
}
