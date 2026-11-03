using Sigil.Sound.Operators;
using Sigil.Sound.Wav;

namespace Sigil.Sound;

/// <summary>
/// An immutable description of a sound. Composing a <see cref="Sound"/> never renders anything
/// and never mutates the original; every operator returns a new value, so a sound can be reused,
/// shared and rendered many times. State only exists in the <see cref="IOperator"/> produced by
/// <see cref="Compile"/>.
/// </summary>
public sealed class Sound
{
    private readonly Func<RenderContext, IOperator> _factory;
    private readonly string _description;

    private Sound(Func<RenderContext, IOperator> factory, string description)
    {
        _factory = factory;
        _description = description;
    }

    /// <summary>
    /// Wraps a custom operator. The factory is called once per <see cref="Compile"/>
    /// and must return a fresh instance each time.
    /// </summary>
    public static Sound FromOperator(Func<RenderContext, IOperator> factory, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new Sound(factory, description ?? "Custom");
    }

    /// <summary>
    /// Builds a running instance of this sound for <paramref name="context"/>.
    /// May allocate; the returned operator's <see cref="IOperator.RenderBlock"/> must not.
    /// </summary>
    public IOperator Compile(RenderContext context) => _factory(context.Validate());

    /// <summary>
    /// Escape hatch to the operator layer: feeds this sound into an operator you write.
    /// </summary>
    /// <param name="wrap">Called once per compile with this sound's running operator; returns the operator that processes it.
    /// Create any state inside it so every compile starts fresh. Use <see cref="Operator.From"/> to write one as a lambda.</param>
    /// <param name="description">Text shown for the result in <see cref="ToString"/>.</param>
    /// <example>
    /// <code>
    /// Saw(110).Through(input => Operator.From(block =>
    /// {
    ///     input.RenderBlock(block);
    ///     for (int i = 0; i != block.Length; i++)
    ///         block[i] = block[i] * 0.5f;
    /// }))
    /// </code>
    /// </example>
    public Sound Through(Func<IOperator, IOperator> wrap, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        return new Sound(ctx => wrap(Compile(ctx)), description ?? $"{this}.Through(...)");
    }

    /// <summary>Scales the amplitude.</summary>
    /// <param name="gain">The factor: a number, another sound (amplitude modulation) or <c>Fn(t => ...)</c>. 1 leaves the level unchanged.</param>
    /// <example><code>Saw(110).Gain(0.5)   // same as Saw(110) * 0.5</code></example>
    public Sound Gain(Param gain) =>
        new(ctx => new GainOperator(Compile(ctx), gain.Compile(ctx)), $"{this}.Gain({gain})");

    /// <summary>Maps a bipolar signal in [-1, 1] to [<paramref name="min"/>, <paramref name="max"/>]; handy for LFOs.</summary>
    /// <param name="min">The value the signal's -1 maps to.</param>
    /// <param name="max">The value the signal's +1 maps to.</param>
    /// <example><code>Sine(0.25).Range(200, 2000)   // sweeps between 200 and 2000, four seconds per cycle</code></example>
    public Sound Range(double min, double max) =>
        new(ctx => new RangeOperator(Compile(ctx), (float)min, (float)max), $"{this}.Range({min}, {max})");

    /// <summary>
    /// Shapes the amplitude with an attack-decay-sustain-release envelope. Times are in seconds.
    /// The release starts after <paramref name="hold"/> seconds (default: once attack and decay finish).
    /// </summary>
    /// <param name="attack">Seconds to rise from silence to full level.</param>
    /// <param name="decay">Seconds to fall from full level to <paramref name="sustain"/>.</param>
    /// <param name="sustain">The level held after the decay, from 0 to 1.</param>
    /// <param name="release">Seconds to fall to silence once the release starts.</param>
    /// <param name="hold">Seconds from the start until the release begins. Omit it to release right after the decay.</param>
    /// <example><code>Saw(220).Adsr(0.01, 0.2, 0.6, 1.5, hold: 3)</code></example>
    public Sound Adsr(double attack, double decay, double sustain, double release, double? hold = null)
    {
        if (attack < 0) throw new ArgumentOutOfRangeException(nameof(attack), attack, "Attack must not be negative.");
        if (decay < 0) throw new ArgumentOutOfRangeException(nameof(decay), decay, "Decay must not be negative.");
        if (sustain is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(sustain), sustain, "Sustain must be between 0 and 1.");
        if (release < 0) throw new ArgumentOutOfRangeException(nameof(release), release, "Release must not be negative.");

        double releaseAt = hold ?? attack + decay;
        return new(ctx => new AdsrOperator(Compile(ctx), attack, decay, (float)sustain, release, releaseAt, ctx),
            $"{this}.Adsr({attack}, {decay}, {sustain}, {release})");
    }

    /// <summary>A resonant two-pole low-pass filter. <paramref name="cutoff"/> is in Hz.</summary>
    /// <param name="cutoff">Frequency in Hz above which the sound is attenuated. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">Resonance at the cutoff. 0.7 is flat; higher values ring. Defaults to 0.7071.</param>
    /// <example><code>Saw(110).LowPass(Sine(0.25).Range(200, 2000), q: 4)</code></example>
    public Sound LowPass(Param cutoff, Param? q = null)
    {
        var resonance = q ?? 0.7071f;
        return new(ctx => new LowPassOperator(Compile(ctx), cutoff.Compile(ctx), resonance.Compile(ctx), ctx),
            $"{this}.LowPass({cutoff}, {resonance})");
    }

    /// <summary>Mixes two sounds by summing them.</summary>
    public static Sound operator +(Sound a, Sound b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new(ctx => new MixOperator(a.Compile(ctx), b.Compile(ctx), ctx), $"({a} + {b})");
    }

    /// <summary>Scales <paramref name="a"/> by a parameter (a constant, a function or another sound).</summary>
    public static Sound operator *(Sound a, Param gain)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.Gain(gain);
    }

    /// <summary>Scales <paramref name="a"/> by a parameter (a constant, a function or another sound).</summary>
    public static Sound operator *(Param gain, Sound a) => a * gain;

    /// <summary>Ring modulation: multiplies <paramref name="a"/> by the mono value of <paramref name="b"/>.</summary>
    public static Sound operator *(Sound a, Sound b) => a * (Param)b;

    /// <summary>Renders <paramref name="seconds"/> of audio offline.</summary>
    public AudioBuffer Render(double seconds, int sampleRate = RenderContext.DefaultSampleRate,
        int blockSize = RenderContext.DefaultBlockSize)
    {
        if (seconds < 0 || double.IsNaN(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Duration must not be negative.");

        var context = new RenderContext(sampleRate, blockSize).Validate();
        var op = Compile(context);
        var frames = new StereoFrame[(int)Math.Round(seconds * sampleRate)];
        for (int offset = 0; offset < frames.Length; offset += blockSize)
        {
            int count = Math.Min(blockSize, frames.Length - offset);
            op.RenderBlock(frames.AsSpan(offset, count));
        }

        return new AudioBuffer(frames, sampleRate);
    }

    /// <summary>Renders <paramref name="seconds"/> of audio and writes it as a WAV file.</summary>
    public void RenderToWav(string path, double seconds, int sampleRate = RenderContext.DefaultSampleRate,
        WavFormat format = WavFormat.Pcm16)
    {
        using var stream = File.Create(path);
        RenderToWav(stream, seconds, sampleRate, format);
    }

    /// <summary>Renders <paramref name="seconds"/> of audio and writes it as WAV to a stream (works without a file system).</summary>
    public void RenderToWav(Stream stream, double seconds, int sampleRate = RenderContext.DefaultSampleRate,
        WavFormat format = WavFormat.Pcm16) =>
        WavWriter.Write(stream, Render(seconds, sampleRate), format);

    public override string ToString() => _description;
}
