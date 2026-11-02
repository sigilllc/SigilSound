namespace Sigil.Sound;

/// <summary>
/// The low-level contract for a running piece of DSP: a compiled, stateful instance
/// produced from a <see cref="Sound"/> for one <see cref="RenderContext"/>.
/// </summary>
/// <remarks>
/// <see cref="RenderBlock"/> runs on the audio thread and MUST be real-time safe:
/// <list type="bullet">
/// <item>no managed allocations (allocate scratch buffers in the constructor, sized to <see cref="RenderContext.BlockSize"/>),</item>
/// <item>no locks that could cause priority inversion,</item>
/// <item>no blocking I/O or anything else with unbounded latency.</item>
/// </list>
/// Each call continues where the previous one stopped; operators keep their own position.
/// </remarks>
public interface IOperator
{
    /// <summary>
    /// Renders the next <c>block.Length</c> frames, overwriting <paramref name="block"/>.
    /// <c>block.Length</c> is at most <see cref="RenderContext.BlockSize"/>.
    /// </summary>
    void RenderBlock(Span<StereoFrame> block);
}
