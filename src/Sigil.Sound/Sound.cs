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
    /// <example><c>sound.Through(input => new MyOperator(input))</c></example>
    public Sound Through(Func<IOperator, IOperator> wrap, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        return new Sound(ctx => wrap(Compile(ctx)), description ?? $"{this}.Through(...)");
    }

    /// <summary>Scales the amplitude by <paramref name="gain"/>.</summary>
    public Sound Gain(Param gain) =>
        new(ctx => new GainOperator(Compile(ctx), gain.Compile(ctx)), $"{this}.Gain({gain})");

    /// <summary>Maps a bipolar signal in [-1, 1] to [<paramref name="min"/>, <paramref name="max"/>]; handy for LFOs.</summary>
    public Sound Range(float min, float max) =>
        new(ctx => new RangeOperator(Compile(ctx), min, max), $"{this}.Range({min}, {max})");

    /// <summary>
    /// Shapes the amplitude with an attack-decay-sustain-release envelope. Times are in seconds.
    /// The release starts after <paramref name="hold"/> seconds (default: once attack and decay finish).
    /// </summary>
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
