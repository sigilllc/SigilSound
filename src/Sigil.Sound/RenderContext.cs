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
    public const int DefaultSampleRate = 48_000;
    public const int DefaultBlockSize = 256;

    /// <summary>A 48 kHz context with the default block size.</summary>
    public static RenderContext Default => new(DefaultSampleRate);

    /// <summary>Seconds per frame.</summary>
    public double SamplePeriod => 1.0 / SampleRate;

    /// <summary>Half the sample rate.</summary>
    public double Nyquist => SampleRate * 0.5;

    /// <summary>Throws if the sample rate or block size is not positive; otherwise returns this context.</summary>
    public RenderContext Validate()
    {
        if (SampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(SampleRate), SampleRate, "Sample rate must be positive.");
        if (BlockSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BlockSize), BlockSize, "Block size must be positive.");
        return this;
    }
}
