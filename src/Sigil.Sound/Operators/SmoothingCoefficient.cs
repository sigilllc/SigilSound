namespace Sigil.Sound.Operators;

/// <summary>
/// The per-sample retention of a one-pole smoother with a given time constant: each sample closes
/// <c>1 - coefficient</c> of the remaining gap. The exponential is only recomputed when the time changes, so a fixed
/// time costs nothing per sample.
/// </summary>
internal sealed class SmoothingCoefficient(float sampleRate)
{
    private float _seconds = float.NaN;
    private float _coefficient;

    /// <summary>
    /// Returns exp(-1 / (seconds * sampleRate)): after <paramref name="seconds"/> the smoother has covered about 63% of a step.
    /// Zero (or negative) seconds returns 0, which follows the target immediately.
    /// </summary>
    /// <param name="seconds">The time constant. Must be finite; callers sanitise it first.</param>
    public float For(float seconds)
    {
        if (seconds != _seconds)
        {
            _seconds = seconds;
            _coefficient = seconds <= 0 ? 0f : MathF.Exp(-1f / (seconds * sampleRate));
        }
        return _coefficient;
    }
}

/// <summary>
/// The per-sample gain of a one-pole low-pass at a cutoff: <c>1 - exp(-2π · cutoff / sampleRate)</c>. Like
/// <see cref="SmoothingCoefficient"/>, it is only recomputed when the cutoff changes.
/// </summary>
internal sealed class CutoffCoefficient(int sampleRate)
{
    private double _cutoff = double.NaN;
    private float _coefficient;

    /// <summary>Returns the low-pass gain for a cutoff in Hz; the caller keeps the cutoff within the audible range.</summary>
    /// <param name="cutoffHz">The cutoff frequency. Must be finite.</param>
    public float For(double cutoffHz)
    {
        if (cutoffHz != _cutoff)
        {
            _cutoff = cutoffHz;
            _coefficient = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate));
        }
        return _coefficient;
    }
}
