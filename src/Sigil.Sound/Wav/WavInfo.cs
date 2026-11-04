namespace Sigil.Sound.Wav;

/// <summary>What a WAV file says about itself, read without decoding the audio. See <see cref="WavReader.ReadInfo"/>.</summary>
public readonly struct WavInfo
{
    /// <summary>Describes a WAV file's format and length.</summary>
    /// <param name="channels">1 for mono, 2 for stereo.</param>
    /// <param name="sampleRate">Frames per second.</param>
    /// <param name="bitsPerSample">8, 16, 24 or 32.</param>
    /// <param name="isFloat">True for 32-bit floating point samples, false for integer PCM.</param>
    /// <param name="frameCount">How many frames (one sample per channel) the file holds.</param>
    public WavInfo(int channels, int sampleRate, int bitsPerSample, bool isFloat, int frameCount)
    {
        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        IsFloat = isFloat;
        FrameCount = frameCount;
    }

    /// <summary>1 for mono, 2 for stereo.</summary>
    public int Channels { get; }

    /// <summary>Frames per second, as the file declares it.</summary>
    public int SampleRate { get; }

    /// <summary>The size of one sample: 8, 16, 24 or 32 bits.</summary>
    public int BitsPerSample { get; }

    /// <summary>True when samples are 32-bit floating point, false when they are integer PCM.</summary>
    public bool IsFloat { get; }

    /// <summary>How many frames the file holds; a frame has one sample per channel.</summary>
    public int FrameCount { get; }

    /// <summary>The length of the audio in seconds at the file's own sample rate.</summary>
    public double Duration => (double)FrameCount / SampleRate;
}
