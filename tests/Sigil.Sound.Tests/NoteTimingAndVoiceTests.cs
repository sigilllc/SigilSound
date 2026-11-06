using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Behaviour that is easy to get wrong and easy to miss: which voice a full pool cuts, which note a note off ends, and how long
/// and where a pattern's notes sound at tempos where a cycle is not exactly one second.
/// </summary>
public class NoteTimingAndVoiceTests
{
    private const int Rate = 48_000;

    // 1 while a note is held, then a release of the given length. Each voice is scaled by its note's velocity, so a velocity
    // is a label: the output level says which notes are sounding.
    private static Sound Held(double release = 0.05) => Constant(1).Adsr(0, 0, 1, release);

    private static Sound GateVoice(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    private static SoundContext Output(Sound sound, HostClock? clock = null)
    {
        var output = new SoundContext(Rate, fadeSeconds: 0, clock: clock);
        output.Play(sound, fadeSeconds: 0);
        return output;
    }

    private static float[] Render(SoundContext output, double seconds)
    {
        var frames = new StereoFrame[(int)(seconds * Rate)];
        output.Render(frames);
        return frames.Select(f => f.ToMono()).ToArray();
    }

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * Rate)];

    // The first frame at or after `from` where a gate goes up (or comes down), so a note's start and end are checked to the frame.
    private static int Edge(float[] samples, int from, bool rising)
    {
        for (int i = Math.Max(from, 1); i < samples.Length; i++)
            if (rising ? samples[i] > 0.5f && samples[i - 1] <= 0.5f : samples[i] <= 0.5f && samples[i - 1] > 0.5f)
                return i;
        throw new InvalidOperationException("No edge found.");
    }

    // 97 bpm on purpose: a beat is 29,690.7 frames, so notes start and end part-way through a render block. At a tempo whose beats are
    // whole blocks (90 bpm is exactly 125 blocks) a note that began at the top of its block would look the same as a correct one.
    // The pattern has notes at beats 0, 2, 4 and 6, each one beat long.
    private const double Bpm97 = 97;

    private static int BeatFrame(double beats) => (int)Math.Round(beats * 60.0 / Bpm97 * Rate);

    private static void AssertNotesStartAndEndOnTheBeat(float[] samples)
    {
        Assert.InRange(Edge(samples, 1, rising: false), BeatFrame(1) - 2, BeatFrame(1) + 2);                 // the first note ends at beat 1
        Assert.InRange(Edge(samples, 1, rising: true), BeatFrame(2) - 2, BeatFrame(2) + 2);                  // the second starts at beat 2
        Assert.InRange(Edge(samples, BeatFrame(2) + 10, rising: false), BeatFrame(3) - 2, BeatFrame(3) + 2);
        Assert.InRange(Edge(samples, BeatFrame(3) + 10, rising: true), BeatFrame(4) - 2, BeatFrame(4) + 2);  // the loop's second pass
        Assert.InRange(Edge(samples, BeatFrame(4) + 10, rising: false), BeatFrame(5) - 2, BeatFrame(5) + 2);
        Assert.InRange(Edge(samples, BeatFrame(5) + 10, rising: true), BeatFrame(6) - 2, BeatFrame(6) + 2);
    }

    // --- A full voice pool -------------------------------------------------------------------------------------------

    [Fact]
    public void A_full_pool_cuts_the_oldest_held_note()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(), polyphony: 2));

        keys.NoteOn(60, 0.1f);
        Render(output, 0.05);
        keys.NoteOn(62, 0.2f);
        Render(output, 0.05);
        keys.NoteOn(64, 0.4f);
        var samples = Render(output, 0.2);

        // The first note (0.1) was cut; 0.2 + 0.4 remain. Cutting the newest or the middle one would leave 0.5 or 0.3.
        Assert.Equal(0.6f, samples[^1], 3);
    }

    [Fact]
    public void A_full_pool_cuts_a_note_that_is_dying_away_before_one_that_is_held()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 2), polyphony: 2));

        keys.NoteOn(60, 0.1f);
        keys.NoteOn(62, 0.2f);
        keys.NoteOff(62);                 // the newer note starts a long release
        Render(output, 0.05);
        keys.NoteOn(64, 0.4f);
        var samples = Render(output, 0.2);

        // The releasing note (0.2) goes, not the oldest held one (0.1): 0.1 + 0.4. Cutting the oldest would leave the
        // releasing note's tail on top of the new one, about 0.57.
        Assert.Equal(0.5f, samples[^1], 3);
    }

    [Fact]
    public void A_note_off_ends_only_the_note_at_that_pitch()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(), polyphony: 4));

        keys.NoteOn(60, 0.1f);
        keys.NoteOn(62, 0.2f);
        keys.NoteOff(62);
        var samples = Render(output, 0.5);

        Assert.Equal(0.1f, samples[^1], 3);
        Assert.True(keys.IsHeld(60));
        Assert.False(keys.IsHeld(62));
    }

    [Fact]
    public void A_note_off_for_a_pitch_that_is_not_held_changes_nothing()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(), polyphony: 4));

        keys.NoteOn(60, 0.1f);
        keys.NoteOn(62, 0.2f);
        keys.NoteOff(70);
        var samples = Render(output, 0.5);

        Assert.Equal(0.3f, samples[^1], 3);
    }

    // --- Where a pattern's notes sound -------------------------------------------------------------------------------

    // One cycle is four beats: 2 s at 120 bpm, 2.667 s at 90 bpm, 0.5 s at 480 bpm. A step is a quarter of that.
    [Theory]
    [InlineData(120.0, 0.5, 0.4, 0.6)]
    [InlineData(90.0, 2.0 / 3.0, 0.6, 0.75)]
    [InlineData(480.0, 0.125, 0.1, 0.15)]
    public void A_note_lasts_for_its_event_at_any_tempo(double bpm, double step, double during, double after)
    {
        var samples = Render(Output(Mini.Notes("c3:1 ~ e3:1 ~").Play(GateVoice, bpm: bpm)), 4 * step + 0.1);

        Assert.Equal(1f, At(samples, during));
        Assert.Equal(0f, At(samples, after));
        Assert.Equal(0f, At(samples, step + 0.01));                       // the rest between the two notes
        Assert.Equal(1f, At(samples, 2 * step + during));                 // the second note, one cycle half in
        Assert.Equal(0f, At(samples, 2 * step + after));
    }

    [Fact]
    public void A_pattern_without_a_tempo_follows_the_host_clock()
    {
        // 90 bpm is not the 120 a pattern falls back to, so a pattern that ignored the clock would be audibly off.
        var clock = new HostClock(Rate, bpm: 90);
        var samples = Render(Output(Mini.Notes("c3:1 ~ e3:1 ~").Play(GateVoice), clock), 3.0);

        double step = 60.0 / 90;              // a quarter of a four-beat cycle is one beat
        Assert.Equal(1f, At(samples, step * 0.5));
        Assert.Equal(0f, At(samples, step * 1.5));
        Assert.Equal(1f, At(samples, step * 2.5));
        Assert.Equal(0f, At(samples, step * 3.5));
    }

    [Fact]
    public void A_looping_pattern_repeats_one_cycle_after_the_last_under_the_host_clock()
    {
        var clock = new HostClock(Rate, bpm: 120);      // a cycle is 2 s, a step 0.5 s
        var samples = Render(Output(Mini.Notes("c3:1 ~ e3:1 ~").Play(GateVoice), clock), 5.0);

        Assert.Equal(1f, At(samples, 0.25));            // cycle 0: c3 at 0, e3 at 1
        Assert.Equal(1f, At(samples, 1.25));
        Assert.Equal(1f, At(samples, 2.25));            // cycle 1: 2 and 3
        Assert.Equal(1f, At(samples, 3.25));
        Assert.Equal(1f, At(samples, 4.25));            // cycle 2
        Assert.Equal(0f, At(samples, 0.75));            // the gaps stay gaps in every cycle
        Assert.Equal(0f, At(samples, 2.75));
        Assert.Equal(0f, At(samples, 3.75));
    }

    // --- Plain patterns (notes with their own lengths) ---------------------------------------------------------------

    // A note every two beats, held for half of that: one beat. Looped, so the second repeat starts at beat 4.
    private static Pattern OneBeatNoteEveryTwo() =>
        new[] { new Note(60, 1f), new Note(62, 1f) }.Every(2, gate: 0.5).Loop();

    [Fact]
    public void A_plain_patterns_notes_last_their_length_at_an_explicit_tempo()
    {
        // 97 bpm: a beat is 0.619 s, so notes sound 0 to 0.619 s, 1.237 to 1.856 s, then again from 2.474 s and 3.711 s.
        var samples = Render(Output(OneBeatNoteEveryTwo().Play(GateVoice, bpm: Bpm97)), 5.0);

        Assert.Equal(1f, At(samples, 0.5));
        Assert.Equal(0f, At(samples, 0.8));
        Assert.Equal(1f, At(samples, 1.5));
        Assert.Equal(0f, At(samples, 2.1));
        Assert.Equal(1f, At(samples, 2.8));      // the loop's second pass
        Assert.Equal(0f, At(samples, 3.3));
        Assert.Equal(1f, At(samples, 4.0));
        Assert.Equal(0f, At(samples, 4.6));
        AssertNotesStartAndEndOnTheBeat(samples);
    }

    [Fact]
    public void A_plain_patterns_notes_last_their_length_under_the_host_clock()
    {
        var clock = new HostClock(Rate, bpm: Bpm97);
        var samples = Render(Output(OneBeatNoteEveryTwo().Play(GateVoice), clock), 5.0);

        Assert.Equal(1f, At(samples, 0.5));
        Assert.Equal(0f, At(samples, 0.8));
        Assert.Equal(1f, At(samples, 1.5));
        Assert.Equal(0f, At(samples, 2.1));
        Assert.Equal(1f, At(samples, 2.8));
        Assert.Equal(0f, At(samples, 3.3));
        Assert.Equal(1f, At(samples, 4.0));
        Assert.Equal(0f, At(samples, 4.6));
        AssertNotesStartAndEndOnTheBeat(samples);
    }

    // --- Argument limits ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void Polyphony_accepts_the_limits(int polyphony)
    {
        var keys = new NoteInput();

        Assert.NotNull(keys.Play(v => Held(), polyphony));
        Assert.NotNull(Mini.Notes("c3").Play(GateVoice, polyphony: polyphony));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Polyphony_rejects_values_just_outside_the_limits(int polyphony)
    {
        var keys = new NoteInput();

        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Play(v => Held(), polyphony));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mini.Notes("c3").Play(GateVoice, polyphony: polyphony));
    }

    [Theory]
    [InlineData(1, 1, 1)]   // one step, one pulse: the whole cycle
    [InlineData(0, 4, 0)]   // no pulses: silence, not an error
    [InlineData(4, 4, 4)]   // every step
    [InlineData(3, 8, 3)]
    public void Euclid_accepts_the_edges_of_its_range(int pulses, int steps, int expected)
    {
        var events = Mini.Notes("c3").Euclid(pulses, steps).Cycle(0).Count(e => e.HasOnset);

        Assert.Equal(expected, events);
    }

    [Theory]
    [InlineData(5, 4)]      // more pulses than steps
    [InlineData(-1, 4)]
    [InlineData(1, 0)]      // no steps
    public void Euclid_rejects_values_outside_its_range(int pulses, int steps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mini.Notes("c3").Euclid(pulses, steps));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Degrade_accepts_a_chance_of_none_and_of_all(double amount)
    {
        Assert.NotNull(Mini.Notes("c3 e3 g3 b3").Degrade(amount));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Degrade_rejects_a_chance_outside_zero_to_one(double amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Mini.Notes("c3 e3 g3 b3").Degrade(amount));
    }
}
