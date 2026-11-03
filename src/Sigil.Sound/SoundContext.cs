namespace Sigil.Sound;

/// <summary>
/// The entry point for live playback: one running output that you <see cref="Play"/> sounds into
/// and an audio callback (Web Audio, a sound card, a test) pulls frames from with <see cref="Render"/>.
/// Every <see cref="Play"/> hot-swaps what is playing, crossfading from the previous sound.
/// </summary>
/// <remarks>
/// <see cref="Play"/> and <see cref="Stop"/> compile on the calling thread and may be called while
/// another thread renders. <see cref="Render"/> is real-time safe and must only be called from one thread.
/// </remarks>
/// <example>
/// <code>
/// var output = new SoundContext(sampleRate: 48_000);
/// output.Play(Saw(110).LowPass(Sine(0.25).Range(200, 2000)));
/// // In the audio callback:
/// output.Render(frames);
/// </code>
/// </example>
public sealed class SoundContext : IDisposable
{
    private readonly Slot _slot;
    private readonly IOperator _output;

    public SoundContext(int sampleRate = RenderContext.DefaultSampleRate, int blockSize = RenderContext.DefaultBlockSize,
        double fadeSeconds = Slot.DefaultFadeSeconds)
    {
        RenderContext = new RenderContext(sampleRate, blockSize).Validate();
        _slot = new Slot(Sounds.Silence, fadeSeconds);
        _output = _slot.Sound.Compile(RenderContext);
    }

    /// <summary>The sample rate and block size this output renders with.</summary>
    public RenderContext RenderContext { get; }

    /// <summary>What is playing now; <see cref="Sounds.Silence"/> until the first <see cref="Play"/>.</summary>
    public Sound Current => _slot.Current;

    /// <summary>Frames rendered since this output was created.</summary>
    public long FramesRendered { get; private set; }

    /// <summary>Seconds rendered since this output was created.</summary>
    public double Time => (double)FramesRendered / RenderContext.SampleRate;

    /// <summary>Crossfades from whatever is playing to <paramref name="sound"/>.</summary>
    public void Play(Sound sound, double? fadeSeconds = null) => _slot.Swap(sound, fadeSeconds);

    /// <summary>Fades to silence.</summary>
    public void Stop(double? fadeSeconds = null) => _slot.Swap(Sounds.Silence, fadeSeconds);

    /// <summary>Releases everything the running sounds hold. Stop rendering first.</summary>
    public void Dispose() => (_output as IDisposable)?.Dispose();

    /// <summary>Fills <paramref name="output"/> with the next frames, in blocks of at most <see cref="RenderContext.BlockSize"/>.</summary>
    public void Render(Span<StereoFrame> output)
    {
        int blockSize = RenderContext.BlockSize;
        for (int offset = 0; offset < output.Length; offset += blockSize)
            _output.RenderBlock(output.Slice(offset, Math.Min(blockSize, output.Length - offset)));
        FramesRendered += output.Length;
    }
}
