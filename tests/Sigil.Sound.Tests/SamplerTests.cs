using System.Buffers.Binary;
using System.Text;
using Sigil.Sound.Testing;
using Sigil.Sound.Wav;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class SamplerTests
{
    // Builds a RIFF/WAVE file in memory: one fmt chunk and one data chunk, which is all the decoder needs.
    private static byte[] MakeWav(ushort formatTag, ushort channels, int sampleRate, ushort bits, byte[] data)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write(formatTag);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(data.Length);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }

    // A buffer whose left channel counts up from 0 to nearly 1, one step per frame, so the position is visible in the value.
    private static AudioBuffer Ramp(int frames, int sampleRate = Measure.SampleRate) =>
        new(Enumerable.Range(0, frames).Select(i => StereoFrame.Mono(i / (float)frames)).ToArray(), sampleRate);

    [Fact]
    public void Sixteen_bit_wav_round_trips_within_quantisation()
    {
        var source = Sine(440).Render(0.05, 44_100);
        var stream = new MemoryStream();
        WavWriter.Write(stream, source, WavFormat.Pcm16);
        stream.Position = 0;

        var decoded = AudioBuffer.FromWav(stream);

        Assert.Equal(source.SampleRate, decoded.SampleRate);
        Assert.Equal(source.FrameCount, decoded.FrameCount);
        for (int i = 0; i < source.FrameCount; i++)
            Assert.InRange(MathF.Abs(decoded[i].Left - source[i].Left), 0f, 2e-4f);
    }

    [Fact]
    public void Wav_loads_from_a_file_path_and_reports_a_missing_file()
    {
        var source = Sine(330).Render(0.05, 48_000);
        string path = Path.Combine(Path.GetTempPath(), $"sigil-sample-{Guid.NewGuid():N}.wav");
        try
        {
            using (var file = File.Create(path))
                WavWriter.Write(file, source, WavFormat.Float32);

            var decoded = AudioBuffer.FromWav(path);

            Assert.Equal(source.FrameCount, decoded.FrameCount);
            Assert.Equal(source[100].Left, decoded[100].Left);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Throws<FileNotFoundException>(() => AudioBuffer.FromWav(path));
    }

    [Fact]
    public void Twenty_four_bit_and_float_wav_round_trip()
    {
        var source = Sine(330).Render(0.05, 48_000);

        foreach (var format in new[] { WavFormat.Pcm24, WavFormat.Float32 })
        {
            var stream = new MemoryStream();
            WavWriter.Write(stream, source, format);
            stream.Position = 0;

            var decoded = AudioBuffer.FromWav(stream);

            float tolerance = format == WavFormat.Float32 ? 0f : 1e-6f;
            for (int i = 0; i < source.FrameCount; i++)
                Assert.InRange(MathF.Abs(decoded[i].Right - source[i].Right), 0f, tolerance);
        }
    }

    [Fact]
    public void Eight_bit_mono_wav_is_unsigned_and_copied_to_both_channels()
    {
        // Unsigned 8-bit: 128 is silence, 0 is full negative and 255 nearly full positive.
        var bytes = MakeWav(formatTag: 1, channels: 1, sampleRate: 8_000, bits: 8, data: [128, 255, 0, 192]);

        var decoded = AudioBuffer.FromWav(bytes);

        Assert.Equal(8_000, decoded.SampleRate);
        Assert.Equal(4, decoded.FrameCount);
        Assert.Equal(0f, decoded[0].Left, 1e-6f);
        Assert.Equal(127f / 128f, decoded[1].Left, 1e-6f);
        Assert.Equal(-1f, decoded[2].Left, 1e-6f);
        Assert.Equal(0.5f, decoded[3].Left, 1e-6f);
        Assert.Equal(decoded[3].Left, decoded[3].Right);
    }

    [Fact]
    public void Extensible_float_wav_is_recognised_by_its_sub_format()
    {
        // WAVE_FORMAT_EXTENSIBLE (0xFFFE) carries the real format tag inside its sub-format GUID; 3 means IEEE float.
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        var data = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(0), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), -0.5f);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(4 + (8 + 40) + (8 + data.Length));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(40);
        writer.Write((ushort)0xFFFE);
        writer.Write((ushort)2);
        writer.Write(48_000);
        writer.Write(48_000 * 8);
        writer.Write((ushort)8);
        writer.Write((ushort)32);
        writer.Write((ushort)22);
        writer.Write((ushort)32);
        writer.Write(3);
        writer.Write((ushort)3);
        writer.Write(new byte[14]);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(data.Length);
        writer.Write(data);
        writer.Flush();

        var decoded = AudioBuffer.FromWav(stream.ToArray());

        Assert.Equal(0.25f, decoded[0].Left);
        Assert.Equal(-0.5f, decoded[0].Right);
    }

    [Fact]
    public void Data_that_is_not_wav_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => AudioBuffer.FromWav(Encoding.ASCII.GetBytes("this is not audio at all")));
    }

    [Fact]
    public void Surround_wav_is_rejected_with_a_clear_message()
    {
        var bytes = MakeWav(formatTag: 1, channels: 6, sampleRate: 48_000, bits: 16, data: new byte[12]);

        var error = Assert.Throws<InvalidDataException>(() => AudioBuffer.FromWav(bytes));

        Assert.Contains("mono and stereo", error.Message);
    }

    [Fact]
    public void Speed_two_plays_an_octave_up()
    {
        var clip = Sine(440).Render(0.5);

        var played = Sample(clip, speed: 2).Render(0.2);

        AudioAssert.FrequencyNear(played, 880, 3);
    }

    [Fact]
    public void Speed_half_plays_an_octave_down()
    {
        var clip = Sine(440).Render(0.5);

        var played = Sample(clip, speed: 0.5).Render(0.8);

        AudioAssert.FrequencyNear(played, 220, 2);
    }

    [Fact]
    public void Speed_one_reproduces_the_frames()
    {
        var clip = Sine(440).Render(0.1);

        var played = Sample(clip).Render(0.1);

        for (int i = 0; i < clip.FrameCount; i++)
            Assert.InRange(MathF.Abs(played[i].Left - clip[i].Left), 0f, 1e-6f);
    }

    [Fact]
    public void A_sample_at_a_different_rate_keeps_its_pitch()
    {
        // A 24 kHz recording must not play an octave down or up when rendered at 48 kHz.
        var clip = Sine(440).Render(0.5, 24_000);

        var played = Sample(clip).Render(0.3, 48_000);

        AudioAssert.FrequencyNear(played, 440, 3);
    }

    [Fact]
    public void Loop_jumps_from_the_loop_end_back_to_the_loop_start()
    {
        // The loop covers frames 960 to 1920 of a 4800-frame ramp.
        var buffer = Ramp(4800);
        var played = Sample(buffer, mode: PlaybackMode.Loop, loopStart: 0.02, loopEnd: 0.04).Render(0.1).Left();

        Assert.Equal(1919f / 4800f, played[1919], 1e-5f);
        Assert.Equal(960f / 4800f, played[1920], 1e-5f);
        Assert.Equal(1919f / 4800f, played[3839], 1e-5f);
        Assert.Equal(960f / 4800f, played[3840], 1e-5f);
    }

    [Fact]
    public void Ping_pong_turns_around_at_both_ends_of_the_loop()
    {
        var buffer = Ramp(4800);
        var played = Sample(buffer, mode: PlaybackMode.PingPong, loopStart: 0.02, loopEnd: 0.04).Render(0.1).Left();

        // Forward to the loop end, then back down toward the loop start, then forward again.
        Assert.Equal(1919f / 4800f, played[1921], 1e-5f);
        Assert.True(played[2000] < played[1930], "After the turnaround the playhead must move backward.");
        Assert.Equal(960f / 4800f, played[2880], 1e-5f);
        Assert.True(played[2950] > played[2900], "After the second turnaround the playhead must move forward again.");
    }

    [Fact]
    public void One_shot_falls_silent_after_its_last_frame()
    {
        var buffer = new AudioBuffer(Enumerable.Repeat(StereoFrame.Mono(0.5f), 2400).ToArray(), Measure.SampleRate);

        var played = Sample(buffer).Render(0.1).Left();

        Assert.Equal(0.5f, played[100], 1e-6f);
        AudioAssert.IsSilent(Measure.Slice(played, 0.06, 0.1));
    }

    [Fact]
    public void Reset_restarts_the_sample_from_its_first_frame()
    {
        var sound = Sample(Ramp(4800), speed: 1.5);
        var op = sound.Compile(RenderContext.Default);
        var first = Measure.Pull(op, 1000);

        ((IResettable)op).Reset();
        var again = Measure.Pull(op, 1000);

        Assert.Equal(first, again);
    }

    [Fact]
    public void A_retriggered_voice_plays_its_sample_again_from_the_first_frame()
    {
        // A quarter note at 120 bpm is half a second. The sample is a quarter-second ramp, so a one-shot is silent after it.
        // The second note must restart the ramp from zero, not find the sample already finished.
        var ramp = new AudioBuffer(Enumerable.Range(0, 12_000).Select(i => StereoFrame.Mono(i / 12_000f)).ToArray(), Measure.SampleRate);
        var sound = Notes.Parse("C4 C4")
            .Select(n => n with { Velocity = 1f })
            .Every(Beats.Quarter)
            .Play(v => Sample(ramp, speed: v.Frequency * (1 / 261.63)), bpm: 120);

        var samples = Measure.Mono(sound, 1.0);

        // The ramp reaches 1 after 0.25 s, so it is 4 per second: 0.1 s in is 0.4, and 0.05 s in is 0.2.
        Assert.InRange(samples[(int)(0.1 * Measure.SampleRate)], 0.395f, 0.405f);   // the first note, 0.1 s into the ramp
        Assert.InRange(samples[(int)(0.55 * Measure.SampleRate)], 0.195f, 0.205f);  // the second note, 0.05 s into the ramp
    }

    [Fact]
    public void A_one_shot_with_negative_speed_plays_backward_from_its_last_frame()
    {
        var ramp = Ramp(4800);

        var played = Sample(ramp, speed: -1).Render(0.05).Left();

        Assert.Equal(4799f / 4800f, played[0], 1e-5f);
        Assert.Equal(3799f / 4800f, played[1000], 1e-5f);
    }

    [Fact]
    public void Pitched_sample_plays_at_the_frequency_over_its_root()
    {
        // A 440 Hz tone recorded at 440 Hz: asking for 880 Hz transposes it up an octave.
        var clip = Sine(440).Render(0.5);

        var played = Sample(clip, 880, 440).Render(0.2);

        AudioAssert.FrequencyNear(played, 880, 3);
    }

    [Fact]
    public void Pitched_sample_rejects_a_root_that_is_not_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(Ramp(4800), 440, 0));
    }

    [Fact]
    public void Empty_samples_and_bad_loop_markers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Sample(new AudioBuffer(Array.Empty<StereoFrame>(), Measure.SampleRate)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(Ramp(4800), mode: PlaybackMode.Loop, loopStart: 0.05, loopEnd: 0.02));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample(Ramp(4800), mode: PlaybackMode.Loop, loopEnd: 1.0));
    }
}
