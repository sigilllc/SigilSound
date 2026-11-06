namespace Sigil.Sound;

/// <summary>
/// One stereo sample: a left and a right single-precision value.
/// This is the unit every operator renders.
/// </summary>
public readonly record struct StereoFrame(float Left, float Right)
{
    /// <summary>A frame with both channels at zero.</summary>
    public static readonly StereoFrame Silence = new(0, 0);

    /// <summary>Creates a frame with the same value in both channels.</summary>
    public static StereoFrame Mono(float value) => new(value, value);

    /// <summary>Adds two frames channel by channel.</summary>
    public static StereoFrame operator +(StereoFrame a, StereoFrame b) => new(a.Left + b.Left, a.Right + b.Right);

    /// <summary>Subtracts two frames channel by channel.</summary>
    public static StereoFrame operator -(StereoFrame a, StereoFrame b) => new(a.Left - b.Left, a.Right - b.Right);

    /// <summary>Scales both channels by <paramref name="scalar"/>.</summary>
    public static StereoFrame operator *(StereoFrame a, float scalar) => new(a.Left * scalar, a.Right * scalar);

    /// <summary>Scales both channels by <paramref name="scalar"/>.</summary>
    public static StereoFrame operator *(float scalar, StereoFrame a) => new(a.Left * scalar, a.Right * scalar);

    /// <summary>Divides both channels by <paramref name="scalar"/>.</summary>
    public static StereoFrame operator /(StereoFrame a, float scalar) => new(a.Left / scalar, a.Right / scalar);

    /// <summary>The center-mixed (mono) value of this frame.</summary>
    public float ToMono() => 0.5f * (Left + Right);
}
