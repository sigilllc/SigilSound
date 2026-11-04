using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class HostClockTests
{
    private const int SampleRate = 48_000;
    private const int Block = 256;

    private static float[] Render(SoundContext output, int frames)
    {
        var buffer = new StereoFrame[frames];
        output.Render(buffer);
        return buffer.Select(f => f.ToMono()).ToArray();
    }

    private static RenderContext WithClock(HostClock clock) => new(SampleRate, Block) { Clock = clock };

    // Renders `blocks` blocks of an already-compiled operator, advancing the clock like a host would.
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

    // --- The clock itself --------------------------------------------------------------------------------------------

    [Fact]
    public void Advance_moves_samples_and_beats_at_the_current_tempo()
    {
        var clock = new HostClock(SampleRate, bpm: 120);

        clock.Advance(SampleRate);          // one second at 120 bpm = 2 beats
        clock.Bpm = 60;
        clock.Advance(SampleRate);          // one more second at 60 bpm = 1 beat

        Assert.Equal(2 * SampleRate, clock.SamplePosition);
        Assert.Equal(2.0, clock.Seconds, 9);
        Assert.Equal(3.0, clock.Beats, 9);
    }

    [Fact]
    public void Seek_moves_both_positions()
    {
        var clock = new HostClock(SampleRate);

        clock.Seek(480_000, 20.5);

        Assert.Equal(480_000, clock.SamplePosition);
        Assert.Equal(20.5, clock.Beats);
    }

    [Fact]
    public void Arguments_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostClock(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostClock(SampleRate, bpm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostClock(SampleRate, bpm: double.NaN));
        var clock = new HostClock(SampleRate);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Bpm = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Seek(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Seek(0, double.PositiveInfinity));
    }

    [Fact]
    public void A_clock_at_another_sample_rate_is_rejected()
    {
        var context = new RenderContext(44_100) { Clock = new HostClock(SampleRate) };

        Assert.Throws<ArgumentException>(() => Constant(0).Compile(context));
    }

    [Fact]
    public void A_sound_context_advances_the_clock_it_was_given()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        using var output = new SoundContext(SampleRate, clock: clock);

        Render(output, 1000);

        Assert.Equal(1000, clock.SamplePosition);
        Assert.Equal(1000.0 / SampleRate * 2, clock.Beats, 9);
    }

    // --- Fn and function parameters ----------------------------------------------------------------------------------

    [Fact]
    public void Fn_reads_host_time_when_there_is_a_clock()
    {
        var clock = new HostClock(SampleRate);
        clock.Seek(10 * SampleRate, 0);
        var op = Fn(t => t).Compile(WithClock(clock));

        var samples = RenderBlocks(op, clock, 2);

        Assert.Equal(10f, samples[0], 4);
        Assert.Equal(10f + 300f / SampleRate, samples[300], 4); // continues into the second block
    }

    [Fact]
    public void Fn_still_counts_from_the_start_without_a_clock()
    {
        var op = Fn(t => t).Compile(new RenderContext(SampleRate, Block));
        var block = new StereoFrame[Block];

        op.RenderBlock(block);

        Assert.Equal(0f, block[0].ToMono());
        Assert.Equal(100f / SampleRate, block[100].ToMono(), 6);
    }

    [Fact]
    public void A_function_parameter_reads_host_time_too()
    {
        var clock = new HostClock(SampleRate);
        clock.Seek(5 * SampleRate, 0);
        // Sine at 0 Hz is silent; a frequency of Fn(t) = 1 + t is not, so this only works if the parameter sees host time.
        Func<double, float> frequency = t => (float)t;
        var op = Sine(frequency).Compile(WithClock(clock));

        var block = new StereoFrame[Block];
        op.RenderBlock(block);

        // 5 Hz: phase advances 2*pi*5/48000 per sample, so the first samples are tiny but non-zero and rising.
        Assert.True(block[200].ToMono() > block[1].ToMono());
    }

    [Fact]
    public void A_note_voice_keeps_its_own_time_even_with_a_clock()
    {
        var clock = new HostClock(SampleRate);
        clock.Seek(10 * SampleRate, 0);
        var pattern = new[] { new Note(60, 1f) }.Every(Beats.Quarter, gate: 4);
        var op = pattern.Play(v => Fn(t => t), bpm: 120).Compile(WithClock(clock));

        var samples = RenderBlocks(op, clock, 2);

        // The voice starts at 0 s with the note, not at the host's 10 s.
        Assert.Equal(0f, samples[0], 4);
        Assert.Equal(200f / SampleRate, samples[200], 5);
    }

    // --- Oscillator phase --------------------------------------------------------------------------------------------

    public static TheoryData<string> Oscillators => new() { "sine", "saw", "square" };

    private static Sound Oscillator(string kind) => kind switch
    {
        "sine" => Sine(440),
        "saw" => Saw(440),
        _ => Square(440, 0.3),
    };

    [Theory]
    [MemberData(nameof(Oscillators))]
    public void A_replacement_oscillator_continues_the_phase_of_the_one_it_replaces(string kind)
    {
        // Reference: one oscillator running for 3000 frames.
        var referenceClock = new HostClock(SampleRate);
        var reference = RenderBlocks(Oscillator(kind).Compile(WithClock(referenceClock)), referenceClock, 12);

        // A second oscillator compiled only when the clock has reached block 5, as a hot swap would.
        var clock = new HostClock(SampleRate);
        clock.Seek(5 * Block, 0);
        var late = RenderBlocks(Oscillator(kind).Compile(WithClock(clock)), clock, 7);

        for (int i = 0; i < late.Length; i++)
            Assert.Equal(reference[5 * Block + i], late[i], 3);
    }

    [Fact]
    public void Without_a_clock_a_new_oscillator_starts_at_phase_zero()
    {
        var op = Sine(440).Compile(new RenderContext(SampleRate, Block));
        var block = new StereoFrame[Block];

        op.RenderBlock(block);

        Assert.Equal(0f, block[0].ToMono());
    }

    [Fact]
    public void A_modulated_frequency_starts_at_phase_zero_because_its_phase_cannot_be_derived()
    {
        var clock = new HostClock(SampleRate);
        clock.Seek(12_345, 0);
        var op = Sine(Constant(440)).Compile(WithClock(clock));
        var block = new StereoFrame[Block];

        op.RenderBlock(block);

        Assert.Equal(0f, block[0].ToMono());
    }

    [Fact]
    public void A_hot_swapped_lfo_does_not_jump_in_a_sound_context_with_a_clock()
    {
        var clock = new HostClock(SampleRate);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Sine(3), fadeSeconds: 0);
        var before = Render(output, 20_000);

        output.Play(Sine(3), fadeSeconds: 0); // an edit that changes nothing audible
        var after = Render(output, 20_000);

        // The step across the swap is as small as between any two neighbouring samples of a 3 Hz sine.
        Assert.InRange(Math.Abs(after[0] - before[^1]), 0f, 0.001f);
    }

    // --- Patterns on the beat grid -----------------------------------------------------------------------------------

    // A voice that is 1 while its note is held and 0 afterwards, so the output shows exactly when notes sound.
    private static Sound Gate(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    // Onsets are found by thresholding, so a frame or two either side is the same instant.
    private static void AssertNear(int expected, int actual, int tolerance = 2) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static int[] OnsetFrames(float[] samples)
    {
        var onsets = new List<int>();
        for (int i = 1; i < samples.Length; i++)
            if (samples[i] > 0.5f && samples[i - 1] <= 0.5f)
                onsets.Add(i);
        if (samples.Length > 0 && samples[0] > 0.5f)
            onsets.Insert(0, 0);
        return onsets.ToArray();
    }

    // One note at the start of every `lengthBeats`, lasting a sixteenth of that.
    private static Pattern Looping(double lengthBeats) =>
        new[] { new Note(60, 1f) }.Every(lengthBeats, gate: 1.0 / 16).Loop();

    [Fact]
    public void A_looping_pattern_sits_on_the_host_beat_grid()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        clock.Seek(0, 7.0); // 1 beat into the third 2-beat cycle: the next note is at beat 8
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Looping(2).Play(Gate), fadeSeconds: 0);

        var onsets = OnsetFrames(Render(output, 2 * SampleRate));

        // Beat 8, 10 and 12 are 0.5 s, 1.5 s and 2.5 s away at 120 bpm; only the first two fall inside 2 s.
        Assert.Equal(2, onsets.Length);
        AssertNear(SampleRate / 2, onsets[0]);
        AssertNear(3 * SampleRate / 2, onsets[1]);
    }

    [Fact]
    public void Replacing_the_sound_keeps_a_looping_pattern_on_the_beat()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Looping(2).Play(Gate), fadeSeconds: 0);
        Render(output, (int)(0.3 * SampleRate)); // the note at beat 0 is long over

        output.Play(Looping(2).Play(Gate), fadeSeconds: 0);
        var after = Render(output, (int)(1.5 * SampleRate)); // t = 0.3 s to 1.8 s: the grid has a note at 1.0 s, the next is at 2.0 s

        // Restarting at the swap would put a note right at its start; staying on the grid waits for beat 2 (t = 1 s).
        var onsets = OnsetFrames(after);
        Assert.Single(onsets);
        AssertNear(SampleRate - (int)(0.3 * SampleRate), onsets[0]);
    }

    [Fact]
    public void An_explicit_tempo_keeps_the_pattern_on_its_own_time_even_with_a_clock()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        clock.Seek(0, 7.0);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Looping(2).Play(Gate, bpm: 120), fadeSeconds: 0);

        var onsets = OnsetFrames(Render(output, SampleRate));

        Assert.Equal(0, onsets[0]); // starts with the sound, not at beat 8
    }

    [Fact]
    public void Without_a_clock_and_without_a_tempo_the_pattern_runs_at_120()
    {
        var onsets = OnsetFrames(Looping(1).Play(Gate).Render(2.0, SampleRate).ToMono());

        Assert.Equal([0, SampleRate / 2, SampleRate, 3 * SampleRate / 2], onsets.Select(o => (int)Math.Round(o / 100.0) * 100));
    }

    [Fact]
    public void A_pattern_follows_tempo_changes_from_the_host()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Looping(1).Play(Gate), fadeSeconds: 0); // a note every beat

        var slow = Render(output, SampleRate);   // 1 s at 120 bpm: notes at 0 and 0.5
        clock.Bpm = 240;
        var fast = Render(output, SampleRate);   // 1 s at 240 bpm: four notes

        Assert.Equal(2, OnsetFrames(slow).Length);
        Assert.Equal(4, OnsetFrames(fast).Length);
    }

    [Fact]
    public void A_pattern_that_does_not_loop_begins_where_it_was_started_not_at_beat_zero()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        clock.Seek(100 * SampleRate, 200.25);
        var once = new[] { new Note(60, 1f) }.Every(Beats.Quarter, gate: 0.5);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(once.Play(Gate), fadeSeconds: 0);

        var onsets = OnsetFrames(Render(output, SampleRate / 2));

        Assert.Equal([0], onsets);
    }

    [Fact]
    public void Notes_on_a_block_boundary_are_neither_dropped_nor_doubled_as_rounding_drifts()
    {
        // At this tempo a beat is exactly one block, so every note lands on a block boundary, where the clock's rounding
        // and the operator's own window arithmetic could disagree.
        double bpm = 60.0 * SampleRate / Block;
        var clock = new HostClock(SampleRate, bpm);
        using var output = new SoundContext(SampleRate, Block, fadeSeconds: 0, clock: clock);
        output.Play(Looping(1).Play(Gate), fadeSeconds: 0);

        var onsets = OnsetFrames(Render(output, 400 * Block));

        Assert.Equal(400, onsets.Length);
        for (int i = 0; i < onsets.Length; i++)
            Assert.Equal(i * Block, onsets[i]);
    }

    [Fact]
    public void Moving_the_transport_relocates_a_looping_pattern()
    {
        var clock = new HostClock(SampleRate, bpm: 120);
        using var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
        output.Play(Looping(4).Play(Gate), fadeSeconds: 0); // a note every 4 beats = 2 s

        Render(output, SampleRate / 4);
        clock.Seek(0, 3.0);                                  // jump to 1 beat before the next note
        var onsets = OnsetFrames(Render(output, SampleRate));

        Assert.Single(onsets);
        AssertNear(SampleRate / 2, onsets[0]);
    }
}
