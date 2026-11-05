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
    /// Places the sound in the stereo field with an equal-power law, so it keeps the same loudness wherever it sits.
    /// A stereo sound is folded to mono first; use <see cref="Balance"/> to keep a stereo image.
    /// </summary>
    /// <param name="position">-1 is hard left, 0 the center (a mono source keeps its level), 1 hard right. A number, or a sound or <c>Fn(t => ...)</c> to move it.</param>
    /// <example><code>Saw(110).Pan(Sine(0.3))   // drifts between the speakers</code></example>
    public Sound Pan(Param position) =>
        new(ctx => new PanOperator(Compile(ctx), position.Compile(ctx)), $"{this}.Pan({position})");

    /// <summary>
    /// Balances the stereo field by turning down the side you pan away from; the other side is left alone.
    /// A mono source balanced to one side ends up silent on the other, as with a balance knob, and a stereo image is kept.
    /// </summary>
    /// <param name="position">-1 is hard left, 0 the center (unchanged), 1 hard right. A number, or a sound or <c>Fn(t => ...)</c> to move it.</param>
    /// <example><code>pad.Reverb().Balance(-0.3)   // nudge a wide pad left without narrowing it</code></example>
    public Sound Balance(Param position) =>
        new(ctx => new BalanceOperator(Compile(ctx), position.Compile(ctx)), $"{this}.Balance({position})");

    /// <summary>
    /// A dynamic range compressor: turns down the parts above <paramref name="thresholdDb"/>, so loud and quiet passages
    /// sit closer together, then adds makeup gain to bring the overall level back up. Use it to glue a drum bus together
    /// or to tame a lead, or with a <paramref name="sidechain"/> to duck one sound under another.
    /// </summary>
    /// <remarks>
    /// The detector follows the louder of the left and right channels, with separate attack and release times.
    /// A soft knee eases the compression in around the threshold so there is no audible corner.
    /// Lookahead delays the audio, so the gain is already falling when a transient arrives, at the cost of that much latency.
    /// </remarks>
    /// <param name="thresholdDb">The level, in dBFS, above which gain reduction starts. Full scale is 0 and quieter is more negative. Defaults to -18.</param>
    /// <param name="ratio">How hard it compresses above the threshold: 4 means 4 dB of input above the threshold gives 1 dB out. 1 does nothing, and very high values act as a limiter. Defaults to 4.</param>
    /// <param name="attack">Seconds to react once the level rises. Shorter catches more transients but can dull the attack. Defaults to 0.01.</param>
    /// <param name="release">Seconds for the gain to recover after the level falls. Shorter pumps more audibly. Defaults to 0.1.</param>
    /// <param name="kneeDb">Width of the soft transition around the threshold, in dB. 0 is a hard knee. Defaults to 6.</param>
    /// <param name="makeupGainDb">Gain added after compression, in dB, to restore the level. Defaults to 0.</param>
    /// <param name="sidechain">A sound whose level drives the compressor instead of this one: a kick keyed into a pad ducks the pad. Null uses this sound.</param>
    /// <param name="lookahead">Seconds the detector runs ahead of the audio, so the gain is already falling when a transient arrives: 0 to 0.02. It adds that much latency. Defaults to 0.003 (3 ms); use 0 for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its range, such as a ratio below 1 or a lookahead above 0.02 seconds.</exception>
    /// <example><code>drums.Compress(thresholdDb: -16, ratio: 4, attack: 0.005, release: 0.1, kneeDb: 6, makeupGainDb: 3)</code></example>
    /// <example><code>pad.Compress(thresholdDb: -20, ratio: 8, attack: 0.002, release: 0.25, sidechain: kick)   // pumping pad</code></example>
    public Sound Compress(Param? thresholdDb = null, Param? ratio = null, Param? attack = null, Param? release = null,
        Param? kneeDb = null, Param? makeupGainDb = null, Sound? sidechain = null, double lookahead = 0.003)
    {
        if (ratio is { IsConstant: true } r && r.ConstantValue < 1)
            throw new ArgumentOutOfRangeException(nameof(ratio), r.ConstantValue, "The ratio must be at least 1.");
        if (attack is { IsConstant: true } a && a.ConstantValue < 0)
            throw new ArgumentOutOfRangeException(nameof(attack), a.ConstantValue, "Attack must not be negative.");
        if (release is { IsConstant: true } rel && rel.ConstantValue < 0)
            throw new ArgumentOutOfRangeException(nameof(release), rel.ConstantValue, "Release must not be negative.");
        if (kneeDb is { IsConstant: true } k && k.ConstantValue < 0)
            throw new ArgumentOutOfRangeException(nameof(kneeDb), k.ConstantValue, "The knee must not be negative.");
        if (double.IsNaN(lookahead) || lookahead is < 0 or > 0.02)
            throw new ArgumentOutOfRangeException(nameof(lookahead), lookahead, "Lookahead must be between 0 and 0.02 seconds.");

        var threshold = thresholdDb ?? -18f;
        var slope = ratio ?? 4f;
        var attackTime = attack ?? 0.01f;
        var releaseTime = release ?? 0.1f;
        var knee = kneeDb ?? 6f;
        var makeup = makeupGainDb ?? 0f;
        return new(ctx => new CompressorOperator(Compile(ctx), sidechain?.Compile(ctx), threshold.Compile(ctx), slope.Compile(ctx),
                attackTime.Compile(ctx), releaseTime.Compile(ctx), knee.Compile(ctx), makeup.Compile(ctx),
                (int)Math.Round(lookahead * ctx.SampleRate), ctx),
            $"{this}.Compress({threshold}, {slope})");
    }

    /// <summary>
    /// A tape echo: repeats the sound, and each repeat is darkened and saturated as it feeds back, like a tape delay.
    /// Changing the delay time glides the pitch of the repeats, as a tape head does, instead of clicking.
    /// </summary>
    /// <param name="time">Seconds between repeats, up to <paramref name="maxTime"/>. A sound or <c>Fn(t => ...)</c> makes the time wander for warble and dub-style sweeps.</param>
    /// <param name="feedback">How much of each repeat feeds the next, from 0 (one echo) to 0.95 (a long, slowly dying trail). Defaults to 0.5.</param>
    /// <param name="damping">The cutoff in Hz of the low-pass in the feedback loop. Lower values make each repeat darker. Defaults to 3000.</param>
    /// <param name="drive">How hard each repeat is pushed into the tape saturation: 1 or more. 1 is gentle; 3 or more is heavy. Defaults to 1.</param>
    /// <param name="mix">0 is only the original, 1 only the echoes. Defaults to 0.35.</param>
    /// <param name="maxTime">The longest delay time in seconds; it sizes the buffer. Defaults to 2.</param>
    /// <param name="pingPong">When true, the repeats bounce between the left and right channels, so each echo lands on the opposite side. Defaults to false.</param>
    /// <exception cref="ArgumentOutOfRangeException">The maximum time is not positive, or a constant drive is below 1.</exception>
    /// <example><code>synth.TapeDelay(time: Sine(0.2).Range(0.2, 0.4), feedback: 0.65, damping: 2800, drive: 1.4, mix: 0.35)</code></example>
    /// <example><code>pluck.TapeDelay(time: 0.375, feedback: 0.5, pingPong: true)   // echoes that bounce across the stereo field</code></example>
    public Sound TapeDelay(Param time, Param? feedback = null, Param? damping = null, Param? drive = null, Param? mix = null,
        double maxTime = 2, bool pingPong = false)
    {
        if (maxTime <= 0) throw new ArgumentOutOfRangeException(nameof(maxTime), maxTime, "The maximum delay must be positive.");
        var fb = feedback ?? 0.5f;
        var cutoff = damping ?? 3000f;
        var push = drive ?? 1f;
        if (push.IsConstant && push.ConstantValue < 1)
            throw new ArgumentOutOfRangeException(nameof(drive), push.ConstantValue, "The drive must be at least 1.");
        var wet = mix ?? 0.35f;
        return new(ctx => new TapeDelayOperator(Compile(ctx), time.Compile(ctx), fb.Compile(ctx), cutoff.Compile(ctx),
                push.Compile(ctx), wet.Compile(ctx), maxTime, pingPong, ctx),
            $"{this}.TapeDelay({time})");
    }

    /// <summary>
    /// A large algorithmic reverb built from eight feedback delay lines that mix into one another. It gives a denser, smoother
    /// tail than <see cref="Reverb"/>, and with <paramref name="sheen"/> it can shimmer upward like a cathedral.
    /// </summary>
    /// <param name="decay">Seconds for the tail to fade by 60 dB. Defaults to 3.</param>
    /// <param name="size">Scales the room: 0.5 is a small room, 1 a medium one, 4 a hall. Defaults to 1.</param>
    /// <param name="damping">How fast the high frequencies die in the tail, from 0 (bright) to 0.99 (dark). Defaults to 0.35.</param>
    /// <param name="predelay">Seconds between the dry sound and the first reflection, from 0 to 0.5. A longer predelay separates the sound from its room. Defaults to 0.01.</param>
    /// <param name="sheen">How much of an octave-up copy of the tail is mixed in, from 0 (none) to 0.7 (very ethereal). The decay time stays the same. A number, or a sound to move it. Defaults to 0.</param>
    /// <param name="mix">0 is only the dry sound, 1 only the reverb. Defaults to 0.3.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its range.</exception>
    /// <example><code>pad.FdnReverb(decay: 5.0, size: 1.5, damping: 0.35, predelay: 0.03, sheen: 0.4, mix: 0.4)   // a shimmering hall</code></example>
    public Sound FdnReverb(double decay = 3, double size = 1, double damping = 0.35, double predelay = 0.01,
        Param? sheen = null, Param? mix = null)
    {
        if (decay <= 0) throw new ArgumentOutOfRangeException(nameof(decay), decay, "Decay must be positive.");
        if (size is <= 0 or > 4 || double.IsNaN(size)) throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be above 0 and at most 4.");
        if (damping is < 0 or >= 1 || double.IsNaN(damping)) throw new ArgumentOutOfRangeException(nameof(damping), damping, "Damping must be from 0 up to, but not including, 1.");
        if (predelay is < 0 or > 0.5 || double.IsNaN(predelay)) throw new ArgumentOutOfRangeException(nameof(predelay), predelay, "Predelay must be from 0 to 0.5 seconds.");
        var shimmer = sheen ?? 0f;
        var wet = mix ?? 0.3f;
        return new(ctx => new FdnReverbOperator(Compile(ctx), shimmer.Compile(ctx), wet.Compile(ctx), decay, size, damping, predelay, ctx),
            $"{this}.FdnReverb({decay}, {size})");
    }

    /// <summary>
    /// Stereo width: scales the difference between the left and right channels while keeping their total level.
    /// Narrowing collapses a stereo sound toward mono; widening spreads it out. Mono input stays mono.
    /// </summary>
    /// <param name="width">0 is mono, 1 leaves the sound unchanged, and values above 1 widen it, up to 4. A number, or a sound or <c>Fn(t => ...)</c> to move it.</param>
    /// <example><code>bass.Spread(0.0)            // keep the low end mono for club systems</code></example>
    /// <example><code>pad.Chorus().Spread(1.8)    // a wide pad</code></example>
    public Sound Spread(Param width) =>
        new(ctx => new StereoSpreadOperator(Compile(ctx), width.Compile(ctx)), $"{this}.Spread({width})");

    /// <summary>
    /// A West Coast wavefolder: drives the sound past ±1 and folds it back, adding harmonics that sound bright, bell-like and
    /// metallic rather than clipped. Symmetry adds warm even harmonics on top of the odd ones.
    /// </summary>
    /// <param name="amount">How many times the sound folds: 1 is barely touched, 3 to 5 is typical, and higher gets very bright. A number, or a sound to sweep it. Defaults to 3.</param>
    /// <param name="symmetry">An offset applied before folding, from -1 to 1. Zero keeps only odd harmonics; a small value brings in even ones. Defaults to 0.</param>
    /// <param name="mix">0 is only the dry sound, 1 only the folded sound. Defaults to 1.</param>
    /// <example><code>Sine(110).Fold(amount: Sine(0.2).Range(1.0, 7.0), symmetry: 0.25, mix: 1.0).LowPass(4000)</code></example>
    public Sound Fold(Param? amount = null, Param? symmetry = null, Param? mix = null)
    {
        var drive = amount ?? 3f;
        var offset = symmetry ?? 0f;
        var wet = mix ?? 1f;
        return new(ctx => new WavefolderOperator(Compile(ctx), drive.Compile(ctx), offset.Compile(ctx), wet.Compile(ctx), ctx),
            $"{this}.Fold({drive})");
    }

    /// <summary>
    /// A vowel filter: four formant resonators that make a sound speak. The position morphs through A, E, I, O and U.
    /// It is good for talkbox leads, choir-like sweeps and humanizing a synth.
    /// </summary>
    /// <param name="vowel">The position from 0 (A) through 0.25 (E), 0.5 (I), 0.75 (O) to 1 (U), with the vowels blended in between. A number, or a sound or <c>Fn(t => ...)</c> to talk. Values outside 0 to 1 are clamped.</param>
    /// <param name="resonance">How sharp the formants are, from 0 (soft and broad) to 1 (sharp and ringing). Defaults to 0.5.</param>
    /// <example><code>Saw(110).Vowel(vowel: Sine(0.5).Range(0.0, 1.0), resonance: 0.8).Distort(drive: 1.5)</code></example>
    public Sound Vowel(Param vowel, Param? resonance = null)
    {
        var sharpness = resonance ?? 0.5f;
        return new(ctx => new VowelFilterOperator(Compile(ctx), vowel.Compile(ctx), sharpness.Compile(ctx), ctx),
            $"{this}.Vowel({vowel})");
    }

    /// <summary>
    /// Tracks the loudness of this sound as a control signal from 0 to 1, so the sound can drive other parameters.
    /// Pair it with <see cref="Range(double, double)"/> to map the level onto a filter cutoff, a gain or a pitch.
    /// </summary>
    /// <param name="attack">Seconds for the output to rise when the sound gets louder. Defaults to 0.01.</param>
    /// <param name="release">Seconds for the output to fall when the sound gets quieter. Defaults to 0.1.</param>
    /// <example><code>var level = guitar.FollowEnvelope(attack: 0.005, release: 0.08);
    /// var autoWah = guitar.LowPass(level.Range(350, 3200), q: 4.0);</code></example>
    public Sound FollowEnvelope(Param? attack = null, Param? release = null)
    {
        var rise = attack ?? 0.01f;
        var fall = release ?? 0.1f;
        return new(ctx => new EnvelopeFollowerOperator(Compile(ctx), rise.Compile(ctx), fall.Compile(ctx), ctx),
            $"{this}.FollowEnvelope({rise}, {fall})");
    }
}
