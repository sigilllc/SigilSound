namespace Sigil.Sound.Operators;

/// <summary>Multiplies its input by a gain parameter.</summary>
public sealed class GainOperator : IOperator
{
    private readonly IOperator _input;
    private readonly ParamSource _gain;

    public GainOperator(IOperator input, ParamSource gain)
    {
        _input = input;
        _gain = gain;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var gain = _gain.Next(block.Length);
        for (int i = 0; i < block.Length; i++)
            block[i] *= gain[i];
    }
}

/// <summary>Sums two inputs.</summary>
public sealed class MixOperator : IOperator
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

    public void RenderBlock(Span<StereoFrame> block)
    {
        _a.RenderBlock(block);
        var other = _scratch.AsSpan(0, block.Length);
        _b.RenderBlock(other);
        for (int i = 0; i < block.Length; i++)
            block[i] += other[i];
    }
}

/// <summary>Maps a bipolar input in [-1, 1] linearly to [min, max] on each channel.</summary>
public sealed class RangeOperator(IOperator input, float min, float max) : IOperator
{
    public void RenderBlock(Span<StereoFrame> block)
    {
        input.RenderBlock(block);
        float half = 0.5f * (max - min);
        for (int i = 0; i < block.Length; i++)
        {
            var f = block[i];
            block[i] = new StereoFrame(min + (f.Left + 1.0f) * half, min + (f.Right + 1.0f) * half);
        }
    }
}
