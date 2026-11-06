using Sigil.Sound.Wav;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>The named sample library scripts load from, and the header-only WAV inspection a host uses before keeping a file.</summary>
public class SampleLibraryTests
{
    private sealed class FakeSource(params string[] names) : ISampleSource
    {
        public List<string> Loaded { get; } = [];

        public IReadOnlyList<string> Names { get; } = names;

        public AudioBuffer Load(string name)
        {
            Loaded.Add(name);
            return new AudioBuffer(new StereoFrame[10], 44_100);
        }
    }

    [Fact]
    public void Load_passes_the_listed_name_to_the_source()
    {
        var source = new FakeSource("kick.wav", "drums/snare.wav");

        new SampleLibrary(source).Load("drums/snare.wav");

        Assert.Equal(["drums/snare.wav"], source.Loaded);
    }

    [Fact]
    public void Load_ignores_case_when_no_name_matches_exactly()
    {
        var source = new FakeSource("Kick.wav");

        new SampleLibrary(source).Load("kick.WAV");

        Assert.Equal(["Kick.wav"], source.Loaded);
    }

    [Fact]
    public void An_exact_match_wins_over_a_case_insensitive_one()
    {
        var source = new FakeSource("kick.wav", "KICK.wav");

        new SampleLibrary(source).Load("KICK.wav");

        Assert.Equal(["KICK.wav"], source.Loaded);
    }

    [Fact]
    public void Backslashes_are_read_as_folder_separators()
    {
        var source = new FakeSource("drums/snare.wav");

        new SampleLibrary(source).Load(@"drums\snare.wav");

        Assert.Equal(["drums/snare.wav"], source.Loaded);
    }

    [Fact]
    public void A_name_the_source_does_not_list_is_never_passed_on()
    {
        // The library is the allow-list: a path that is not a listed name cannot reach the source, whatever the source does with it.
        var source = new FakeSource("kick.wav");
        var library = new SampleLibrary(source);

        Assert.Throws<KeyNotFoundException>(() => library.Load("../secret.wav"));
        Assert.Throws<KeyNotFoundException>(() => library.Load("/etc/passwd"));
        Assert.Throws<KeyNotFoundException>(() => library.Load("C:/Windows/media/x.wav"));
        Assert.Empty(source.Loaded);
    }

    [Fact]
    public void A_missing_name_lists_similar_ones()
    {
        var library = new SampleLibrary(new FakeSource("kick.wav", "kick_long.wav", "snare.wav"));

        var error = Assert.Throws<KeyNotFoundException>(() => library.Load("kick"));

        Assert.Contains("\"kick\"", error.Message);
        Assert.Contains("kick.wav", error.Message);
        Assert.Contains("kick_long.wav", error.Message);
        Assert.DoesNotContain("snare.wav", error.Message);
    }

    [Fact]
    public void A_missing_name_with_no_similar_one_lists_what_is_available()
    {
        var library = new SampleLibrary(new FakeSource("kick.wav", "snare.wav"));

        var error = Assert.Throws<KeyNotFoundException>(() => library.Load("hat.wav"));

        Assert.Contains("Available", error.Message);
        Assert.Contains("kick.wav", error.Message);
        Assert.Contains("snare.wav", error.Message);
    }

    [Fact]
    public void A_long_list_in_the_message_is_cut_short()
    {
        var library = new SampleLibrary(new FakeSource(Enumerable.Range(0, 30).Select(i => $"s{i:00}.wav").ToArray()));

        var error = Assert.Throws<KeyNotFoundException>(() => library.Load("nothing"));

        Assert.Contains("and 22 more", error.Message);
    }

    [Fact]
    public void An_empty_library_says_so()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => new SampleLibrary(new FakeSource()).Load("kick.wav"));

        Assert.Contains("empty", error.Message);
    }

    [Fact]
    public void Names_and_Contains_follow_the_source_as_it_changes()
    {
        var source = new MutableSource();
        var library = new SampleLibrary(source);
        Assert.False(library.Contains("late.wav"));

        source.Names = ["late.wav"];

        Assert.True(library.Contains("late.wav"));
        Assert.Equal(["late.wav"], library.Names);
    }

    private sealed class MutableSource : ISampleSource
    {
        public IReadOnlyList<string> Names { get; set; } = [];

        public AudioBuffer Load(string name) => new(new StereoFrame[1], 44_100);
    }

    // A source whose one sample is a short burst at a known level, so what comes out of Sample can be checked.
    private sealed class BurstSource : ISampleSource
    {
        public IReadOnlyList<string> Names { get; } = ["burst.wav"];

        public AudioBuffer Load(string name) =>
            new(Enumerable.Repeat(new StereoFrame(0.25f, 0.25f), 480).ToArray(), 48_000);
    }

    [Fact]
    public void A_loaded_sample_plays_through_Sample()
    {
        var buffer = new SampleLibrary(new BurstSource()).Load("burst.wav");

        var audio = Sample(buffer).Render(0.05, 48_000).ToMono();

        // The burst is 10 ms long: at its level at the start, silent once it has played.
        Assert.Equal(0.25f, audio[100], 2);
        Assert.Equal(0f, audio[^1], 3);
    }

    [Fact]
    public void The_constructor_and_Load_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => new SampleLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => new SampleLibrary(new FakeSource("a.wav")).Load(null!));
    }

    [Fact]
    public void The_name_parameter_of_Load_is_marked_as_a_sample_name_so_editors_can_complete_it()
    {
        var parameter = typeof(SampleLibrary).GetMethod(nameof(SampleLibrary.Load))!.GetParameters().Single();

        Assert.NotNull(Attribute.GetCustomAttribute(parameter, typeof(SampleNameAttribute)));
    }

    // ---- WavReader.ReadInfo ---------------------------------------------------------------------

    private static byte[] Wav(AudioBuffer audio, WavFormat format = WavFormat.Pcm16)
    {
        var stream = new MemoryStream();
        WavWriter.Write(stream, audio, format);
        return stream.ToArray();
    }

    [Fact]
    public void ReadInfo_reports_the_format_and_length_without_decoding()
    {
        var audio = new AudioBuffer(new StereoFrame[22_050], 44_100);

        var info = WavReader.ReadInfo(Wav(audio, WavFormat.Pcm24));

        Assert.Equal(2, info.Channels);
        Assert.Equal(44_100, info.SampleRate);
        Assert.Equal(24, info.BitsPerSample);
        Assert.False(info.IsFloat);
        Assert.Equal(22_050, info.FrameCount);
        Assert.Equal(0.5, info.Duration, 6);
    }

    [Fact]
    public void ReadInfo_recognises_float_files()
    {
        var info = WavReader.ReadInfo(Wav(new AudioBuffer(new StereoFrame[100], 48_000), WavFormat.Float32));

        Assert.True(info.IsFloat);
        Assert.Equal(32, info.BitsPerSample);
    }

    [Fact]
    public void ReadInfo_agrees_with_what_Read_decodes()
    {
        var bytes = Wav(new AudioBuffer(new StereoFrame[1234], 32_000));

        var info = WavReader.ReadInfo(bytes);
        var audio = WavReader.Read(bytes);

        Assert.Equal(audio.FrameCount, info.FrameCount);
        Assert.Equal(audio.SampleRate, info.SampleRate);
    }

    [Fact]
    public void ReadInfo_refuses_what_Read_refuses()
    {
        Assert.Throws<InvalidDataException>(() => WavReader.ReadInfo("not a wav file at all"u8));
        Assert.Throws<InvalidDataException>(() => WavReader.ReadInfo(new byte[4]));
    }
}
