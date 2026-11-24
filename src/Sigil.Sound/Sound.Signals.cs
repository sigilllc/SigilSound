using Sigil.Sound.Operators;

namespace Sigil.Sound;

// Signal arithmetic and the small signal tools scripts otherwise write by hand with Fn: subtraction, a range whose ends move,
// a crossfade, a lag, sample and hold, a gate and a DC blocker.
public sealed partial class Sound
{
    /// <summary>
    /// Subtracts one sound from another, sample by sample on each channel. A sound used on both sides is rendered once.
    /// </summary>
    /// <remarks>
    /// <c>x - x.LowPass(500)</c> leaves what the filter took away. Only an effect that adds no delay cancels this way:
    /// <see cref="PitchShift"/> and <see cref="Compress"/> (with its lookahead) are late, so subtracting them gives a comb filter.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Two saws half a hertz apart: subtracting them leaves a slow, hollow beating.
    /// (Saw(110) - Saw(110.5)) * 0.3
    /// </code>
    /// </example>
    public static Sound operator -(Sound a, Sound b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return new(ctx => new SubtractOperator(a.Compile(ctx), b.Compile(ctx), ctx), $"({a} - {b})");
    }

    /// <summary>
    /// Subtracts a constant or a modulating signal from this sound; <c>lfo - 0.5</c> moves a 0 to 1 signal down to swing around zero.
    /// </summary>
    /// <example>
    /// <code>
    /// // A 0 to 1 square, re-centred around zero, moves the cutoff up and down by 500 Hz.
    /// Saw(110).LowPass(1500 + (Square(2).Range(0, 1) - 0.5) * 1000) * 0.3
    /// </code>
    /// </example>
    public static Sound operator -(Sound a, Param offset)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(ctx => new SubtractOperator(a.Compile(ctx), Sounds.FromParam(offset).Compile(ctx), ctx), $"({a} - {offset})");
    }

    /// <summary>Subtracts this sound from a constant or a signal; <c>1 - lfo</c> turns a 0 to 1 signal upside down.</summary>
    /// <example>
    /// <code>
    /// // A tremolo that dips down from full level instead of rising up to it.
    /// Saw(110) * (1 - Sine(4).Range(0, 0.8)) * 0.4
    /// </code>
    /// </example>
    public static Sound operator -(Param offset, Sound a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(ctx => new SubtractOperator(Sounds.FromParam(offset).Compile(ctx), a.Compile(ctx), ctx), $"({offset} - {a})");
    }

    /// <summary>Turns the sound upside down (flips its polarity): the same as <c>a * -1</c>.</summary>
    /// <example>
    /// <code>
    /// // A saw upside down: falling ramps instead of rising ones.
    /// -Saw(110) * 0.3
    /// </code>
    /// </example>
    public static Sound operator -(Sound a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(ctx => new GainOperator(a.Compile(ctx), ((Param)(-1f)).Compile(ctx)), $"-{a}");
    }

    /// <summary>
    /// Maps a bipolar signal in [-1, 1] to [<paramref name="min"/>, <paramref name="max"/>], where either end can be a
    /// <see cref="Control"/>, a function or another sound, so a knob can set how far an LFO reaches.
    /// </summary>
    /// <param name="min">The value the signal's -1 maps to.</param>
    /// <param name="max">The value the signal's +1 maps to.</param>
    /// <example>
    /// <code>
    /// // The knob sets how high the sweep reaches; the bottom stays at 200 Hz.
    /// var top = new Control(2000, 400, 8000, name: "Top");
    /// Saw(110).LowPass(Sine(0.25).Range(200, top)) * 0.3
    /// </code>
    /// </example>
    public Sound Range(Param min, Param max) =>
        new(ctx => new RangeParamOperator(Compile(ctx), min.Compile(ctx), max.Compile(ctx)), $"{this}.Range({min}, {max})");

    /// <summary>
    /// Crossfades from this sound to <paramref name="other"/> by <paramref name="amount"/>, keeping the loudness steady
    /// (an equal-power fade): 0 is this sound alone, 1 is <paramref name="other"/> alone.
    /// </summary>
    /// <param name="other">The sound to fade towards.</param>
    /// <param name="amount">How far to fade, from 0 to 1. Defaults to 0.5, both at once.</param>
    /// <example>
    /// <code>
    /// // Fades between a saw and a square an octave up, once every two seconds.
    /// Saw(110).Mix(Square(220), Sine(0.5).Range(0, 1)) * 0.3
    /// </code>
    /// </example>
    public Sound Mix(Sound other, Param? amount = null)
    {
        ArgumentNullException.ThrowIfNull(other);
        var fade = amount ?? 0.5f;
        return new(ctx => new CrossfadeOperator(Compile(ctx), other.Compile(ctx), fade.Compile(ctx), ctx), $"{this}.Mix({other}, {fade})");
    }

    /// <summary>
    /// Limits how fast the signal can change, so a jump becomes a glide: portamento on a pitch, smoothing on a stepped control,
    /// softer corners on a square LFO. A glide covers about 63% of the way in the given time, and nearly all of it in five times that.
    /// </summary>
    /// <param name="rise">Seconds to glide upward. Defaults to 0.05.</param>
    /// <param name="fall">Seconds to glide downward. Defaults to the same as <paramref name="rise"/>.</param>
    /// <example>
    /// <code>
    /// // A two-note trill that glides between its notes.
    /// Sine(Square(2).Range(220, 330).Slew(0.05)) * 0.3
    /// </code>
    /// </example>
    public Sound Slew(Param? rise = null, Param? fall = null)
    {
        var up = rise ?? 0.05f;
        var down = fall ?? up;
        return new(ctx => new SlewOperator(Compile(ctx), up.Compile(ctx), down.Compile(ctx), ctx), $"{this}.Slew({up}, {down})");
    }

    /// <summary>
    /// Sample and hold: takes the signal's value <paramref name="rate"/> times a second and holds it until the next time, which
    /// turns noise into stepped random modulation. With a host clock the steps follow the host's time.
    /// </summary>
    /// <param name="rate">Steps per second. Defaults to 8.</param>
    /// <example>
    /// <code>
    /// // A new random pitch eight times a second.
    /// Sine(Noise().Hold(8).Range(220, 880)) * 0.3
    /// </code>
    /// </example>
    public Sound Hold(Param? rate = null)
    {
        var steps = rate ?? 8f;
        return new(ctx => new HoldOperator(Compile(ctx), steps.Compile(ctx), ctx), $"{this}.Hold({steps})");
    }

    /// <summary>
    /// A noise gate: silences the sound while its level, or the level of <paramref name="sidechain"/>, is below
    /// <paramref name="thresholdDb"/>. It closes 4 dB below the threshold, so a level that hovers there does not chatter.
    /// </summary>
    /// <param name="thresholdDb">The level in dB that opens the gate. Defaults to -40.</param>
    /// <param name="attack">Seconds for the gate to open fully once the level is above the threshold. Defaults to 0.001.</param>
    /// <param name="release">Seconds for the gate to close fully once the level has fallen. Defaults to 0.05.</param>
    /// <param name="sidechain">A sound whose level opens the gate instead of this one's: a drum keyed into a pad chops the pad to its beat. By default (null) it uses this sound.</param>
    /// <example>
    /// <code>
    /// // Noise that is loud then quiet three times a second: the gate cuts the quiet half to silence.
    /// (Noise() * Square(3).Range(0.02, 1)).Gate(-20) * 0.3
    /// </code>
    /// </example>
    public Sound Gate(Param? thresholdDb = null, Param? attack = null, Param? release = null, Sound? sidechain = null)
    {
        var threshold = thresholdDb ?? -40f;
        var open = attack ?? 0.001f;
        var close = release ?? 0.05f;
        return new(ctx => new GateOperator(Compile(ctx), sidechain?.Compile(ctx), threshold.Compile(ctx), open.Compile(ctx), close.Compile(ctx), ctx),
            sidechain is null ? $"{this}.Gate({threshold}, {open}, {close})" : $"{this}.Gate({threshold}, {open}, {close}, {sidechain})");
    }

    /// <summary>
    /// Removes the constant offset (DC) that leaves a wave off centre, with a gentle high-pass. A narrow pulse, a wavefolder,
    /// asymmetric distortion or a subtraction can all leave one; it wastes headroom and makes a click when the sound stops.
    /// </summary>
    /// <param name="cutoff">The frequency below which the high-pass works, in Hz. Defaults to 20.</param>
    /// <example>
    /// <code>
    /// // A pulse with a 10% duty cycle sits well below zero on average; DcBlock re-centres it.
    /// Square(110, width: 0.1).DcBlock() * 0.3
    /// </code>
    /// </example>
    public Sound DcBlock(Param? cutoff = null)
    {
        var frequency = cutoff ?? 20f;
        return new(ctx => new DcBlockOperator(Compile(ctx), frequency.Compile(ctx), ctx), $"{this}.DcBlock({frequency})");
    }
}
