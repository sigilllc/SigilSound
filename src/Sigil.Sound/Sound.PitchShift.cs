using Sigil.Sound.Operators;

namespace Sigil.Sound;

public sealed partial class Sound
{
    /// <summary>
    /// Shifts the pitch up or down without changing the speed, by moving every frequency in the sound's spectrum. Whatever shape
    /// the spectrum has moves with it, so formants follow the shift: a voice shifted up gets smaller, shifted down gets bigger,
    /// as when a tape is sped up or slowed. Shifted down, the top of the spectrum falls too, so a dive darkens as well as lowers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a phase-vocoder shifter that finds each spectral peak, moves it and the bins around it together, and keeps their
    /// phases in step. It is clean on steady and layered material (chords, pads, voices) and does best within about an octave
    /// either way. Sharp transients are softened a little by the analysis window.
    /// </para>
    /// <para>
    /// The frame is about 40 ms (2048 samples at 44.1 and 48 kHz) and the sound comes out that much later than it went in; the
    /// <paramref name="mix"/> dry signal is delayed to match. For a short-latency shifter, such as inside a feedback loop,
    /// shift in the time domain instead. A stereo sound is shifted channel by channel.
    /// </para>
    /// </remarks>
    /// <param name="semitones">How far to move the pitch: 12 is an octave up, -12 an octave down, 0 leaves it alone. Limited to ±24. A number, or a sound, <c>Control</c> or <c>Fn(t => ...)</c> to bend it; it is read once per frame, so a fast sweep steps every 10 ms. Defaults to 12, an octave up.</param>
    /// <param name="mix">0 is only the original, 1 only the shifted sound. Use a middle value to add a harmony. Defaults to 1.</param>
    /// <example><code>Saw(220).PitchShift(7, mix: 0.5)                 // the sound plus a fifth above it</code></example>
    /// <example>
    /// <code>
    /// // A two-second dive down an octave.
    /// var pad = (Saw(220) + Saw(330)).LowPass(2000) * 0.2;
    ///
    /// pad.PitchShift(Fn(t => -12 * Math.Min(t / 2, 1)))
    /// </code>
    /// </example>
    public Sound PitchShift(Param? semitones = null, Param? mix = null)
    {
        var shift = semitones ?? 12f;
        var wet = mix ?? 1f;
        return new(ctx => new PitchShiftOperator(Compile(ctx), shift.Compile(ctx), wet.Compile(ctx), ctx),
            $"{this}.PitchShift({shift})");
    }
}
