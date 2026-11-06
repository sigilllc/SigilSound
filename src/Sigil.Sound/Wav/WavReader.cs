using System.Buffers.Binary;

namespace Sigil.Sound.Wav;

/// <summary>
/// Decodes RIFF/WAVE data into an in-memory <see cref="AudioBuffer"/>. Reads 8, 16, 24 and 32-bit integer PCM and
/// 32-bit IEEE float, mono or stereo (mono is copied to both channels). The file's own sample rate is kept; playback
/// converts it to the render rate. Stream-based, so it works without a file system.
/// </summary>
public static class WavReader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;
    private const int RiffHeaderSize = 12;
    private const int ExtensibleFormatMinimumSize = 26;

    /// <summary>
    /// Decodes a WAV file from a stream. The stream is read to its end, so it does not need to be seekable.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the first byte of the WAV file.</param>
    /// <returns>The decoded audio at the file's own sample rate.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file.</exception>
    /// <example>
    /// <code>
    /// using Sigil.Sound;
    ///
    /// using var stream = File.OpenRead("pad.wav");
    /// var pad = AudioBuffer.FromWav(stream);
    /// </code>
    /// </example>
    public static AudioBuffer Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Read(copy.GetBuffer().AsSpan(0, (int)copy.Length));
    }

    /// <summary>
    /// Decodes a WAV file that is already in memory. Use it for embedded resources or bytes received over a network.
    /// </summary>
    /// <param name="bytes">The complete contents of a .wav file.</param>
    /// <returns>The decoded audio at the file's own sample rate. Samples that are not finite numbers become silence.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file: only mono or stereo, and 8, 16, 24 or 32-bit PCM or 32-bit float.</exception>
    /// <example>
    /// <code>
    /// using Sigil.Sound;
    ///
    /// var kick = AudioBuffer.FromWav(File.ReadAllBytes("kick.wav"));
    /// </code>
    /// </example>
    public static AudioBuffer Read(ReadOnlySpan<byte> bytes)
    {
        var (info, dataStart, dataLength) = ParseHeader(bytes);

        int bytesPerSample = info.BitsPerSample / 8;
        int frameBytes = info.Channels * bytesPerSample;
        var data = bytes.Slice(dataStart, dataLength);
        var frames = new StereoFrame[info.FrameCount];
        for (int i = 0; i < frames.Length; i++)
        {
            var frame = data.Slice(i * frameBytes);
            float left = ReadSample(frame, info.BitsPerSample, info.IsFloat);
            float right = info.Channels == 2 ? ReadSample(frame[bytesPerSample..], info.BitsPerSample, info.IsFloat) : left;
            frames[i] = new StereoFrame(left, right);
        }

        return new AudioBuffer(frames, info.SampleRate);
    }

    /// <summary>
    /// Reads only what a WAV file says about itself: channels, sample rate, bit depth and length. It checks that the file is one
    /// <see cref="Read(ReadOnlySpan{byte})"/> can decode but does not decode it, so it is quick even for a long recording. Use it to
    /// accept or refuse a file, or to show its length, before paying for the full decode.
    /// </summary>
    /// <param name="bytes">The complete contents of a .wav file.</param>
    /// <returns>The file's format and length.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported WAV file: only mono or stereo, and 8, 16, 24 or 32-bit PCM or 32-bit float.</exception>
    /// <example>
    /// <code>
    /// using Sigil.Sound;
    /// using Sigil.Sound.Wav;
    ///
    /// // Check the file before paying for the full decode.
    /// var info = WavReader.ReadInfo(File.ReadAllBytes("kick.wav"));
    /// Console.WriteLine(info.Duration);   // the length in seconds
    /// </code>
    /// </example>
    public static WavInfo ReadInfo(ReadOnlySpan<byte> bytes) => ParseHeader(bytes).Info;

    // Finds the fmt and data chunks and checks the format is one this reader decodes. Returns where the sample data lies in `bytes`.
    private static (WavInfo Info, int DataStart, int DataLength) ParseHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < RiffHeaderSize || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The data is not a RIFF/WAVE file.");

        ushort formatTag = 0, channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        bool haveFormat = false, haveData = false;
        int dataStart = 0, dataLength = 0;

        // Walk the chunks. Each chunk is a four-byte id, a little-endian size, and a body padded to an even length.
        long position = RiffHeaderSize;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.Slice((int)position, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[((int)position + 4)..]);
            long bodyStart = position + 8;
            // A data chunk that claims more bytes than the file holds is read up to the end of the file.
            int available = (int)Math.Min(size, bytes.Length - bodyStart);
            var body = bytes.Slice((int)bodyStart, available);

            if (id.SequenceEqual("fmt "u8))
            {
                if (body.Length < 16)
                    throw new InvalidDataException("The fmt chunk is truncated.");
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (formatTag == FormatExtensible)
                {
                    // Extensible files name the real format in a GUID; its first two bytes are the format tag.
                    if (body.Length < ExtensibleFormatMinimumSize)
                        throw new InvalidDataException("The extensible fmt chunk is truncated.");
                    formatTag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
                }
                haveFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                dataStart = (int)bodyStart;
                dataLength = available;
                haveData = true;
            }

            if (haveFormat && haveData)
                break;

            long next = bodyStart + size + (size & 1);
            if (next > bytes.Length)
                break;
            position = next;
        }

        if (!haveFormat)
            throw new InvalidDataException("The WAV file has no fmt chunk.");
        if (!haveData)
            throw new InvalidDataException("The WAV file has no data chunk.");
        if (channels is < 1 or > 2)
            throw new InvalidDataException($"Only mono and stereo WAV files are supported; this file has {channels} channels.");
        if (sampleRate <= 0)
            throw new InvalidDataException("The WAV file declares a sample rate of zero.");

        bool isFloat = formatTag == FormatIeeeFloat;
        bool supported = isFloat ? bitsPerSample == 32
            : formatTag == FormatPcm && bitsPerSample is 8 or 16 or 24 or 32;
        if (!supported)
            throw new InvalidDataException($"Unsupported WAV sample format: tag {formatTag} at {bitsPerSample} bits.");

        int frameCount = dataLength / (channels * (bitsPerSample / 8));   // a trailing partial frame is dropped
        return (new WavInfo(channels, sampleRate, bitsPerSample, isFloat, frameCount), dataStart, dataLength);
    }

    // Converts one sample to the -1..1 range. Integer formats are scaled by their full range, so 16-bit -32768 maps to -1.
    private static float ReadSample(ReadOnlySpan<byte> bytes, int bits, bool isFloat)
    {
        float value;
        if (isFloat)
        {
            value = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        }
        else
        {
            switch (bits)
            {
                case 8:
                    // 8-bit WAV is unsigned, with silence at 128.
                    value = (bytes[0] - 128) / 128f;
                    break;
                case 16:
                    value = BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f;
                    break;
                case 24:
                    // Assemble the three bytes, then shift left and back so the sign bit is carried through.
                    int raw = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
                    raw = (raw << 8) >> 8;
                    value = raw / 8388608f;
                    break;
                default:
                    value = BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f;
                    break;
            }
        }

        return float.IsFinite(value) ? value : 0f;
    }
}
