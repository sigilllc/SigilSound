using Sigil.Sound.Wav;

namespace Sigil.Sound;

/// <summary>
/// Stereo audio held in memory: a rendered sound, or a sample decoded from a WAV file for <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/>.
/// </summary>
public sealed class AudioBuffer
{
    private readonly StereoFrame[] _frames;

    /// <summary>Wraps frames that are already rendered or decoded.</summary>
    /// <param name="frames">The audio, left and right per frame. The buffer keeps this array, so do not change it afterwards.</param>
    /// <param name="sampleRate">Frames per second of <paramref name="frames"/>.</param>
    public AudioBuffer(StereoFrame[] frames, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _frames = frames;
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Decodes a WAV file from disk into memory, ready for <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/>. Reads 8, 16, 24 and 32-bit PCM and
    /// 32-bit float, mono or stereo. The whole file is read before this returns.
    /// </summary>
    /// <remarks>
    /// The browser REPL does not allow this call in snippets, because a snippet must not read files on the host. Hosts
    /// that load samples from disk can call it directly.
    /// </remarks>
    /// <param name="path">Path to a .wav file, absolute or relative to the current directory.</param>
    /// <returns>The decoded audio at the file's own sample rate.</returns>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a supported WAV file.</exception>
    /// <example><code>var kick = AudioBuffer.FromWav("samples/kick.wav");</code></example>
    public static AudioBuffer FromWav(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return WavReader.Read(stream);
    }

    /// <summary>
    /// Decodes a WAV file from a stream into memory, ready for <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/>. Reads 8, 16, 24 and 32-bit PCM and
    /// 32-bit float, mono or stereo.
    /// </summary>
    /// <param name="stream">A readable stream holding a complete WAV file. It is read to its end.</param>
    /// <returns>The decoded audio at the file's own sample rate.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file.</exception>
    /// <example><code>var pad = AudioBuffer.FromWav(stream);</code></example>
    public static AudioBuffer FromWav(Stream stream) => WavReader.Read(stream);

    /// <summary>
    /// Decodes a WAV file that is already in memory into a buffer for <see cref="Sounds.Sample(AudioBuffer, Param?, PlaybackMode, double, double?)"/>.
    /// </summary>
    /// <param name="bytes">The complete contents of a .wav file, such as an embedded resource.</param>
    /// <returns>The decoded audio at the file's own sample rate.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file.</exception>
    /// <example><code>var kick = AudioBuffer.FromWav(bytes);</code></example>
    public static AudioBuffer FromWav(ReadOnlySpan<byte> bytes) => WavReader.Read(bytes);

    /// <summary>The sample rate of the frames, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>The number of stereo frames.</summary>
    public int FrameCount => _frames.Length;

    /// <summary>The length in seconds.</summary>
    public double Duration => (double)_frames.Length / SampleRate;

    /// <summary>All frames, read-only.</summary>
    public ReadOnlySpan<StereoFrame> Frames => _frames;

    /// <summary>The frame at <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based frame position.</param>
    public StereoFrame this[int index] => _frames[index];

    // The playback engine reads the samples directly, without copying them for every voice that plays this buffer.
    internal StereoFrame[] Storage => _frames;

    /// <summary>The left channel as a new array.</summary>
    public float[] Left() => Select(static f => f.Left);

    /// <summary>The right channel as a new array.</summary>
    public float[] Right() => Select(static f => f.Right);

    /// <summary>The center-mixed signal as a new array.</summary>
    public float[] ToMono() => Select(static f => f.ToMono());

    /// <summary>Left/right interleaved samples as a new array, the layout most audio APIs expect.</summary>
    public float[] ToInterleaved()
    {
        var samples = new float[_frames.Length * 2];
        for (int i = 0; i < _frames.Length; i++)
        {
            samples[2 * i] = _frames[i].Left;
            samples[2 * i + 1] = _frames[i].Right;
        }
        return samples;
    }

    private float[] Select(Func<StereoFrame, float> channel)
    {
        var samples = new float[_frames.Length];
        for (int i = 0; i < _frames.Length; i++)
            samples[i] = channel(_frames[i]);
        return samples;
    }
}
