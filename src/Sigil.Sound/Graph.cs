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
/// with several, the source renders once and every reader is served from a short history of what it rendered.
/// </summary>
/// <remarks>
/// Readers are not in lockstep. A <c>Feedback</c> loop pulls its input in chunks of its own, and a custom operator may pull in any
/// size, so the readers of one sound can ask for different amounts at different moments. Each reader therefore counts the frames it
/// has taken and reads that stretch of the source's output; the source runs forward only when a reader needs frames it has not made yet.
/// The history is a few blocks long, which covers a reader that runs ahead of the others by a chunk or a block. A reader that falls
/// further behind than that gets silence for the frames that are gone, and nothing else is disturbed.
/// </remarks>
internal sealed class SharedSource(IOperator source, int blockSize)
{
    // Blocks of history. A reader that makes its input in a chunk is ahead of the others by at most that chunk (never more than a
    // block), and the slowest reader is at most a block behind the host: two blocks cover it, four leave room.
    private const int HistoryBlocks = 4;

    // Only a sound with several readers needs the history, so a sound used once costs nothing here.
    private StereoFrame[] _history = [];
    private long _produced;

    public IOperator Source { get; } = source;

    public int Readers { get; private set; }

    /// <summary>Changes whenever the source is reset, so a reader can tell that its position no longer means anything.</summary>
    public int Generation { get; private set; }

    public SharedReader NewReader()
    {
        Readers++;
        if (Readers == 2)
            _history = new StereoFrame[blockSize * HistoryBlocks];
        return new SharedReader(this);
    }

    /// <summary>Fills <paramref name="block"/> with the source's frames from <paramref name="position"/> on, rendering more of it if needed.</summary>
    public void Read(long position, Span<StereoFrame> block)
    {
        long end = position + block.Length;
        while (_produced < end)
        {
            int slot = (int)(_produced % _history.Length);
            // Never more than a block at once, and never across the end of the history, so the source sees ordinary block sizes.
            int count = (int)Math.Min(Math.Min(end - _produced, blockSize), _history.Length - slot);
            Source.RenderBlock(_history.AsSpan(slot, count));
            _produced += count;
        }

        // Frames older than the history have been written over.
        int gone = (int)Math.Clamp(_produced - _history.Length - position, 0, block.Length);
        block[..gone].Clear();

        int done = gone;
        while (done < block.Length)
        {
            int slot = (int)((position + done) % _history.Length);
            int count = Math.Min(block.Length - done, _history.Length - slot);
            _history.AsSpan(slot, count).CopyTo(block.Slice(done, count));
            done += count;
        }
    }

    public void Reset()
    {
        Source.ResetState();
        _produced = 0;
        Generation++;
    }
}

/// <summary>One use of a shared sound. It reads the source's frames in order, from the first, at its own pace.</summary>
internal sealed class SharedReader(SharedSource core) : IOperator, IResettable
{
    private long _position;
    private int _generation;

    public void RenderBlock(Span<StereoFrame> block)
    {
        if (core.Readers == 1)
        {
            core.Source.RenderBlock(block);
            return;
        }

        // The source was reset, perhaps by another reader: start again from its first frame.
        if (_generation != core.Generation)
        {
            _generation = core.Generation;
            _position = 0;
        }

        core.Read(_position, block);
        _position += block.Length;
    }

    public void Reset()
    {
        core.Reset();
        _generation = core.Generation;
        _position = 0;
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

    // What the player is doing to the note while it sounds. Written by the voice pool on the audio thread, once per block,
    // from the note's own state; a note started without a handle leaves them at zero.
    public float Bend;      // semitones
    public float Pressure;  // 0 to 1
    public float Slide;     // 0 to 1

    public void Set(double frequency, int pitch, float velocity, double holdSeconds)
    {
        Frequency = (float)frequency;
        Pitch = pitch;
        Velocity = velocity;
        HoldSeconds = holdSeconds;
        Bend = 0;
        Pressure = 0;
        Slide = 0;
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
    // Frequency and Pitch follow the note's bend, so an instrument written with v.Frequency answers to a bending player
    // without being changed. With no bend they are the note's own value, exactly as before.
    public static Sound Frequency { get; } = Sound.FromOperator(ctx => new VoiceExpressionOperator(ctx.Voice, VoiceExpression.Frequency, ctx), "voice.Frequency");

    public static Sound Pitch { get; } = Sound.FromOperator(ctx => new VoiceExpressionOperator(ctx.Voice, VoiceExpression.Pitch, ctx), "voice.Pitch");

    public static Sound Bend { get; } = Sound.FromOperator(ctx => new VoiceExpressionOperator(ctx.Voice, VoiceExpression.Bend, ctx), "voice.Bend");

    public static Sound Pressure { get; } = Sound.FromOperator(ctx => new VoiceExpressionOperator(ctx.Voice, VoiceExpression.Pressure, ctx), "voice.Pressure");

    public static Sound Slide { get; } = Sound.FromOperator(ctx => new VoiceExpressionOperator(ctx.Voice, VoiceExpression.Slide, ctx), "voice.Slide");

    public static Sound Velocity { get; } = Sound.FromOperator(ctx => new VoiceValueOperator(ctx.Voice, static v => v.Velocity), "voice.Velocity");

    public static Sound Gate { get; } = Sound.FromOperator(ctx => new VoiceGateOperator(ctx.Voice, ctx), "voice.Gate");
}

internal enum VoiceExpression
{
    Frequency,
    Pitch,
    Bend,
    Pressure,
    Slide,
}

/// <summary>
/// A note's pitch or expression as a signal. Bend, pressure and slide change while the note sounds, and the pool hands over a
/// new value once per block, so the signal glides to it (a one-pole smoother, like <see cref="Control"/>) instead of stepping at
/// block boundaries, which would be heard as a buzz on a bending note.
/// </summary>
/// <remarks>
/// Settled values take a constant fill, and a note that was never bent takes the same path with the note's own frequency, so
/// plain notes render exactly as they did before expression existed. A voice that is retriggered starts at the new note's
/// values, with no glide from the note it replaced.
/// </remarks>
internal sealed class VoiceExpressionOperator : IOperator, IResettable
{
    /// <summary>The glide time. A controller sends updates a few hundred times a second; this hides the steps without lagging the hand.</summary>
    internal const float GlideSeconds = 0.01f;

    private readonly VoiceControl? _voice;
    private readonly VoiceExpression _kind;
    private readonly float _retain;
    private float _current;

    public VoiceExpressionOperator(VoiceControl? voice, VoiceExpression kind, RenderContext context)
    {
        _voice = voice;
        _kind = kind;
        _retain = new Operators.SmoothingCoefficient(context.SampleRate).For(GlideSeconds);
        Reset();
    }

    // Frequency and Pitch glide the bend and derive from it, so a bend moves the pitch by whole semitones smoothly.
    private float Target => _voice is null ? 0f : _kind switch
    {
        VoiceExpression.Pressure => _voice.Pressure,
        VoiceExpression.Slide => _voice.Slide,
        _ => _voice.Bend,
    };

    public void Reset() => _current = Target;

    public void RenderBlock(Span<StereoFrame> block)
    {
        float target = Target;
        if (_current == target || Math.Abs(target - _current) <= 1e-6f)
        {
            _current = target;
            block.Fill(StereoFrame.Mono(Read(_current)));
            return;
        }

        for (int i = 0; i < block.Length; i++)
        {
            _current = target + (_current - target) * _retain;
            block[i] = StereoFrame.Mono(Read(_current));
        }
    }

    private float Read(float smoothed) => _kind switch
    {
        VoiceExpression.Frequency => _voice is null ? 0f : smoothed == 0f ? _voice.Frequency : _voice.Frequency * MathF.Pow(2f, smoothed / 12f),
        VoiceExpression.Pitch => _voice is null ? 0f : _voice.Pitch + smoothed,
        _ => smoothed,
    };
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

