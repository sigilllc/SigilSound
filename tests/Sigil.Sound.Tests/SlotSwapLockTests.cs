using System.Diagnostics;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// <c>Swap</c> compiles the new sound on the calling thread, which can take a while (a long impulse response, a big graph).
/// Nobody else who asks the slot a question should wait for that.
/// </summary>
public class SlotSwapLockTests
{
    private const int Rate = 48_000;

    // A sound whose compile stops until the test lets it go, so the test can look at the slot while a swap is mid-compile.
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
    public async Task Asking_what_a_slot_plays_does_not_wait_for_a_swap_that_is_compiling()
    {
        var slot = new Slot(Constant(0.1));
        var running = slot.Sound.Compile(new RenderContext(Rate));
        var slow = new SlowCompile();
        var next = slow.Sound(0.5f);

        var swap = Task.Run(() => slot.Swap(next));
        Assert.True(slow.Started.Wait(TimeSpan.FromSeconds(3)), "the swap never started compiling");

        var watch = Stopwatch.StartNew();
        var current = slot.Current;
        watch.Stop();
        slow.Release.Set();
        await swap.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(next, current);
        Assert.True(watch.ElapsedMilliseconds < 500, $"Current waited {watch.ElapsedMilliseconds} ms for the compile.");
        GC.KeepAlive(running);
    }

    [Fact]
    public async Task Two_swaps_in_a_row_leave_the_slot_playing_the_second()
    {
        var slot = new Slot(Constant(0.1), fadeSeconds: 0.005);
        var op = slot.Sound.Compile(new RenderContext(Rate));
        var slow = new SlowCompile();

        var first = Task.Run(() => slot.Swap(slow.Sound(0.25f)));
        Assert.True(slow.Started.Wait(TimeSpan.FromSeconds(3)));
        var second = Task.Run(() => slot.Swap(Constant(0.75)));
        Thread.Sleep(50);   // the second swap is waiting for the first to finish compiling
        slow.Release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        var block = new StereoFrame[256];
        for (int i = 0; i < 20; i++)
            op.RenderBlock(block);

        Assert.Equal(0.75f, block[^1].Left, 4);
    }
}
