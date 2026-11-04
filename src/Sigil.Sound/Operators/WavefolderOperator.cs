namespace Sigil.Sound.Operators;

/// <summary>
/// A West Coast style wavefolder. The signal is driven into a triangle fold: anything beyond ±1 reflects back toward zero
/// rather than clipping, so a higher drive adds more folds and more harmonics. A symmetry offset before the fold breaks the
/// waveform's symmetry and brings in even harmonics. A DC blocker then removes the offset that asymmetry leaves behind.
/// </summary>
internal sealed class WavefolderOperator : IOperator, IResettable
{
    // The blocker's corner is about 10 Hz, low enough not to touch bass, high enough to clear a DC offset quickly.
    private const double DcCornerHz = 10.0;
    private const float DenormalOffset = 1e-25f;

    private readonly IOperator _input;
    private readonly ParamSource _amount;
    private readonly ParamSource _symmetry;
    private readonly ParamSource _mix;
    private readonly float _dcPole;

    private float _inLeft, _inRight, _outLeft, _outRight;

    public WavefolderOperator(IOperator input, ParamSource amount, ParamSource symmetry, ParamSource mix,
        RenderContext context)
    {
        _input = input;
        _amount = amount;
        _symmetry = symmetry;
        _mix = mix;
        _dcPole = (float)Math.Exp(-2.0 * Math.PI * DcCornerHz / context.SampleRate);
    }

    public void Reset()
    {
        _inLeft = _inRight = _outLeft = _outRight = 0;
        _input.ResetState();
        _amount.Reset();
        _symmetry.Reset();
        _mix.Reset();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        _input.RenderBlock(block);
        if (block.Length == 0)
            return;

        var amount = _amount.Next(block.Length);
        var symmetry = _symmetry.Next(block.Length);
        var mix = _mix.Next(block.Length);

        float inLeft = _inLeft, inRight = _inRight, outLeft = _outLeft, outRight = _outRight;
        for (int i = 0; i < block.Length; i++)
        {
            var dry = block[i];
            float drive = Math.Max(0f, Dsp.Finite(amount[i], 3f));
            float offset = Dsp.Finite(symmetry[i], 0f);

            float folded = Fold(Dsp.Safe(dry.Left) * drive + offset);
            float foldedRight = Fold(Dsp.Safe(dry.Right) * drive + offset);

            // Standard one-pole DC blocker: y[n] = x[n] - x[n-1] + pole * y[n-1].
            float left = folded - inLeft + _dcPole * outLeft + DenormalOffset;
            float right = foldedRight - inRight + _dcPole * outRight + DenormalOffset;
            inLeft = folded;
            inRight = foldedRight;
            outLeft = left;
            outRight = right;

            block[i] = Dsp.Blend(dry, new StereoFrame(left, right), mix[i]);
        }

        _inLeft = inLeft;
        _inRight = inRight;
        _outLeft = outLeft;
        _outRight = outRight;
    }

    // The triangle fold: a period-4 zigzag that equals x on [-1, 1] and reflects back from ±1 beyond it. It is the limit of
    // stacking infinitely many fold stages, so one expression covers any drive without a loop.
    private static float Fold(float x)
    {
        float u = x + 1f;
        u -= 4f * MathF.Floor(u * 0.25f);
        return 1f - MathF.Abs(u - 2f);
    }
}
