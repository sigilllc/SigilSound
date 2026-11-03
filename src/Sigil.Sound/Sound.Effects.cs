using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>The curve <see cref="Sound.Distort"/> shapes the signal with.</summary>
public enum DistortionMode
{
    /// <summary>Smooth saturation (tanh): warm, rounds peaks off gradually.</summary>
    Soft,

    /// <summary>Hard clipping: flat tops and a harsh, buzzy edge.</summary>
    Hard,

    /// <summary>Wavefolding: peaks fold back on themselves, giving bright, metallic overtones.</summary>
    Fold,

    /// <summary>Rectified, clipped and tone-shaped: a gritty, octave-up fuzz pedal.</summary>
    Fuzz,
}

public sealed partial class Sound
{
    /// <summary>
    /// An echo: repeats the sound after <paramref name="time"/> seconds, and repeats the repeats.
    /// </summary>
    /// <param name="time">Seconds between repeats, up to <paramref name="maxTime"/>. A number, or a sound or <c>Fn(t => ...)</c> to bend the pitch of the echoes.</param>
    /// <param name="feedback">How much of each echo feeds the next, from 0 (one echo) to 0.99 (nearly endless). Defaults to 0.4.</param>
    /// <param name="mix">0 is only the original, 1 only the echoes. Defaults to 0.5.</param>
    /// <param name="maxTime">The longest delay time in seconds; it sizes the buffer. Defaults to 2.</param>
    /// <example><code>Saw(220).Adsr(0.01, 0.1, 0, 0.1).Delay(0.375, feedback: 0.5)</code></example>
    public Sound Delay(Param time, Param? feedback = null, Param? mix = null, double maxTime = 2)
    {
        if (maxTime <= 0) throw new ArgumentOutOfRangeException(nameof(maxTime), maxTime, "The maximum delay must be positive.");
        var fb = feedback ?? 0.4f;
        var wet = mix ?? 0.5f;
        return new(ctx => new DelayOperator(Compile(ctx), time.Compile(ctx), fb.Compile(ctx), wet.Compile(ctx), maxTime, ctx),
            $"{this}.Delay({time})");
    }

    /// <summary>
    /// A reverb: the sound as if played in a room. Built from parallel comb filters and all-pass filters
    /// (a Schroeder design), with a slightly different room for each ear.
    /// </summary>
    /// <param name="size">Scales the room: below 1 is smaller, above 1 larger. Defaults to 1.</param>
    /// <param name="decay">Seconds for the tail to fade by 60 dB. Defaults to 2.5.</param>
    /// <param name="damping">How fast high frequencies die in the tail, from 0 (bright) to 1 (dark). Defaults to 0.4.</param>
    /// <param name="mix">0 is only the dry sound, 1 only the reverb. Defaults to 0.3.</param>
    /// <example><code>Sine(440).Adsr(0.005, 0.2, 0, 0.1).Reverb(decay: 4, mix: 0.4)</code></example>
    public Sound Reverb(double size = 1, double decay = 2.5, double damping = 0.4, Param? mix = null)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
        if (decay <= 0) throw new ArgumentOutOfRangeException(nameof(decay), decay, "Decay must be positive.");
        if (damping is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(damping), damping, "Damping must be between 0 and 1.");
        var wet = mix ?? 0.3f;
        return new(ctx => new ReverbOperator(Compile(ctx), wet.Compile(ctx), size, decay, damping, ctx),
            $"{this}.Reverb({size}, {decay})");
    }

    /// <summary>
    /// A comb filter tuned to a pitch: it resonates at <paramref name="frequency"/> and its multiples.
    /// Fed a short burst it rings like a plucked string (Karplus-Strong); fed a steady sound it adds a metallic ring.
    /// </summary>
    /// <param name="frequency">The pitch it resonates at, in Hz (20 and up). A number, or a sound or <c>Fn(t => ...)</c> to sweep it.</param>
    /// <param name="feedback">How long it rings, from 0 (not at all) to 0.999 (very long). Defaults to 0.95.</param>
    /// <param name="damping">How quickly the highs fade while it rings, from 0 (bright) to 0.99 (dull). Defaults to 0.2.</param>
    /// <example><code>Noise().Adsr(0, 0.01, 0, 0.001).Comb(220, feedback: 0.995)   // a plucked string</code></example>
    public Sound Comb(Param frequency, Param? feedback = null, Param? damping = null)
    {
        var fb = feedback ?? 0.95f;
        var damp = damping ?? 0.2f;
        return new(ctx => new CombOperator(Compile(ctx), frequency.Compile(ctx), fb.Compile(ctx), damp.Compile(ctx), ctx),
            $"{this}.Comb({frequency})");
    }

    /// <summary>
    /// Distortion: pushes the sound through a curve that adds harmonics, from warm saturation to fuzz.
    /// </summary>
    /// <param name="drive">How hard the sound is pushed into the curve. 1 is gentle; 10 is heavy. A number, or a sound or <c>Fn(t => ...)</c>. Defaults to 4.</param>
    /// <param name="mode">The curve: <see cref="DistortionMode.Soft"/> (default), <see cref="DistortionMode.Hard"/>, <see cref="DistortionMode.Fold"/> or <see cref="DistortionMode.Fuzz"/>.</param>
    /// <param name="level">The output level, to balance against the clean sound. Defaults to 1.</param>
    /// <example><code>Saw(110).Distort(drive: 8, DistortionMode.Fold, level: 0.5)</code></example>
    public Sound Distort(Param? drive = null, DistortionMode mode = DistortionMode.Soft, Param? level = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown distortion mode.");
        var amount = drive ?? 4f;
        var output = level ?? 1f;
        return new(ctx => new DistortOperator(Compile(ctx), amount.Compile(ctx), output.Compile(ctx), mode, ctx),
            $"{this}.Distort({amount}, {mode})");
    }

    /// <summary>
    /// A lo-fi crusher: reduces the bit depth, and optionally the sample rate, for a gritty, aliased sound.
    /// </summary>
    /// <param name="bits">Bits of resolution: 16 is clean, 8 is classic lo-fi, 2 or 3 is brutal. Fractions work. Defaults to 8.</param>
    /// <param name="rate">The reduced sample rate in Hz. 8000 sounds like an old sampler; omit it to keep the full rate.</param>
    /// <example><code>Saw(110).BitCrush(bits: 5, rate: 6000)</code></example>
    public Sound BitCrush(Param? bits = null, Param? rate = null)
    {
        var depth = bits ?? 8f;
        return new(ctx => new BitCrushOperator(Compile(ctx), depth.Compile(ctx),
                rate?.Compile(ctx) ?? ((Param)(float)ctx.SampleRate).Compile(ctx), ctx),
            $"{this}.BitCrush({depth})");
    }

    /// <summary>
    /// A chorus: thickens the sound by mixing in slightly detuned, slowly drifting copies spread across the stereo field.
    /// </summary>
    /// <param name="rate">How fast the copies drift, in Hz. Defaults to 0.8.</param>
    /// <param name="depth">How far they drift, from 0 (none) to 1 (up to 12 ms). Defaults to 0.5.</param>
    /// <param name="mix">0 is only the original, 1 only the copies. Defaults to 0.5.</param>
    /// <param name="voices">How many copies, from 1 to 8. More is wider and thicker. Defaults to 3.</param>
    /// <example><code>(Saw(110) + Saw(165)).LowPass(1500).Chorus(rate: 0.5, voices: 4) * 0.3</code></example>
    public Sound Chorus(Param? rate = null, Param? depth = null, Param? mix = null, int voices = 3)
    {
        if (voices is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(voices), voices, "Voices must be between 1 and 8.");
        var speed = rate ?? 0.8f;
        var amount = depth ?? 0.5f;
        var wet = mix ?? 0.5f;
        return new(ctx => new ChorusOperator(Compile(ctx), speed.Compile(ctx), amount.Compile(ctx), wet.Compile(ctx), voices, ctx),
            $"{this}.Chorus({voices})");
    }

    /// <summary>
    /// A phaser: sweeps notches up and down the spectrum with a chain of all-pass filters, for a swirling, jet-like movement.
    /// </summary>
    /// <param name="rate">Sweeps per second. Defaults to 0.4.</param>
    /// <param name="feedback">Sharpens the notches, from 0 to 0.95. Negative values change the character. Defaults to 0.3.</param>
    /// <param name="mix">0 is only the original, 1 only the filtered sound; around 0.5 gives the deepest notches. Defaults to 0.5.</param>
    /// <param name="stages">All-pass stages, from 1 to 12; every two make one notch. Defaults to 4.</param>
    /// <param name="minFrequency">The bottom of the sweep in Hz. Defaults to 400.</param>
    /// <param name="maxFrequency">The top of the sweep in Hz. Defaults to 1500.</param>
    /// <example><code>Saw(110).Phaser(rate: 0.2, feedback: 0.6, stages: 6)</code></example>
    public Sound Phaser(Param? rate = null, Param? feedback = null, Param? mix = null, int stages = 4,
        double minFrequency = 400, double maxFrequency = 1500)
    {
        if (stages is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(stages), stages, "Stages must be between 1 and 12.");
        if (minFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(minFrequency), minFrequency, "The sweep range must be positive.");
        if (maxFrequency <= minFrequency) throw new ArgumentOutOfRangeException(nameof(maxFrequency), maxFrequency, "The top of the sweep must be above the bottom.");
        var speed = rate ?? 0.4f;
        var fb = feedback ?? 0.3f;
        var wet = mix ?? 0.5f;
        return new(ctx => new PhaserOperator(Compile(ctx), speed.Compile(ctx), fb.Compile(ctx), wet.Compile(ctx), stages,
                minFrequency, Math.Min(maxFrequency, ctx.Nyquist * 0.9), ctx),
            $"{this}.Phaser({stages})");
    }

    /// <summary>
    /// A limiter: keeps the sound from passing a ceiling by turning it down just enough, then lets the level recover.
    /// Put it last in a chain so a loud mix cannot clip.
    /// </summary>
    /// <param name="ceiling">The highest level that may come out, above 0 and up to 1. Defaults to 0.95.</param>
    /// <param name="release">Seconds for the level to recover after a peak. Defaults to 0.08.</param>
    /// <example><code>(Saw(110) + Saw(165) + Saw(220)).Limit()</code></example>
    public Sound Limit(double ceiling = 0.95, double release = 0.08)
    {
        if (ceiling is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "The ceiling must be above 0 and at most 1.");
        if (release < 0) throw new ArgumentOutOfRangeException(nameof(release), release, "Release must not be negative.");
        return new(ctx => new LimiterOperator(Compile(ctx), ceiling, release, ctx), $"{this}.Limit({ceiling})");
    }

    /// <summary>
    /// Balances the stereo field by turning down the side you pan away from; the other side is left alone.
    /// A mono source panned to one side ends up silent on the other, as with a balance knob.
    /// </summary>
    /// <param name="position">-1 is hard left, 0 the center (unchanged), 1 hard right. A number, or a sound or <c>Fn(t => ...)</c> to move it.</param>
    /// <example><code>Saw(110).Pan(Sine(0.3))   // drifts between the speakers</code></example>
    public Sound Pan(Param position) =>
        new(ctx => new PanOperator(Compile(ctx), position.Compile(ctx)), $"{this}.Pan({position})");
}
