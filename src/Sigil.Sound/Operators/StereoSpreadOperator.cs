namespace Sigil.Sound.Operators;

/// <summary>
/// Widens or narrows a stereo signal by scaling its side component. The mid (L + R) is left untouched, so the total level
/// stays put, and only the difference between the channels changes. Width 0 is mono, 1 is unchanged, above 1 is wider.
/// </summary>
internal sealed class StereoSpreadOperator : IOperator, IResettable
{
    // Above this the side signal is so large that the channels drift far apart and clip; the value is generous on purpose.
    private const float MaxWidth = 4f;

    private readonly IOperator _input;
    private readonly ParamSource _width;

    public StereoSpreadOperator(IOperator input, ParamSource width)
    {
        _input = input;
        _width = width;
    }

    public void Reset()
    {
        _input.ResetState();
        _width.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        var width = _width.Next(block.Length);
        for (int i = 0; i < block.Length; i++)
        {
            float w = Math.Clamp(Dsp.Finite(width[i], 1f), 0f, MaxWidth);
            var frame = block[i];
            float left = Dsp.Safe(frame.Left);
            float right = Dsp.Safe(frame.Right);

            // Mid and side are half the sum and half the difference, so mid + side rebuilds the left channel exactly.
            float mid = 0.5f * (left + right);
            float side = 0.5f * (left - right) * w;
            block[i] = new StereoFrame(mid + side, mid - side);
        }
    }
}
