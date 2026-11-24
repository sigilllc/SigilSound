namespace Sigil.Sound.Operators;

/// <summary>
/// The running form of a <see cref="Slot"/>. Swaps arrive through an atomic exchange and are taken at the start of a block.
/// During a fade the new operator fades in while the ones it replaced fade out; a swap that arrives during a fade starts a new
/// fade from wherever the old ones have got to, so no sound is cut off part of the way through.
/// </summary>
internal sealed class SlotOperator : IOperator, IResettable, IDisposable
{
    // Sounds fading out at once. Past SoftLimit the quietest is hurried out over QuickFadeSeconds, so a burst of swaps keeps the
    // cost bounded; only when every place is taken (eight swaps inside one quick fade) is the quietest dropped outright.
    private const int MaxFading = 8;
    private const int SoftLimit = 4;
    private const double QuickFadeSeconds = 0.002;

    private readonly StereoFrame[] _scratch;
    private readonly int _quickFadeFrames;
    private readonly Fading[] _fading = new Fading[MaxFading];
    private int _fadingCount;

    // The sound playing now, the handover that brought it (which carries it to its disposal later), and its fade-in.
    private Handover _current;
    private int _inPosition;
    private int _inLength;
    private FadeCurve _inCurve;

    // Written by the swapping thread, taken by the audio thread.
    private Handover? _pending;

    // Operators the audio thread no longer touches, waiting for the swapping thread to dispose them.
    // A lock-free stack threaded through the handover nodes, so retiring never allocates.
    private Handover? _retired;

    public SlotOperator(IOperator initial, long version, RenderContext context)
    {
        _current = new Handover(initial, 0, FadeCurve.EqualPower, version);
        Context = context;
        _scratch = new StereoFrame[context.BlockSize];
        _quickFadeFrames = Math.Max(1, (int)Math.Round(QuickFadeSeconds * context.SampleRate));
    }

    /// <summary>The context swapped-in sounds are compiled with: the render's settings, without any compile scope of its own.</summary>
    public RenderContext Context { get; }

    /// <summary>
    /// Queues <paramref name="next"/> for the next block. Called off the audio thread, possibly from two threads at once: the sound
    /// with the higher <paramref name="version"/> (the later swap) wins, whichever arrives first.
    /// </summary>
    public void Post(IOperator next, int fadeFrames, FadeCurve curve, long version)
    {
        var node = new Handover(next, fadeFrames, curve, version);
        while (true)
        {
            var waiting = Volatile.Read(ref _pending);
            if (waiting is not null && waiting.Version > version)
            {
                (next as IDisposable)?.Dispose();
                return;
            }

            if (Interlocked.CompareExchange(ref _pending, node, waiting) == waiting)
            {
                // The audio thread never saw a handover that was replaced before it was taken.
                (waiting?.Next as IDisposable)?.Dispose();
                return;
            }
        }
    }

    /// <summary>Disposes operators the audio thread has finished with. Called off the audio thread.</summary>
    public void DisposeRetired()
    {
        for (var node = Interlocked.Exchange(ref _retired, null); node is not null; node = node.NextRetired)
            (node.Next as IDisposable)?.Dispose();
    }

    /// <summary>Disposes everything this slot holds. Stop rendering first.</summary>
    public void Dispose()
    {
        DisposeRetired();
        (Interlocked.Exchange(ref _pending, null)?.Next as IDisposable)?.Dispose();
        for (int i = 0; i < _fadingCount; i++)
            (_fading[i].Node.Next as IDisposable)?.Dispose();
        Array.Clear(_fading);
        _fadingCount = 0;
        (_current.Next as IDisposable)?.Dispose();
    }

    public void Reset()
    {
        RetireAllFading();
        _inLength = 0;
        _current.Next.ResetState();
    }

    public void RenderBlock(Span<StereoFrame> block)
    {
        var handover = Interlocked.Exchange(ref _pending, null);
        if (handover is not null)
            TakeOver(handover);

        _current.Next.RenderBlock(block);

        if (_inLength > 0)
        {
            for (int i = 0; i < block.Length; i++)
                block[i] *= FadeIn(_inCurve, Progress(_inPosition + i, _inLength));
            _inPosition += block.Length;
            if (_inPosition >= _inLength)
                _inLength = 0;
        }

        if (_fadingCount == 0)
            return;

        var outgoing = _scratch.AsSpan(0, block.Length);
        for (int f = 0; f < _fadingCount; f++)
        {
            ref var fading = ref _fading[f];
            fading.Node.Next.RenderBlock(outgoing);
            for (int i = 0; i < block.Length; i++)
                block[i] += outgoing[i] * (fading.StartGain * FadeOut(fading.Curve, Progress(fading.Position + i, fading.Length)));
            fading.Position += block.Length;
        }

        // Retire the sounds whose fade is over, keeping the rest in order.
        int kept = 0;
        for (int f = 0; f < _fadingCount; f++)
        {
            if (_fading[f].Position >= _fading[f].Length)
                Retire(_fading[f].Node);
            else
                _fading[kept++] = _fading[f];
        }
        Array.Clear(_fading, kept, _fadingCount - kept);
        _fadingCount = kept;
    }

    private void TakeOver(Handover handover)
    {
        // A sound compiled for an older swap that arrived after a newer one was taken: it never plays.
        if (handover.Version <= _current.Version)
        {
            Retire(handover);
            return;
        }

        if (handover.FadeFrames <= 0)
        {
            // No fade asked for: everything that was playing stops at once.
            RetireAllFading();
            Retire(_current);
        }
        else
        {
            // The sound playing now fades out from the level it has reached, which is below one if it was still fading in.
            float level = _inLength > 0 ? FadeIn(_inCurve, Progress(_inPosition, _inLength)) : 1f;
            if (_fadingCount == MaxFading)
                DropQuietest();
            _fading[_fadingCount++] = new Fading(_current, level, handover.FadeFrames, handover.Curve);
            if (_fadingCount > SoftLimit)
                HurryQuietest();
        }

        _current = handover;
        _inPosition = 0;
        _inLength = Math.Max(0, handover.FadeFrames);
        _inCurve = handover.Curve;
    }

    private void RetireAllFading()
    {
        for (int i = 0; i < _fadingCount; i++)
            Retire(_fading[i].Node);
        Array.Clear(_fading, 0, _fadingCount);
        _fadingCount = 0;
    }

    private static float Level(in Fading fading) =>
        fading.StartGain * FadeOut(fading.Curve, Progress(fading.Position, fading.Length));

    // The quietest fading sound, at its present level, is the one whose early end is least audible.
    private int QuietestIndex(bool skipHurried)
    {
        int index = -1;
        float quietest = float.MaxValue;
        for (int f = 0; f < _fadingCount; f++)
        {
            if (skipHurried && _fading[f].Hurried)
                continue;
            float level = Level(_fading[f]);
            if (level < quietest)
            {
                quietest = level;
                index = f;
            }
        }
        return index;
    }

    // Restarts the quietest fade from its present level as a short linear one.
    private void HurryQuietest()
    {
        int index = QuietestIndex(skipHurried: true);
        if (index < 0)
            return;
        ref var fading = ref _fading[index];
        int remaining = Math.Max(1, fading.Length - fading.Position);
        fading = new Fading(fading.Node, Level(fading), Math.Min(_quickFadeFrames, remaining), FadeCurve.Linear) { Hurried = true };
    }

    private void DropQuietest()
    {
        int index = QuietestIndex(skipHurried: false);
        Retire(_fading[index].Node);
        _fading[index] = _fading[--_fadingCount];
        _fading[_fadingCount] = default;
    }

    private static double Progress(int position, int length) => Math.Min(1.0, (double)position / length);

    private static float FadeIn(FadeCurve curve, double t) =>
        curve == FadeCurve.Linear ? (float)t : (float)Math.Sin(t * Math.PI * 0.5);

    private static float FadeOut(FadeCurve curve, double t) =>
        curve == FadeCurve.Linear ? (float)(1.0 - t) : (float)Math.Cos(t * Math.PI * 0.5);

    private void Retire(Handover node)
    {
        Handover? head;
        do
        {
            head = Volatile.Read(ref _retired);
            node.NextRetired = head;
        } while (Interlocked.CompareExchange(ref _retired, node, head) != head);
    }

    private struct Fading(Handover node, float startGain, int length, FadeCurve curve)
    {
        public Handover Node = node;
        public float StartGain = startGain;
        public int Length = length;
        public FadeCurve Curve = curve;
        public int Position;
        public bool Hurried;
    }

    // One sound's way through the slot: posted by a swap, played, then retired through the same node, so retiring never allocates.
    private sealed class Handover(IOperator next, int fadeFrames, FadeCurve curve, long version)
    {
        public IOperator Next { get; } = next;
        public int FadeFrames { get; } = fadeFrames;
        public FadeCurve Curve { get; } = curve;
        public long Version { get; } = version;
        public Handover? NextRetired { get; set; }
    }
}
