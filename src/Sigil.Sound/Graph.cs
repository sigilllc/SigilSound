using Sigil.Sound.Operators;

namespace Sigil.Sound;

/// <summary>
/// Lives for the length of one top-level <see cref="Sound.Compile"/>. It makes a <see cref="Sound"/> that appears
/// several times in a graph compile once and fan out, and it remembers which operators need disposing.
/// </summary>
internal sealed class CompileScope
{
    private readonly Dictionary<Sound, SharedSource> _shared = [];
    private readonly List<IDisposable> _disposables = [];

    public IOperator Share(Sound sound, Func<IOperator> build, RenderContext context)
    {
        if (_shared.TryGetValue(sound, out var existing))
            return existing.NewReader();

        var source = build();
        Track(source);
        var shared = new SharedSource(source, context.BlockSize);
        _shared.Add(sound, shared);
        return shared.NewReader();
    }

    public void Track(IOperator op)
    {
        if (op is IDisposable disposable && !_disposables.Contains(disposable))
            _disposables.Add(disposable);
    }

    public IReadOnlyList<IDisposable> Disposables => _disposables;
}

/// <summary>
/// The operator one <see cref="Sound"/> compiled to. With a single reader (the usual case) it is a pass-through;
/// with several, the source renders once per block and every reader gets a copy.
/// </summary>
internal sealed class SharedSource(IOperator source, int blockSize)
{
    private readonly StereoFrame[] _cache = new StereoFrame[blockSize];
    private int _cached;

    public IOperator Source { get; } = source;

    public int Readers { get; private set; }

    public int Epoch { get; private set; }

    public SharedReader NewReader()
    {
        Readers++;
        return new SharedReader(this);
    }

    public void Advance(int length)
    {
        Source.RenderBlock(_cache.AsSpan(0, length));
        _cached = length;
        Epoch++;
    }

    public void CopyTo(Span<StereoFrame> block)
    {
        int count = Math.Min(block.Length, _cached);
        _cache.AsSpan(0, count).CopyTo(block);
        block[count..].Clear();
    }

    public void Reset()
    {
        Source.ResetState();
        Epoch = 0;
        _cached = 0;
    }
}

/// <summary>One use of a shared sound. Readers rendered in lockstep each see the same block.</summary>
internal sealed class SharedReader(SharedSource core) : IOperator, IResettable
{
    private int _seen;

    public void RenderBlock(Span<StereoFrame> block)
    {
        if (core.Readers == 1)
        {
            core.Source.RenderBlock(block);
            return;
        }

        // A reader that has already taken the current block is asking for the next one.
        if (_seen == core.Epoch)
            core.Advance(block.Length);
        _seen = core.Epoch;
        core.CopyTo(block);
    }

    public void Reset()
    {
        core.Reset();
        _seen = 0;
    }
}

/// <summary>
/// The root of a compiled graph: what <see cref="Sound.Compile"/> returns. Disposing it disposes every
/// disposable operator in the graph, so callers only ever deal with one object.
/// </summary>
internal sealed class CompiledGraph(IOperator root, IReadOnlyList<IDisposable> disposables) : IOperator, IResettable, IDisposable
{
    public void RenderBlock(Span<StereoFrame> block) => root.RenderBlock(block);

    public void Reset() => root.ResetState();

    public void Dispose()
    {
        foreach (var disposable in disposables)
            disposable.Dispose();
    }
}

/// <summary>
/// Pitch, loudness and length of the note a voice is playing. A voice is compiled once and then retriggered,
/// so these are read by the operators while they render rather than baked in when the sound is built.
/// </summary>
internal sealed class VoiceControl
{
    public float Frequency;
    public float Pitch;
    public float Velocity;
    public double HoldSeconds = double.PositiveInfinity;

    public void Set(double frequency, int pitch, float velocity, double holdSeconds)
    {
        Frequency = (float)frequency;
        Pitch = pitch;
        Velocity = velocity;
        HoldSeconds = holdSeconds;
    }
}

internal static class OperatorExtensions
{
    public static void ResetState(this IOperator op)
    {
        if (op is IResettable resettable)
            resettable.Reset();
    }
}

/// <summary>The signals a note voice exposes; see the <c>Voice</c> type in Sigil.Sound.Notes.</summary>
internal static class VoiceSignals
{
    public static Sound Frequency { get; } = Sound.FromOperator(ctx => new VoiceValueOperator(ctx.Voice, static v => v.Frequency), "voice.Frequency");

    public static Sound Pitch { get; } = Sound.FromOperator(ctx => new VoiceValueOperator(ctx.Voice, static v => v.Pitch), "voice.Pitch");

    public static Sound Velocity { get; } = Sound.FromOperator(ctx => new VoiceValueOperator(ctx.Voice, static v => v.Velocity), "voice.Velocity");

    public static Sound Gate { get; } = Sound.FromOperator(ctx => new VoiceGateOperator(ctx.Voice, ctx), "voice.Gate");
}

internal sealed class VoiceValueOperator(VoiceControl? voice, Func<VoiceControl, float> read) : IOperator
{
    public void RenderBlock(Span<StereoFrame> block) =>
        block.Fill(StereoFrame.Mono(voice is null ? 0f : read(voice)));
}

/// <summary>1 while the note is held, 0 after it ends.</summary>
internal sealed class VoiceGateOperator(VoiceControl? voice, RenderContext context) : IOperator, IResettable
{
    private readonly double _period = context.SamplePeriod;
    private double _time;

    public void RenderBlock(Span<StereoFrame> block)
    {
        double hold = voice?.HoldSeconds ?? double.PositiveInfinity;
        for (int i = 0; i < block.Length; i++)
        {
            block[i] = StereoFrame.Mono(_time < hold ? 1f : 0f);
            _time += _period;
        }
    }

    public void Reset() => _time = 0;
}

