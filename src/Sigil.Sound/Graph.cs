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

