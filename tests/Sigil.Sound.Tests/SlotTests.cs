using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class SlotTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void SoundContext_is_silent_until_something_plays()
    {
        var output = new SoundContext(SampleRate);

        var frames = Render(output, 0.1);

        AudioAssert.IsSilent(frames.ToMono());
    }

    [Fact]
    public void Play_swaps_to_the_new_sound()
    {
        var output = new SoundContext(SampleRate);
        output.Play(Sine(440));
        Render(output, 0.1);

        output.Play(Sine(1000));
        Render(output, 0.1);

        AudioAssert.FrequencyNear(Render(output, 0.5), 1000, 1.0);
    }

    [Fact]
    public void Swap_crossfades_without_a_jump()
    {
        // A constant level makes any click visible as a step between neighbouring frames.
        var output = new SoundContext(SampleRate, fadeSeconds: 0.01);
        output.Play(Constant(1f));
        Render(output, 0.05);

        output.Play(Constant(-1f));
        var samples = Render(output, 0.05).ToMono();

        float largestStep = 0;
        for (int i = 1; i < samples.Length; i++)
            largestStep = Math.Max(largestStep, Math.Abs(samples[i] - samples[i - 1]));

        Assert.True(largestStep < 0.01f, $"largest step {largestStep}");
        Assert.Equal(-1f, samples[^1], 1e-5f);
    }

    [Fact]
    public void Zero_fade_swaps_on_the_next_block()
    {
        var output = new SoundContext(SampleRate, blockSize: 64);
        output.Play(Constant(0.5f), fadeSeconds: 0);

        var samples = Render(output, 64.0 / SampleRate).ToMono();

        Assert.All(samples, s => Assert.Equal(0.5f, s));
    }

    [Fact]
    public void Slot_inside_a_chain_follows_swaps()
    {
        var slot = new Slot(Sine(220));
        var op = (slot.Sound * 0.5).Compile(new RenderContext(SampleRate));
        Render(op, 0.05);

        slot.Swap(Sine(880), fadeSeconds: 0);
        var audio = Render(op, 0.5);

        AudioAssert.FrequencyNear(audio, 880, 1.0);
        AudioAssert.PeakNear(audio, 0.5f, 0.001f);
    }

    [Fact]
    public void Swap_disposes_replaced_operators_once_the_audio_thread_lets_go()
    {
        var first = new DisposableConstant();
        var slot = new Slot(Sound.FromOperator(_ => first));
        var op = slot.Sound.Compile(new RenderContext(SampleRate));

        slot.Swap(Silence, fadeSeconds: 0);
        Assert.False(first.Disposed);   // not taken by the audio thread yet

        Render(op, 0.01);
        slot.Swap(Silence, fadeSeconds: 0);

        Assert.True(first.Disposed);
    }

    private static AudioBuffer Render(SoundContext output, double seconds)
    {
        var frames = new StereoFrame[(int)Math.Round(seconds * SampleRate)];
        output.Render(frames);
        return new AudioBuffer(frames, SampleRate);
    }

    private static AudioBuffer Render(IOperator op, double seconds)
    {
        var frames = new StereoFrame[(int)Math.Round(seconds * SampleRate)];
        for (int offset = 0; offset < frames.Length; offset += RenderContext.DefaultBlockSize)
            op.RenderBlock(frames.AsSpan(offset, Math.Min(RenderContext.DefaultBlockSize, frames.Length - offset)));
        return new AudioBuffer(frames, SampleRate);
    }

    private sealed class DisposableConstant : IOperator, IDisposable
    {
        public bool Disposed { get; private set; }

        public void RenderBlock(Span<StereoFrame> block) => block.Fill(StereoFrame.Mono(1));

        public void Dispose() => Disposed = true;
    }
}

public class OperatorTests
{
    [Fact]
    public void Operator_From_wraps_a_lambda_that_keeps_state_between_blocks()
    {
        var sound = Sound.FromOperator(_ =>
        {
            int count = 0;
            return Operator.From(block =>
            {
                for (int i = 0; i != block.Length; i++)
                    block[i] = StereoFrame.Mono(count++);
            });
        });

        var audio = sound.Render(600.0 / 48_000, blockSize: 256);

        Assert.Equal(599f, audio[599].Left);
    }

    [Fact]
    public void Through_with_a_lambda_operator_reworks_its_input()
    {
        var doubled = Constant(0.25).Through(input => Operator.From(block =>
        {
            input.RenderBlock(block);
            for (int i = 0; i != block.Length; i++)
                block[i] *= 2f;
        }));

        Assert.Equal(0.5f, doubled.Render(0.01)[10].Left, 1e-6f);
    }
}
