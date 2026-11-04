using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class StereoSpreadTests
{
    [Fact]
    public void Width_zero_collapses_to_the_mid_signal_on_both_channels()
    {
        // (0.6 + 0.2) / 2 = 0.4 on both channels.
        var audio = Measure.Stereo(0.6f, 0.2f).Spread(0).Render(0.01);

        for (int i = 0; i < audio.FrameCount; i++)
        {
            Assert.Equal(audio[i].Left, audio[i].Right);
            Assert.Equal(0.4f, audio[i].Left, 1e-6f);
        }
    }

    [Fact]
    public void Width_one_leaves_the_channels_as_they_were()
    {
        var audio = Measure.Stereo(0.6f, 0.2f).Spread(1).Render(0.01);

        Assert.Equal(0.6f, audio[10].Left, 1e-6f);
        Assert.Equal(0.2f, audio[10].Right, 1e-6f);
    }

    [Fact]
    public void Widening_keeps_the_total_level_and_stretches_the_difference()
    {
        // Mid (L + R) is untouched at 0.8. The difference L - R starts at 0.4 and is scaled by the width: 2.5 gives 1.0.
        var audio = Measure.Stereo(0.6f, 0.2f).Spread(2.5).Render(0.01);

        Assert.Equal(0.8f, audio[5].Left + audio[5].Right, 1e-5f);
        Assert.Equal(1.0f, audio[5].Left - audio[5].Right, 1e-5f);
    }

    [Fact]
    public void A_mono_source_stays_mono_at_any_width()
    {
        var audio = (Sine(440) * 0.5).Spread(3).Render(0.05);

        for (int i = 0; i < audio.FrameCount; i++)
            Assert.Equal(audio[i].Left, audio[i].Right);
    }
}
