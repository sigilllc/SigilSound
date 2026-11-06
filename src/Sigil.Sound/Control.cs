using System.Globalization;

namespace Sigil.Sound;

/// <summary>How <see cref="Control.Map(double, double, ControlCurve)"/> spreads the control's range over the output range.</summary>
public enum ControlCurve
{
    /// <summary>Equal steps in the control give equal steps in the output.</summary>
    Linear,

    /// <summary>Equal steps give equal ratios, which is how pitch and filter cutoff are heard. Both output ends must be positive.</summary>
    Exponential,
}

/// <summary>
/// A value the game or UI sets while sound is playing, such as engine RPM or how tense the music is. The audio thread
/// follows it smoothly, so a jump in <see cref="Value"/> (a game frame is 16 ms, a block is 5 ms) glides over
/// <c>slewSeconds</c> instead of clicking. Setting it from any thread is safe and takes no lock.
/// </summary>
/// <remarks>
/// A control converts to a <see cref="Param"/> and to a <see cref="Sound"/>, so it goes anywhere a modulating value does.
/// One control can drive any number of sounds and voices; each reads it once per block. A graph compiled later starts at
/// the current value, with no glide from the default.
/// </remarks>
/// <example>
/// <code>
/// // A control is a value the game changes while the sound plays. Here an engine note follows an RPM control.
/// var rpm = new Control(800, min: 600, max: 7000, slewSeconds: 0.03);
/// var engine = Saw(rpm / 30).LowPass(rpm.Map(200, 4000, ControlCurve.Exponential));
/// rpm.Value = 3500;   // set from the game loop, every frame
/// engine * 0.3
/// </code>
/// </example>
public sealed class Control
{
    private float _target;

    /// <param name="value">The starting value.</param>
    /// <param name="min">The lowest value; <see cref="Value"/> is clamped to it.</param>
    /// <param name="max">The highest value; <see cref="Value"/> is clamped to it.</param>
    /// <param name="slewSeconds">
    /// The glide time: after this long the sound has covered about 63% of a jump. 0 follows at once, which can click on
    /// gain, pitch and cutoff.
    /// </param>
    /// <param name="name">A label for debugging and for showing the control in a UI.</param>
    public Control(double value = 0, double min = 0, double max = 1, double slewSeconds = 0.02, string? name = null)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min >= max)
            throw new ArgumentOutOfRangeException(nameof(min), min, "The range needs finite ends with min below max.");
        if (!double.IsFinite(slewSeconds) || slewSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(slewSeconds), slewSeconds, "The glide time must be zero or more.");
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a number.");

        Min = (float)min;
        Max = (float)max;
        SlewSeconds = (float)slewSeconds;
        Name = name;
        _target = Math.Clamp((float)value, Min, Max);
    }

    /// <summary>The lowest value.</summary>
    public float Min { get; }

    /// <summary>The highest value.</summary>
    public float Max { get; }

    /// <summary>The glide time in seconds.</summary>
    public float SlewSeconds { get; }

    /// <summary>The label given at creation, or null.</summary>
    public string? Name { get; }

    /// <summary>
    /// The value the sound is heading for. Set it from any thread; out-of-range values are clamped to
    /// <see cref="Min"/> and <see cref="Max"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    public double Value
    {
        get => Volatile.Read(ref _target);
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a number.");
            Volatile.Write(ref _target, Math.Clamp((float)value, Min, Max));
        }
    }

    internal float Target => Volatile.Read(ref _target);

    /// <summary>
    /// The control mapped from its own range onto <paramref name="outMin"/> to <paramref name="outMax"/>. The glide happens
    /// before the mapping, so an exponential cutoff sweep stays smooth.
    /// </summary>
    /// <param name="outMin">The output when the control is at <see cref="Min"/>.</param>
    /// <param name="outMax">The output when the control is at <see cref="Max"/>.</param>
    /// <param name="curve">Use <see cref="ControlCurve.Exponential"/> for frequencies.</param>
    public Param Map(double outMin, double outMax, ControlCurve curve = ControlCurve.Linear) =>
        Map(Min, Max, outMin, outMax, curve);

    /// <summary>The control mapped from <paramref name="inMin"/> to <paramref name="inMax"/> onto the output range; values outside the input range extend the line.</summary>
    /// <param name="inMin">The input value that maps to <paramref name="outMin"/>.</param>
    /// <param name="inMax">The input value that maps to <paramref name="outMax"/>.</param>
    /// <param name="outMin">The output at <paramref name="inMin"/>.</param>
    /// <param name="outMax">The output at <paramref name="inMax"/>.</param>
    /// <param name="curve">Use <see cref="ControlCurve.Exponential"/> for frequencies.</param>
    public Param Map(double inMin, double inMax, double outMin, double outMax, ControlCurve curve = ControlCurve.Linear)
    {
        if (!double.IsFinite(inMin) || !double.IsFinite(inMax) || inMin == inMax)
            throw new ArgumentOutOfRangeException(nameof(inMin), inMin, "The input range needs two different finite ends.");
        if (!double.IsFinite(outMin) || !double.IsFinite(outMax))
            throw new ArgumentOutOfRangeException(nameof(outMin), outMin, "The output range needs finite ends.");
        if (!Enum.IsDefined(curve))
            throw new ArgumentOutOfRangeException(nameof(curve), curve, "Unknown curve.");
        if (curve == ControlCurve.Exponential && (outMin <= 0 || outMax <= 0))
            throw new ArgumentOutOfRangeException(nameof(outMin), outMin, "An exponential map needs both output ends above zero.");

        return Param.FromControl(this, ControlMap.Create((float)inMin, (float)inMax, (float)outMin, (float)outMax, curve));
    }

    /// <summary>Uses the control as a parameter; the value is read live, once per block.</summary>
    public static implicit operator Param(Control control) =>
        Param.FromControl(control ?? throw new ArgumentNullException(nameof(control)), null);

    /// <summary>Uses the control as a sound, so it can be added to, scaled and fed to other sounds.</summary>
    public static implicit operator Sound(Control control) => Sounds.FromParam(control);

    // Scaling and offsetting fold into the control's own map, so "rpm / 30" is still one parameter that reads the control
    // directly: no extra operators, and it converts to a Param without being rendered as audio.
    private static Sound Affine(Control control, double gain, double bias) =>
        Sounds.FromParam(Param.FromControl(control, ControlMap.Identity.WithAffine((float)gain, (float)bias)));

    /// <summary>The control plus a constant, as a signal.</summary>
    public static Sound operator +(Control control, double offset) => Affine(control, 1, offset);

    /// <summary>The control minus a constant, as a signal.</summary>
    public static Sound operator -(Control control, double offset) => Affine(control, 1, -offset);

    /// <summary>The control times a constant, as a signal.</summary>
    public static Sound operator *(Control control, double gain) => Affine(control, gain, 0);

    /// <summary>The control times a constant, as a signal.</summary>
    public static Sound operator *(double gain, Control control) => control * gain;

    /// <summary>The control divided by a constant, as a signal.</summary>
    public static Sound operator /(Control control, double divisor)
    {
        if (divisor == 0 || !double.IsFinite(divisor))
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "The divisor must be a non-zero number.");
        return Affine(control, 1.0 / divisor, 0);
    }

    /// <summary>A short description of the control, such as its current value.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Control({Name ?? "unnamed"}={Value:0.###})");
}

/// <summary>
/// A remap of a control's value, with its constants worked out once. Linear is <c>Scale * v + Offset</c>. Exponential is
/// <c>Scale * exp((v - Origin) * Rate) + Offset</c>, so a glide costs one <c>exp</c> per frame and no division or <c>pow</c>.
/// </summary>
internal readonly record struct ControlMap(ControlCurve Curve, float Scale, float Offset, float Origin, float Rate)
{
    public static ControlMap Identity => new(ControlCurve.Linear, 1f, 0f, 0f, 0f);

    public static ControlMap Create(float inMin, float inMax, float outMin, float outMax, ControlCurve curve)
    {
        if (curve == ControlCurve.Exponential)
            return new ControlMap(curve, outMin, 0f, inMin, MathF.Log(outMax / outMin) / (inMax - inMin));

        float scale = (outMax - outMin) / (inMax - inMin);
        return new ControlMap(curve, scale, outMin - inMin * scale, 0f, 0f);
    }

    /// <summary>This map followed by <c>gain * x + bias</c>.</summary>
    public ControlMap WithAffine(float gain, float bias) => this with { Scale = Scale * gain, Offset = Offset * gain + bias };

    public float Apply(float value) => Curve == ControlCurve.Exponential
        ? Scale * MathF.Exp((value - Origin) * Rate) + Offset
        : Scale * value + Offset;
}

/// <summary>
/// Follows a <see cref="Control"/> with a one-pole glide, reading the target once per block so a setter on another thread
/// never tears or allocates. Smoothing runs in the control's own units, then the optional map is applied.
/// </summary>
internal sealed class ControlParamSource : ParamSource
{
    private readonly Control _control;
    private readonly ControlMap? _map;
    private readonly Operators.SmoothingCoefficient _coefficient;
    // Double precision: in single precision the update stalls about 1e-4 short of the target for slow glides, which would
    // leave a small permanent error and keep the settled fast path from ever being taken.
    private double _current;

    public ControlParamSource(Control control, ControlMap? map, RenderContext context) : base(context)
    {
        _control = control;
        _map = map;
        _coefficient = new Operators.SmoothingCoefficient(context.SampleRate);
        _current = control.Target;
    }

    // A reset voice starts at the value the control holds now, not where it last glided to.
    internal override void Reset() => _current = _control.Target;

    private protected override void Fill(Span<float> values)
    {
        float target = _control.Target;
        double retain = _coefficient.For(_control.SlewSeconds);

        // Settled: one mapped value for the whole block, which also skips the exponential per frame for mapped controls.
        if (_current == target || retain == 0.0 || Math.Abs(target - _current) <= 1e-7 * Math.Max(1.0, Math.Abs(target)))
        {
            _current = target;
            values.Fill(_map is { } m ? m.Apply(target) : target);
            return;
        }

        double current = _current;
        for (int i = 0; i < values.Length; i++)
        {
            current = target + (current - target) * retain;
            float value = (float)current;
            values[i] = _map is { } map ? map.Apply(value) : value;
        }
        _current = current;
    }
}
