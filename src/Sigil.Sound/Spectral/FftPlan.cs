using System.Numerics;
using System.Runtime.InteropServices;

namespace Sigil.Sound.Spectral;

/// <summary>
/// A fast Fourier transform of one fixed size, with its tables built up front. Operators that work in the frequency domain
/// create one in their constructor, so no transform ever allocates or builds a table while audio is playing.
/// </summary>
/// <remarks>
/// <para>
/// Data is split into separate real and imaginary arrays. The forward transform uses the usual sign convention
/// (<c>e^(-2πi·k·n/N)</c>) and is unscaled; <see cref="Inverse"/> divides by the size, so a forward transform followed by an
/// inverse one returns the original data.
/// </para>
/// <para>
/// A plan keeps nothing between calls, so several operators on one thread can share a plan. The arrays being transformed
/// must not be used by two threads at once.
/// </para>
/// </remarks>
internal sealed class FftPlan
{
    private readonly float[][] _stageRe;
    private readonly float[][] _stageIm;
    private readonly int[] _bitReverse;
    private readonly bool _vectorised;

    /// <param name="size">The transform length: a power of two, at least 2.</param>
    public FftPlan(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The FFT size must be a power of two, at least 2.");

        Size = size;
        int stages = BitOperations.Log2((uint)size);
        _vectorised = Vector.IsHardwareAccelerated && size >= Vector<float>.Count * 2;

        // One table per stage: the twiddle factors of a butterfly of that span, laid out so a vector can load them directly.
        _stageRe = new float[stages][];
        _stageIm = new float[stages][];
        for (int stage = 0, span = 2; stage < stages; stage++, span <<= 1)
        {
            int half = span >> 1;
            var re = new float[half];
            var im = new float[half];
            for (int j = 0; j < half; j++)
            {
                double angle = -2.0 * Math.PI * j / span;
                re[j] = (float)Math.Cos(angle);
                im[j] = (float)Math.Sin(angle);
            }
            _stageRe[stage] = re;
            _stageIm[stage] = im;
        }

        _bitReverse = new int[size];
        for (int i = 0; i < size; i++)
            _bitReverse[i] = (int)(BitOperations.RotateLeft(ReverseBits((uint)i), stages) & (uint)(size - 1));
    }

    /// <summary>The transform length.</summary>
    public int Size { get; }

    /// <summary>The number of distinct frequency bins of a real signal: <c>Size / 2 + 1</c>, from 0 Hz up to and including Nyquist.</summary>
    public int Bins => Size / 2 + 1;

    /// <summary>Transforms in place from time to frequency.</summary>
    public void Forward(Span<float> re, Span<float> im)
    {
        Check(re, im);
        Permute(re, im);

        for (int stage = 0, span = 2; span <= Size; stage++, span <<= 1)
        {
            int half = span >> 1;
            if (_vectorised && half >= Vector<float>.Count)
                StageVector(re, im, span, _stageRe[stage], _stageIm[stage]);
            else
                StageScalar(re, im, span, _stageRe[stage], _stageIm[stage]);
        }
    }

    /// <summary>Transforms in place from frequency back to time, scaled so that <see cref="Forward"/> then this is the identity.</summary>
    public void Inverse(Span<float> re, Span<float> im)
    {
        InverseUnscaled(re, im);
        float scale = 1f / Size;
        for (int i = 0; i < re.Length; i++)
        {
            re[i] *= scale;
            im[i] *= scale;
        }
    }

    /// <summary>
    /// The inverse transform without the division by the size, for callers that fold the <c>1/Size</c> into something else
    /// (a convolution can bake it into the filter's spectrum once). Uses the identity IFFT(Y) = swap(FFT(swap(Y))), so it
    /// needs no conjugation pass and no scratch memory.
    /// </summary>
    public void InverseUnscaled(Span<float> re, Span<float> im)
    {
        Check(re, im);
        Forward(im, re);
    }

    /// <summary>Transforms a real signal: copies it into <paramref name="re"/>, clears <paramref name="im"/>, and transforms.</summary>
    public void ForwardReal(ReadOnlySpan<float> signal, Span<float> re, Span<float> im)
    {
        if (signal.Length != Size)
            throw new ArgumentException($"The signal must have {Size} samples.", nameof(signal));
        Check(re, im);
        signal.CopyTo(re);
        im.Clear();
        Forward(re, im);
    }

    private void Check(Span<float> re, Span<float> im)
    {
        if (re.Length != Size || im.Length != Size)
            throw new ArgumentException($"Both arrays must have {Size} elements.");
    }

    private void Permute(Span<float> re, Span<float> im)
    {
        for (int i = 0; i < _bitReverse.Length; i++)
        {
            int j = _bitReverse[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
    }

    private void StageScalar(Span<float> re, Span<float> im, int span, float[] twRe, float[] twIm)
    {
        int half = span >> 1;
        for (int start = 0; start < Size; start += span)
        {
            for (int j = 0; j < half; j++)
            {
                int even = start + j;
                int odd = even + half;

                float oddRe = re[odd], oddIm = im[odd];
                float tr = oddRe * twRe[j] - oddIm * twIm[j];
                float ti = oddRe * twIm[j] + oddIm * twRe[j];

                float evenRe = re[even], evenIm = im[even];
                re[even] = evenRe + tr;
                im[even] = evenIm + ti;
                re[odd] = evenRe - tr;
                im[odd] = evenIm - ti;
            }
        }
    }

    // Spans of at least one vector's width: the even and odd halves and the twiddles are all contiguous, so each step does a
    // whole vector of butterflies at once. Spans are powers of two, so there is never a ragged tail.
    private void StageVector(Span<float> re, Span<float> im, int span, float[] twRe, float[] twIm)
    {
        int half = span >> 1;
        int lanes = Vector<float>.Count;
        var vRe = MemoryMarshal.Cast<float, Vector<float>>(re);
        var vIm = MemoryMarshal.Cast<float, Vector<float>>(im);
        var vTwRe = MemoryMarshal.Cast<float, Vector<float>>(twRe.AsSpan());
        var vTwIm = MemoryMarshal.Cast<float, Vector<float>>(twIm.AsSpan());

        for (int start = 0; start < Size; start += span)
        {
            for (int j = 0, t = 0; j < half; j += lanes, t++)
            {
                int even = (start + j) / lanes;
                int odd = (start + j + half) / lanes;

                var oddRe = vRe[odd];
                var oddIm = vIm[odd];
                var wr = vTwRe[t];
                var wi = vTwIm[t];

                var tr = oddRe * wr - oddIm * wi;
                var ti = oddRe * wi + oddIm * wr;

                var evenRe = vRe[even];
                var evenIm = vIm[even];
                vRe[even] = evenRe + tr;
                vIm[even] = evenIm + ti;
                vRe[odd] = evenRe - tr;
                vIm[odd] = evenIm - ti;
            }
        }
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value >> 1) & 0x55555555u) | ((value & 0x55555555u) << 1);
        value = ((value >> 2) & 0x33333333u) | ((value & 0x33333333u) << 2);
        value = ((value >> 4) & 0x0F0F0F0Fu) | ((value & 0x0F0F0F0Fu) << 4);
        value = ((value >> 8) & 0x00FF00FFu) | ((value & 0x00FF00FFu) << 8);
        return (value >> 16) | (value << 16);
    }
}
