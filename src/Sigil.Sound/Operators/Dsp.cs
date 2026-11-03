namespace Sigil.Sound.Operators;

/// <summary>Small helpers shared by the effect operators.</summary>
internal static class Dsp
{
    /// <summary>Crossfades from <paramref name="dry"/> (mix 0) to <paramref name="wet"/> (mix 1).</summary>
    public static StereoFrame Blend(StereoFrame dry, StereoFrame wet, float mix)
    {
        mix = Math.Clamp(mix, 0f, 1f);
        return dry * (1f - mix) + wet * mix;
    }

    /// <summary>Replaces NaN and infinities with silence, so one bad value cannot poison a feedback loop.</summary>
    public static float Safe(float value) => float.IsFinite(value) ? value : 0f;

    /// <summary>A finite parameter value, or <paramref name="fallback"/> when the parameter produced NaN or infinity.</summary>
    public static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    /// <summary>
    /// Re-expresses a one-pole smoothing coefficient tuned at 48 kHz for another sample rate,
    /// so the filter has the same cutoff everywhere.
    /// </summary>
    public static float ScaleCoefficient(float coefficientAt48k, int sampleRate) =>
        1f - MathF.Pow(1f - coefficientAt48k, 48_000f / sampleRate);
}

/// <summary>A stereo ring buffer read with linear interpolation, the basis of delays, combs and choruses.</summary>
internal sealed class DelayLine
{
    private readonly float[] _left;
    private readonly float[] _right;
    private int _position;

    public DelayLine(int capacity)
    {
        capacity = Math.Max(4, capacity);
        _left = new float[capacity];
        _right = new float[capacity];
    }

    /// <summary>The largest delay <see cref="Read"/> accepts, in frames.</summary>
    public int MaxDelay => _left.Length - 1;

    /// <summary>
    /// The frame written <paramref name="delay"/> frames ago (fractions interpolate). Read before <see cref="Write"/>
    /// to get a feedback delay of exactly <paramref name="delay"/> frames.
    /// </summary>
    public StereoFrame Read(float delay)
    {
        delay = Math.Clamp(delay, 1f, MaxDelay);
        int whole = (int)delay;
        float fraction = delay - whole;
        int newer = _position - whole;
        if (newer < 0) newer += _left.Length;
        int older = newer == 0 ? _left.Length - 1 : newer - 1;
        return new StereoFrame(
            _left[newer] + (_left[older] - _left[newer]) * fraction,
            _right[newer] + (_right[older] - _right[newer]) * fraction);
    }

    public void Write(StereoFrame frame)
    {
        _left[_position] = Safe(frame.Left);
        _right[_position] = Safe(frame.Right);
        if (++_position == _left.Length) _position = 0;
    }

    public void Clear()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _position = 0;
    }

    private static float Safe(float value) => Dsp.Safe(value);
}
