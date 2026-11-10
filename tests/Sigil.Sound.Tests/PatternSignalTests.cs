namespace Sigil.Sound.Tests;

public class PatternSignalTests
{
    private const int Rate = 48_000;

    // 240 bpm makes a cycle exactly one second, so step times are easy to read.
    private static float[] Render(Pattern<double> pattern, double seconds, int blockSize = 256, double smooth = 0, double initial = 0) =>
        pattern.ToSignal(bpm: 240, smoothSeconds: smooth, initial: initial).Render(seconds, Rate, blockSize).ToMono();

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * Rate)];

    [Fact]
    public void Each_value_holds_for_its_step_and_the_pattern_repeats()
    {
        var samples = Render(Mini.Numbers("100 200"), 2.0);

        Assert.Equal(100f, At(samples, 0.25));
        Assert.Equal(200f, At(samples, 0.75));
        Assert.Equal(100f, At(samples, 1.25));
        Assert.Equal(200f, At(samples, 1.75));
    }

    [Fact]
    public void A_step_changes_exactly_on_its_frame()
    {
        var samples = Render(Mini.Numbers("100 200"), 1.0);

        Assert.Equal(100f, samples[Rate / 2 - 1]);
        Assert.Equal(200f, samples[Rate / 2]);
    }

    [Fact]
    public void A_rest_holds_the_previous_value()
    {
        var samples = Render(Mini.Numbers("100 ~"), 1.0);

        Assert.Equal(100f, At(samples, 0.75));
    }

    [Fact]
    public void Before_the_first_value_the_initial_value_shows()
    {
        var samples = Render(Mini.Numbers("~ 200"), 1.0, initial: 7);

        Assert.Equal(7f, At(samples, 0.25));
        Assert.Equal(200f, At(samples, 0.75));
    }

    [Fact]
    public void The_result_does_not_depend_on_the_block_size()
    {
        var pattern = Mini.Numbers("100 [200 300]*3 <400 500>(3,8)").Degrade(0.2, 1);

        Assert.Equal(Render(pattern, 3.0, blockSize: 512), Render(pattern, 3.0, blockSize: 37));
    }

    [Fact]
    public void Smoothing_glides_between_values()
    {
        var samples = Render(Mini.Numbers("0 100"), 1.0, smooth: 0.05);

        Assert.Equal(0f, At(samples, 0.25));
        Assert.InRange(samples[Rate / 2 + (int)(0.05 * Rate)], 62f, 65f);
        Assert.InRange(At(samples, 0.99), 99f, 100f);
        for (int i = Rate / 2 + 1; i < Rate; i++)
            Assert.True(samples[i] >= samples[i - 1]);
    }

    [Fact]
    public void Following_a_host_clock_tracks_its_position_and_tempo()
    {
        var clock = new HostClock(Rate, bpm: 120); // a cycle is 2 seconds
        var op = Mini.Numbers("<100 200>").ToSignal().Compile(new RenderContext(Rate) { Clock = clock });
        var block = new StereoFrame[256];

        op.RenderBlock(block);
        Assert.Equal(100f, block[0].Left);

        clock.Seek(0, beats: 4); // the start of cycle 1
        op.RenderBlock(block);
        Assert.Equal(200f, block[0].Left);

        clock.Seek(0, beats: 8);
        op.RenderBlock(block);
        Assert.Equal(100f, block[0].Left);
    }

    [Fact]
    public void An_explicit_tempo_ignores_the_clock_position()
    {
        var clock = new HostClock(Rate, bpm: 120);
        clock.Seek(0, beats: 4);
        var op = Mini.Numbers("<100 200>").ToSignal(bpm: 120).Compile(new RenderContext(Rate) { Clock = clock });
        var block = new StereoFrame[256];

        op.RenderBlock(block);

        Assert.Equal(100f, block[0].Left);
    }

    [Fact]
    public void It_drives_a_filter_cutoff()
    {
        var cutoff = Mini.Numbers("200 12000").ToSignal(bpm: 240, smoothSeconds: 0.005);
        var audio = Sounds.Saw(110).LowPass(cutoff).Render(1.0, Rate).ToMono();

        double dark = Rough(audio[(Rate / 8)..(Rate * 3 / 8)]);
        double bright = Rough(audio[(Rate * 5 / 8)..(Rate * 7 / 8)]);

        Assert.True(bright > dark * 3, $"dark {dark}, bright {bright}");
    }

    private static double Rough(float[] samples)
    {
        var diff = new float[samples.Length - 1];
        for (int i = 1; i < samples.Length; i++)
            diff[i - 1] = samples[i] - samples[i - 1];
        return Measure.Rms(diff);
    }

    [Fact]
    public void Rendering_allocates_nothing_in_steady_state()
    {
        var op = Mini.Numbers("100 [200 300]*3 <400 500>(3,8)").Degrade(0.2).ToSignal(bpm: 240, smoothSeconds: 0.01).Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        for (int i = 0; i < 500; i++) op.RenderBlock(block);

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 1000; i++) op.RenderBlock(block);
        });
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        var numbers = Mini.Numbers("1");

        Assert.Throws<ArgumentOutOfRangeException>(() => numbers.ToSignal(bpm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => numbers.ToSignal(smoothSeconds: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => numbers.ToSignal(initial: double.NaN));
    }
}
