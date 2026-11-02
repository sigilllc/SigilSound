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

    private Param(float constant, Sound? sound, Func<double, float>? function)
    {
        _constant = constant;
        _sound = sound;
        _function = function;
    }

    public static implicit operator Param(float value) => new(value, null, null);

    public static implicit operator Param(double value) => new((float)value, null, null);

    public static implicit operator Param(Sound sound) =>
        new(0, sound ?? throw new ArgumentNullException(nameof(sound)), null);

    public static implicit operator Param(Func<double, float> function) =>
        new(0, null, function ?? throw new ArgumentNullException(nameof(function)));

    /// <summary>True when this parameter never changes.</summary>
    public bool IsConstant => _sound is null && _function is null;

    /// <summary>The constant value; only meaningful when <see cref="IsConstant"/> is true.</summary>
    public float ConstantValue => _constant;

    /// <summary>Creates the per-render source that produces this parameter's values block by block.</summary>
    public ParamSource Compile(RenderContext context)
    {
        if (_sound is not null)
            return new SoundParamSource(_sound.Compile(context), context);
        if (_function is not null)
            return new FunctionParamSource(_function, context);
        return new ConstantParamSource(_constant, context);
    }

    public override string ToString() =>
        _sound is not null ? _sound.ToString()
        : _function is not null ? "f(t)"
        : _constant.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The compiled, stateful form of a <see cref="Param"/>. Produces one value per frame.
/// </summary>
public abstract class ParamSource
{
    private readonly float[] _buffer;

    protected ParamSource(RenderContext context) => _buffer = new float[context.BlockSize];

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

    /// <summary>Writes the next <c>values.Length</c> values.</summary>
    protected abstract void Fill(Span<float> values);
}

internal sealed class ConstantParamSource : ParamSource
{
    private readonly float _value;

    public ConstantParamSource(float value, RenderContext context) : base(context) => _value = value;

    public override bool IsConstant => true;

    protected override void Fill(Span<float> values) => values.Fill(_value);
}

internal sealed class FunctionParamSource : ParamSource
{
    private readonly Func<double, float> _function;
    private readonly double _samplePeriod;
    private long _position;

    public FunctionParamSource(Func<double, float> function, RenderContext context) : base(context)
    {
        _function = function;
        _samplePeriod = context.SamplePeriod;
    }

    protected override void Fill(Span<float> values)
    {
        for (int i = 0; i < values.Length; i++)
            values[i] = _function((_position + i) * _samplePeriod);
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

    protected override void Fill(Span<float> values)
    {
        var frames = _scratch.AsSpan(0, values.Length);
        _operator.RenderBlock(frames);
        for (int i = 0; i < values.Length; i++)
            values[i] = frames[i].ToMono();
    }
}
