namespace Sigil.Sound;

/// <summary>
/// Everything an operator needs to know about the render it belongs to.
/// Passed explicitly when a <see cref="Sound"/> is compiled; there is no global audio context.
/// </summary>
/// <param name="SampleRate">Frames per second.</param>
/// <param name="BlockSize">
/// The largest block the renderer will ask for. Operators size scratch buffers to this
/// at compile time; <see cref="IOperator.RenderBlock"/> is never called with a longer span.
/// </param>
public readonly record struct RenderContext(int SampleRate, int BlockSize = RenderContext.DefaultBlockSize)
{
    /// <summary>The sample rate of <see cref="Default"/>: 48 kHz.</summary>
    public const int DefaultSampleRate = 48_000;

    /// <summary>The default largest block size: 256 frames.</summary>
    public const int DefaultBlockSize = 256;

    /// <summary>A 48 kHz context with the default block size.</summary>
    public static RenderContext Default => new(DefaultSampleRate);

    /// <summary>Seconds per frame.</summary>
    public double SamplePeriod => 1.0 / SampleRate;

    /// <summary>Half the sample rate.</summary>
    public double Nyquist => SampleRate * 0.5;

    /// <summary>
    /// The host's clock, or null for sounds that count time from their own first frame (the default).
    /// See <see cref="HostClock"/> for what following a clock changes.
    /// </summary>
    public HostClock? Clock { get; init; }

    // The two members below are engine plumbing and deliberately not public: they let the fluent layer
    // share one compiled instance between several uses of the same Sound and give note voices their pitch
    // and length, without any of it showing up in user code.
    internal CompileScope? Scope { get; init; }

    internal VoiceControl? Voice { get; init; }

    // The clock that time-based sources follow. A note voice keeps its own time (zero at each note), so it follows none.
    internal HostClock? TimeClock => Voice is null ? Clock : null;

    /// <summary>Throws if the sample rate or block size is not positive; otherwise returns this context.</summary>
    public RenderContext Validate()
    {
        if (SampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(SampleRate), SampleRate, "Sample rate must be positive.");
        if (BlockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BlockSize), BlockSize, "Block size must be positive.");
        if (Clock is { } clock && clock.SampleRate != SampleRate)
            throw new ArgumentException($"The clock counts at {clock.SampleRate} Hz but the render context is {SampleRate} Hz.", nameof(Clock));
        return this;
    }
}
