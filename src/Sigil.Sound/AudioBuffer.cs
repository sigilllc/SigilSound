namespace Sigil.Sound;

/// <summary>Rendered stereo audio held in memory.</summary>
public sealed class AudioBuffer
{
    private readonly StereoFrame[] _frames;

    public AudioBuffer(StereoFrame[] frames, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        _frames = frames;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public int FrameCount => _frames.Length;

    public double Duration => (double)_frames.Length / SampleRate;

    public ReadOnlySpan<StereoFrame> Frames => _frames;

    public StereoFrame this[int index] => _frames[index];

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
