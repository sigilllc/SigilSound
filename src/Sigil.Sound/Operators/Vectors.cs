using System.Numerics;
using System.Runtime.InteropServices;

namespace Sigil.Sound.Operators;

/// <summary>
/// Element-wise loops over the interleaved floats of a block, written with <see cref="Vector{T}"/> where the machine has vector
/// instructions. Each one does exactly the arithmetic of the plain loop, in the same order for every float, so the result is the
/// same to the bit; only the number of floats done per instruction changes. Where vectors are not accelerated (the .NET
/// interpreter in the browser) the plain loop runs.
/// </summary>
internal static class Vectors
{
    /// <summary><c>block[i] += other[i]</c> on both channels.</summary>
    public static void Add(Span<StereoFrame> block, ReadOnlySpan<StereoFrame> other)
    {
        var to = MemoryMarshal.Cast<StereoFrame, float>(block);
        var from = MemoryMarshal.Cast<StereoFrame, float>(other);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var fromVectors = MemoryMarshal.Cast<float, Vector<float>>(from);
            for (int v = 0; v < toVectors.Length; v++)
                toVectors[v] += fromVectors[v];
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
            to[i] += from[i];
    }

    /// <summary><c>block[i] -= other[i]</c> on both channels.</summary>
    public static void Subtract(Span<StereoFrame> block, ReadOnlySpan<StereoFrame> other)
    {
        var to = MemoryMarshal.Cast<StereoFrame, float>(block);
        var from = MemoryMarshal.Cast<StereoFrame, float>(other);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var fromVectors = MemoryMarshal.Cast<float, Vector<float>>(from);
            for (int v = 0; v < toVectors.Length; v++)
                toVectors[v] -= fromVectors[v];
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
            to[i] -= from[i];
    }

    /// <summary><c>block[i] *= other[i]</c>, left by left and right by right.</summary>
    public static void Multiply(Span<StereoFrame> block, ReadOnlySpan<StereoFrame> other)
    {
        var to = MemoryMarshal.Cast<StereoFrame, float>(block);
        var from = MemoryMarshal.Cast<StereoFrame, float>(other);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var fromVectors = MemoryMarshal.Cast<float, Vector<float>>(from);
            for (int v = 0; v < toVectors.Length; v++)
                toVectors[v] *= fromVectors[v];
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
            to[i] *= from[i];
    }

    /// <summary><c>block[i] *= gain</c> on both channels.</summary>
    public static void Scale(Span<StereoFrame> block, float gain)
    {
        var to = MemoryMarshal.Cast<StereoFrame, float>(block);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var g = new Vector<float>(gain);
            for (int v = 0; v < toVectors.Length; v++)
                toVectors[v] *= g;
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
            to[i] *= gain;
    }

    /// <summary><c>x = min + (x + 1) * half</c> on both channels.</summary>
    public static void Range(Span<StereoFrame> block, float min, float half)
    {
        var to = MemoryMarshal.Cast<StereoFrame, float>(block);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var lo = new Vector<float>(min);
            var h = new Vector<float>(half);
            for (int v = 0; v < toVectors.Length; v++)
                toVectors[v] = lo + (toVectors[v] + Vector<float>.One) * h;
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
            to[i] = min + (to[i] + 1.0f) * half;
    }

    /// <summary>
    /// <c>to[i] += from[i] * gain</c>, returning the largest magnitude added. A NaN is skipped by the peak, as in the plain
    /// comparison <c>magnitude &gt; peak</c>.
    /// </summary>
    public static float AddScaledPeak(Span<float> to, ReadOnlySpan<float> from, float gain)
    {
        float peak = 0;
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var toVectors = MemoryMarshal.Cast<float, Vector<float>>(to);
            var fromVectors = MemoryMarshal.Cast<float, Vector<float>>(from);
            var g = new Vector<float>(gain);
            var peaks = Vector<float>.Zero;
            for (int v = 0; v < toVectors.Length; v++)
            {
                var value = fromVectors[v] * g;
                toVectors[v] += value;
                var magnitude = Vector.Abs(value);
                peaks = Vector.ConditionalSelect(Vector.GreaterThan(magnitude, peaks), magnitude, peaks);
            }
            for (int k = 0; k < Vector<float>.Count; k++)
                if (peaks[k] > peak)
                    peak = peaks[k];
            i = toVectors.Length * Vector<float>.Count;
        }
        for (; i < to.Length; i++)
        {
            float value = from[i] * gain;
            to[i] += value;
            float magnitude = value < 0 ? -value : value;
            if (magnitude > peak)
                peak = magnitude;
        }
        return peak;
    }
}
