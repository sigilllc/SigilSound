using System.Runtime.InteropServices;
using NAudio.Wave;
using Sigil.Sound;
using Sigil.Sound.Repl;

namespace LiveScript;

/// <summary>
/// The audio callback: NAudio asks for bytes of 32-bit float stereo, and they are the frames of a <see cref="SoundContext"/>.
/// A <see cref="StereoFrame"/> is two floats, so the sound renders straight into NAudio's buffer.
/// </summary>
internal sealed class SoundContextWaveProvider(SoundContext output) : IWaveProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(output.RenderContext.SampleRate, 2);

    public int Read(byte[] buffer, int offset, int count)
    {
        var frames = MemoryMarshal.Cast<byte, StereoFrame>(buffer.AsSpan(offset, count));
        // A script's loops are counted per rendered frame. The count is per thread, so it is reset here, on the audio thread.
        ExecutionGuard.Reset(frames.Length);
        try
        {
            output.Render(frames);
        }
        catch (Exception e)
        {
            // A script that throws while it plays: go quiet, say why, and wait for the next save.
            frames.Clear();
            output.Play(Sounds.Silence, 0);
            Console.WriteLine($"stopped: {e.Message}");
        }
        return frames.Length * 8;
    }
}
