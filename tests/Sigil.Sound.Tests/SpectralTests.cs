using Sigil.Sound.Spectral;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class FftPlanTests
{
    private static (float[] Re, float[] Im) RandomSignal(int n, int seed)
    {
        var random = new Random(seed);
        var re = new float[n];
        var im = new float[n];
        for (int i = 0; i < n; i++)
        {
            re[i] = (float)(random.NextDouble() * 2 - 1);
            im[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return (re, im);
    }

    // The textbook definition, in double precision, as the reference.
    private static (double[] Re, double[] Im) Dft(float[] re, float[] im)
    {
        int n = re.Length;
        var outRe = new double[n];
        var outIm = new double[n];
        for (int k = 0; k < n; k++)
            for (int t = 0; t < n; t++)
            {
                double angle = -2 * Math.PI * k * t / n;
                outRe[k] += re[t] * Math.Cos(angle) - im[t] * Math.Sin(angle);
                outIm[k] += re[t] * Math.Sin(angle) + im[t] * Math.Cos(angle);
            }
        return (outRe, outIm);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(1024)]
    public void Forward_matches_the_definition(int n)
    {
        var (re, im) = RandomSignal(n, n);
        var expected = Dft(re, im);

        new FftPlan(n).Forward(re, im);

        for (int k = 0; k < n; k++)
        {
            Assert.Equal(expected.Re[k], re[k], 1e-3 * Math.Sqrt(n));
            Assert.Equal(expected.Im[k], im[k], 1e-3 * Math.Sqrt(n));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    [InlineData(2048)]
    [InlineData(16384)]
    public void Inverse_undoes_forward(int n)
    {
        var (re, im) = RandomSignal(n, 7);
        var (re0, im0) = ((float[])re.Clone(), (float[])im.Clone());
        var plan = new FftPlan(n);

        plan.Forward(re, im);
        plan.Inverse(re, im);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(re0[i], re[i], 1e-4f);
            Assert.Equal(im0[i], im[i], 1e-4f);
        }
    }

    [Fact]
    public void An_impulse_has_a_flat_spectrum_and_a_sine_peaks_at_its_bin()
    {
        var plan = new FftPlan(256);
        var re = new float[256];
        var im = new float[256];
        re[0] = 1;
        plan.Forward(re, im);
        Assert.All(re, v => Assert.Equal(1f, v, 1e-6f));
        Assert.All(im, v => Assert.Equal(0f, v, 1e-6f));

        for (int i = 0; i < 256; i++) { re[i] = MathF.Cos(MathF.Tau * 5 * i / 256); im[i] = 0; }
        plan.Forward(re, im);
        Assert.Equal(128f, re[5], 1e-2f);
        Assert.Equal(128f, re[251], 1e-2f);
        Assert.Equal(0f, re[6], 1e-2f);
    }

    [Fact]
    public void Energy_is_conserved()
    {
        var (re, im) = RandomSignal(512, 3);
        double timeEnergy = re.Sum(v => (double)v * v) + im.Sum(v => (double)v * v);

        new FftPlan(512).Forward(re, im);

        double freqEnergy = (re.Sum(v => (double)v * v) + im.Sum(v => (double)v * v)) / 512;
        Assert.Equal(timeEnergy, freqEnergy, timeEnergy * 1e-4);
    }

    [Fact]
    public void ForwardReal_copies_clears_and_transforms()
    {
        var plan = new FftPlan(8);
        var re = new float[8];
        var im = Enumerable.Repeat(9f, 8).ToArray();

        plan.ForwardReal([1, 0, 0, 0, 0, 0, 0, 0], re, im);

        Assert.All(re, v => Assert.Equal(1f, v));
        Assert.All(im, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Unscaled_inverse_differs_from_inverse_by_the_size()
    {
        var (re, im) = RandomSignal(64, 5);
        var re2 = (float[])re.Clone();
        var im2 = (float[])im.Clone();
        var plan = new FftPlan(64);

        plan.InverseUnscaled(re, im);
        plan.Inverse(re2, im2);

        for (int i = 0; i < 64; i++)
            Assert.Equal(re[i], re2[i] * 64, 1e-3f);
    }

    [Fact]
    public void Bad_sizes_and_lengths_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FftPlan(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FftPlan(12));
        Assert.Throws<ArgumentException>(() => new FftPlan(8).Forward(new float[8], new float[4]));
        Assert.Throws<ArgumentException>(() => new FftPlan(8).ForwardReal(new float[4], new float[8], new float[8]));
    }

    [Fact]
    public void Transforms_allocate_nothing()
    {
        var plan = new FftPlan(2048);
        var (re, im) = RandomSignal(2048, 1);
        plan.Forward(re, im);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            plan.Forward(re, im);
            plan.Inverse(re, im);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Mirroring_the_lower_half_makes_the_inverse_real()
    {
        var plan = new FftPlan(64);
        var re = new float[64];
        var im = new float[64];
        re[3] = 2; im[3] = 1;
        re[10] = -1; im[10] = 0.5f;

        SpectralMath.MirrorLowerHalf(re, im);
        plan.Inverse(re, im);

        Assert.All(im, v => Assert.Equal(0f, v, 1e-6f));
        Assert.Contains(re, v => Math.Abs(v) > 0.01f);
    }

    [Fact]
    public void Polar_conversion_round_trips_and_phase_wraps()
    {
        float[] re = [1, 0, -1, 0.5f], im = [0, 2, 0, -0.5f];
        var mag = new float[4];
        var phase = new float[4];
        var re2 = new float[4];
        var im2 = new float[4];

        SpectralMath.ToPolar(re, im, mag, phase);
        SpectralMath.FromPolar(mag, phase, re2, im2);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(re[i], re2[i], 1e-5f);
            Assert.Equal(im[i], im2[i], 1e-5f);
        }
        Assert.Equal(-MathF.PI + 0.5f, SpectralMath.WrapPhase(MathF.PI + 0.5f), 1e-5f);
    }
}

public class StftProcessorTests
{
    private const int Rate = 48_000;

    private static float[] TestSignal(int length)
    {
        var random = new Random(11);
        var signal = new float[length];
        for (int i = 0; i < length; i++)
            signal[i] = 0.4f * MathF.Sin(MathF.Tau * 440 * i / Rate) + 0.2f * MathF.Sin(MathF.Tau * 3100 * i / Rate) + 0.05f * (float)(random.NextDouble() * 2 - 1);
        return signal;
    }

    private static float[] Run(StftProcessor processor, float[] input, params int[] chunkSizes)
    {
        var output = new float[input.Length];
        for (int done = 0, i = 0; done < input.Length; i++)
        {
            int count = Math.Min(chunkSizes[i % chunkSizes.Length], input.Length - done);
            processor.Process(input.AsSpan(done, count), output.AsSpan(done, count));
            done += count;
        }
        return output;
    }

    private static void Halve(Span<float> re, Span<float> im)
    {
        for (int k = 0; k < re.Length; k++)
        {
            re[k] *= 0.5f;
            im[k] *= 0.5f;
        }
    }

    [Theory]
    [InlineData(1024, 256)]
    [InlineData(2048, 512)]
    [InlineData(2048, 256)]
    [InlineData(512, 64)]
    public void With_no_edit_the_output_is_the_input_delayed_by_the_latency(int size, int hop)
    {
        var input = TestSignal(20_000);
        var processor = new StftProcessor(size, hop, (_, _) => { });

        var output = Run(processor, input, 256);

        Assert.Equal(size, processor.Latency);
        for (int i = size; i < input.Length; i++)
            Assert.Equal(input[i - size], output[i], 1e-4f);
        Assert.All(output.Take(size), v => Assert.Equal(0f, v, 1e-6f));
    }

    [Fact]
    public void The_output_is_the_same_however_the_input_is_chopped_up()
    {
        var input = TestSignal(12_000);

        var reference = Run(new StftProcessor(1024, 256, Halve), input, 4096);

        Assert.Equal(reference, Run(new StftProcessor(1024, 256, Halve), input, 1));
        Assert.Equal(reference, Run(new StftProcessor(1024, 256, Halve), input, 37, 256, 5, 1000));
        Assert.Equal(reference, Run(new StftProcessor(1024, 256, Halve), input, 256));
    }

    [Fact]
    public void The_handler_runs_once_per_hop()
    {
        int frames = 0;
        var processor = new StftProcessor(1024, 256, (_, _) => frames++);

        Run(processor, new float[10_240], 100);

        Assert.Equal(40, frames);
    }

    [Fact]
    public void An_edit_to_the_spectrum_shows_in_the_sound()
    {
        // Keep only the bins below 1 kHz: a 440 Hz sine passes and a 3100 Hz one is removed.
        int size = 2048;
        int cutoffBin = (int)(1000.0 * size / Rate);
        var processor = new StftProcessor(size, 512, (re, im) =>
        {
            for (int k = cutoffBin; k <= size / 2; k++)
            {
                re[k] = 0;
                im[k] = 0;
            }
            SpectralMath.MirrorLowerHalf(re, im);
        });
        var input = TestSignal(30_000);

        var output = Run(processor, input, 256);

        var settled = output.AsSpan(8192, 16384).ToArray();
        Assert.Equal(440, SignalAnalysis.PeakFrequency(settled, Rate), 2);
        var spectrum = SignalAnalysis.MagnitudeSpectrum(settled);
        int bin440 = (int)Math.Round(440.0 * spectrum.Length * 2 / Rate);
        int bin3100 = (int)Math.Round(3100.0 * spectrum.Length * 2 / Rate);
        Assert.True(spectrum[bin3100] < spectrum[bin440] * 0.01f);
    }

    [Fact]
    public void Halving_every_bin_halves_the_level()
    {
        var input = TestSignal(10_000);

        var output = Run(new StftProcessor(1024, 256, Halve), input, 256);

        for (int i = 2048; i < 8000; i += 97)
            Assert.Equal(0.5f * input[i - 1024], output[i], 1e-4f);
    }

    [Fact]
    public void Processing_in_place_works()
    {
        var input = TestSignal(8000);
        var expected = Run(new StftProcessor(512, 128, (_, _) => { }), input, 64);
        var buffer = (float[])input.Clone();
        var processor = new StftProcessor(512, 128, (_, _) => { });

        for (int i = 0; i < buffer.Length; i += 64)
            processor.Process(buffer.AsSpan(i, 64), buffer.AsSpan(i, 64));

        Assert.Equal(expected, buffer);
    }

    [Fact]
    public void Reset_starts_again_from_silence()
    {
        var input = TestSignal(6000);
        var processor = new StftProcessor(512, 128, (_, _) => { });
        var first = Run(processor, input, 100);

        processor.Reset();

        Assert.Equal(first, Run(processor, input, 100));
    }

    [Fact]
    public void Processing_allocates_nothing()
    {
        var processor = new StftProcessor(2048, 512, SpectralMath.MirrorLowerHalf);
        var input = TestSignal(4096);
        var output = new float[4096];
        for (int i = 0; i < 4; i++) processor.Process(input, output);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) processor.Process(input, output);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void A_window_and_hop_that_do_not_overlap_add_to_a_constant_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new StftProcessor(1024, 512, (_, _) => { }));
        Assert.Throws<ArgumentException>(() => new StftProcessor(1024, 300, (_, _) => { }));
        Assert.Throws<ArgumentException>(() => new StftProcessor(1024, 256, (_, _) => { }, new float[512]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StftProcessor(1000, 250, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new StftProcessor(1024, 256, null!));
        Assert.Throws<ArgumentException>(() => new StftProcessor(1024, 256, (_, _) => { }).Process(new float[10], new float[5]));
    }

    [Fact]
    public void The_hann_window_is_periodic_with_unit_peak()
    {
        var window = Windows.Hann(1024);

        Assert.Equal(0f, window[0]);
        Assert.Equal(1f, window[512], 1e-6f);
        Assert.Equal(window[100], window[1024 - 100], 1e-6f);
    }
}

public class SpectrumAnalysisTests
{
    [Theory]
    [InlineData(440.0)]
    [InlineData(1000.0)]
    [InlineData(4321.5)]
    [InlineData(100.0)]
    public void PeakFrequency_finds_a_sine_to_a_fraction_of_a_bin(double hz)
    {
        var samples = Sine(hz).Render(0.5, 48_000).ToMono();

        Assert.Equal(hz, SignalAnalysis.PeakFrequency(samples, 48_000), 0.5);
    }

    [Fact]
    public void PeakFrequency_picks_the_loudest_partial_in_a_mix()
    {
        var mix = (Sine(300) * 0.3 + Sine(1200) * 0.8 + Sine(5000) * 0.2).Render(0.5, 48_000);

        AudioAssert.PeakFrequencyNear(mix, 1200, 1);
    }

    [Fact]
    public void A_unit_sine_reads_near_one_in_the_spectrum()
    {
        var samples = Sine(1000).Render(0.25, 48_000).ToMono();
        var spectrum = SignalAnalysis.MagnitudeSpectrum(samples, 1 << 16);

        Assert.InRange(spectrum.Max(), 0.95f, 1.05f);
    }

    [Fact]
    public void The_assertion_reports_a_miss()
    {
        var tone = Sine(1000).Render(0.25, 48_000);

        Assert.Throws<AudioAssertionException>(() => AudioAssert.PeakFrequencyNear(tone, 1200, 5));
    }
}
