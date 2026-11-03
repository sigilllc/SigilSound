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

/// <summary>
/// Optional: an operator that can return to its just-compiled state without being rebuilt.
/// The engine uses it to reuse note voices without allocating. Built-in operators all implement it,
/// and <see cref="Sound.Through"/> and <see cref="Operator.From(BlockRenderer, Action)"/> forward it for you,
/// so most custom operators never need to.
/// </summary>
public interface IResettable
{
    /// <summary>Clears filters, delay lines, envelopes and positions, as if the operator had just been created.</summary>
    void Reset();
}

/// <summary>
/// The shape of a block renderer written as a lambda; see <see cref="Operator.From(BlockRenderer)"/>.
/// A custom delegate, because <see cref="Span{T}"/> cannot be a type argument of <see cref="Action{T}"/>.
/// </summary>
/// <param name="block">The frames to fill, or to rework when the operator has an input.</param>
public delegate void BlockRenderer(Span<StereoFrame> block);

/// <summary>Creating <see cref="IOperator"/> instances without declaring a class.</summary>
public static class Operator
{
    /// <summary>
    /// Wraps a lambda as an operator, for use with <see cref="Sound.Through"/> and <see cref="Sound.FromOperator"/>.
    /// State lives in variables the lambda captures; declare them where the operator is created, so every
    /// compile of the sound starts fresh. The same real-time rules as <see cref="IOperator.RenderBlock"/> apply.
    /// </summary>
    /// <param name="render">Renders the next block. Must not allocate or block.</param>
    /// <example>
    /// <code>
    /// Saw(110).Through(input => Operator.From(block =>
    /// {
    ///     input.RenderBlock(block);              // pull the input, then rework it in place
    ///     for (int i = 0; i != block.Length; i++)
    ///         block[i] = block[i] * 2f;
    /// }))
    /// </code>
    /// </example>
    public static IOperator From(BlockRenderer render) => From(render, null);

    /// <summary>
    /// Like <see cref="From(BlockRenderer)"/>, with a <paramref name="reset"/> that restores the lambda's captured state.
    /// Only needed when the sound is played as a note voice and your state must start clean for every note.
    /// </summary>
    /// <param name="render">Renders the next block. Must not allocate or block.</param>
    /// <param name="reset">Restores the captured state to its initial values.</param>
    public static IOperator From(BlockRenderer render, Action? reset)
    {
        ArgumentNullException.ThrowIfNull(render);
        return new DelegateOperator(render, reset);
    }

    private sealed class DelegateOperator(BlockRenderer render, Action? reset) : IOperator, IResettable
    {
        public void RenderBlock(Span<StereoFrame> block) => render(block);

        public void Reset() => reset?.Invoke();
    }
}
