using Sigil.Sound.Operators;
using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class ConvolutionTests
{
    private const int Rate = Measure.SampleRate;

    // --- helpers ---------------------------------------------------------------------------------------------------------

    private static float[] Taps(int length, int seed, float scale = 1f)
    {
        var random = new Random(seed);
        var values = new float[length];
        for (int i = 0; i < length; i++)
            values[i] = (float)(random.NextDouble() * 2 - 1) * scale;
        return values;
    }

    /// <summary>The definition of convolution, in double precision: y[n] = sum over k of h[k] * x[n - k].</summary>
    private static double[] Direct(float[] x, float[] h)
    {
        var y = new double[x.Length];
        for (int n = 0; n < x.Length; n++)
        {
            double sum = 0;
            int top = Math.Min(n, h.Length - 1);
            for (int k = 0; k <= top; k++)
                sum += (double)h[k] * x[n - k];
            y[n] = sum;
        }
        return y;
    }

    /// <summary>Runs the engine over a signal in blocks of unequal, awkward sizes.</summary>
    private static (float[] Left, float[] Right) Run(ConvolutionEngine engine, float[] inL, float[] inR, int[]? sizes = null)
    {
        sizes ??= [64, 1, 257, 512, 1000, 33, 128];
        var outL = new float[inL.Length];
        var outR = new float[inL.Length];
        for (int offset = 0, i = 0; offset < inL.Length; i++)
        {
            int n = Math.Min(sizes[i % sizes.Length], inL.Length - offset);
            engine.Process(inL.AsSpan(offset, n), inR.AsSpan(offset, n), outL.AsSpan(offset, n), outR.AsSpan(offset, n));
            offset += n;
        }
        return (outL, outR);
    }

    private static double MaxError(float[] actual, double[] expected)
    {
        double worst = 0;
        for (int i = 0; i < actual.Length; i++)
            worst = Math.Max(worst, Math.Abs(actual[i] - expected[i]));
        return worst;
    }

    /// <summary>A sound that is silent except for one frame of 1 at the given position.</summary>
    private static Sound Click(int position = 0)
    {
        return Sound.FromOperator(_ =>
        {
            long at = 0;
            return Operator.From(block =>
            {
                for (int i = 0; i < block.Length; i++)
                    block[i] = at++ == position ? StereoFrame.Mono(1f) : StereoFrame.Silence;
            }, () => at = 0);
        }, "Click");
    }

    private static AudioBuffer Buffer(float[] left, float[]? right = null, int rate = Rate)
    {
        right ??= left;
        var frames = new StereoFrame[left.Length];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = new StereoFrame(left[i], right[i]);
        return new AudioBuffer(frames, rate);
    }

    // --- the engine against the definition --------------------------------------------------------------------------------

    // One length on each side of every boundary of the partition plan: head only, then each FFT stage in turn.
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(600)]
    [InlineData(1100)]
    [InlineData(5000)]
    [InlineData(17000)]
    [InlineData(40000)]
    public void The_engine_matches_a_direct_convolution_for_a_stereo_response(int taps)
    {
        float scale = 1f / MathF.Sqrt(taps);
        float[] hL = Taps(taps, 1, scale), hR = Taps(taps, 2, scale);
        float[] xL = Taps(12_000, 3), xR = Taps(12_000, 4);

        var engine = new ConvolutionEngine(new ConvolutionKernel(hL, hR));
        var (yL, yR) = Run(engine, xL, xR);

        Assert.True(MaxError(yL, Direct(xL, hL)) < 1e-3, "left channel");
        Assert.True(MaxError(yR, Direct(xR, hR)) < 1e-3, "right channel");
    }

    [Theory]
    [InlineData(50)]
    [InlineData(1100)]
    [InlineData(17000)]
    public void The_engine_matches_a_direct_convolution_for_a_mono_response(int taps)
    {
        var h = Taps(taps, 5, 1f / MathF.Sqrt(taps));
        float[] xL = Taps(12_000, 6), xR = Taps(12_000, 7);

        var engine = new ConvolutionEngine(new ConvolutionKernel(h, h));
        var (yL, yR) = Run(engine, xL, xR);

        Assert.True(MaxError(yL, Direct(xL, h)) < 1e-3, "left channel");
        Assert.True(MaxError(yR, Direct(xR, h)) < 1e-3, "right channel");
    }

    [Fact]
    public void The_result_does_not_depend_on_how_the_signal_is_cut_into_blocks()
    {
        var h = Taps(9000, 8, 0.01f);
        float[] xL = Taps(15_000, 9), xR = Taps(15_000, 10);

        var a = Run(new ConvolutionEngine(new ConvolutionKernel(h, h)), xL, xR, [256]);
        var b = Run(new ConvolutionEngine(new ConvolutionKernel(h, h)), xL, xR, [7, 1, 500, 64]);

        for (int i = 0; i < xL.Length; i++)
            Assert.Equal(a.Left[i], b.Left[i], 1e-5f);
    }

    [Fact]
    public void A_delta_answers_on_the_same_frame_so_the_convolver_adds_no_latency()
    {
        var delta = new float[4000];
        delta[0] = 1;
        var x = Taps(8000, 11);

        var (y, _) = Run(new ConvolutionEngine(new ConvolutionKernel(delta, delta)), x, x);

        for (int i = 0; i < x.Length; i++)
            Assert.Equal(x[i], y[i], 1e-5f);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(3000)]
    [InlineData(25_000)]
    public void A_delayed_delta_delays_the_signal_by_exactly_that_many_frames(int delay)
    {
        var h = new float[delay + 1];
        h[delay] = 1;
        var x = Taps(delay + 4000, 12);

        var (y, _) = Run(new ConvolutionEngine(new ConvolutionKernel(h, h)), x, x);

        for (int i = 0; i < delay; i++)
            Assert.True(Math.Abs(y[i]) < 1e-5f, $"output before the delay at {i}");
        for (int i = delay; i < x.Length; i++)
            Assert.Equal(x[i - delay], y[i], 1e-4f);
    }

    [Fact]
    public void Reset_clears_the_tail_so_a_second_run_repeats_the_first()
    {
        var h = Taps(6000, 13, 0.02f);
        var x = Taps(9000, 14);
        var engine = new ConvolutionEngine(new ConvolutionKernel(h, h));

        var first = Run(engine, x, x);
        Run(engine, x, x);   // leaves a tail in every stage
        engine.Reset();
        var again = Run(engine, x, x);

        Assert.Equal(first.Left, again.Left);
    }

    [Fact]
    public void Engines_sharing_a_kernel_do_not_disturb_each_other()
    {
        var h = Taps(5000, 15, 0.02f);
        var kernel = new ConvolutionKernel(h, h);
        float[] x1 = Taps(9000, 16), x2 = Taps(9000, 17);

        var alone = Run(new ConvolutionEngine(kernel), x1, x1);
        var a = new ConvolutionEngine(kernel);
        var b = new ConvolutionEngine(kernel);
        Run(b, x2, x2);
        var withNeighbour = Run(a, x1, x1);

        Assert.Equal(alone.Left, withNeighbour.Left);
    }

    [Fact]
    public void The_plan_covers_every_tap_exactly_once()
    {
        foreach (int taps in new[] { 1, 127, 128, 129, 1023, 1024, 1025, 4097, 16_384, 16_500, 100_000, 480_000 })
        {
            var plan = PartitionPlan.Create(taps);
            int covered = plan.HeadTaps;
            foreach (var stage in plan.Stages)
            {
                Assert.Equal(covered, stage.Start);
                covered += stage.Length;
            }
            Assert.Equal(taps, covered);
        }
    }

    // --- Sound.Convolve ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_click_through_a_response_gives_the_response_back()
    {
        var ir = Taps(5000, 18, 0.1f);

        var result = Click().Convolve(Buffer(ir), normalize: IrNormalize.Off).Render(0.2, Rate).Left();

        for (int i = 0; i < ir.Length; i++)
            Assert.Equal(ir[i], result[i], 1e-5f);
        AudioAssert.IsSilent(result.AsSpan(ir.Length), 1e-5f);
    }

    [Fact]
    public void A_stereo_response_keeps_each_side_to_its_own_channel()
    {
        float[] left = Taps(2000, 19, 0.1f), right = Taps(2000, 20, 0.1f);

        var audio = Click().Convolve(Buffer(left, right), normalize: IrNormalize.Off).Render(0.1, Rate);

        Assert.Equal(left, audio.Left().AsSpan(0, left.Length).ToArray(), (a, b) => Math.Abs(a - b) < 1e-5f);
        Assert.Equal(right, audio.Right().AsSpan(0, right.Length).ToArray(), (a, b) => Math.Abs(a - b) < 1e-5f);
    }

    [Fact]
    public void Peak_normalisation_makes_the_largest_tap_one()
    {
        var ir = Taps(3000, 21, 0.5f);

        var result = Click().Convolve(Buffer(ir), normalize: IrNormalize.Peak).Render(0.1, Rate).Left();

        Assert.Equal(1f, result.Max(Math.Abs), 1e-4f);
    }

    [Theory]
    [InlineData(0.002f)]
    [InlineData(0.5f)]
    public void Energy_normalisation_passes_noise_at_the_level_it_came_in(float level)
    {
        // A loud response and a quiet one end up at the same level, because the scale follows the energy.
        var ir = Taps(8000, 22, level);

        var result = (Noise(seed: 1) * 0.6).Convolve(Buffer(ir)).Render(2, Rate).Left();

        // Uniform noise of amplitude 0.6 has an RMS of 0.346, and unity power gain keeps it there.
        Assert.InRange(SignalAnalysis.Rms(result.AsSpan(Rate / 2)), 0.9 * 0.346, 1.1 * 0.346);
    }

    [Fact]
    public void Off_leaves_the_response_at_its_own_level()
    {
        var ir = Taps(3000, 23, 0.01f);

        var result = Click().Convolve(Buffer(ir), normalize: IrNormalize.Off).Render(0.1, Rate).Left();

        Assert.True(result.Max(Math.Abs) < 0.011f);
    }

    [Fact]
    public void Mix_blends_the_dry_sound_with_the_convolved_sound()
    {
        var ir = new float[100];
        ir[40] = 1;   // a plain 40-frame delay
        var dry = Click(10);

        var half = dry.Convolve(Buffer(ir), mix: 0.5, normalize: IrNormalize.Off).Render(0.05, Rate).Left();

        Assert.Equal(0.5f, half[10], 1e-5f);
        Assert.Equal(0.5f, half[50], 1e-5f);
        Assert.Equal(0f, half[30], 1e-5f);

        var onlyDry = dry.Convolve(Buffer(ir), mix: 0, normalize: IrNormalize.Off).Render(0.05, Rate).Left();
        Assert.Equal(1f, onlyDry[10], 1e-5f);
        Assert.Equal(0f, onlyDry[50], 1e-5f);
    }

    [Fact]
    public void A_response_at_another_sample_rate_is_converted_to_the_rate_of_the_render()
    {
        // 1 kHz tone burst recorded at 24 kHz; played at 48 kHz it must still sound 1 kHz, not 2 kHz.
        var ir = new float[2400];
        for (int i = 0; i < ir.Length; i++)
            ir[i] = (float)(Math.Sin(2 * Math.PI * 1000 * i / 24_000) * Math.Sin(Math.PI * i / ir.Length));

        var audio = Click().Convolve(Buffer(ir, rate: 24_000), normalize: IrNormalize.Off).Render(0.3, Rate);

        AudioAssert.PeakFrequencyNear(audio, 1000, 30);
        // The burst lasts 100 ms at either rate.
        var mono = audio.ToMono();
        Assert.True(SignalAnalysis.Rms(mono.AsSpan(4400, 400)) > 0.01);
        AudioAssert.IsSilent(mono.AsSpan((int)(0.12 * Rate)), 1e-3f);
    }

    [Fact]
    public void A_response_longer_than_ten_seconds_is_cut_and_ends_without_a_click()
    {
        // 12 s at 1 kHz is 12,000 frames; the cap is 10 s, then it is converted to 48 kHz.
        var ir = Enumerable.Repeat(0.001f, 12_000).ToArray();

        var result = Click().Convolve(Buffer(ir, rate: 1000), normalize: IrNormalize.Off).Render(11, Rate).Left();

        Assert.True(result[(int)(9.0 * Rate)] > 0.0005f, "still sounding at 9 s");
        AudioAssert.IsSilent(result.AsSpan((int)(10.1 * Rate)), 1e-6f);
        Assert.True(result[(int)(9.99 * Rate)] < result[(int)(9.0 * Rate)], "fades out at the cut");
    }

    [Fact]
    public void A_nan_in_the_input_does_not_poison_the_tail()
    {
        var ir = Taps(4000, 24, 0.02f);
        var poisoned = Sound.FromOperator(_ =>
        {
            long at = 0;
            return Operator.From(block =>
            {
                for (int i = 0; i < block.Length; i++, at++)
                    block[i] = at == 100 ? StereoFrame.Mono(float.NaN) : StereoFrame.Mono(0.1f);
            });
        });

        var audio = poisoned.Convolve(Buffer(ir)).Render(0.5, Rate);

        AudioAssert.IsFinite(audio);
    }

    [Fact]
    public void A_compiled_sound_resets_to_the_start()
    {
        var ir = Taps(6000, 25, 0.02f);
        var op = Saw(220).Convolve(Buffer(ir)).Compile(new RenderContext(Rate));

        var first = Measure.Pull(op, 4096);
        Measure.Pull(op, 4096);
        ((IResettable)op).Reset();
        var again = Measure.Pull(op, 4096);

        Assert.Equal(first, again);
    }

    [Fact]
    public void Every_compile_shares_one_prepared_response()
    {
        var source = new ImpulseResponseSource(Buffer(Taps(20_000, 26, 0.01f)), IrNormalize.Energy);

        Assert.Same(source.KernelFor(Rate), source.KernelFor(Rate));
        Assert.NotSame(source.KernelFor(Rate), source.KernelFor(44_100));
    }

    [Fact]
    public void The_response_is_copied_so_changing_the_buffer_afterwards_changes_nothing()
    {
        var frames = new StereoFrame[1000];
        frames[0] = StereoFrame.Mono(1f);
        var sound = Click().Convolve(new AudioBuffer(frames, Rate), normalize: IrNormalize.Off);

        frames[0] = StereoFrame.Mono(0f);
        frames[5] = StereoFrame.Mono(1f);

        var result = sound.Render(0.05, Rate).Left();
        Assert.Equal(1f, result[0], 1e-5f);
        Assert.Equal(0f, result[5], 1e-5f);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        var ok = Buffer(Taps(100, 27));

        Assert.Throws<ArgumentNullException>(() => Saw(110).Convolve(null!));
        Assert.Throws<ArgumentException>(() => Saw(110).Convolve(new AudioBuffer([], Rate)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Convolve(ok, normalize: (IrNormalize)99));
    }
}
