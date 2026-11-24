using System.Diagnostics;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Swaps that follow each other faster than the crossfade: each new fade starts from where the sounds have got to, so nothing is
/// cut off part of the way out. Constant levels make a click visible as a step between neighbouring frames.
/// </summary>
public class SlotOverlapTests
{
    private const int Rate = 48_000;

    private static float LargestStep(ReadOnlySpan<StereoFrame> frames, float previous)
    {
        float largest = 0;
        foreach (var frame in frames)
        {
            largest = Math.Max(largest, Math.Abs(frame.Left - previous));
            previous = frame.Left;
        }
        return largest;
    }

    private static StereoFrame[] Run(IOperator op, int blocks, int blockSize = 256)
    {
        var frames = new StereoFrame[blocks * blockSize];
        for (int b = 0; b < blocks; b++)
            op.RenderBlock(frames.AsSpan(b * blockSize, blockSize));
        return frames;
    }

    [Fact]
    public void A_swap_during_a_fade_does_not_cut_the_sound_that_is_fading_out()
    {
        var slot = new Slot(Constant(0.5), fadeSeconds: 0.1);
        var op = slot.Sound.Compile(new RenderContext(Rate));
        Run(op, 2);

        slot.Swap(Constant(-0.5));
        var first = Run(op, 9);      // about halfway through the 100 ms fade
        slot.Swap(Constant(0.25));
        var second = Run(op, 40);

        float step = Math.Max(LargestStep(first, 0.5f), LargestStep(second, first[^1].Left));
        Assert.True(step < 0.01f, $"largest step {step}");
        Assert.Equal(0.25f, second[^1].Left, 4);
    }

    [Fact]
    public void Ten_swaps_five_milliseconds_apart_never_click_and_end_on_the_last()
    {
        var slot = new Slot(Constant(0), fadeSeconds: 0.02);
        var op = slot.Sound.Compile(new RenderContext(Rate, 240));   // 240 frames is 5 ms
        float previous = 0, step = 0;
        for (int i = 0; i < 10; i++)
        {
            slot.Swap(Constant(i % 2 == 0 ? 0.8 : -0.8));
            var block = Run(op, 1, 240);
            step = Math.Max(step, LargestStep(block, previous));
            previous = block[^1].Left;
        }

        var tail = Run(op, 20, 240);
        step = Math.Max(step, LargestStep(tail, previous));

        Assert.True(step < 0.05f, $"largest step {step}");
        Assert.Equal(-0.8f, tail[^1].Left, 4);
    }

    [Fact]
    public void A_burst_of_swaps_in_one_block_each_keeps_the_output_continuous()
    {
        // More swaps than there are places for fading sounds: the oldest are hurried out rather than cut.
        var slot = new Slot(Constant(0.5), fadeSeconds: 0.05);
        var op = slot.Sound.Compile(new RenderContext(Rate, 64));
        float previous = 0.5f, step = 0;
        Run(op, 1, 64);
        for (int i = 0; i < 30; i++)
        {
            slot.Swap(Constant(0.5));
            var block = Run(op, 1, 64);
            step = Math.Max(step, LargestStep(block, previous));
            previous = block[^1].Left;
        }

        Assert.True(step < 0.05f, $"largest step {step}");
    }

    [Fact]
    public void Play_right_after_Stop_fades_from_where_the_stop_had_got_to()
    {
        var output = new SoundContext(Rate, fadeSeconds: 0.05);
        output.Play(Constant(0.6), 0);
        var frames = new StereoFrame[256];
        output.Render(frames);

        output.Stop(0.08);
        output.Render(frames);       // 5 ms into an 80 ms fade to silence
        float previous = frames[^1].Left;
        output.Play(Constant(-0.6));
        var after = new StereoFrame[48_00];
        output.Render(after);

        float step = LargestStep(after, previous);
        Assert.True(step < 0.01f, $"largest step {step}");
        Assert.Equal(-0.6f, after[^1].Left, 4);
    }

    [Fact]
    public void An_equal_power_swap_of_a_constant_for_itself_rises_and_a_linear_one_does_not()
    {
        static float Peak(FadeCurve curve)
        {
            var slot = new Slot(Constant(0.5), fadeSeconds: 0.05) { FadeCurve = curve };
            var op = slot.Sound.Compile(new RenderContext(Rate));
            Run(op, 1);
            slot.Swap(Constant(0.5));
            return Run(op, 20).Max(f => f.Left);
        }

        Assert.True(Peak(FadeCurve.EqualPower) > 0.7f);
        Assert.Equal(0.5f, Peak(FadeCurve.Linear), 4);
    }

    [Fact]
    public void A_sound_context_passes_its_curve_to_every_play()
    {
        var output = new SoundContext(Rate, fadeSeconds: 0.05) { FadeCurve = FadeCurve.Linear };
        output.Play(Constant(0.5), 0);
        var frames = new StereoFrame[256];
        output.Render(frames);

        output.Play(Constant(0.5));
        var fade = new StereoFrame[4800];
        output.Render(fade);

        Assert.Equal(FadeCurve.Linear, output.FadeCurve);
        Assert.All(fade, f => Assert.Equal(0.5f, f.Left, 4));
    }

    [Fact]
    public void Swapping_during_a_fade_allocates_nothing_on_the_audio_thread()
    {
        var slot = new Slot(Sine(220), fadeSeconds: 0.05);
        var op = slot.Sound.Compile(new RenderContext(Rate));
        var block = new StereoFrame[256];
        op.RenderBlock(block);

        AllocationAssert.NoneOf(() =>
        {
            slot.Swap(Saw(330));        // compiles and allocates on this thread: not counted
            long before = GC.GetAllocatedBytesForCurrentThread();
            op.RenderBlock(block);
            op.RenderBlock(block);
            long after = GC.GetAllocatedBytesForCurrentThread();
            return after - before;
        });
    }

    // A sound whose compile waits until the test lets it go.
    private sealed class SlowCompile
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public Sound Sound(float level) =>
            Sigil.Sound.Sound.FromOperator(_ =>
            {
                Started.Set();
                Release.Wait(TimeSpan.FromSeconds(3));
                return Operator.From(block => block.Fill(StereoFrame.Mono(level)));
            }, "SlowCompile");
    }

    [Fact]
    public async Task Asking_what_a_slot_plays_does_not_wait_for_a_render_that_is_compiling_it()
    {
        var slow = new SlowCompile();
        var slot = new Slot(slow.Sound(0.3f));

        var attach = Task.Run(() => slot.Sound.Compile(new RenderContext(Rate)));
        Assert.True(slow.Started.Wait(TimeSpan.FromSeconds(3)), "the render never started compiling");

        var watch = Stopwatch.StartNew();
        _ = slot.Current;
        watch.Stop();
        slow.Release.Set();
        await attach.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(watch.ElapsedMilliseconds < 500, $"Current waited {watch.ElapsedMilliseconds} ms for the compile.");
    }

    [Fact]
    public async Task A_swap_made_while_a_render_compiles_the_slot_is_not_lost()
    {
        var slow = new SlowCompile();
        var slot = new Slot(slow.Sound(0.3f), fadeSeconds: 0.005);

        var attach = Task.Run(() => slot.Sound.Compile(new RenderContext(Rate)));
        Assert.True(slow.Started.Wait(TimeSpan.FromSeconds(3)));
        slot.Swap(Constant(0.9));    // the running render is not registered yet
        slow.Release.Set();
        var op = await attach.WaitAsync(TimeSpan.FromSeconds(5));

        var frames = Run(op, 10);
        Assert.Equal(0.9f, frames[^1].Left, 4);
    }
}
