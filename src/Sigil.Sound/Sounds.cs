using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>
/// Sources: the starting points of a sound. Designed for <c>using static Sigil.Sound.Sounds;</c>.
/// </summary>
public static class Sounds
{
    /// <summary>A sine wave. <paramref name="frequency"/> is in Hz.</summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> for vibrato and sweeps. Also used as an LFO below 20 Hz.</param>
    /// <example><code>Sine(440)</code></example>
    public static Sound Sine(Param frequency) =>
        Sound.FromOperator(ctx => new SineOperator(frequency.Compile(ctx), ctx), $"Sine({frequency})");

    /// <summary>An anti-aliased (PolyBLEP) sawtooth wave. <paramref name="frequency"/> is in Hz.</summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> to modulate it.</param>
    /// <example><code>Saw(110).LowPass(800)</code></example>
    public static Sound Saw(Param frequency) =>
        Sound.FromOperator(ctx => new SawOperator(frequency.Compile(ctx), ctx), $"Saw({frequency})");

    /// <summary>
    /// An anti-aliased (PolyBLEP) pulse wave. <paramref name="width"/> is the duty cycle in (0, 1); 0.5 is a square.
    /// </summary>
    /// <param name="frequency">Pitch in Hz: a number, or a sound or <c>Fn(t => ...)</c> to modulate it.</param>
    /// <param name="width">Fraction of each cycle spent high, from 0.01 to 0.99. Modulate it for pulse-width movement. Defaults to 0.5.</param>
    /// <example><code>Square(110, Sine(0.3).Range(0.1, 0.9))</code></example>
    public static Sound Square(Param frequency, Param? width = null)
    {
        var duty = width ?? 0.5f;
        return Sound.FromOperator(ctx => new SquareOperator(frequency.Compile(ctx), duty.Compile(ctx), ctx),
            $"Square({frequency})");
    }

    /// <summary>White noise, independent per channel. The same <paramref name="seed"/> renders the same noise.</summary>
    /// <param name="seed">Starting value of the generator; change it for different noise. Defaults to 1.</param>
    /// <example><code>Noise().LowPass(1000) * 0.2</code></example>
    public static Sound Noise(int seed = 1) =>
        Sound.FromOperator(_ => new NoiseOperator(seed), "Noise()");

    /// <summary>
    /// A sound whose value is a function of time in seconds, such as <c>Fn(t => 200 + 100 * t)</c>.
    /// Use it wherever a <see cref="Param"/> is expected; C# cannot convert a bare lambda to <see cref="Param"/>.
    /// The function runs on the audio thread once per frame, so it must not allocate.
    /// </summary>
    /// <param name="function">
    /// Maps time in seconds since the sound started to a value in [-1, 1] when played, or to any number when used as a parameter.
    /// When the render context has a <see cref="HostClock"/>, time is the host's instead, so a replacement sound carries on from where the old one was.
    /// Inside a note voice it is always the time since the note began.
    /// </param>
    /// <example><code>Sine(Fn(t => 200 + 100 * t))   // a rising pitch</code></example>
    public static Sound Fn(Func<double, double> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Sound.FromOperator(ctx => new FunctionOperator(function, ctx), "Fn(t)");
    }

    /// <summary>A constant signal (DC). Mostly useful as a building block.</summary>
    /// <param name="value">The level to output.</param>
    public static Sound Constant(double value) =>
        Sound.FromOperator(_ => new ConstantOperator((float)value), $"Constant({value})");

    // A parameter as a sound, for arithmetic such as sound + 12.
    internal static Sound FromParam(Param param) =>
        Sound.FromOperator(ctx => new ParamOperator(param.Compile(ctx)), param.ToString());

    /// <summary>Silence.</summary>
    public static Sound Silence => Constant(0);
}
