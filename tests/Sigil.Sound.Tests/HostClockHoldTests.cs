using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// A host holds its clock while its transport is paused or stopped: beats stand still, samples keep counting, and patterns start nothing
/// new and carry on exactly where they were when the clock runs again.
/// </summary>
public class HostClockHoldTests
{
    private const int Rate = 48_000;
    private const int Block = 256;

    private static Sound Gate(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    private static float[] RenderBlocks(IOperator op, HostClock clock, int blocks)
    {
        var all = new List<float>();
        var block = new StereoFrame[Block];
        for (int b = 0; b < blocks; b++)
        {
            op.RenderBlock(block);
            clock.Advance(Block);
            all.AddRange(block.Select(f => f.ToMono()));
        }
        return all.ToArray();
    }

    [Fact]
    public void A_held_clock_counts_samples_but_not_beats()
    {
        var clock = new HostClock(Rate, bpm: 120);
        clock.Advance(Rate); // 2 beats

        clock.Running = false;
        clock.Advance(Rate);

        Assert.Equal(2.0, clock.Beats, 9);
        Assert.Equal(2 * Rate, clock.SamplePosition);

        clock.Running = true;
        clock.Advance(Rate);
        Assert.Equal(4.0, clock.Beats, 9);
    }

    [Fact]
    public void Pattern_play_starts_nothing_while_the_clock_is_held()
    {
        var clock = new HostClock(Rate, bpm: 120) { Running = false };
        var op = Mini.Notes("c3:1 e3:1 g3:1 b3:1").Play(Gate).Compile(new RenderContext(Rate, Block) { Clock = clock });

        var held = RenderBlocks(op, clock, 100);

        Assert.All(held, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Pattern_play_resumes_exactly_where_it_was_held()
    {
        var pattern = Mini.Notes("c3:1 ~ e3:1 ~");

        var referenceClock = new HostClock(Rate, bpm: 120);
        var reference = RenderBlocks(pattern.Play(Gate).Compile(new RenderContext(Rate, Block) { Clock = referenceClock }), referenceClock, 400);

        // Same pattern, held after 100 blocks (the first note has ended by then) for 50 blocks.
        var clock = new HostClock(Rate, bpm: 120);
        var op = pattern.Play(Gate).Compile(new RenderContext(Rate, Block) { Clock = clock });
        var before = RenderBlocks(op, clock, 100);
        clock.Running = false;
        var during = RenderBlocks(op, clock, 50);
        clock.Running = true;
        var after = RenderBlocks(op, clock, 300);

        Assert.Equal(reference[..(100 * Block)], before);
        Assert.All(during, s => Assert.Equal(0f, s));
        Assert.Equal(reference[(100 * Block)..], after);
    }

    [Fact]
    public void A_held_clock_is_not_a_seek_a_seek_while_held_plays_from_the_new_beat()
    {
        var clock = new HostClock(Rate, bpm: 120);
        var op = Mini.Notes("c3:1 ~ ~ ~").Play(Gate).Compile(new RenderContext(Rate, Block) { Clock = clock });
        RenderBlocks(op, clock, 100); // the first note (one beat) has ended by now

        clock.Running = false;
        clock.Seek(clock.SamplePosition, beats: 8); // the start of a later cycle
        RenderBlocks(op, clock, 5);
        clock.Running = true;
        var after = RenderBlocks(op, clock, 4);

        Assert.Equal(1f, after[0]);
    }

    [Fact]
    public void A_one_shot_pattern_follows_the_hold_too()
    {
        var clock = new HostClock(Rate, bpm: 120) { Running = false };
        var pattern = Notes.Parse("C3 E3").Every(Beats.Quarter).Loop();
        var op = pattern.Play(Gate).Compile(new RenderContext(Rate, Block) { Clock = clock });

        var held = RenderBlocks(op, clock, 50);
        clock.Running = true;
        var running = RenderBlocks(op, clock, 50);

        Assert.All(held, s => Assert.Equal(0f, s));
        Assert.Contains(running, s => s > 0.5f);
    }
}
