namespace Sigil.Sound;

/// <summary>The shape of the crossfade a <see cref="Slot"/> or a <see cref="SoundContext"/> makes when its sound is swapped.</summary>
public enum FadeCurve
{
    /// <summary>
    /// Keeps the loudness steady when the two sounds are unrelated, which is what a freshly compiled sound usually is: the default
    /// for audio. Two copies of the same signal rise by 3 dB halfway through.
    /// </summary>
    EqualPower,

    /// <summary>
    /// Keeps the level steady when the two sounds are alike, such as two versions of a control signal or a constant: the curve for
    /// modulation and control outputs. Two unrelated sounds dip by 3 dB halfway through.
    /// </summary>
    Linear,
}
