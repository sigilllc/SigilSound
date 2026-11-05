using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class ControlTests
{
    // Frame-to-frame change: grows with high-frequency content, which a low cutoff removes from a saw.
    private static double Roughness(StereoFrame[] frames)
    {
        var diff = new float[frames.Length - 1];
        for (int i = 1; i < frames.Length; i++)
            diff[i - 1] = frames[i].Left - frames[i - 1].Left;
        return Measure.Rms(diff);
    }

    private static float Last(StereoFrame[] frames) => frames[^1].Left;

    [Fact]
    public void A_control_renders_its_starting_value()
    {
        var control = new Control(0.25, 0, 1);

        var samples = Measure.Mono(control, 0.05);

        Assert.All(samples, s => Assert.Equal(0.25f, s));
    }

    [Fact]
    public void Value_is_clamped_to_the_range_and_rejects_non_numbers()
    {
        var control = new Control(5, min: 1, max: 10);

        control.Value = 99;
        Assert.Equal(10, control.Value);
        control.Value = -4;
        Assert.Equal(1, control.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Value = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.Value = double.PositiveInfinity);
        Assert.Equal(1, control.Value);
    }

    [Fact]
    public void Constructor_rejects_a_bad_range_or_glide()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Control(0, min: 1, max: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Control(0, slewSeconds: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Control(double.NaN));
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void A_step_covers_63_percent_after_one_glide_time(int sampleRate)
    {
        var control = new Control(0, 0, 1, slewSeconds: 0.05);
        var op = ((Sound)control).Compile(new RenderContext(sampleRate));
        Measure.Pull(op, sampleRate / 10);

        control.Value = 1;
        var frames = Measure.Pull(op, (int)(0.05 * sampleRate));

        Assert.InRange(Last(frames), 0.62f, 0.645f);
    }

    [Fact]
    public void Zero_glide_follows_on_the_next_block()
    {
        var control = new Control(0, 0, 1, slewSeconds: 0);
        var op = ((Sound)control).Compile(RenderContext.Default);
        Measure.Pull(op, 256);

        control.Value = 1;

        Assert.Equal(1f, Measure.Pull(op, 256)[0].Left);
    }

    [Fact]
    public void A_glide_has_no_step_between_neighbouring_frames()
    {
        var smooth = new Control(0, 0, 1, slewSeconds: 0.01);
        var op = ((Sound)smooth).Compile(RenderContext.Default);
        Measure.Pull(op, 512);
        smooth.Value = 1;

        var frames = Measure.Pull(op, 4800);

        float biggestStep = 0;
        for (int i = 1; i < frames.Length; i++)
            biggestStep = Math.Max(biggestStep, Math.Abs(frames[i].Left - frames[i - 1].Left));
        Assert.True(biggestStep < 0.01f, $"step of {biggestStep}");
    }

    [Fact]
    public void A_graph_compiled_later_starts_at_the_current_value()
    {
        var control = new Control(0, 0, 1, slewSeconds: 1);
        control.Value = 0.8;

        var first = Measure.Pull(((Sound)control).Compile(RenderContext.Default), 256)[0].Left;

        Assert.Equal(0.8f, first, 5);
    }

    [Fact]
    public void One_control_drives_several_graphs()
    {
        var control = new Control(0, 0, 1, slewSeconds: 0);
        var a = ((Sound)control).Compile(RenderContext.Default);
        var b = (((Sound)control) * 2).Compile(RenderContext.Default);

        control.Value = 0.5;

        Assert.Equal(0.5f, Measure.Pull(a, 256)[^1].Left);
        Assert.Equal(1f, Measure.Pull(b, 256)[^1].Left);
    }

    [Fact]
    public void Linear_map_scales_the_range()
    {
        var control = new Control(0.5, 0, 1, slewSeconds: 0);

        var value = Measure.Mono(Constant(0) + control.Map(100, 300), 0.01);

        Assert.Equal(200f, value[^1], 3);
    }

    [Fact]
    public void Exponential_map_gives_equal_ratios()
    {
        var control = new Control(0.5, 0, 1, slewSeconds: 0);

        var middle = Measure.Mono(Constant(0) + control.Map(200, 3200, ControlCurve.Exponential), 0.01)[^1];

        Assert.Equal(800f, middle, 1);
    }

    [Fact]
    public void Exponential_map_needs_positive_output_ends()
    {
        var control = new Control();

        Assert.Throws<ArgumentOutOfRangeException>(() => control.Map(0, 100, ControlCurve.Exponential));
    }

    [Fact]
    public void Arithmetic_scales_and_offsets_the_control()
    {
        var rpm = new Control(3000, 0, 7000, slewSeconds: 0);

        Assert.Equal(100f, Measure.Mono(rpm / 30, 0.01)[^1], 2);
        Assert.Equal(6000f, Measure.Mono(rpm * 2, 0.01)[^1], 1);
        Assert.Equal(3010f, Measure.Mono(rpm + 10, 0.01)[^1], 1);
        Assert.Equal(2990f, Measure.Mono(rpm - 10, 0.01)[^1], 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rpm / 0);
    }

    [Fact]
    public void A_control_can_modulate_a_filter_cutoff()
    {
        var cutoff = new Control(200, 20, 18_000, slewSeconds: 0);
        var op = Saw(110).LowPass(cutoff).Compile(RenderContext.Default);
        double dark = Roughness(Measure.Pull(op, 4800));

        cutoff.Value = 12_000;
        Measure.Pull(op, 1024);
        double bright = Roughness(Measure.Pull(op, 4800));

        Assert.True(bright > dark * 3, $"dark {dark}, bright {bright}");
    }

    [Fact]
    public async Task Setting_from_another_thread_while_rendering_never_tears_or_leaves_the_range()
    {
        var control = new Control(0, -1, 1, slewSeconds: 0.001);
        var op = ((Sound)control).Compile(RenderContext.Default);
        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
                control.Value = (i & 1) == 0 ? 5 : -5;
        });

        for (int block = 0; block < 2000; block++)
            foreach (var frame in Measure.Pull(op, 256))
                Assert.InRange(frame.Left, -1f, 1f);

        stop.Cancel();
        await writer;
    }

    [Fact]
    public void Rendering_a_moving_control_allocates_nothing()
    {
        var control = new Control(0, 0, 1, slewSeconds: 0.02);
        var op = (Saw(110).LowPass(control.Map(200, 4000, ControlCurve.Exponential)) * control).Compile(RenderContext.Default);
        var buffer = new StereoFrame[256];
        for (int i = 0; i < 50; i++)
            op.RenderBlock(buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            control.Value = (i & 31) / 31.0;
            op.RenderBlock(buffer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}

public class ControlRateFnTests
{
    [Fact]
    public void Control_rate_calls_the_function_about_once_per_millisecond()
    {
        int calls = 0;
        var fn = Fn(t => { calls++; return t; }, FunctionRate.PerMillisecond);

        fn.Render(1.0, Measure.SampleRate);

        Assert.InRange(calls, 990, 1020);
    }

    [Fact]
    public void Control_rate_follows_a_straight_line_exactly()
    {
        var samples = Measure.Mono(Fn(t => 2 * t, FunctionRate.PerMillisecond), 0.1);

        for (int i = 0; i < samples.Length; i++)
            Assert.Equal(2.0 * i / Measure.SampleRate, samples[i], 1e-5);
    }

    [Fact]
    public void Control_rate_does_not_depend_on_the_block_size()
    {
        var sound = Fn(t => Math.Sin(t * 40), FunctionRate.PerMillisecond);

        var small = sound.Render(0.2, Measure.SampleRate, blockSize: 37).ToMono();
        var large = sound.Render(0.2, Measure.SampleRate, blockSize: 512).ToMono();

        Assert.Equal(large, small);
    }

    [Fact]
    public void Per_sample_is_the_default_behaviour()
    {
        var a = Measure.Mono(Fn(t => Math.Sin(t * 900)), 0.05);
        var b = Measure.Mono(Fn(t => Math.Sin(t * 900), FunctionRate.PerSample), 0.05);

        Assert.Equal(a, b);
    }
}

public class ControlFoldingTests
{
    [Fact]
    public void Arithmetic_on_a_control_stays_one_parameter()
    {
        var rpm = new Control(3000, 0, 7000, slewSeconds: 0);

        Sound scaled = rpm / 30;

        Assert.NotNull(scaled.BackingParam);
        Assert.False(scaled.BackingParam!.Value.IsConstant);
    }

    [Fact]
    public void Converting_a_derived_control_to_a_param_does_not_render_it_as_audio()
    {
        var rpm = new Control(3000, 0, 7000, slewSeconds: 0);
        Param param = rpm * 2;

        var source = param.Compile(RenderContext.Default);

        Assert.Equal(6000f, source.Next(256)[^1]);
        Assert.IsType<ControlParamSource>(source);
    }

    [Fact]
    public void A_hard_balance_is_exactly_silent_on_the_far_side()
    {
        var source = Measure.Stereo(0.6f, 0.6f);

        Assert.All(source.Balance(1).Render(0.01, Measure.SampleRate).Left(), s => Assert.Equal(0f, s));
        Assert.All(source.Balance(-1).Render(0.01, Measure.SampleRate).Right(), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void An_exponential_glide_stays_between_its_ends_and_rises_steadily()
    {
        var control = new Control(0, 0, 1, slewSeconds: 0.05);
        var source = control.Map(200, 3200, ControlCurve.Exponential).Compile(RenderContext.Default);
        Assert.Equal(200f, source.Next(256)[^1], 1);
        control.Value = 1;

        float previous = 0;
        for (int block = 0; block < 300; block++)
        {
            foreach (float value in source.Next(256))
            {
                Assert.InRange(value, 199f, 3201f);
                Assert.True(value >= previous - 1e-3f);
                previous = value;
            }
        }

        Assert.Equal(3200f, previous, 0);
    }
}
