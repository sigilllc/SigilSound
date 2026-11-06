using System.Numerics;
using System.Runtime.InteropServices;
using Sigil.Sound.Spectral;

namespace Sigil.Sound.Operators;

/// <summary>
/// An impulse response prepared for convolution: its taps split by a <see cref="PartitionPlan"/>, the first ones stored for a
/// direct-form FIR and the rest transformed into FFT spectra. It is immutable once built, so one kernel serves every
/// <see cref="ConvolutionEngine"/> made from it (every voice, every recompile of a sound) without copying the spectra.
/// </summary>
/// <remarks>
/// Building costs one FFT per partition, so it belongs in a sound's factory, never on the audio thread.
/// A mono impulse response (the same array for both channels) stores one set of spectra and halves the work per block.
/// </remarks>
internal sealed class ConvolutionKernel
{
    /// <summary>Largest slice an engine handles in one go, which bounds its head history buffer.</summary>
    public const int MaxChunk = 512;

    // Per-stage block phases (samples). Without them every stage hits its FFT boundary on the same sample
    // every 8192 frames; offsetting them spreads the work (10 s IR at 64 frames: worst block 1.7 ms -> 0.5 ms).
    // A phase must stay below the stage's partition size so output before the first boundary is truly silent.
    private static readonly int[] DefaultStagePhases = [0, 173, 301, 1009];

    /// <param name="left">The left taps.</param>
    /// <param name="right">The right taps; pass the same array as <paramref name="left"/> for a mono response.</param>
    public ConvolutionKernel(float[] left, float[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Length == 0)
            throw new ArgumentException("The impulse response is empty.", nameof(left));
        Mono = ReferenceEquals(left, right);
        if (!Mono && right.Length != left.Length)
            throw new ArgumentException("The two channels of the impulse response differ in length.", nameof(right));

        Plan = PartitionPlan.Create(left.Length);

        int lanes = Math.Max(Vector<float>.Count, 4);
        HeadPadded = (Plan.HeadTaps + lanes - 1) / lanes * lanes;
        HeadRevLeft = new float[HeadPadded];
        HeadRevRight = Mono ? HeadRevLeft : new float[HeadPadded];

        // Taps are stored reversed and right-aligned so the dot product runs forward over the history.
        for (int j = 0; j < Plan.HeadTaps; j++)
        {
            HeadRevLeft[HeadPadded - 1 - j] = left[j];
            if (!Mono) HeadRevRight[HeadPadded - 1 - j] = right[j];
        }

        Stages = new StageKernel[Plan.Stages.Count];
        for (int i = 0; i < Stages.Length; i++)
        {
            int phase = i < DefaultStagePhases.Length ? DefaultStagePhases[i] : 0;
            Stages[i] = new StageKernel(Plan.Stages[i], Mono, phase, left, right);
        }
    }

    public PartitionPlan Plan { get; }

    public bool Mono { get; }

    public int HeadPadded { get; }

    public float[] HeadRevLeft { get; }

    public float[] HeadRevRight { get; }

    public StageKernel[] Stages { get; }

    /// <summary>The spectra of one FFT-partitioned stage: taps [Start, Start + Length) cut into partitions of B samples.</summary>
    internal sealed class StageKernel
    {
        public StageKernel(PartitionPlan.Stage stage, bool mono, int phase, float[] left, float[] right)
        {
            B = stage.PartitionSize;
            N2 = stage.FftSize;
            M = stage.PartitionCount;
            Sliced = stage.Sliced;
            Channels = mono ? 1 : 2;
            Phase = ((phase % B) + B) % B;
            Fft = new FftPlan(N2);

            IrRe = new float[Channels][];
            IrIm = new float[Channels][];
            for (int c = 0; c < Channels; c++)
            {
                IrRe[c] = new float[M * N2];
                IrIm[c] = new float[M * N2];
            }

            // Two real channels travel through one complex transform: left as the real part, right as the imaginary part.
            var re = new float[N2];
            var im = new float[N2];
            float scale = 1f / N2;   // folds the inverse transform's normalisation into the spectra, once
            for (int p = 0; p < M; p++)
            {
                int offset = stage.Start + p * B;
                int count = Math.Min(B, stage.Length - p * B);

                Array.Clear(re);
                Array.Clear(im);
                Array.Copy(left, offset, re, 0, count);
                if (!mono) Array.Copy(right, offset, im, 0, count);

                Fft.Forward(re, im);
                for (int i = 0; i < N2; i++)
                {
                    re[i] *= scale;
                    im[i] *= scale;
                }

                int slot = p * N2;
                if (mono)
                {
                    Array.Copy(re, 0, IrRe[0], slot, N2);
                    Array.Copy(im, 0, IrIm[0], slot, N2);
                }
                else
                {
                    SplitPacked(re, im, IrRe[0], IrIm[0], IrRe[1], IrIm[1], slot);
                }
            }
        }

        /// <summary>Partition size.</summary>
        public int B { get; }

        /// <summary>FFT size, twice the partition size (overlap-save).</summary>
        public int N2 { get; }

        /// <summary>Partition count.</summary>
        public int M { get; }

        /// <summary>True when the stage starts at two partitions or later, which leaves a whole block to spread the multiply-accumulate over.</summary>
        public bool Sliced { get; }

        /// <summary>Spectrum channels: 1 for a mono response, 2 for left and right.</summary>
        public int Channels { get; }

        /// <summary>Samples by which the stage's block boundaries are shifted from the start.</summary>
        public int Phase { get; }

        public FftPlan Fft { get; }

        public float[][] IrRe { get; }

        public float[][] IrIm { get; }
    }

    /// <summary>
    /// Splits the spectrum Z of z = l + j*r into the spectra of l and r:
    /// L[k] = (Z[k] + conj(Z[N-k])) / 2 and R[k] = (Z[k] - conj(Z[N-k])) / 2j.
    /// Both outputs are spectra of real signals, so bin N-k is the conjugate of bin k and each pair is computed once.
    /// Outputs are written at <paramref name="dstOffset"/> into the destination arrays.
    /// </summary>
    internal static void SplitPacked(float[] zRe, float[] zIm,
        float[] lRe, float[] lIm, float[] rRe, float[] rIm, int dstOffset)
    {
        int n = zRe.Length;
        int half = n >> 1;

        // DC and Nyquist map onto themselves, so they are purely real for real inputs.
        lRe[dstOffset] = zRe[0];
        lIm[dstOffset] = 0f;
        rRe[dstOffset] = zIm[0];
        rIm[dstOffset] = 0f;
        lRe[dstOffset + half] = zRe[half];
        lIm[dstOffset + half] = 0f;
        rRe[dstOffset + half] = zIm[half];
        rIm[dstOffset + half] = 0f;

        for (int k = 1; k < half; k++)
        {
            int j = n - k;
            float a = zRe[k], b = zIm[k];
            float c = zRe[j], d = zIm[j];

            float lr = 0.5f * (a + c);
            float li = 0.5f * (b - d);
            float rr = 0.5f * (b + d);
            float ri = 0.5f * (c - a);

            lRe[dstOffset + k] = lr;
            lIm[dstOffset + k] = li;
            rRe[dstOffset + k] = rr;
            rIm[dstOffset + k] = ri;
            lRe[dstOffset + j] = lr;
            lIm[dstOffset + j] = -li;
            rRe[dstOffset + j] = rr;
            rIm[dstOffset + j] = -ri;
        }
    }
}

/// <summary>
/// Zero-latency stereo convolver. A SIMD time-domain FIR covers the first 128 taps and overlap-save FFT stages with growing
/// partition sizes cover the rest (see <see cref="PartitionPlan"/>). L is convolved with IR_L and R with IR_R; a mono
/// response shares one spectrum set and halves the multiply-accumulate work.
/// </summary>
/// <remarks>
/// An engine holds only the signal state (input history, frequency-domain delay lines, queued output). The response itself
/// is in the shared <see cref="ConvolutionKernel"/>. Every buffer is allocated in the constructor, so <see cref="Process"/>
/// never allocates, locks or does I/O, accepts any block length and keeps its own sample clock.
/// </remarks>
internal sealed class ConvolutionEngine
{
    // Stage outputs below this become exact zeros so long silent tails never run the CPU through subnormal floats.
    private const float DenormalFloor = 1e-20f;

    private const int MaxChunk = ConvolutionKernel.MaxChunk;

    private readonly ConvolutionKernel _kernel;
    private readonly float[] _histLeft;
    private readonly float[] _histRight;
    private readonly Stage[] _stages;

    public ConvolutionEngine(ConvolutionKernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _histLeft = new float[kernel.HeadPadded - 1 + MaxChunk];
        _histRight = new float[kernel.HeadPadded - 1 + MaxChunk];
        _stages = new Stage[kernel.Stages.Length];
        for (int i = 0; i < _stages.Length; i++)
            _stages[i] = new Stage(kernel.Stages[i], kernel.Mono);
        Reset();
    }

    /// <summary>Clears all signal state (history, spectra delay lines, queued outputs) but keeps the response.</summary>
    public void Reset()
    {
        Array.Clear(_histLeft);
        Array.Clear(_histRight);
        foreach (var stage in _stages)
            stage.Reset();
    }

    /// <summary>Convolves a stereo block. Inputs and outputs have the same length; the output is overwritten.</summary>
    public void Process(ReadOnlySpan<float> inL, ReadOnlySpan<float> inR, Span<float> outL, Span<float> outR)
    {
        int total = inL.Length;
        if (inR.Length != total || outL.Length != total || outR.Length != total)
            throw new ArgumentException("All spans must have the same length.");

        for (int off = 0; off < total; off += MaxChunk)
        {
            int n = Math.Min(MaxChunk, total - off);
            var xl = inL.Slice(off, n);
            var xr = inR.Slice(off, n);
            var yl = outL.Slice(off, n);
            var yr = outR.Slice(off, n);

            ProcessHead(_histLeft, _kernel.HeadRevLeft, xl, yl);
            ProcessHead(_histRight, _kernel.HeadRevRight, xr, yr);

            foreach (var stage in _stages)
                stage.Process(xl, xr, yl, yr);
        }
    }

    /// <summary>Direct-form FIR over the first taps: y[i] = sum_t revTaps[t] * hist[i + t].</summary>
    private void ProcessHead(float[] hist, float[] revTaps, ReadOnlySpan<float> x, Span<float> y)
    {
        int n = x.Length;
        int hp = _kernel.HeadPadded;
        x.CopyTo(hist.AsSpan(hp - 1));

        var taps = MemoryMarshal.Cast<float, Vector<float>>(revTaps.AsSpan());
        int vecs = taps.Length;
        for (int i = 0; i < n; i++)
        {
            var window = MemoryMarshal.Cast<float, Vector<float>>(new ReadOnlySpan<float>(hist, i, hp));
            var acc0 = Vector<float>.Zero;
            var acc1 = Vector<float>.Zero;
            int k = 0;
            for (; k + 1 < vecs; k += 2)
            {
                acc0 += taps[k] * window[k];
                acc1 += taps[k + 1] * window[k + 1];
            }
            if (k < vecs) acc0 += taps[k] * window[k];
            y[i] = Vector.Sum(acc0 + acc1);
        }

        // Keep the newest hp-1 samples as history for the next chunk.
        Array.Copy(hist, n, hist, 0, hp - 1);
    }

    /// <summary>
    /// Overlap-save partitioned convolution of the taps [O, O + L) of the response. Input is accumulated into B-sample blocks;
    /// at each block boundary the stage transforms [previous | current], pushes the spectrum into a frequency-domain delay
    /// line, and produces the next B output samples. Stages with O = 2B (<see cref="ConvolutionKernel.StageKernel.Sliced"/>)
    /// spread the partition multiply-accumulate over the preceding block; only the FFTs run at the boundary.
    /// </summary>
    private sealed class Stage
    {
        private readonly ConvolutionKernel.StageKernel _kernel;
        private readonly bool _mono;
        private readonly int _b;
        private readonly int _n2;
        private readonly int _m;
        private readonly int _channels;

        // The frequency-domain delay line and the accumulator, [channel][slot * N2 + bin].
        private readonly float[][] _fdlRe;
        private readonly float[][] _fdlIm;
        private readonly float[][] _accRe;
        private readonly float[][] _accIm;

        private readonly float[] _winL;   // [previous block | current block]
        private readonly float[] _winR;
        private readonly float[] _zRe;    // FFT scratch, also used to combine channels for the inverse
        private readonly float[] _zIm;
        private readonly float[] _outL;   // output block being played
        private readonly float[] _outR;

        private int _pos;
        private int _fdlHead;
        private bool _jobActive;
        private int _jobHead;
        private int _jobDone;

        public Stage(ConvolutionKernel.StageKernel kernel, bool mono)
        {
            _kernel = kernel;
            _mono = mono;
            _b = kernel.B;
            _n2 = kernel.N2;
            _m = kernel.M;
            _channels = kernel.Channels;

            _winL = new float[_n2];
            _winR = new float[_n2];
            _zRe = new float[_n2];
            _zIm = new float[_n2];
            _outL = new float[_b];
            _outR = new float[_b];
            _accRe = NewChannels(_n2);
            _accIm = NewChannels(_n2);
            _fdlRe = NewChannels(_m * _n2);
            _fdlIm = NewChannels(_m * _n2);
        }

        private float[][] NewChannels(int length)
        {
            var channels = new float[_channels][];
            for (int c = 0; c < _channels; c++)
                channels[c] = new float[length];
            return channels;
        }

        public void Reset()
        {
            Array.Clear(_winL);
            Array.Clear(_winR);
            Array.Clear(_outL);
            Array.Clear(_outR);
            for (int c = 0; c < _channels; c++)
            {
                Array.Clear(_fdlRe[c]);
                Array.Clear(_fdlIm[c]);
                Array.Clear(_accRe[c]);
                Array.Clear(_accIm[c]);
            }

            // A phase offset makes the first block boundary arrive after Phase samples.
            _pos = _kernel.Phase == 0 ? 0 : _b - _kernel.Phase;
            _fdlHead = 0;
            _jobActive = false;
            _jobHead = 0;
            _jobDone = 0;
        }

        public void Process(ReadOnlySpan<float> inL, ReadOnlySpan<float> inR, Span<float> outL, Span<float> outR)
        {
            int n = inL.Length;
            int off = 0;
            while (off < n)
            {
                int take = Math.Min(n - off, _b - _pos);

                // Add this stage's already-computed output for the block being played.
                var oL = _outL.AsSpan(_pos, take);
                var oR = _outR.AsSpan(_pos, take);
                var dL = outL.Slice(off, take);
                var dR = outR.Slice(off, take);
                for (int i = 0; i < take; i++)
                {
                    dL[i] += oL[i];
                    dR[i] += oR[i];
                }

                inL.Slice(off, take).CopyTo(_winL.AsSpan(_b + _pos));
                inR.Slice(off, take).CopyTo(_winR.AsSpan(_b + _pos));

                _pos += take;
                off += take;

                if (_pos == _b)
                {
                    Boundary();
                    _pos = 0;
                }
                else if (_kernel.Sliced && _jobActive)
                {
                    RunMacs((int)((long)_m * _pos / _b));
                }
            }
        }

        private void Boundary()
        {
            if (!_kernel.Sliced)
            {
                ForwardAndPush();
                StartJob();
                RunMacs(_m);
                FinishJob();
                return;
            }

            // Sliced: the job started one block ago has had a full block to run its MACs.
            if (_jobActive)
            {
                RunMacs(_m);
                FinishJob();
            }
            else
            {
                Array.Clear(_outL);
                Array.Clear(_outR);
            }

            ForwardAndPush();
            StartJob();
        }

        private void ForwardAndPush()
        {
            _winL.CopyTo(_zRe, 0);
            _winR.CopyTo(_zIm, 0);
            _kernel.Fft.Forward(_zRe, _zIm);

            _fdlHead = _fdlHead + 1 == _m ? 0 : _fdlHead + 1;
            int slot = _fdlHead * _n2;
            if (_mono)
            {
                Array.Copy(_zRe, 0, _fdlRe[0], slot, _n2);
                Array.Copy(_zIm, 0, _fdlIm[0], slot, _n2);
            }
            else
            {
                ConvolutionKernel.SplitPacked(_zRe, _zIm, _fdlRe[0], _fdlIm[0], _fdlRe[1], _fdlIm[1], slot);
            }

            // Current block becomes the "previous" half of the next window.
            Array.Copy(_winL, _b, _winL, 0, _b);
            Array.Copy(_winR, _b, _winR, 0, _b);
        }

        private void StartJob()
        {
            _jobActive = true;
            _jobHead = _fdlHead;
            _jobDone = 0;
            for (int c = 0; c < _channels; c++)
            {
                Array.Clear(_accRe[c]);
                Array.Clear(_accIm[c]);
            }
        }

        /// <summary>Multiply-accumulates partitions up to (excluding) <paramref name="upTo"/> for the running job.</summary>
        private void RunMacs(int upTo)
        {
            while (_jobDone < upTo)
            {
                int p = _jobDone++;
                int fdlSlot = _jobHead - p;
                if (fdlSlot < 0) fdlSlot += _m;
                int fOff = fdlSlot * _n2;
                int hOff = p * _n2;
                for (int c = 0; c < _channels; c++)
                    Mac(_accRe[c], _accIm[c], _fdlRe[c], _fdlIm[c], fOff, _kernel.IrRe[c], _kernel.IrIm[c], hOff, _n2);
            }
        }

        /// <summary>Inverse-transforms the accumulated spectrum and keeps the valid (last B) samples.</summary>
        private void FinishJob()
        {
            float[] yRe, yIm;
            if (_mono)
            {
                yRe = _accRe[0];
                yIm = _accIm[0];
            }
            else
            {
                // Y = Y_L + j*Y_R, so one complex inverse yields L in the real part and R in the imaginary part.
                float[] lRe = _accRe[0], lIm = _accIm[0], rRe = _accRe[1], rIm = _accIm[1];
                yRe = _zRe;
                yIm = _zIm;
                for (int i = 0; i < _n2; i++)
                {
                    yRe[i] = lRe[i] - rIm[i];
                    yIm[i] = lIm[i] + rRe[i];
                }
            }

            _kernel.Fft.InverseUnscaled(yRe, yIm);

            for (int i = 0; i < _b; i++)
            {
                float l = yRe[_b + i];
                float r = yIm[_b + i];
                _outL[i] = MathF.Abs(l) < DenormalFloor ? 0f : l;
                _outR[i] = MathF.Abs(r) < DenormalFloor ? 0f : r;
            }
            _jobActive = false;
        }

        /// <summary>acc += f * h over n bins (complex). n is a power of two of at least 256.</summary>
        private static void Mac(float[] accRe, float[] accIm, float[] fRe, float[] fIm, int fOff,
            float[] hRe, float[] hIm, int hOff, int n)
        {
            var aR = MemoryMarshal.Cast<float, Vector<float>>(accRe.AsSpan(0, n));
            var aI = MemoryMarshal.Cast<float, Vector<float>>(accIm.AsSpan(0, n));
            var fR = MemoryMarshal.Cast<float, Vector<float>>(new ReadOnlySpan<float>(fRe, fOff, n));
            var fI = MemoryMarshal.Cast<float, Vector<float>>(new ReadOnlySpan<float>(fIm, fOff, n));
            var hR = MemoryMarshal.Cast<float, Vector<float>>(new ReadOnlySpan<float>(hRe, hOff, n));
            var hI = MemoryMarshal.Cast<float, Vector<float>>(new ReadOnlySpan<float>(hIm, hOff, n));

            for (int i = 0; i < aR.Length; i++)
            {
                var fr = fR[i];
                var fi = fI[i];
                var hr = hR[i];
                var hi = hI[i];
                aR[i] += fr * hr - fi * hi;
                aI[i] += fr * hi + fi * hr;
            }
        }
    }
}
