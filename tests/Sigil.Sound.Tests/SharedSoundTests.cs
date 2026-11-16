using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// A sound used in several places of one graph is compiled once and fans out. Its readers do not have to ask for the same
/// frames in the same sizes at the same moment: <c>Feedback</c> works in chunks of its own, and so can any custom operator.
/// Whatever the pattern of pulls, every reader must see the sound from its first frame, in order.
/// </summary>
public class SharedSoundTests
{
    private const int Rate = 48_000;

    // Pulls its input in pieces of a size of its own and passes the frames through unchanged.
    private static Sound InPieces(Sound sound, int piece) =>
        sound.Through(input => Operator.From(block =>
        {
            for (int offset = 0; offset < block.Length; offset += piece)
                input.RenderBlock(block.Slice(offset, Math.Min(piece, block.Length - offset)));
        }, null), $"InPieces({piece})");

    private static void AssertSame(AudioBuffer expected, AudioBuffer actual)
    {
        Assert.Equal(expected.FrameCount, actual.FrameCount);
        for (int i = 0; i < expected.FrameCount; i++)
        {
            Assert.True(Math.Abs(expected[i].Left - actual[i].Left) < 1e-6f, $"left frame {i}: {expected[i].Left} against {actual[i].Left}");
            Assert.True(Math.Abs(expected[i].Right - actual[i].Right) < 1e-6f, $"right frame {i}: {expected[i].Right} against {actual[i].Right}");
        }
    }

    [Theory]
    [InlineData(256)]
    [InlineData(100)]
    [InlineData(37)]
    [InlineData(1)]
    public void A_source_shared_by_a_feedback_loop_and_the_outside_is_heard_once_by_each(int blockSize)
    {
        var source = Saw(110);

        // The loop returns nothing, so the result is the source twice: once through Feedback's own chunks, once from outside.
        var shared = source.Feedback(loop => loop * 0.0) + source;

        AssertSame((source * 2.0).Render(0.25, Rate, blockSize), shared.Render(0.25, Rate, blockSize));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(48)]
    [InlineData(255)]
    public void A_reader_that_pulls_in_pieces_sees_the_same_frames_as_one_that_pulls_whole_blocks(int piece)
    {
        var source = Saw(110);

        AssertSame((source * 2.0).Render(0.25, Rate), (InPieces(source, piece) + source).Render(0.25, Rate));
        AssertSame((source * 2.0).Render(0.25, Rate), (source + InPieces(source, piece)).Render(0.25, Rate));
    }

    [Fact]
    public void Several_readers_with_different_pulls_each_get_the_whole_sound()
    {
        var source = Noise(7);

        var mixed = InPieces(source, 5) + InPieces(source, 64) + source;

        AssertSame((source * 3.0).Render(0.25, Rate), mixed.Render(0.25, Rate));
    }

    [Fact]
    public void A_reader_that_is_never_pulled_does_not_disturb_the_others()
    {
        var source = Saw(220);
        var ignored = source.Through(_ => Operator.From(block => block.Clear()), "Ignored");

        AssertSame(source.Render(0.25, Rate), (source + ignored).Render(0.25, Rate));
    }

    [Fact]
    public void Resetting_the_graph_starts_every_reader_from_the_first_frame_again()
    {
        var source = Saw(110);
        var op = (InPieces(source, 48) + source).Compile(new RenderContext(Rate));
        var first = new StereoFrame[300];
        var again = new StereoFrame[300];

        op.RenderBlock(first.AsSpan(0, 256));
        op.RenderBlock(first.AsSpan(256, 44));
        ((IResettable)op).Reset();
        op.RenderBlock(again.AsSpan(0, 256));
        op.RenderBlock(again.AsSpan(256, 44));

        Assert.Equal(first, again);
    }

    [Fact]
    public void Sharing_allocates_nothing_while_rendering_even_when_the_readers_pull_differently()
    {
        var source = Saw(110);
        var op = (InPieces(source, 48) + source.Feedback(loop => loop * 0.5)).Compile(new RenderContext(Rate));
        var block = new StereoFrame[256];
        for (int i = 0; i < 50; i++) op.RenderBlock(block);

        AllocationAssert.None(() =>
        {
            for (int i = 0; i < 200; i++) op.RenderBlock(block);
        });
    }

    [Fact]
    public void A_reader_that_pulls_far_ahead_of_the_others_cannot_break_them()
    {
        // The frames a slow reader would need have been overwritten: it is told so with silence, and nothing throws or turns to noise.
        var source = Saw(110);
        var ahead = source.Through(input => Operator.From(block =>
        {
            var scratch = new StereoFrame[256];
            for (int i = 0; i < 40; i++) input.RenderBlock(scratch);
            block.Clear();
        }), "Ahead");

        var op = (ahead + source).Compile(new RenderContext(Rate));
        var output = new StereoFrame[256];
        for (int i = 0; i < 4; i++) op.RenderBlock(output);

        AudioAssert.IsFinite(new AudioBuffer(output, Rate));
    }
}
