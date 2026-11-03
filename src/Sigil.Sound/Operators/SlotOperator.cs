namespace Sigil.Sound.Operators;

/// <summary>
/// The running form of a <see cref="Slot"/>. Swaps arrive through an atomic exchange and are
/// taken at the start of a block; during a fade both the old and the new operator render.
/// </summary>
internal sealed class SlotOperator : IOperator, IResettable, IDisposable
{
    private readonly StereoFrame[] _scratch;

    private IOperator _current;
    private IOperator? _fadingOut;
    private Handover? _fadeHandover;
    private int _fadePosition;
    private int _fadeLength;

    // Written by the swapping thread, taken by the audio thread.
    private Handover? _pending;

    // Operators the audio thread no longer touches, waiting for the swapping thread to dispose them.
    // A lock-free stack threaded through the handover nodes, so retiring never allocates.
    private Handover? _retired;

    public SlotOperator(IOperator initial, RenderContext context)
    {
        _current = initial;
        Context = context;
        _scratch = new StereoFrame[context.BlockSize];
    }

    /// <summary>The context swapped-in sounds are compiled with: the render's settings, without any compile scope of its own.</summary>
    public RenderContext Context { get; }

    /// <summary>Queues <paramref name="next"/> for the next block. Called off the audio thread.</summary>
    public void Post(IOperator next, int fadeFrames)
    {
        var replaced = Interlocked.Exchange(ref _pending, new Handover(next, fadeFrames));
        // The audio thread never saw a handover that was replaced before it was taken.
        (replaced?.Next as IDisposable)?.Dispose();
    }

    /// <summary>Disposes operators the audio thread has finished with. Called off the audio thread.</summary>
    public void DisposeRetired()
    {
        for (var node = Interlocked.Exchange(ref _retired, null); node is not null; node = node.NextRetired)
            (node.Old as IDisposable)?.Dispose();
    }

    /// <summary>Disposes everything this slot holds. Stop rendering first.</summary>
    public void Dispose()
    {
        DisposeRetired();
        (Interlocked.Exchange(ref _pending, null)?.Next as IDisposable)?.Dispose();
        (_fadingOut as IDisposable)?.Dispose();
        (_current as IDisposable)?.Dispose();
    }

    public void Reset() => _current.ResetState();

    public void RenderBlock(Span<StereoFrame> block)
    {
        var handover = Interlocked.Exchange(ref _pending, null);
        if (handover is not null)
            TakeOver(handover);

        _current.RenderBlock(block);
        if (_fadingOut is null)
            return;

        var outgoing = _scratch.AsSpan(0, block.Length);
        _fadingOut.RenderBlock(outgoing);
        for (int i = 0; i < block.Length; i++)
        {
            double t = Math.Min(1.0, (double)(_fadePosition + i) / _fadeLength);
            (double fadeIn, double fadeOut) = Math.SinCos(t * Math.PI * 0.5);
            block[i] = block[i] * (float)fadeIn + outgoing[i] * (float)fadeOut;
        }

        _fadePosition += block.Length;
        if (_fadePosition >= _fadeLength)
        {
            Retire(_fadeHandover!, _fadingOut);
            _fadingOut = null;
            _fadeHandover = null;
        }
    }

    private void TakeOver(Handover handover)
    {
        // A swap during a fade drops the sound that was already on its way out.
        if (_fadingOut is not null)
        {
            Retire(_fadeHandover!, _fadingOut);
            _fadingOut = null;
            _fadeHandover = null;
        }

        if (handover.FadeFrames <= 0)
        {
            Retire(handover, _current);
        }
        else
        {
            _fadingOut = _current;
            _fadeHandover = handover;
            _fadePosition = 0;
            _fadeLength = handover.FadeFrames;
        }

        _current = handover.Next;
    }

    private void Retire(Handover node, IOperator old)
    {
        node.Old = old;
        Handover? head;
        do
        {
            head = Volatile.Read(ref _retired);
            node.NextRetired = head;
        } while (Interlocked.CompareExchange(ref _retired, node, head) != head);
    }

    private sealed class Handover(IOperator next, int fadeFrames)
    {
        public IOperator Next { get; } = next;
        public int FadeFrames { get; } = fadeFrames;
        public IOperator? Old { get; set; }
        public Handover? NextRetired { get; set; }
    }
}
