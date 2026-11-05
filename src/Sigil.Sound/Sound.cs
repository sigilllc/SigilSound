using Sigil.Sound.Operators;
using Sigil.Sound.Wav;

namespace Sigil.Sound;

/// <summary>
/// An immutable description of a sound. Composing a <see cref="Sound"/> never renders anything
/// and never mutates the original; every operator returns a new value, so a sound can be reused,
/// shared and rendered many times. State only exists in the <see cref="IOperator"/> produced by
/// <see cref="Compile"/>.
/// </summary>
public sealed partial class Sound
{
    private readonly Func<RenderContext, IOperator> _factory;
    private readonly string _description;

    private Sound(Func<RenderContext, IOperator> factory, string description)
    {
        _factory = factory;
        _description = description;
    }

    /// <summary>
    /// Set when this sound is only a parameter seen as a signal (a control, say). Converting it back to a <see cref="Param"/>
    /// returns that parameter itself, instead of rendering it as stereo audio and averaging it down to mono again.
    /// </summary>
    internal Param? BackingParam { get; private init; }

    internal static Sound FromBackingParam(Param param, Func<RenderContext, IOperator> factory) =>
        new(factory, param.ToString()) { BackingParam = param };

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
    /// <remarks>
    /// A sound used in several places of one graph is built once and fans out to all of them,
    /// so <c>var bus = ...; bus + bus.Delay(0.2)</c> renders <c>bus</c> a single time.
    /// The result is <see cref="IDisposable"/> when anything inside it is; dispose it when you are done rendering.
    /// </remarks>
    public IOperator Compile(RenderContext context)
    {
        context.Validate();
        if (context.Scope is { } scope)
            return scope.Share(this, () => _factory(context), context);

        // The outermost compile owns the scope and hands back one object that resets and disposes the whole graph.
        scope = new CompileScope();
        var root = scope.Share(this, () => _factory(context with { Scope = scope }), context);
        return new CompiledGraph(root, scope.Disposables);
    }

    /// <summary>
    /// Escape hatch to the operator layer: feeds this sound into an operator you write.
    /// </summary>
    /// <param name="wrap">Called once per compile with this sound's running operator; returns the operator that processes it.
    /// Create any state inside it so every compile starts fresh. Use <see cref="Operator.From(BlockRenderer)"/> to write one as a lambda.</param>
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
        return new Sound(ctx =>
        {
            var input = Compile(ctx);
            var output = wrap(input);
            ctx.Scope?.Track(output);
            // Reset reaches the input and your operator without you wiring it up.
            return new ThroughOperator(input, output);
        }, description ?? $"{this}.Through(...)");
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
    /// Inside a note voice (<c>Play</c>) the release starts when the note ends. Anywhere else the sound
    /// sustains until you give <paramref name="hold"/>, or for as long as it plays.
    /// </summary>
    /// <param name="attack">Seconds to rise from silence to full level.</param>
    /// <param name="decay">Seconds to fall from full level to <paramref name="sustain"/>.</param>
    /// <param name="sustain">The level held after the decay, from 0 to 1.</param>
    /// <param name="release">Seconds to fall to silence once the release starts.</param>
    /// <param name="hold">Seconds from the start until the release begins. Omit it to follow the note, or to sustain.</param>
    /// <example><code>Saw(220).Adsr(0.01, 0.2, 0.6, 1.5, hold: 3)</code></example>
    public Sound Adsr(double attack, double decay, double sustain, double release, double? hold = null) =>
        Adsr((Param)attack, (Param)decay, (Param)sustain, (Param)release, hold);

    /// <summary>Applies an attack-decay-sustain-release envelope driven by constant or modulated parameters. Times are in seconds; sustain is a level from 0 to 1.</summary>
    /// <param name="attack">Seconds to ramp from 0 to peak. A number, or a sound/knob to modulate it.</param>
    /// <param name="decay">Seconds to fall from peak to the sustain level. A number, or a sound/knob to modulate it.</param>
    /// <param name="sustain">Level held until release begins, between 0 and 1. A number, or a sound/knob to modulate it.</param>
    /// <param name="release">Seconds to fade to silence once released. A number, or a sound/knob to modulate it.</param>
    /// <param name="hold">Seconds from the start until the release begins. Omit it to follow the note, or to sustain.</param>
    /// <example><code>Saw(220).Adsr(0.01, 0.2, 0.6, 1.5, hold: 3)</code></example>
    public Sound Adsr(Param attack, Param decay, Param sustain, Param release, double? hold = null)
    {
        if (attack.IsConstant && attack.ConstantValue < 0) throw new ArgumentOutOfRangeException(nameof(attack), attack.ConstantValue, "Attack must not be negative.");
        if (decay.IsConstant && decay.ConstantValue < 0) throw new ArgumentOutOfRangeException(nameof(decay), decay.ConstantValue, "Decay must not be negative.");
        if (sustain.IsConstant && sustain.ConstantValue is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(sustain), sustain.ConstantValue, "Sustain must be between 0 and 1.");
        if (release.IsConstant && release.ConstantValue < 0) throw new ArgumentOutOfRangeException(nameof(release), release.ConstantValue, "Release must not be negative.");

        if (hold is < 0 or double.NaN) throw new ArgumentOutOfRangeException(nameof(hold), hold, "Hold must not be negative.");

        return new(ctx => new AdsrOperator(Compile(ctx), attack.Compile(ctx), decay.Compile(ctx), sustain.Compile(ctx), release.Compile(ctx), hold, ctx),
            $"{this}.Adsr({attack}, {decay}, {sustain}, {release})");
    }

    /// <summary>A resonant two-pole low-pass filter. <paramref name="cutoff"/> is in Hz.</summary>
    /// <param name="cutoff">Frequency in Hz above which the sound is attenuated. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">Resonance at the cutoff. 0.7 is flat; higher values ring. Defaults to 0.7071.</param>
    /// <example><code>Saw(110).LowPass(Sine(0.25).Range(200, 2000), q: 4)</code></example>
    public Sound LowPass(Param cutoff, Param? q = null) => Filter(FilterMode.LowPass, cutoff, q);

    /// <summary>A resonant two-pole high-pass filter: removes what is below <paramref name="cutoff"/>. Mirror image of <see cref="LowPass"/>.</summary>
    /// <param name="cutoff">Frequency in Hz below which the sound is attenuated. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">Resonance at the cutoff. 0.7 is flat; higher values ring. Defaults to 0.7071.</param>
    /// <example><code>Noise().HighPass(Sine(0.2).Range(500, 6000), q: 3) * 0.3</code></example>
    public Sound HighPass(Param cutoff, Param? q = null) => Filter(FilterMode.HighPass, cutoff, q);

    /// <summary>A band-pass filter: keeps a band around <paramref name="center"/> and removes the rest.</summary>
    /// <param name="center">Frequency in Hz at the middle of the band. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">Narrowness of the band: higher is narrower. Defaults to 0.7071.</param>
    /// <example><code>Noise().BandPass(Sine(0.3).Range(300, 3000), q: 8)</code></example>
    public Sound BandPass(Param center, Param? q = null) => Filter(FilterMode.BandPass, center, q);

    /// <summary>A notch filter: removes a narrow band around <paramref name="center"/> and keeps the rest.</summary>
    /// <param name="center">Frequency in Hz at the middle of the notch. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">Narrowness of the notch: higher is narrower. Defaults to 0.7071.</param>
    /// <example><code>Saw(110).Notch(1000, q: 4)</code></example>
    public Sound Notch(Param center, Param? q = null) => Filter(FilterMode.Notch, center, q);

    /// <summary>
    /// A peaking (bell) equalizer band: boosts or cuts a band around <paramref name="frequency"/> and leaves the rest of
    /// the spectrum alone. Chain several for a parametric EQ.
    /// </summary>
    /// <param name="frequency">Centre of the band in Hz. A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="q">How narrow the band is: 0.7 is broad and musical, 3 to 10 is surgical. Defaults to 0.7071.</param>
    /// <param name="gainDb">Boost (positive) or cut (negative) at the centre, in decibels. 0 changes nothing. Defaults to 0.</param>
    /// <example><code>lead.Peak(2500, q: 3, gainDb: 4)   // lifts the presence band</code></example>
    public Sound Peak(Param frequency, Param? q = null, Param? gainDb = null) =>
        Band(EqualizerShape.Peak, frequency, q, gainDb);

    /// <summary>
    /// A low shelf: boosts or cuts everything below <paramref name="frequency"/> by the same amount, for bass weight or thinning.
    /// </summary>
    /// <param name="frequency">The shelf's corner in Hz, roughly where the change is half of <paramref name="gainDb"/>. A number, or a sound or <c>Fn(t => ...)</c>.</param>
    /// <param name="gainDb">Boost (positive) or cut (negative) below the corner, in decibels. Defaults to 0.</param>
    /// <param name="q">How steep the transition is: 0.7 is gentle and musical, higher is sharper. Defaults to 0.7071.</param>
    /// <example><code>bass.LowShelf(120, gainDb: 3)   // a little more weight</code></example>
    public Sound LowShelf(Param frequency, Param? gainDb = null, Param? q = null) =>
        Band(EqualizerShape.LowShelf, frequency, q, gainDb);

    /// <summary>
    /// A high shelf: boosts or cuts everything above <paramref name="frequency"/> by the same amount, for air or for taming hiss.
    /// </summary>
    /// <param name="frequency">The shelf's corner in Hz, roughly where the change is half of <paramref name="gainDb"/>. A number, or a sound or <c>Fn(t => ...)</c>.</param>
    /// <param name="gainDb">Boost (positive) or cut (negative) above the corner, in decibels. Defaults to 0.</param>
    /// <param name="q">How steep the transition is: 0.7 is gentle and musical, higher is sharper. Defaults to 0.7071.</param>
    /// <example><code>pad.HighShelf(8000, gainDb: -4)   // softens the top end</code></example>
    public Sound HighShelf(Param frequency, Param? gainDb = null, Param? q = null) =>
        Band(EqualizerShape.HighShelf, frequency, q, gainDb);

    /// <summary>
    /// A simple three-band tone control: a low shelf at 200 Hz, a broad bell at 1 kHz and a high shelf at 5 kHz.
    /// Bands at 0 dB are skipped, so an all-zero call returns this sound unchanged.
    /// </summary>
    /// <param name="lowDb">Boost or cut of the low shelf, in decibels. Defaults to 0.</param>
    /// <param name="midDb">Boost or cut of the 1 kHz bell, in decibels. Defaults to 0.</param>
    /// <param name="highDb">Boost or cut of the high shelf, in decibels. Defaults to 0.</param>
    /// <example><code>mix.Equalize(lowDb: 2, midDb: -1.5, highDb: 3)</code></example>
    public Sound Equalize(double lowDb = 0, double midDb = 0, double highDb = 0)
    {
        var sound = this;
        if (lowDb != 0) sound = sound.LowShelf(200, lowDb);
        if (midDb != 0) sound = sound.Peak(1000, 0.7071, midDb);
        if (highDb != 0) sound = sound.HighShelf(5000, highDb);
        return sound;
    }

    private Sound Band(EqualizerShape shape, Param frequency, Param? q, Param? gainDb)
    {
        var resonance = q ?? 0.7071f;
        var gain = gainDb ?? 0f;
        return new(ctx => new EqualizerOperator(Compile(ctx), shape, frequency.Compile(ctx), resonance.Compile(ctx), gain.Compile(ctx), ctx),
            $"{this}.{shape}({frequency}, {gain})");
    }

    private Sound Filter(FilterMode mode, Param cutoff, Param? q)
    {
        var resonance = q ?? 0.7071f;
        return new(ctx => new FilterOperator(Compile(ctx), mode, cutoff.Compile(ctx), resonance.Compile(ctx), ctx),
            $"{this}.{mode}({cutoff}, {resonance})");
    }

    /// <summary>Adds a constant or a modulating signal to this sound; <c>sound + 12</c> shifts a control signal up by 12.</summary>
    public static Sound operator +(Sound a, Param offset)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a + Sounds.FromParam(offset);
    }

    /// <summary>Adds a constant or a modulating signal to this sound.</summary>
    public static Sound operator +(Param offset, Sound a) => a + offset;

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

    /// <summary>
    /// Ring modulation: multiplies <paramref name="a"/> by <paramref name="b"/> channel by channel, left by left and right by right.
    /// A mono modulator (an LFO) scales both sides alike; to scale by the mono mix of a stereo sound, use <c>a * (Param)b</c>.
    /// </summary>
    public static Sound operator *(Sound a, Sound b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new(ctx => new MultiplyOperator(a.Compile(ctx), b.Compile(ctx), ctx), $"({a} * {b})");
    }

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
