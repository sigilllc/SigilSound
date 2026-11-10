using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class FeedbackTests
{
    private const int Rate = 48_000;

    // A single full-scale click on the first frame, then silence.
    private static Sound Impulse() =>
        Sound.FromOperator(_ =>
        {
            bool fired = false;
            return Operator.From(block =>
            {
                block.Clear();
                if (!fired && block.Length > 0)
                    block[0] = StereoFrame.Mono(1f);
                fired = true;
            }, () => fired = false);
        }, "Impulse");

    private static float[] Mono(Sound sound, double seconds, int blockSize = 256) =>
        sound.Render(seconds, Rate, blockSize).ToMono();

    [Fact]
    public void A_gain_in_the_loop_repeats_the_click_one_chunk_apart_and_fading()
    {
        var samples = Mono(Impulse().Feedback(loop => loop * 0.5), 0.05);

        // The default chunk is 1 ms = 48 frames.
        Assert.Equal(1f, samples[0]);
        Assert.Equal(0.5f, samples[48]);
        Assert.Equal(0.25f, samples[96]);
        Assert.Equal(0.125f, samples[144]);
        Assert.Equal(0f, samples[47]);
        Assert.Equal(0f, samples[49]);
    }

    [Fact]
    public void The_latency_sets_the_spacing()
    {
        var samples = Mono(Impulse().Feedback(loop => loop * 0.5, latencySeconds: 0.002), 0.05);

        Assert.Equal(0.5f, samples[96]);
        Assert.Equal(0.25f, samples[192]);
    }

    [Fact]
    public void A_delay_inside_the_loop_adds_to_the_latency()
    {
        var samples = Mono(Impulse().Feedback(loop => loop.Delay(0.01, feedback: 0, mix: 1) * 0.5), 0.1);

        int first = Array.FindIndex(samples, 10, s => Math.Abs(s) > 0.1f);

        // 0.01 s = 480 frames of delay plus the 48-frame chunk, give or take the delay line's own rounding.
        Assert.InRange(first, 480 + 48 - 2, 480 + 48 + 2);
        Assert.InRange(samples[first], 0.45f, 0.5f);
    }

    [Fact]
    public void The_result_does_not_depend_on_the_block_size()
    {
        var sound = Saw(220).Adsr(0.002, 0.1, 0, 0.05).Feedback(loop => loop.Delay(0.013, feedback: 0, mix: 1).LowPass(2500) * 0.6);

        // The sound is compiled for blocks of up to 256 frames; the host then asks for odd, smaller or larger-than-chunk spans.
        float[] Pull(params int[] sizes)
        {
            var op = sound.Compile(RenderContext.Default);
            var result = new float[Rate];
            for (int done = 0, i = 0; done < result.Length; i++)
            {
                var block = new StereoFrame[Math.Min(sizes[i % sizes.Length], result.Length - done)];
                op.RenderBlock(block);
                for (int k = 0; k < block.Length; k++) result[done + k] = block[k].ToMono();
                done += block.Length;
            }
            return result;
        }

        var reference = Pull(256);
        Assert.Equal(reference, Pull(37));
        Assert.Equal(reference, Pull(1, 100, 7, 256, 48));
    }

    [Fact]
    public void A_loop_that_grows_is_held_in_check()
    {
        var sound = Sine(220).Feedback(loop => loop * 1.5);

        var samples = Mono(sound, 2.0);

        Assert.All(samples, s => Assert.True(float.IsFinite(s) && Math.Abs(s) < 8f));
        Assert.True(samples.Max(Math.Abs) > 1f, "the runaway should be loud, not silent");
    }

    [Fact]
    public void A_loop_that_produces_nan_does_not_poison_the_output()
    {
        var sound = Sine(220).Feedback(loop => loop.Through(_ => Operator.From(block => block.Fill(StereoFrame.Mono(float.NaN)))));

        var samples = Mono(sound, 0.2);

        Assert.All(samples, s => Assert.True(float.IsFinite(s)));
        Assert.True(samples.Max(Math.Abs) > 0.5f);
    }

    [Fact]
    public void The_stand_in_can_be_used_more_than_once()
    {
        var sound = Impulse().Feedback(loop => loop * 0.3 + loop.Delay(0.005, feedback: 0, mix: 1) * 0.2);

        var samples = Mono(sound, 0.1);

        AudioAssert.IsFinite(sound.Render(0.1, Rate));
        Assert.Equal(0.3f, samples[48], 3);
    }

    [Fact]
    public void Reset_returns_to_the_start()
    {
        var op = Impulse().Feedback(loop => loop * 0.5).Compile(RenderContext.Default);
        var first = Measure.Pull(op, 2000);

        ((IResettable)op).Reset();
        var second = Measure.Pull(op, 2000);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rendering_allocates_nothing()
    {
        var op = Saw(110).Feedback(loop => loop.Delay(0.02, feedback: 0, mix: 1).LowPass(2000) * 0.5).Compile(RenderContext.Default);
        var block = new StereoFrame[256];
        for (int i = 0; i < 200; i++) op.RenderBlock(block);

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 1000; i++) op.RenderBlock(block);
        });
    }

    [Fact]
    public void It_works_in_a_context_with_a_small_block_size()
    {
        var audio = Impulse().Feedback(loop => loop * 0.5).Render(0.01, Rate, blockSize: 16);

        AudioAssert.IsFinite(audio);
        Assert.Equal(0.5f, audio.ToMono()[16], 4); // the chunk cannot exceed the block, so it is 16 frames here
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Saw(110).Feedback(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Feedback(l => l, latencySeconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Saw(110).Feedback(l => l, latencySeconds: 1));
        Assert.Throws<InvalidOperationException>(() => Saw(110).Feedback(_ => null!).Render(0.01));
    }

    [Fact]
    public void A_feedback_sound_can_be_swapped_in_a_slot()
    {
        var output = new SoundContext(Rate);
        output.Play(Saw(220).Feedback(loop => loop * 0.4), 0);
        var block = new StereoFrame[1024];
        output.Render(block);

        output.Play(Saw(330).Feedback(loop => loop * 0.4), 0.01);
        output.Render(block);

        AudioAssert.IsFinite(new AudioBuffer(block, Rate));
    }
}
