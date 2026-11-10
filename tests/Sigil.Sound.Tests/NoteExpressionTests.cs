using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

/// <summary>
/// Per-note expression, as a listener hears it: notes that are their own objects, and a voice that follows the bend, pressure and
/// slide the player gives each one. Everything renders offline through a <see cref="SoundContext"/>.
/// </summary>
public class NoteExpressionTests
{
    private const int SampleRate = 48_000;

    private static SoundContext Output(Sound sound)
    {
        var output = new SoundContext(SampleRate, fadeSeconds: 0);
        output.Play(sound, fadeSeconds: 0);
        return output;
    }

    private static float[] Render(SoundContext output, double seconds)
    {
        var frames = new StereoFrame[(int)(seconds * SampleRate)];
        output.Render(frames);
        return frames.Select(f => f.ToMono()).ToArray();
    }

    private static int FramesFor(double seconds) => (int)(seconds * SampleRate);

    private static double Frequency(float[] samples, double fromSeconds = 0.15) =>
        SignalAnalysis.EstimateFrequency(samples.AsSpan(FramesFor(fromSeconds)), SampleRate);

    // A sine that is on while its note is held and rings out over `release` seconds.
    private static Sound Tone(Voice v, double release = 0.05) => Sine(v.Frequency).Adsr(0, 0, 1, release);

    // --- Pitch -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 440)]
    [InlineData(12, 880)]
    [InlineData(-12, 220)]
    [InlineData(7, 659.26)]    // a fifth up, equal tempered
    public void Bending_a_note_moves_the_pitch_of_a_voice_written_with_Frequency(double semitones, double expected)
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Tone(v)));
        var note = keys.Start(69, 1f);
        note.Bend = semitones;

        var samples = Render(output, 0.6);

        Assert.InRange(Frequency(samples), expected * 0.99, expected * 1.01);
    }

    [Fact]
    public void A_bend_made_while_the_note_sounds_is_followed_and_is_smooth()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Tone(v)));
        var note = keys.Start(69, 1f);
        Render(output, 0.3);

        note.Bend = 12;
        var after = Render(output, 0.5);

        Assert.InRange(Frequency(after, 0.2), 870, 890);
        // A step in frequency would put a discontinuity in the waveform. A gliding one keeps every sample close to its neighbour.
        double largestJump = 0;
        for (int i = 1; i < after.Length; i++)
            largestJump = Math.Max(largestJump, Math.Abs(after[i] - after[i - 1]));
        Assert.True(largestJump < 0.2, $"the waveform jumped by {largestJump}");
    }

    [Fact]
    public void Pitch_includes_the_bend_in_semitones()
    {
        var keys = new NoteInput();
        // The pitch as a number, so the bend is visible directly: 69 + 2 semitones, divided down to stay in range.
        using var output = Output(keys.Play(v => v.Pitch * (1.0 / 100)));
        var note = keys.Start(69, 1f);
        note.Bend = 2;

        var samples = Render(output, 0.3);

        Assert.Equal(0.71f, samples[^1], 3);
    }

    // --- Pressure, slide, bend as signals ----------------------------------------------------------------------------------

    [Fact]
    public void Pressure_and_slide_reach_the_voice_and_a_note_nobody_shapes_reads_zero()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => v.Pressure * 0.5 + v.Slide * 0.25));
        var shaped = keys.Start(60, 1f);
        shaped.Pressure = 1;
        shaped.Slide = 1;

        Assert.Equal(0.75f, Render(output, 0.2)[^1], 3);

        // A note with no handle: nobody is shaping it, so it reads zero (a fresh input, since this voice has no envelope to end).
        var plain = new NoteInput();
        using var plainOutput = Output(plain.Play(v => v.Pressure * 0.5 + v.Slide * 0.25 + v.Bend * 0.1));
        plain.NoteOn(62, 1f);
        Assert.Equal(0f, Render(plainOutput, 0.2)[^1], 3);
    }

    [Fact]
    public void An_expression_signal_glides_to_a_new_value_instead_of_stepping()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => v.Pressure * 1.0));
        var note = keys.Start(60, 1f);
        Render(output, 0.1);

        note.Pressure = 1;
        var samples = Render(output, 0.1);

        Assert.True(samples[0] < 0.1f, $"it started at {samples[0]} instead of gliding up from 0");
        Assert.True(samples[FramesFor(0.005)] > samples[0] && samples[FramesFor(0.005)] < 0.9f);
        for (int i = 1; i < samples.Length; i++)
            Assert.True(samples[i] >= samples[i - 1] - 1e-6f, "the glide should only rise");
        Assert.Equal(1f, samples[^1], 2);
    }

    [Fact]
    public void Values_are_clamped_and_non_numbers_are_refused()
    {
        var note = new NoteInput().Start(60);

        note.Pressure = 5;
        note.Slide = -1;
        note.Bend = 500;
        Assert.Equal(1, note.Pressure);
        Assert.Equal(0, note.Slide);
        Assert.Equal(PlayingNote.MaxBend, note.Bend);
        note.Bend = -500;
        Assert.Equal(-PlayingNote.MaxBend, note.Bend);
        Assert.Throws<ArgumentOutOfRangeException>(() => note.Bend = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => note.Pressure = double.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => note.Slide = double.NaN);
        Assert.Equal(PlayingNote.MaxBend * -1, note.Bend);
    }

    // --- Notes are their own objects ---------------------------------------------------------------------------------------

    [Fact]
    public void Two_notes_at_one_pitch_are_independent_and_each_can_be_released_alone()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Sine(v.Frequency).Adsr(0, 0, 0.5, 0.02)));
        var first = keys.Start(69, 1f);
        var second = keys.Start(69, 1f);
        var both = Render(output, 0.3);

        first.Release();
        var onlySecond = Render(output, 0.3);

        Assert.InRange(SignalAnalysis.Peak(both.AsSpan(FramesFor(0.1))), 0.95f, 1.01f);                    // two in phase: 2 x 0.5
        Assert.InRange(SignalAnalysis.Peak(onlySecond.AsSpan(FramesFor(0.1))), 0.45f, 0.55f);              // one left
        Assert.False(first.IsHeld);
        Assert.True(second.IsHeld);
    }

    [Fact]
    public void Two_notes_at_one_pitch_can_be_bent_apart()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Tone(v)));
        keys.Start(60, 1f);
        keys.Start(60, 1f).Bend = 12;

        var samples = Render(output, 0.6);

        Assert.True(Measure.Power(samples.AsSpan(FramesFor(0.2)), 261.63) > 0.05, "the unbent note should sound at middle C");
        Assert.True(Measure.Power(samples.AsSpan(FramesFor(0.2)), 523.25) > 0.05, "the bent note should sound an octave above");
    }

    [Fact]
    public void NoteOff_by_pitch_does_not_end_a_note_that_has_a_handle()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Tone(v, release: 0.02)));
        var note = keys.Start(69, 1f);
        Render(output, 0.2);

        keys.NoteOff(69);
        var samples = Render(output, 0.3);

        Assert.True(note.IsHeld);
        Assert.True(SignalAnalysis.Peak(samples.AsSpan(FramesFor(0.2))) > 0.9f, "the held note must keep sounding");
    }

    [Fact]
    public void Releasing_a_handle_note_does_not_end_a_NoteOn_note_of_the_same_pitch()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Sine(v.Frequency).Adsr(0, 0, 0.5, 0.02)));
        keys.NoteOn(69, 1f);
        var note = keys.Start(69, 1f);
        Render(output, 0.2);

        note.Release();
        var samples = Render(output, 0.3);

        Assert.InRange(SignalAnalysis.Peak(samples.AsSpan(FramesFor(0.1))), 0.45f, 0.55f);
        Assert.True(keys.IsHeld(69));
    }

    [Fact]
    public void Release_is_safe_to_repeat_and_AllNotesOff_releases_handles()
    {
        var keys = new NoteInput();
        var note = keys.Start(60);
        var other = keys.Start(64);

        Assert.True(keys.IsHeld(60));
        note.Release();
        note.Release();
        Assert.False(note.IsHeld);
        Assert.False(keys.IsHeld(60));

        keys.AllNotesOff();
        Assert.False(other.IsHeld);
        Assert.False(keys.IsHeld(64));
    }

    [Fact]
    public void A_sound_built_later_starts_with_the_held_notes_at_their_current_bend()
    {
        var keys = new NoteInput();
        var note = keys.Start(69, 1f);
        note.Bend = 12;

        using var output = Output(keys.Play(v => Tone(v)));
        var samples = Render(output, 0.6);

        Assert.InRange(Frequency(samples, 0.05), 870, 890);   // at pitch from the first block, with no glide up from 440
    }

    [Fact]
    public void A_voice_that_is_reused_starts_clean_and_does_not_inherit_the_last_notes_bend()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Tone(v, release: 0.01), polyphony: 1));
        var bent = keys.Start(69, 1f);
        bent.Bend = 12;
        Render(output, 0.3);
        bent.Release();
        Render(output, 0.6);

        keys.NoteOn(69, 1f);
        var samples = Render(output, 0.4);

        Assert.InRange(Frequency(samples, 0.1), 435, 445);
    }

    [Fact]
    public void Notes_without_a_handle_sound_exactly_as_before()
    {
        var plain = new NoteInput();
        using var plainOutput = Output(plain.Play(v => Tone(v)));
        plain.NoteOn(69, 1f);

        var handled = new NoteInput();
        using var handledOutput = Output(handled.Play(v => Tone(v)));
        handled.Start(69, 1f);

        Assert.Equal(Render(plainOutput, 0.3), Render(handledOutput, 0.3));
    }

    [Fact]
    public void Start_validates_its_arguments()
    {
        var keys = new NoteInput();

        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Start(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Start(128));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Start(60, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Start(60, 1.5f));
    }

    [Fact]
    public async Task Expression_can_be_set_from_another_thread_while_rendering()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Saw(v.Frequency).LowPass(400 + v.Slide * 3000) * (0.2 + v.Pressure * 0.5)));
        var note = keys.Start(57, 1f);
        using var stop = new CancellationTokenSource();
        var hand = Task.Run(() =>
        {
            var random = new Random(1);
            while (!stop.IsCancellationRequested)
            {
                note.Bend = random.NextDouble() * 4 - 2;
                note.Pressure = random.NextDouble();
                note.Slide = random.NextDouble();
            }
        });

        var samples = Render(output, 1.0);
        stop.Cancel();
        await hand;

        Assert.False(SignalAnalysis.HasNonFinite(samples));
        Assert.True(SignalAnalysis.Peak(samples) > 0.01f);
    }

    // --- A note modifier: every note falls by itself -----------------------------------------------------------------------

    // A function that changes a note over its own life. Fn's time restarts with each note inside a voice, so this needs no
    // clock: it multiplies the pitch by a ratio that falls from 1 to 2^-octaves over `seconds`.
    private static Sound Fall(Sound pitch, double octaves, double seconds) =>
        pitch * Fn(t => Math.Pow(2, -octaves * Math.Min(t / seconds, 1.0)));

    [Fact]
    public void A_pitch_decay_function_makes_each_note_fall_from_its_own_start()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Sine(Fall(v.Frequency, octaves: 1, seconds: 1)).Adsr(0, 0, 1, 0.05), polyphony: 4));
        keys.Start(81, 1f);   // A5, 880 Hz
        var early = Render(output, 0.2);
        Render(output, 0.6);
        var late = Render(output, 0.4);

        Assert.True(Frequency(early, 0.05) > 800, "a young note is still near its own pitch");
        Assert.InRange(Frequency(late, 0.1), 430, 470);   // settled an octave down

        // A second note starts high again, whatever the first has done.
        keys.Start(81, 1f);
        var restarted = Render(output, 0.2);
        Assert.True(SignalAnalysis.Peak(restarted) > 1.0f, "two notes sounding");
    }

    [Fact]
    public void The_modifier_and_the_players_bend_combine()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Sine(Fall(v.Frequency, octaves: 1, seconds: 0.1)).Adsr(0, 0, 1, 0.05)));
        var note = keys.Start(69, 1f);   // 440 Hz, falling to 220
        note.Bend = 12;                   // lifted an octave: settles at 440
        var samples = Render(output, 0.8);

        Assert.InRange(Frequency(samples, 0.5), 430, 450);
    }
}
