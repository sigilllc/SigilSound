using System.Globalization;

namespace Sigil.Sound;

/// <summary>
/// A value that drives an operator: a constant, another <see cref="Sound"/> (audio-rate modulation),
/// or a function of time in seconds. Converts implicitly from all three, so one overload covers them.
/// </summary>
public readonly struct Param
{
    private readonly float _constant;
    private readonly Sound? _sound;
    private readonly Func<double, float>? _function;

    private readonly Control? _control;
    private readonly ControlMap? _controlMap;

    private Param(float constant, Sound? sound, Func<double, float>? function, Control? control = null, ControlMap? controlMap = null)
    {
        _constant = constant;
        _sound = sound;
        _function = function;
        _control = control;
        _controlMap = controlMap;
    }

    internal static Param FromControl(Control control, ControlMap? map) => new(0, null, null, control, map);

    /// <summary>A constant parameter.</summary>
    public static implicit operator Param(float value) => new(value, null, null);

    /// <summary>A constant parameter.</summary>
    public static implicit operator Param(double value) => new((float)value, null, null);

    /// <summary>A parameter whose value is a sound, rendered at the audio rate and averaged to mono: audio-rate modulation.</summary>
    public static implicit operator Param(Sound sound) =>
        (sound ?? throw new ArgumentNullException(nameof(sound))).BackingParam ?? new(0, sound, null);

    /// <summary>A parameter that is a function of time in seconds, such as <c>t =&gt; (float)(440 + 20 * Math.Sin(t))</c>.</summary>
    public static implicit operator Param(Func<double, float> function) =>
        new(0, null, function ?? throw new ArgumentNullException(nameof(function)));

    /// <summary>True when this parameter never changes.</summary>
    public bool IsConstant => _sound is null && _function is null && _control is null;

    /// <summary>The constant value; only meaningful when <see cref="IsConstant"/> is true.</summary>
    public float ConstantValue => _constant;

    /// <summary>Creates the per-render source that produces this parameter's values block by block.</summary>
    public ParamSource Compile(RenderContext context)
    {
        if (_sound is not null)
            return new SoundParamSource(_sound.Compile(context), context);
        if (_function is not null)
            return new FunctionParamSource(_function, context);
        if (_control is not null)
            return new ControlParamSource(_control, _controlMap, context);
        return new ConstantParamSource(_constant, context);
    }

    /// <summary>A short description of the parameter, used in the description of the sound it is part of.</summary>
    public override string ToString() =>
        _sound is not null ? _sound.ToString()
        : _function is not null ? "f(t)"
        : _control is not null ? _control.ToString()
        : _constant.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The compiled, stateful form of a <see cref="Param"/>. Produces one value per frame.
/// </summary>
public abstract class ParamSource
{
    private readonly float[] _buffer;

    internal ParamSource(RenderContext context) => _buffer = new float[context.BlockSize];

    /// <summary>True when every value this source produces is the same.</summary>
    public virtual bool IsConstant => false;

    /// <summary>Advances by <paramref name="count"/> frames and returns their values. Real-time safe.</summary>
    public ReadOnlySpan<float> Next(int count)
    {
        if ((uint)count > (uint)_buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count exceeds the render context block size.");
        var span = _buffer.AsSpan(0, count);
        Fill(span);
        return span;
    }

    /// <summary>Returns to the state just after compiling, so a time function starts again at zero.</summary>
    internal virtual void Reset()
    {
    }

    /// <summary>Writes the next <c>values.Length</c> values.</summary>
    private protected abstract void Fill(Span<float> values);

    // Lets a source that never changes fill its buffer once, up front.
    private protected void Prefill(float value) => _buffer.AsSpan().Fill(value);
}

internal sealed class ConstantParamSource : ParamSource
{
    public ConstantParamSource(float value, RenderContext context) : base(context)
    {
        Value = value;
        Prefill(value);
    }

    public float Value { get; }

    public override bool IsConstant => true;

    // The buffer already holds the value.
    private protected override void Fill(Span<float> values)
    {
    }
}

internal sealed class FunctionParamSource : ParamSource
{
    private readonly Func<double, float> _function;
    private readonly double _samplePeriod;
    private readonly HostClock? _clock;
    private long _position;

    public FunctionParamSource(Func<double, float> function, RenderContext context) : base(context)
    {
        _function = function;
        _samplePeriod = context.SamplePeriod;
        _clock = context.TimeClock;
    }

    internal override void Reset() => _position = 0;

    private protected override void Fill(Span<float> values)
    {
        long start = _clock?.SamplePosition ?? _position;
        for (int i = 0; i < values.Length; i++)
            values[i] = _function((start + i) * _samplePeriod);
        _position += values.Length;
    }
}

internal sealed class SoundParamSource : ParamSource
{
    private readonly IOperator _operator;
    private readonly StereoFrame[] _scratch;

    public SoundParamSource(IOperator op, RenderContext context) : base(context)
    {
        _operator = op;
        _scratch = new StereoFrame[context.BlockSize];
    }

    internal override void Reset() => _operator.ResetState();

    private protected override void Fill(Span<float> values)
    {
        var frames = _scratch.AsSpan(0, values.Length);
        _operator.RenderBlock(frames);
        // ToMono is written out: this copy runs per frame for every modulated parameter, including each voice's pitch.
        for (int i = 0; i < values.Length; i++)
        {
            var frame = frames[i];
            values[i] = 0.5f * (frame.Left + frame.Right);
        }
    }
}