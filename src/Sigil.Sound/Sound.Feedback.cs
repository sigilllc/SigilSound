using Sigil.Sound.Operators;

namespace Sigil.Sound;

public sealed partial class Sound
{
    /// <summary>
    /// Feeds the sound's own output back through a chain you write, so echoes, shimmer and resonances that grow from what
    /// came before can be built from the same pieces as everything else. The result is this sound plus <paramref name="loop"/>
    /// applied to that result, a moment late.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop sees the output one fixed chunk (<paramref name="latencySeconds"/>, 1 ms by default) late, so a loop with a delay of
    /// 0.35 s repeats every 0.35 s plus that chunk. The chunk is the same whatever block size the host renders in, so the sound does
    /// not change between hosts. Loops shorter than the chunk cannot be built this way; use <see cref="Comb"/>.
    /// </para>
    /// <para>
    /// Give the loop a gain below 1 so the repeats die away. A loop that keeps growing is held in check instead of running away: values past full
    /// scale are eased toward a ceiling of 2 and anything that is not a number is dropped, so a mistake gives a loud sound, not silence
    /// or a crash.
    /// </para>
    /// <para>
    /// <paramref name="loop"/> is called each time the sound is compiled, with a stand-in for the delayed output. It should only build a
    /// sound from it, and may use the stand-in more than once.
    /// </para>
    /// </remarks>
    /// <param name="loop">Builds what is fed back from the delayed output.</param>
    /// <param name="latencySeconds">How late the loop sees the output, from 0.0001 to 0.05. Defaults to 0.001.</param>
    /// <example>
    /// <code>
    /// Saw(220).Adsr(0.005, 0.1, 0, 0.1).Feedback(echo => echo.Delay(0.35, mix: 1).LowPass(3000) * 0.6)
    /// </code>
    /// </example>
    public Sound Feedback(Func<Sound, Sound> loop, double latencySeconds = 0.001)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (!(latencySeconds is >= 0.0001 and <= 0.05))
            throw new ArgumentOutOfRangeException(nameof(latencySeconds), latencySeconds, "The latency must be between 0.0001 and 0.05 seconds.");

        return new(ctx =>
        {
            int chunk = Math.Clamp((int)Math.Round(ctx.SampleRate * latencySeconds), 1, ctx.BlockSize);
            var tap = new FeedbackTap(chunk);
            var returned = loop(FromOperator(_ => tap, "Feedback")) ?? throw new InvalidOperationException("The feedback loop returned null.");
            return new FeedbackOperator(Compile(ctx), returned.Compile(ctx), tap);
        }, $"{this}.Feedback(...)");
    }
}
