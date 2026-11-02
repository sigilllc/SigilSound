using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>
/// Sources: the starting points of a sound. Designed for <c>using static Sigil.Sound.Sounds;</c>.
/// </summary>
public static class Sounds
{
    /// <summary>A sine wave. <paramref name="frequency"/> is in Hz.</summary>
    public static Sound Sine(Param frequency) =>
        Sound.FromOperator(ctx => new SineOperator(frequency.Compile(ctx), ctx), $"Sine({frequency})");

    /// <summary>An anti-aliased (PolyBLEP) sawtooth wave. <paramref name="frequency"/> is in Hz.</summary>
    public static Sound Saw(Param frequency) =>
        Sound.FromOperator(ctx => new SawOperator(frequency.Compile(ctx), ctx), $"Saw({frequency})");

    /// <summary>
    /// An anti-aliased (PolyBLEP) pulse wave. <paramref name="width"/> is the duty cycle in (0, 1); 0.5 is a square.
    /// </summary>
    public static Sound Square(Param frequency, Param? width = null)
    {
        var duty = width ?? 0.5f;
        return Sound.FromOperator(ctx => new SquareOperator(frequency.Compile(ctx), duty.Compile(ctx), ctx),
            $"Square({frequency})");
    }

    /// <summary>White noise, independent per channel. The same <paramref name="seed"/> renders the same noise.</summary>
    public static Sound Noise(int seed = 1) =>
        Sound.FromOperator(_ => new NoiseOperator(seed), "Noise()");

    /// <summary>
    /// A sound whose value is a function of time in seconds, such as <c>Fn(t => 200 + 100 * t)</c>.
    /// Use it wherever a <see cref="Param"/> is expected; C# cannot convert a bare lambda to <see cref="Param"/>.
    /// The function runs on the audio thread once per frame, so it must not allocate.
    /// </summary>
    public static Sound Fn(Func<double, double> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Sound.FromOperator(ctx => new FunctionOperator(function, ctx), "Fn(t)");
    }

    /// <summary>A constant signal (DC). Mostly useful as a building block.</summary>
    public static Sound Constant(float value) =>
        Sound.FromOperator(_ => new ConstantOperator(value), $"Constant({value})");

    /// <summary>Silence.</summary>
    public static Sound Silence => Constant(0);
}
