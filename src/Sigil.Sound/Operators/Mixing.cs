namespace Sigil.Sound.Operators;

/// <summary>Multiplies its input by a gain parameter.</summary>
internal sealed class GainOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly ParamSource _gain;

    public GainOperator(IOperator input, ParamSource gain)
    {
        _input = input;
        _gain = gain;
    }

    public void Reset()
    {
        _input.ResetState();
        _gain.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var gain = _gain.Next(block.Length);
        if (_gain.IsFlat && block.Length > 0)
        {
            Vectors.Scale(block, gain[0]);
            return;
        }

        for (int i = 0; i < block.Length; i++)
            block[i] *= gain[i];
    }
}

/// <summary>Sums two inputs.</summary>
internal sealed class MixOperator : IOperator, IResettable
{
    private readonly IOperator _a;
    private readonly IOperator _b;
    private readonly StereoFrame[] _scratch;

    public MixOperator(IOperator a, IOperator b, RenderContext context)
    {
        _a = a;
        _b = b;
        _scratch = new StereoFrame[context.BlockSize];
    }

    public void Reset()
    {
        _a.ResetState();
        _b.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _a.RenderBlock(block);
        var other = _scratch.AsSpan(0, block.Length);
        _b.RenderBlock(other);
        Vectors.Add(block, other);
    }
}

/// <summary>Multiplies two inputs channel by channel: left by left, right by right.</summary>
internal sealed class MultiplyOperator : IOperator, IResettable
{
    private readonly IOperator _a;
    private readonly IOperator _b;
    private readonly StereoFrame[] _scratch;

    public MultiplyOperator(IOperator a, IOperator b, RenderContext context)
    {
        _a = a;
        _b = b;
        _scratch = new StereoFrame[context.BlockSize];
    }

    public void Reset()
    {
        _a.ResetState();
        _b.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _a.RenderBlock(block);
        var other = _scratch.AsSpan(0, block.Length);
        _b.RenderBlock(other);
        Vectors.Multiply(block, other);
    }
}

/// <summary>Maps a bipolar input in [-1, 1] linearly to [min, max] on each channel.</summary>
internal sealed class RangeOperator(IOperator input, float min, float max) : IOperator, IResettable
{
    public void Reset() => input.ResetState();

    public void RenderBlock(Span<StereoFrame> block)
    {
        input.RenderBlock(block);
        Vectors.Range(block, min, 0.5f * (max - min));
    }
}

/// <summary>A parameter's values as a mono sound.</summary>
internal sealed class ParamOperator(ParamSource source) : IOperator, IResettable
{
    public void Reset() => source.Reset();

    public void RenderBlock(Span<StereoFrame> block)
    {
        var values = source.Next(block.Length);
        for (int i = 0; i < block.Length; i++)
            block[i] = StereoFrame.Mono(values[i]);
    }
}

/// <summary>
/// What <see cref="Sound.Through"/> returns: the user's operator, with reset passed on to the input
/// it wraps so the user's operator does not have to do it.
/// </summary>
internal sealed class ThroughOperator(IOperator input, IOperator output) : IOperator, IResettable
{
    public void RenderBlock(Span<StereoFrame> block) => output.RenderBlock(block);

    public void Reset()
    {
        input.ResetState();
        output.ResetState();
    }
}
