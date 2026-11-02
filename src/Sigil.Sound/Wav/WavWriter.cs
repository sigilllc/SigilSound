using System.Buffers.Binary;

namespace Sigil.Sound.Wav;

/// <summary>Sample formats <see cref="WavWriter"/> can write.</summary>
public enum WavFormat
{
    /// <summary>16-bit signed integer PCM.</summary>
    Pcm16,

    /// <summary>24-bit signed integer PCM.</summary>
    Pcm24,

    /// <summary>32-bit IEEE float.</summary>
    Float32,
}

/// <summary>Writes stereo <see cref="AudioBuffer"/>s as RIFF/WAVE. Stream-based, so it works without a file system.</summary>
public static class WavWriter
{
    private const int HeaderSize = 44;
    private const short FormatPcm = 1;
    private const short FormatIeeeFloat = 3;

    public static void Write(Stream stream, AudioBuffer audio, WavFormat format = WavFormat.Pcm16)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(audio);

        const short channels = 2;
        int bytesPerSample = format switch
        {
            WavFormat.Pcm16 => 2,
            WavFormat.Pcm24 => 3,
            WavFormat.Float32 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
        short blockAlign = (short)(channels * bytesPerSample);
        long dataSize = (long)audio.FrameCount * blockAlign;
        if (dataSize > int.MaxValue - HeaderSize)
            throw new ArgumentException("Audio is too long for a WAV file.", nameof(audio));

        Span<byte> header = stackalloc byte[HeaderSize];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], (int)(HeaderSize - 8 + dataSize));
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], format == WavFormat.Float32 ? FormatIeeeFloat : FormatPcm);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], audio.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], audio.SampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], (short)(bytesPerSample * 8));
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], (int)dataSize);
        stream.Write(header);

        const int framesPerChunk = 4096;
        var chunk = new byte[framesPerChunk * blockAlign];
        var frames = audio.Frames;
        for (int start = 0; start < frames.Length; start += framesPerChunk)
        {
            int count = Math.Min(framesPerChunk, frames.Length - start);
            var bytes = chunk.AsSpan(0, count * blockAlign);
            for (int i = 0; i < count; i++)
            {
                var frame = frames[start + i];
                var slot = bytes[(i * blockAlign)..];
                WriteSample(slot, frame.Left, format);
                WriteSample(slot[bytesPerSample..], frame.Right, format);
            }
            stream.Write(bytes);
        }
    }

    private static void WriteSample(Span<byte> destination, float sample, WavFormat format)
    {
        float clamped = Math.Clamp(sample, -1f, 1f);
        switch (format)
        {
            case WavFormat.Pcm16:
                BinaryPrimitives.WriteInt16LittleEndian(destination, (short)MathF.Round(clamped * short.MaxValue));
                break;
            case WavFormat.Pcm24:
                int value = (int)MathF.Round(clamped * 8_388_607f);
                destination[0] = (byte)value;
                destination[1] = (byte)(value >> 8);
                destination[2] = (byte)(value >> 16);
                break;
            case WavFormat.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(destination, sample);
                break;
        }
    }
}
