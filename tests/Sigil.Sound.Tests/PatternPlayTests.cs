using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class PatternPlayTests
{
    private const int Rate = 48_000;

    // 1 while a note is held and 0 after, so the output shows when notes start and stop.
    private static Sound Gate(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    // 240 bpm makes a cycle exactly one second.
    private static float[] Mono(Sound sound, double seconds, int blockSize = 256) =>
        sound.Render(seconds, Rate, blockSize).ToMono();

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * Rate)];

    [Fact]
    public void Notes_sound_for_the_length_of_their_event()
    {
        var samples = Mono(Mini.Notes("c3:1 ~ e3:1 ~").Play(Gate, bpm: 240), 1.0);

        Assert.Equal(1f, At(samples, 0.1));
        Assert.Equal(0f, At(samples, 0.4));
        Assert.Equal(1f, At(samples, 0.6));
        Assert.Equal(0f, At(samples, 0.9));
    }

    [Fact]
    public void A_note_starts_on_its_exact_frame()
    {
        var samples = Mono(Mini.Notes("~ c3:1").Play(Gate, bpm: 240), 1.0);

        Assert.Equal(0f, samples[Rate / 2 - 1]);
        Assert.Equal(1f, samples[Rate / 2]);
    }

    [Fact]
    public void Playing_does_not_depend_on_the_block_size()
    {
        var pattern = Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>(3,8)").Degrade(0.2, 4);
        var sound = pattern.Play(v => Saw(v.Frequency).Adsr(0.003, 0.05, 0.5, 0.05), bpm: 300);

        Assert.Equal(Mono(sound, 4.0, 512), Mono(sound, 4.0, 37));
    }

    [Fact]
    public void A_pattern_that_changes_each_cycle_plays_the_change()
    {
        var sound = Mini.Notes("<c3 e3>").Play(v => Sine(v.Frequency).Adsr(0, 0, 1, 0.01), bpm: 240);

        var audio = sound.Render(2.0, Rate);

        AudioAssert.FrequencyNear(Slice(audio, 0.1, 0.9), 130.81, 1.5);
        AudioAssert.FrequencyNear(Slice(audio, 1.1, 1.9), 164.81, 1.5);
    }

    private static AudioBuffer Slice(AudioBuffer audio, double from, double to) =>
        new(audio.Frames[(int)(from * Rate)..(int)(to * Rate)].ToArray(), Rate);

    [Fact]
    public void Every_changes_only_its_cycles_when_played()
    {
        var pattern = Mini.Notes("c3:1 e3:1").Every(2, p => p.Fast(2));
        var samples = Mono(pattern.Play(v => Constant(1).Adsr(0, 0, 1, 0).Gain(v.Frequency * (1 / 130.81)), bpm: 240), 2.0);

        // Cycle 0 runs twice as fast, so a second pair of notes begins at 0.5; cycle 1 plays "c3 e3" once.
        Assert.Equal(1f, At(samples, 0.1), 2);
        Assert.Equal(1.26f, At(samples, 0.3), 2);
        Assert.Equal(1f, At(samples, 0.6), 2);
        Assert.Equal(1f, At(samples, 1.1), 2);
        Assert.Equal(1.26f, At(samples, 1.6), 2);
    }

    [Fact]
    public void Following_a_host_clock_plays_the_cycle_the_clock_is_in()
    {
        var clock = new HostClock(Rate, bpm: 120); // a cycle is 4 beats, 2 seconds
        var pattern = Mini.Notes("<c3:1 ~>").Play(Gate);
        var block = new StereoFrame[256];

        // Start in cycle 1 (beat 4), where the pattern rests, then in cycle 2 (beat 8), where it plays.
        clock.Seek(0, beats: 4);
        var inRest = pattern.Compile(new RenderContext(Rate) { Clock = clock });
        inRest.RenderBlock(block);
        Assert.All(block, f => Assert.Equal(0f, f.Left));

        clock.Seek(0, beats: 8);
        var inPlay = pattern.Compile(new RenderContext(Rate) { Clock = clock });
        inPlay.RenderBlock(block);
        Assert.Equal(1f, block[0].Left);
    }

    [Fact]
    public void Replacing_a_sound_mid_pattern_keeps_the_beat()
    {
        var clock = new HostClock(Rate, bpm: 120);
        var context = new SoundContext(Rate, clock: clock, fadeSeconds: 0.0);
        var pattern = Mini.Notes("c3:1 ~ ~ ~").Play(Gate);
        context.Play(pattern, 0);
        var block = new StereoFrame[Rate / 2]; // half a second = one cycle step
        context.Render(block);

        // Swapping to an identical sound mid-cycle must not restart its notes from the top.
        context.Play(Mini.Notes("c3:1 ~ ~ ~").Play(Gate), 0);
        var after = new StereoFrame[Rate / 2];
        context.Render(after);

        Assert.All(after, f => Assert.Equal(0f, f.Left));
    }

    [Fact]
    public void Playing_a_busy_pattern_allocates_nothing()
    {
        var pattern = Mini.Notes("c3 [e3 g3]*3 <b3 d4>(3,8) ~").Every(3, p => p.Rev()).Degrade(0.1);
        var op = pattern.Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.002, 0.05, 0.5, 0.05), bpm: 300).Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        for (int i = 0; i < 2000; i++) op.RenderBlock(block);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) op.RenderBlock(block);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Jux_puts_the_original_left_and_the_changed_copy_right()
    {
        var sound = Mini.Notes("c3").Jux(p => p.Transpose(12), v => Sine(v.Frequency).Adsr(0, 0, 1, 0.01), bpm: 240);

        var audio = sound.Render(0.9, Rate);

        AudioAssert.FrequencyNear(new AudioBuffer(audio.Left().Select(x => new StereoFrame(x, x)).ToArray(), Rate), 130.81, 1.5);
        AudioAssert.FrequencyNear(new AudioBuffer(audio.Right().Select(x => new StereoFrame(x, x)).ToArray(), Rate), 261.63, 1.5);
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        var notes = Mini.Notes("c3");

        Assert.Throws<ArgumentOutOfRangeException>(() => notes.Play(Gate, bpm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => notes.Play(Gate, polyphony: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => notes.Jux(p => p, Gate, width: 2));
    }
}
