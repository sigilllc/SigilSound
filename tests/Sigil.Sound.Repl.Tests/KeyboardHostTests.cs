using ReplHost;
using Sigil.Sound.Testing;

namespace Sigil.Sound.Repl.Tests;

/// <summary>
/// The REPL page's keyboard as snippets see it: <c>Keys</c> reaches them through the page's own <see cref="SnippetProfile"/>,
/// and the compiler and the library stay unaware of it.
/// </summary>
/// <remarks>
/// <c>ReplApi.Keys</c> is one instance for the whole process, like the page's, so every test releases its keys.
/// </remarks>
public sealed class KeyboardHostTests : IDisposable
{
    private const int SampleRate = 48_000;

    private static readonly SnippetProfile Profile = ReplProfile.Instance;

    public KeyboardHostTests() => ReplApi.Keys.AllNotesOff();

    public void Dispose() => ReplApi.Keys.AllNotesOff();

    private static SoundContext Play(string code)
    {
        var result = SnippetCompiler.Compile(code, Profile);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var output = new SoundContext(SampleRate, fadeSeconds: 0);
        output.Play(result.Run(), fadeSeconds: 0);
        return output;
    }

    private static float[] Render(SoundContext output, double seconds)
    {
        var frames = new StereoFrame[(int)(seconds * SampleRate)];
        output.Render(frames);
        return frames.Select(f => f.ToMono()).ToArray();
    }

    [Fact]
    public void A_snippet_can_play_the_keys_under_the_pages_profile()
    {
        using var output = Play("Keys.Play(v => Sine(v.Frequency))");

        ReplApi.Keys.NoteOn(69, 1f); // A4
        var samples = Render(output, 0.5);

        Assert.InRange(SignalAnalysis.EstimateFrequency(samples.AsSpan(SampleRate / 10), SampleRate), 439, 441);
    }

    [Fact]
    public void Keys_means_nothing_to_the_default_policy_so_the_library_stays_unaware_of_the_page()
    {
        var result = SnippetCompiler.Compile("Keys.Play(v => Sine(v.Frequency))");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "CS0103");
    }

    [Fact]
    public void Keys_held_while_a_snippet_is_edited_keep_sounding_in_the_new_version()
    {
        using var output = Play("Keys.Play(v => Sine(v.Frequency).Adsr(0, 0, 1, 0.1))");
        ReplApi.Keys.NoteOn(60, 1f);
        Render(output, 0.2);

        // The page does exactly this on every edit: compile the new text and play it over the old one.
        var edited = SnippetCompiler.Compile("Keys.Play(v => Saw(v.Frequency).Adsr(0, 0, 1, 0.1)) * 0.5", Profile);
        Assert.True(edited.Success, string.Join("\n", edited.Diagnostics));
        output.Play(edited.Run(), fadeSeconds: 0);
        var after = Render(output, 0.2);

        Assert.True(SignalAnalysis.Peak(after) > 0.2f, "the held key should still be heard after the edit");
    }

    [Fact]
    public void Every_example_on_the_page_compiles_and_renders_under_its_profile()
    {
        Assert.Contains(ReplExamples.All, e => e.Name == "Keyboard");

        foreach (var example in ReplExamples.All)
        {
            var result = SnippetCompiler.Compile(example.Code, Profile);
            Assert.True(result.Success, $"{example.Name}: {string.Join("\n", result.Diagnostics)}");
            AudioAssert.IsFinite(result.Run().Render(0.05, SampleRate));
        }
    }

    [Fact]
    public void The_keyboard_example_is_silent_until_a_key_is_played_and_then_sounds()
    {
        var example = ReplExamples.All.Single(e => e.Name == "Keyboard");
        using var output = Play(example.Code);

        AudioAssert.IsSilent(Render(output, 0.1));

        ReplApi.Keys.NoteOn(64, 0.8f);
        Assert.True(SignalAnalysis.Rms(Render(output, 0.3)) > 0.005);
    }

    [Fact]
    public void The_page_profile_does_not_open_up_the_rest_of_the_page()
    {
        var result = SnippetCompiler.Compile("var p = Sigil.Sound.Repl.ReplProfile.Instance;\nSine(440)", Profile);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC08");
    }

    [Fact]
    public void The_page_profile_still_applies_every_base_restriction()
    {
        var result = SnippetCompiler.Compile("System.IO.File.Exists(\"x\");\nKeys.Play(v => Sine(v.Frequency))", Profile);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.IsError && d.Id == "SIGIL_SEC08");
    }

    [Fact]
    public void Keys_and_Samples_are_the_only_things_the_page_api_gives_a_snippet()
    {
        // Everything public on this type is callable from a pasted snippet. Adding a member should be a decision.
        var members = typeof(ReplApi).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                                                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m is not System.Reflection.ConstructorInfo)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Keys", "Samples", "get_Keys", "get_Samples"], members);
    }

    [Fact]
    public void Completion_offers_Keys_and_its_members_under_the_page_profile_only()
    {
        Assert.Contains(EditorServices.Complete("Ke", 2, Profile), c => c.Label == "Keys");
        Assert.DoesNotContain(EditorServices.Complete("Ke", 2), c => c.Label == "Keys");

        var members = EditorServices.Complete("Keys.", 5, Profile).Select(c => c.Label).ToArray();
        Assert.Contains("Play", members);
        Assert.Contains("NoteOn", members);
        Assert.Contains("NoteOff", members);
    }

    [Fact]
    public void A_snippet_that_presses_keys_itself_does_not_break_anything()
    {
        // Allowed on purpose: Keys is an ordinary NoteInput. The page's keyboard and the snippet share the same held set.
        using var output = Play("Keys.NoteOn(60);\nKeys.Play(v => Sine(v.Frequency).Adsr(0, 0, 1, 0.05))");

        Assert.True(SignalAnalysis.Peak(Render(output, 0.2)) > 0.2f);
    }
}
