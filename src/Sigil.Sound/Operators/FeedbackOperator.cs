namespace Sigil.Sound.Operators;

/// <summary>The loop's input: the most recent chunk of the feedback operator's own output.</summary>
internal sealed class FeedbackTap(int chunk) : IOperator, IResettable
{
    public StereoFrame[] Frames { get; } = new StereoFrame[chunk];

    public void RenderBlock(Span<StereoFrame> block) => Frames.AsSpan(0, block.Length).CopyTo(block);

    public void Reset() => Array.Clear(Frames);
}

/// <summary>
/// Output = input + loop(previous output). The loop sees the output one fixed chunk late, so the result is the same
/// whatever block size the host renders in: the operator always works in chunks of the same length and hands out
/// the frames a host asks for from the chunk it has just made.
/// </summary>
internal sealed class FeedbackOperator : IOperator, IResettable
{
    private readonly IOperator _input;
    private readonly IOperator _loop;
    private readonly FeedbackTap _tap;
    private readonly StereoFrame[] _output;
    private readonly StereoFrame[] _returned;
    private int _position;

    public FeedbackOperator(IOperator input, IOperator loop, FeedbackTap tap)
    {
        _input = input;
        _loop = loop;
        _tap = tap;
        _output = new StereoFrame[tap.Frames.Length];
        _returned = new StereoFrame[tap.Frames.Length];
        _position = _output.Length;
    }

    public void Reset()
    {
        _input.ResetState();
        _loop.ResetState();
        _tap.Reset();
        Array.Clear(_output);
        _position = _output.Length;
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        int done = 0;
        while (done < block.Length)
        {
            if (_position >= _output.Length)
                MakeChunk();

            int count = Math.Min(_output.Length - _position, block.Length - done);
            _output.AsSpan(_position, count).CopyTo(block.Slice(done, count));
            _position += count;
            done += count;
        }
    }

    private void MakeChunk()
    {
        _input.RenderBlock(_output);
        _loop.RenderBlock(_returned);
        for (int i = 0; i < _output.Length; i++)
            _output[i] += new StereoFrame(Guard(_returned[i].Left), Guard(_returned[i].Right));

        // The next chunk's loop will read this one.
        _output.CopyTo(_tap.Frames, 0);
        _position = 0;
    }

    // Whatever the user's loop does, it cannot blow the output up or poison it with NaN: anything not finite becomes
    // silence, and anything past full scale is eased toward a ceiling of 2 instead of growing without bound.
    private static float Guard(float value)
    {
        if (!float.IsFinite(value)) return 0f;
        float magnitude = MathF.Abs(value);
        if (magnitude <= 1f) return value;
        return MathF.CopySign(1f + MathF.Tanh(magnitude - 1f), value);
    }
}
