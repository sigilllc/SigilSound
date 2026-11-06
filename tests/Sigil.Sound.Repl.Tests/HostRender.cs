namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// Renders a snippet's sound the way the REPL host does: the loop step budget is reset before every block,
/// sized to the frames about to be rendered. <c>Sound.Render</c> alone never resets it, so a per-sample
/// loop (a 32-partial organ is about 33 steps a frame) would run out of the flat 200,000 steps after a fraction of a second,
/// which no host would ever see.
/// </summary>
internal static class HostRender
{
    public static AudioBuffer Render(Sound sound, double seconds, int sampleRate = RenderContext.DefaultSampleRate)
    {
        var context = new RenderContext(sampleRate);
        var op = sound.Compile(context);
        var frames = new StereoFrame[(int)Math.Round(seconds * sampleRate)];
        for (int offset = 0; offset < frames.Length; offset += context.BlockSize)
        {
            var block = frames.AsSpan(offset, Math.Min(context.BlockSize, frames.Length - offset));
            ExecutionGuard.Reset(block.Length);
            op.RenderBlock(block);
        }

        (op as IDisposable)?.Dispose();
        return new AudioBuffer(frames, sampleRate);
    }
}
