using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class PatternTests
{
    private const int SampleRate = 48_000;

    // A voice that is exactly 1 while its note is held and 0 afterwards, so the output shows the note timing.
    private static Sound Gate(Voice v) => Constant(1).Adsr(0, 0, 1, 0);

    // Notes default to velocity 0.8, which Play applies; full velocity keeps the expected levels round.
    private static IEnumerable<Note> Full(string names) => Notes.Parse(names).Select(n => n with { Velocity = 1f });

    private static float[] Mono(Sound sound, double seconds) => sound.Render(seconds, SampleRate).ToMono();

    private static float At(float[] samples, double seconds) => samples[(int)(seconds * SampleRate)];

    [Fact]
    public void Every_places_notes_one_step_apart_and_sets_their_length()
    {
        var pattern = Full("C4 E4 G4").Every(Beats.Eighth, gate: 2);

        Assert.Equal([0.0, 0.5, 1.0], pattern.Events.Select(e => e.Start));
        Assert.All(pattern.Events, e => Assert.Equal(1.0, e.Note.Beats));
        Assert.Equal(1.5, pattern.Length);
        Assert.False(pattern.IsLooping);
    }

    [Fact]
    public void Pattern_operations_return_new_patterns()
    {
        var pattern = Full("C4 E4").Every(Beats.Quarter);
        var transposed = pattern.Transpose(12).Loop();

        Assert.Equal([60, 64], pattern.Events.Select(e => e.Note.Pitch));
        Assert.Equal([72, 76], transposed.Events.Select(e => e.Note.Pitch));
        Assert.True(transposed.IsLooping);
        Assert.False(pattern.IsLooping);
    }

    [Fact]
    public void Play_starts_each_note_on_time_and_ends_it_when_its_length_is_up()
    {
        // 120 bpm: a beat is half a second, so an eighth note is 0.25 s. Notes at 0 s and 0.25 s.
        var sound = Full("C4 E4").Every(Beats.Eighth).Play(Gate, bpm: 120);
        var samples = Mono(sound, 1.0);

        Assert.Equal(1f, At(samples, 0.10));
        Assert.Equal(1f, At(samples, 0.35));
        Assert.Equal(0f, At(samples, 0.60));
        Assert.Equal(0f, At(samples, 0.95));
    }

    [Fact]
    public void Play_passes_pitch_and_scales_by_velocity()
    {
        var notes = new[] { new Note(69, Velocity: 0.5f) };
        var sound = notes.Every(Beats.Quarter).Play(v => Sine(v.Frequency).Adsr(0, 0, 1, 0));

        var audio = sound.Render(0.5, SampleRate);

        AudioAssert.FrequencyNear(audio, 440, 2.0);
        AudioAssert.PeakNear(audio, 0.5f, 0.01f);
    }

    [Fact]
    public void Overlapping_notes_sound_together()
    {
        // Each note is held for three steps, so three are sounding at once.
        var sound = Full("C4 E4 G4 B4").Every(Beats.Sixteenth, gate: 3).Play(Gate, bpm: 120, polyphony: 8);
        var samples = Mono(sound, 1.0);

        Assert.Equal(3f, At(samples, 0.30), 1e-4f);
    }

    [Fact]
    public void Polyphony_limits_how_many_notes_sound_and_cuts_the_oldest()
    {
        var sound = Full("C4 E4 G4 B4").Every(Beats.Sixteenth, gate: 4).Play(Gate, bpm: 120, polyphony: 2);
        var samples = Mono(sound, 1.0);

        // A cut voice fades out over a couple of milliseconds, so the count briefly exceeds the limit by one.
        Assert.True(samples.Max() <= 3.0001f, $"peak {samples.Max()}");
        Assert.True(samples.Count(x => x > 2.0001f) < 500, "voices overlapped for longer than the fade");
        Assert.Equal(2f, At(samples, 0.30), 1e-4f);
    }

    [Fact]
    public void Looping_patterns_repeat_and_finite_ones_stop()
    {
        // One note per quarter beat at 120 bpm: a 0.5 s cycle with the note held for 0.25 s.
        var one = Full("C4").Every(Beats.Quarter);
        Func<Voice, Sound> shortNote = _ => Constant(1).Adsr(0, 0, 1, 0, hold: 0.25);

        var looping = Mono(one.Loop().Play(shortNote, bpm: 120), 2.0);
        var once = Mono(one.Play(shortNote, bpm: 120), 2.0);

        Assert.Equal(1f, At(looping, 1.10));
        Assert.Equal(0f, At(looping, 1.40));
        Assert.Equal(1f, At(looping, 1.60));
        Assert.Equal(1f, At(once, 0.10));
        Assert.Equal(0f, At(once, 1.10));
    }

    [Fact]
    public void Finished_voices_are_released_so_long_playing_does_not_run_out_of_voices()
    {
        // Far more notes than voices, each short: everything must still sound at full level when it starts.
        var notes = Enumerable.Repeat(new Note(60, Velocity: 1f), 64);
        var sound = notes.Every(Beats.Sixteenth).Play(Gate, bpm: 240, polyphony: 2);
        var samples = Mono(sound, 3.0);

        Assert.Equal(1f, At(samples, 2.9));
    }

    [Fact]
    public void Play_is_a_normal_sound_that_can_be_composed_and_reused()
    {
        var arp = Full("C3 E3 G3").Every(Beats.Eighth, gate: 2).Loop()
            .Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.005, 0.1, 0.5, 0.2) * 0.3);
        var mix = (arp + Sine(55) * 0.2).Delay(0.2).Reverb().Limit();

        var first = mix.Render(1.0, SampleRate);
        AudioAssert.IsFinite(first);
        Assert.Equal(first.ToInterleaved(), mix.Render(1.0, SampleRate).ToInterleaved());
    }

    [Fact]
    public void Play_validates_its_arguments()
    {
        var pattern = Full("C4").Every(Beats.Quarter);

        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.Play(Gate, bpm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.Play(Gate, polyphony: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Full("C4").Every(0));
    }
}
