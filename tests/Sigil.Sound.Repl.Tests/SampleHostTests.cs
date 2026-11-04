using Sigil.Sound.Testing;
using Sigil.Sound.Wav;

namespace SampleTestHost
{
    /// <summary>A host API the way the REPL page and Audio Workbench build theirs: a plain name that reaches the sample library.</summary>
    public static class SampleTestApi
    {
        public static Sigil.Sound.SampleLibrary Samples { get; set; } = new(new Sigil.Sound.Repl.Tests.ListedSource());
    }
}

namespace Sigil.Sound.Repl.Tests
{
    /// <summary>A source that only lists names; loading hands back a short silent clip.</summary>
    public sealed class ListedSource : ISampleSource
    {
        public IReadOnlyList<string> Names { get; set; } = [];

        public AudioBuffer Load(string name) => new(new StereoFrame[4_800], 48_000);
    }

    /// <summary>
    /// Samples as a host exposes them to snippets: names listed by a source, completed in the editor, loaded when the snippet runs.
    /// The page and the Workbench differ in where the files live; everything checked here is what they share.
    /// </summary>
    public sealed class SampleHostTests : IDisposable
    {
        private readonly ListedSource _listed = new() { Names = ["kick.wav", "drums/snare.wav", "pad.wav"] };
        private readonly SnippetProfile _profile;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "sigil-samples-" + Guid.NewGuid().ToString("N"));

        public SampleHostTests()
        {
            SampleTestHost.SampleTestApi.Samples = new SampleLibrary(_listed);
            _profile = new SnippetProfile(
                extraUsings: ["using static SampleTestHost.SampleTestApi;"],
                extraAssemblies: [typeof(SampleTestHost.SampleTestApi).Assembly],
                extraAllowedNamespaces: ["SampleTestHost"],
                samples: _listed);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        // ---- Completion -------------------------------------------------------------------------

        // The caret is written as '|' in the code.
        private IReadOnlyList<CompletionItem> Complete(string codeWithCaret)
        {
            int caret = codeWithCaret.IndexOf('|');
            string code = codeWithCaret.Remove(caret, 1);
            return EditorServices.Complete(code, caret, _profile);
        }

        private static IEnumerable<CompletionItem> SamplesOf(IReadOnlyList<CompletionItem> items) => items.Where(i => i.Kind == "Sample");

        [Fact]
        public void Inside_empty_quotes_the_library_names_are_offered_to_replace_the_string()
        {
            var items = SamplesOf(Complete("Samples.Load(\"|\")")).ToList();

            Assert.Equal(["\"kick.wav\"", "\"drums/snare.wav\"", "\"pad.wav\""], items.Select(i => i.Label));
            var kick = items[0];
            Assert.Equal("kick.wav", kick.InsertText);
            Assert.Equal("kick.wav", kick.FilterText);
            Assert.Equal(0, kick.ReplaceBefore);
            Assert.Equal(0, kick.ReplaceAfter);
        }

        [Fact]
        public void Inside_quotes_the_range_covers_the_whole_string_around_the_caret()
        {
            // The caret is after "kic"; the rest of the string, "k.wav", is replaced too.
            var kick = Assert.Single(SamplesOf(Complete("Samples.Load(\"kic|k.wav\")")), i => i.Label == "\"kick.wav\"");

            Assert.Equal(3, kick.ReplaceBefore);
            Assert.Equal(5, kick.ReplaceAfter);
        }

        [Fact]
        public void An_unfinished_string_is_completed_too()
        {
            var items = SamplesOf(Complete("Samples.Load(\"ki|")).ToList();

            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.Equal(2, i.ReplaceBefore));
        }

        [Fact]
        public void Line_endings_do_not_move_the_range()
        {
            // The editor counts offsets in its own line endings; the range is measured from the caret, so CRLF changes nothing.
            var kick = Assert.Single(SamplesOf(Complete("var a = 1;\r\nvar b = 2;\r\nSamples.Load(\"ki|\")\r\n")), i => i.Label == "\"kick.wav\"");

            Assert.Equal(2, kick.ReplaceBefore);
            Assert.Equal(0, kick.ReplaceAfter);
        }

        [Fact]
        public void A_named_argument_is_completed()
        {
            Assert.Equal(3, SamplesOf(Complete("Samples.Load(name: \"|\")")).Count());
        }

        [Fact]
        public void Completion_follows_a_sample_name_parameter_wherever_the_call_is()
        {
            // Nested in another call, in a lambda body and on a local: the parameter is what matters, not the spelling.
            Assert.NotEmpty(SamplesOf(Complete("Sample(Samples.Load(\"|\"))")));
            Assert.NotEmpty(SamplesOf(Complete("var lib = Samples;\nlib.Load(\"|\")")));
        }

        [Fact]
        public void Other_strings_are_not_offered_sample_names()
        {
            Assert.Empty(SamplesOf(Complete("Notes.Parse(\"|\")")));
            Assert.Empty(SamplesOf(Complete("var s = \"|\";\nSine(440)")));
        }

        [Fact]
        public void In_an_empty_argument_the_names_come_with_their_quotes_and_lead_the_list()
        {
            var items = Complete("Samples.Load(|)");

            var first = items[0];
            Assert.Equal("Sample", first.Kind);
            Assert.Equal("\"kick.wav\"", first.InsertText);
            Assert.Null(first.ReplaceBefore);
            // The ordinary suggestions are still there for someone who is passing a variable.
            Assert.Contains(items, i => i.Label == "Sine");
        }

        [Fact]
        public void After_a_comma_only_the_parameter_that_takes_a_name_is_offered_names()
        {
            Assert.Empty(SamplesOf(Complete("Sample(Samples.Load(\"kick.wav\"), |)")));
            Assert.Empty(SamplesOf(Complete("Sine(|)")));
        }

        [Fact]
        public void Names_added_later_show_up_without_a_new_profile()
        {
            _listed.Names = [.. _listed.Names, "late.wav"];

            Assert.Contains(SamplesOf(Complete("Samples.Load(\"|\")")), i => i.Label == "\"late.wav\"");
        }

        [Fact]
        public void A_host_without_a_library_offers_no_names()
        {
            var plain = new SnippetProfile(
                extraUsings: ["using static SampleTestHost.SampleTestApi;"],
                extraAssemblies: [typeof(SampleTestHost.SampleTestApi).Assembly],
                extraAllowedNamespaces: ["SampleTestHost"]);
            const string code = "Samples.Load(\"\")";

            Assert.Empty(EditorServices.Complete(code, code.Length - 2, plain));
        }

        [Fact]
        public void The_default_profile_is_untouched()
        {
            Assert.DoesNotContain(EditorServices.Complete("Sa", 2), i => i.Kind == "Sample");
        }

        // ---- Snippets ---------------------------------------------------------------------------

        [Fact]
        public void A_snippet_loads_a_sample_by_name_and_plays_it()
        {
            var result = SnippetCompiler.Compile("Sample(Samples.Load(\"kick.wav\"))", _profile);

            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            Assert.NotNull(result.Run());
        }

        [Fact]
        public void A_snippet_that_names_a_missing_sample_fails_with_a_message_that_helps()
        {
            var result = SnippetCompiler.Compile("Sample(Samples.Load(\"kik.wav\"))", _profile);

            Assert.True(result.Success, "A wrong name is found when the snippet runs, not when it compiles.");
            var error = Assert.Throws<KeyNotFoundException>(() => result.Run());
            Assert.Contains("kik.wav", error.Message);
            Assert.Contains("kick.wav", error.Message);
        }

        [Fact]
        public void Reading_a_file_by_path_is_still_refused()
        {
            // Samples.Load is the way in; a snippet naming a file path directly never reaches the file system.
            var result = SnippetCompiler.Compile("Sample(AudioBuffer.FromWav(\"kick.wav\"))", _profile);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Id == "SIGIL_SEC09");
        }

        // ---- FolderSampleSource -----------------------------------------------------------------

        private static byte[] Wav(int frames = 480, int sampleRate = 48_000)
        {
            var stream = new MemoryStream();
            WavWriter.Write(stream, new AudioBuffer(new StereoFrame[frames], sampleRate));
            return stream.ToArray();
        }

        private string Put(string relative, byte[]? bytes = null)
        {
            string path = Path.Combine(_folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes ?? Wav());
            return path;
        }

        [Fact]
        public void A_folder_lists_its_wav_files_with_forward_slashes_sorted_ignoring_case()
        {
            Put("snare.wav");
            Put("Kick.WAV");
            Put("drums/hat.wav");
            Put("notes.txt", [1, 2, 3]);

            var source = new FolderSampleSource(_folder);

            Assert.Equal(["drums/hat.wav", "Kick.WAV", "snare.wav"], source.Names);
        }

        [Fact]
        public void A_missing_folder_is_an_empty_library_not_an_error()
        {
            Assert.Empty(new FolderSampleSource(Path.Combine(_folder, "nowhere")).Names);
        }

        [Fact]
        public void Refresh_notices_files_added_since_the_last_listing()
        {
            Put("a.wav");
            var source = new FolderSampleSource(_folder) { RescanInterval = TimeSpan.FromHours(1) };
            Assert.Single(source.Names);

            Put("b.wav");
            Assert.Single(source.Names);          // still the cached listing
            source.Refresh();

            Assert.Equal(["a.wav", "b.wav"], source.Names);
        }

        [Fact]
        public void The_listing_is_rescanned_after_the_interval()
        {
            Put("a.wav");
            var source = new FolderSampleSource(_folder) { RescanInterval = TimeSpan.Zero };
            Assert.Single(source.Names);

            Put("b.wav");

            Assert.Equal(2, source.Names.Count);
        }

        [Fact]
        public void Load_decodes_the_file_and_reuses_the_result_until_it_changes()
        {
            string path = Put("kick.wav", Wav(frames: 480));
            var source = new FolderSampleSource(_folder);

            var first = source.Load("kick.wav");
            Assert.Equal(480, first.FrameCount);
            Assert.Same(first, source.Load("kick.wav"));

            File.WriteAllBytes(path, Wav(frames: 960));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(960, source.Load("kick.wav").FrameCount);
        }

        [Fact]
        public void Load_only_serves_names_in_the_listing_so_a_path_cannot_leave_the_folder()
        {
            Put("kick.wav");
            string outside = Path.Combine(Path.GetTempPath(), "sigil-outside-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(outside, Wav());
            try
            {
                var source = new FolderSampleSource(_folder);

                Assert.Throws<KeyNotFoundException>(() => source.Load("../" + Path.GetFileName(outside)));
                Assert.Throws<KeyNotFoundException>(() => source.Load(outside));
                Assert.Throws<KeyNotFoundException>(() => source.Load("KICK.wav.bak"));
            }
            finally
            {
                File.Delete(outside);
            }
        }

        [Fact]
        public void A_file_over_the_size_limit_is_refused_with_its_size()
        {
            Put("big.wav", Wav(frames: 100_000));
            var source = new FolderSampleSource(_folder, maxFileBytes: 1024);

            var error = Assert.Throws<InvalidDataException>(() => source.Load("big.wav"));

            Assert.Contains("big.wav", error.Message);
        }

        [Fact]
        public void A_file_that_is_not_a_wav_fails_with_the_decoders_reason()
        {
            Put("broken.wav", "this is not audio"u8.ToArray());

            Assert.Throws<InvalidDataException>(() => new FolderSampleSource(_folder).Load("broken.wav"));
        }

        [Fact]
        public void A_name_that_could_not_be_written_between_quotes_is_left_out_of_the_listing()
        {
            if (OperatingSystem.IsWindows())
                return; // Windows cannot create such a file

            Put("say \"hi\".wav");
            Put("ok.wav");

            Assert.Equal(["ok.wav"], new FolderSampleSource(_folder).Names);
        }

        [Fact]
        public void A_snippet_loads_from_a_real_folder_through_the_library()
        {
            Put("tone.wav", Wav(frames: 4800));
            var library = new SampleLibrary(new FolderSampleSource(_folder));
            SampleTestHost.SampleTestApi.Samples = library;

            var result = SnippetCompiler.Compile("Sample(Samples.Load(\"tone.wav\"))", _profile);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            var audio = result.Run().Render(0.05);

            Assert.True(audio.FrameCount > 0);
        }

        // ---- ReplSamples (the page's shelf) -----------------------------------------------------

        private ReplSamples Shelf() => new(Path.Combine(_folder, "shelf"));

        [Theory]
        [InlineData("kick.wav", "kick.wav")]
        [InlineData("KICK.WAV", "KICK.wav")]
        [InlineData("C:\\Users\\me\\Music\\My Kick (2).wav", "My Kick (2).wav")]
        [InlineData("/home/me/loop-120.wav", "loop-120.wav")]
        [InlineData("say \"hi\".wav", "say _hi_.wav")]
        [InlineData("../../evil.wav", "evil.wav")]
        [InlineData(".hidden.wav", "hidden.wav")]
        [InlineData("héllo wörld.wav", "héllo wörld.wav")]
        public void A_dropped_name_is_cleaned_to_something_a_snippet_can_type(string dropped, string expected)
        {
            Assert.Equal(expected, ReplSamples.CleanName(dropped));
        }

        [Theory]
        [InlineData("kick.mp3")]
        [InlineData("kick")]
        [InlineData(".wav")]
        [InlineData("")]
        [InlineData("   ")]
        public void Only_wav_files_are_taken(string dropped)
        {
            Assert.Null(ReplSamples.CleanName(dropped));
        }

        [Fact]
        public void A_very_long_name_is_shortened_but_stays_a_wav()
        {
            string cleaned = ReplSamples.CleanName(new string('a', 300) + ".wav")!;

            Assert.True(cleaned.Length <= 80);
            Assert.EndsWith(".wav", cleaned);
        }

        [Fact]
        public void Adding_a_wav_keeps_it_and_lists_it_with_its_length()
        {
            var shelf = Shelf();

            var result = shelf.Add("C:\\Music\\kick.wav", Wav(frames: 24_000, sampleRate: 48_000));

            Assert.True(result.Ok, result.Message);
            Assert.Equal("kick.wav", result.Name);
            Assert.Contains("0.5 s", result.Message);
            Assert.Contains("Samples.Load(\"kick.wav\")", result.Message);
            var entry = Assert.Single(shelf.Entries());
            Assert.Equal("kick.wav", entry.Name);
            Assert.Equal(0.5, entry.Seconds, 3);
            Assert.Equal(["kick.wav"], shelf.Source.Names);
        }

        [Fact]
        public void A_sample_added_to_the_shelf_can_be_loaded_straight_away()
        {
            var shelf = Shelf();
            shelf.Add("tone.wav", Wav(frames: 2_400));

            Assert.Equal(2_400, shelf.Source.Load("tone.wav").FrameCount);
        }

        [Fact]
        public void Adding_a_file_with_the_same_name_replaces_it()
        {
            var shelf = Shelf();
            shelf.Add("kick.wav", Wav(frames: 480));

            var again = shelf.Add("kick.wav", Wav(frames: 4_800));

            Assert.True(again.Ok);
            Assert.StartsWith("Replaced", again.Message);
            Assert.Single(shelf.Entries());
            Assert.Equal(4_800, shelf.Source.Load("kick.wav").FrameCount);
        }

        [Fact]
        public void Replacing_with_a_file_of_the_same_size_still_updates_the_listed_length()
        {
            var shelf = Shelf();
            // Same number of bytes, different sample rate, so a different length in seconds.
            shelf.Add("a.wav", Wav(frames: 480, sampleRate: 48_000));
            Assert.Equal(0.01, Assert.Single(shelf.Entries()).Seconds, 4);

            shelf.Add("a.wav", Wav(frames: 480, sampleRate: 24_000));

            Assert.Equal(0.02, Assert.Single(shelf.Entries()).Seconds, 4);
        }

        [Fact]
        public void A_file_that_is_not_audio_is_refused_with_the_reason_and_nothing_is_kept()
        {
            var shelf = Shelf();

            var result = shelf.Add("song.wav", "definitely not riff"u8.ToArray());

            Assert.False(result.Ok);
            Assert.Contains("song.wav", result.Message);
            Assert.Contains("RIFF", result.Message);
            Assert.Empty(shelf.Entries());
        }

        [Fact]
        public void A_wav_this_engine_cannot_play_is_refused_at_the_drop()
        {
            // Six channels is a valid WAV but not one the engine decodes; the drop says so instead of the snippet failing later.
            var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8.ToArray()); writer.Write(36 + 12); writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray()); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)6);
            writer.Write(48_000); writer.Write(48_000 * 12); writer.Write((ushort)12); writer.Write((ushort)16);
            writer.Write("data"u8.ToArray()); writer.Write(12); writer.Write(new byte[12]);
            writer.Flush();

            var result = Shelf().Add("surround.wav", stream.ToArray());

            Assert.False(result.Ok);
            Assert.Contains("channels", result.Message);
        }

        [Fact]
        public void Something_that_is_not_a_wav_name_is_refused_before_its_contents_are_looked_at()
        {
            var result = Shelf().Add("kick.mp3", Wav());

            Assert.False(result.Ok);
            Assert.Contains("only .wav", result.Message);
        }

        [Fact]
        public void A_file_over_the_size_cap_is_refused()
        {
            var big = Wav(frames: 4);
            Array.Resize(ref big, (int)ReplSamples.MaxFileBytes + 1);

            var result = Shelf().Add("big.wav", big);

            Assert.False(result.Ok);
            Assert.Contains("limit", result.Message);
        }

        [Fact]
        public void Remove_deletes_the_file_and_the_name_goes_from_the_listing()
        {
            var shelf = Shelf();
            shelf.Add("kick.wav", Wav());

            Assert.True(shelf.Remove("kick.wav"));

            Assert.Empty(shelf.Entries());
            Assert.Empty(shelf.Source.Names);
            Assert.False(shelf.Remove("kick.wav"));
        }

        [Fact]
        public void Remove_will_not_delete_a_file_outside_the_shelf()
        {
            var shelf = Shelf();
            string outside = Put("outside.wav");

            Assert.False(shelf.Remove("../outside.wav"));

            Assert.True(File.Exists(outside));
        }

        [Fact]
        public void Restore_puts_a_saved_sample_back_without_decoding_it()
        {
            var shelf = Shelf();

            Assert.True(shelf.Restore("kick.wav", Wav()));

            Assert.Equal(["kick.wav"], shelf.Source.Names);
        }

        [Fact]
        public void Restore_skips_what_no_longer_fits()
        {
            Assert.False(Shelf().Restore("kick.mp3", Wav()));
        }

        // ---- Samples that ship with the page ----------------------------------------------------

        private ReplSamples ShelfWithBuiltIn(string name = "demo.wav", int frames = 480) =>
            new(Path.Combine(_folder, "shelf"), new Dictionary<string, byte[]> { [name] = Wav(frames) });

        [Fact]
        public void A_built_in_sample_is_listed_loadable_and_marked_before_anything_is_dropped()
        {
            var shelf = ShelfWithBuiltIn();

            var entry = Assert.Single(shelf.Entries());
            Assert.Equal("demo.wav", entry.Name);
            Assert.True(entry.BuiltIn);
            Assert.True(entry.Seconds > 0);
            Assert.Equal(480, shelf.Source.Load("demo.wav").FrameCount);
        }

        [Fact]
        public void User_files_and_built_in_samples_are_listed_together_in_order()
        {
            var shelf = ShelfWithBuiltIn("m.wav");
            shelf.Add("a.wav", Wav());
            shelf.Add("z.wav", Wav());

            Assert.Equal(["a.wav", "m.wav", "z.wav"], shelf.Entries().Select(e => e.Name));
            Assert.Equal([false, true, false], shelf.Entries().Select(e => e.BuiltIn));
        }

        [Fact]
        public void A_built_in_sample_cannot_be_removed()
        {
            var shelf = ShelfWithBuiltIn();

            Assert.False(shelf.Remove("demo.wav"));

            Assert.Equal(["demo.wav"], shelf.Source.Names);
        }

        [Fact]
        public void A_dropped_file_with_a_built_in_name_wins_and_removing_it_brings_the_built_in_one_back()
        {
            var shelf = ShelfWithBuiltIn(frames: 480);

            shelf.Add("demo.wav", Wav(frames: 960));
            Assert.Equal(960, shelf.Source.Load("demo.wav").FrameCount);
            var shadowing = Assert.Single(shelf.Entries());
            Assert.False(shadowing.BuiltIn);

            Assert.True(shelf.Remove("demo.wav"));

            Assert.Equal(480, shelf.Source.Load("demo.wav").FrameCount);
            Assert.True(Assert.Single(shelf.Entries()).BuiltIn);
        }

        [Fact]
        public void Built_in_samples_do_not_count_against_the_users_limits()
        {
            var shelf = ShelfWithBuiltIn(frames: 480);
            var big = Wav(frames: 4);
            Array.Resize(ref big, (int)ReplSamples.MaxFileBytes);   // one file at the cap leaves room for two more

            Assert.True(shelf.Add("one.wav", big).Ok);
            Assert.True(shelf.Add("two.wav", big).Ok);
        }

        // The pitch of a harmonically rich sound: the lag between 60 and 400 Hz at which the signal best matches itself,
        // refined with a parabola through the peak and its neighbours.
        private static double FundamentalByAutocorrelation(float[] samples, int start, int length, int sampleRate)
        {
            int minLag = sampleRate / 400, maxLag = sampleRate / 60;
            var correlation = new double[maxLag + 2];
            for (int lag = minLag - 1; lag <= maxLag + 1; lag++)
            {
                double sum = 0;
                for (int i = start; i < start + length; i++)
                    sum += samples[i] * samples[i + lag];
                correlation[lag] = sum;
            }

            int best = minLag;
            for (int lag = minLag; lag <= maxLag; lag++)
                if (correlation[lag] > correlation[best])
                    best = lag;

            double before = correlation[best - 1], at = correlation[best], after = correlation[best + 1];
            double shift = 0.5 * (before - after) / (before - 2 * at + after);
            return sampleRate / (best + shift);
        }

        [Fact]
        public void The_page_ships_a_pluck_that_is_tuned_to_the_root_its_example_states()
        {
            var shipped = BuiltInSamples.FromAssembly();

            Assert.True(shipped.ContainsKey("pluck.wav"), "pluck.wav should be embedded");
            var info = WavReader.ReadInfo(shipped["pluck.wav"]);
            Assert.Equal(1, info.Channels);
            Assert.InRange(info.Duration, 1.0, 1.5);
            Assert.True(shipped["pluck.wav"].Length < 150 * 1024, "the built-in sample should stay small");

            var audio = WavReader.Read(shipped["pluck.wav"]);
            var mono = audio.ToMono();
            // The example plays it with root 130.81 Hz (C3); a pitched Sample() is only in tune if the file is. The zero-crossing
            // estimator is for clean tones and a pluck is rich in harmonics, so the pitch is read from the autocorrelation.
            double hz = FundamentalByAutocorrelation(mono, start: audio.SampleRate / 50, length: audio.SampleRate / 4, audio.SampleRate);
            Assert.InRange(hz, 130.3, 131.3);
            Assert.InRange(SignalAnalysis.Peak(mono), 0.5f, 0.99f);
            AudioAssert.IsSilent(mono.AsSpan(mono.Length - 8));
        }

        [Fact]
        public void The_file_example_sits_next_to_the_generated_one_and_is_heard_under_the_page_profile()
        {
            var names = ReplExamples.All.Select(e => e.Name).ToList();
            Assert.Equal(names.IndexOf("Sampler") + 1, names.IndexOf("Sampler (file)"));

            var result = SnippetCompiler.Compile(ReplExamples.All.Single(e => e.Name == "Sampler (file)").Code, ReplProfile.Instance);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            var audio = result.Run().Render(1.0, 48_000);

            AudioAssert.IsFinite(audio);
            Assert.True(SignalAnalysis.Peak(audio.ToMono()) > 0.05f, "the file example is silent");
        }

        [Fact]
        public void Completion_offers_the_built_in_sample_under_the_page_profile()
        {
            const string code = "Samples.Load(\"\")";

            var items = EditorServices.Complete(code, code.Length - 2, ReplProfile.Instance);

            Assert.Contains(items, i => i.Kind == "Sample" && i.Label == "\"pluck.wav\"");
        }
    }
}
