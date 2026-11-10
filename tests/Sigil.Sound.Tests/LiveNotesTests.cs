using Sigil.Sound.Testing;
using static Sigil.Sound.Sounds;

namespace Sigil.Sound.Tests;

public class LiveNotesTests
{
    private const int SampleRate = 48_000;

    // A voice that is exactly `level` while its note is held, ramps down over `release` seconds afterwards and is 0 after.
    private static Sound Held(double level = 1, double release = 0.1) => Constant(1).Adsr(0, 0, level, release);

    private static SoundContext Output(Sound sound, HostClock? clock = null)
    {
        var output = new SoundContext(SampleRate, fadeSeconds: 0, clock: clock);
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

    [Fact]
    public void A_note_on_sounds_at_the_pitch_of_the_note()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Sine(v.Frequency)));

        keys.NoteOn(69, 1f); // A4
        var samples = Render(output, 0.5);

        Assert.InRange(SignalAnalysis.EstimateFrequency(samples.AsSpan(FramesFor(0.1)), SampleRate), 439, 441);
        Assert.InRange(SignalAnalysis.Peak(samples), 0.95f, 1.01f);
    }

    [Fact]
    public void Nothing_sounds_before_a_note_is_played()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held()));

        AudioAssert.IsSilent(Render(output, 0.2));
    }

    [Fact]
    public void A_held_note_sustains_for_as_long_as_the_key_is_down()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(level: 0.8)));

        keys.NoteOn(60, 1f);
        var samples = Render(output, 3.0);

        // Far longer than any envelope stage: the note has no length of its own, only the key does.
        Assert.Equal(0.8f, samples[FramesFor(1.0)], 3);
        Assert.Equal(0.8f, samples[^1], 3);
    }

    [Fact]
    public void A_note_off_releases_from_the_level_the_voice_had_reached()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(level: 0.8, release: 0.2)));

        keys.NoteOn(60, 1f);
        Render(output, 0.5);
        keys.NoteOff(60);
        var tail = Render(output, 0.5);

        Assert.Equal(0.8f, tail[0], 2);                            // no jump at the release
        Assert.InRange(tail[FramesFor(0.1)], 0.3f, 0.5f);          // halfway down the 0.2 s release
        Assert.Equal(0f, tail[FramesFor(0.3)], 3);                 // finished
        AudioAssert.IsSilent(tail.AsSpan(FramesFor(0.3)));
    }

    [Fact]
    public void The_gate_signal_is_open_while_the_key_is_down_and_closed_after()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Constant(1).Gain(v.Gate)));

        keys.NoteOn(60, 1f);
        var down = Render(output, 0.3);
        keys.NoteOff(60);
        Render(output, 0.01);
        var after = Render(output, 0.2);

        Assert.All(down, s => Assert.Equal(1f, s));
        AudioAssert.IsSilent(after);
    }

    [Fact]
    public void A_very_quick_tap_still_plays_its_attack()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Constant(1).Adsr(0.002, 0, 1, 0.1)));

        // Both land before the next block renders: the note must still last a block, not zero frames.
        keys.NoteOn(60, 1f);
        keys.NoteOff(60);
        var samples = Render(output, 0.3);

        Assert.True(SignalAnalysis.Peak(samples) > 0.5f, "a tap shorter than a block should still be heard");
    }

    [Fact]
    public void Velocity_scales_the_voice()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held()));

        keys.NoteOn(60, 0.5f);
        var samples = Render(output, 0.2);

        Assert.Equal(0.5f, samples[^1], 3);
    }

    [Fact]
    public void A_note_on_with_velocity_zero_is_a_note_off()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 0.05)));

        keys.NoteOn(60, 1f);
        Render(output, 0.2);
        keys.NoteOn(60, 0f);

        Assert.False(keys.IsHeld(60));
        AudioAssert.IsSilent(Render(output, 0.4).AsSpan(FramesFor(0.2)));
    }

    [Fact]
    public void Notes_that_overlap_are_mixed()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held()));

        keys.NoteOn(60, 1f);
        keys.NoteOn(64, 1f);
        keys.NoteOn(67, 1f);

        Assert.Equal(3f, Render(output, 0.2)[^1], 3);
    }

    [Fact]
    public void Only_the_matching_pitch_is_released()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 0.05)));

        keys.NoteOn(60, 1f);
        keys.NoteOn(64, 1f);
        Render(output, 0.1);
        keys.NoteOff(60);

        Assert.Equal(1f, Render(output, 0.3)[^1], 3);
        Assert.True(keys.IsHeld(64));
    }

    [Fact]
    public void Past_the_polyphony_limit_the_oldest_held_note_is_cut()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(), polyphony: 2));

        keys.NoteOn(60, 1f);
        Render(output, 0.05);
        keys.NoteOn(62, 1f);
        Render(output, 0.05);
        keys.NoteOn(64, 1f);

        // Two voices remain; the third note took the oldest one's place.
        Assert.Equal(2f, Render(output, 0.2)[^1], 3);
    }

    [Fact]
    public void A_voice_that_is_already_releasing_is_cut_before_a_held_one()
    {
        var keys = new NoteInput();
        // A long release tail: if the wrong voice were cut, that tail would still be audible next to the held notes.
        using var output = Output(keys.Play(v => Held(release: 5), polyphony: 2));

        keys.NoteOn(60, 1f);
        keys.NoteOn(62, 1f);
        Render(output, 0.1);
        keys.NoteOff(62); // the newer of the two starts dying
        Render(output, 0.05);
        keys.NoteOn(64, 1f);

        // 60 and 64 are held; the dying 62 gave way. Cutting the oldest voice (60) instead would leave 62's tail on top.
        Assert.Equal(2f, Render(output, 0.2)[^1], 2);
    }

    [Fact]
    public void Keys_held_when_the_sound_is_replaced_keep_sounding_in_the_replacement()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(level: 1)));

        keys.NoteOn(60, 1f);
        Render(output, 0.2);
        output.Play(keys.Play(v => Held(level: 0.5)), fadeSeconds: 0);
        var after = Render(output, 0.2);

        Assert.Equal(0.5f, after[^1], 3);
    }

    [Fact]
    public void A_replacement_sound_is_released_by_the_note_off_too()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 0.05)));

        keys.NoteOn(60, 1f);
        Render(output, 0.1);
        output.Play(keys.Play(v => Held(release: 0.05)), fadeSeconds: 0);
        Render(output, 0.1);
        keys.NoteOff(60);

        AudioAssert.IsSilent(Render(output, 0.4).AsSpan(FramesFor(0.2)));
    }

    [Fact]
    public void One_input_can_feed_two_sounds()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(level: 0.25)) + keys.Play(v => Held(level: 0.5)));

        keys.NoteOn(60, 1f);

        Assert.Equal(0.75f, Render(output, 0.2)[^1], 3);
    }

    [Fact]
    public void AllNotesOff_releases_everything()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 0.05)));

        keys.NoteOn(60, 1f);
        keys.NoteOn(64, 1f);
        Render(output, 0.1);
        keys.AllNotesOff();

        Assert.False(keys.IsHeld(60));
        AudioAssert.IsSilent(Render(output, 0.5).AsSpan(FramesFor(0.2)));
    }

    [Fact]
    public void Notes_posted_from_another_thread_while_rendering_never_stick_or_throw()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held(release: 0.02), polyphony: 8));
        var failure = (Exception?)null;
        var done = false;

        var producer = new Thread(() =>
        {
            try
            {
                var random = new Random(7);
                for (int i = 0; i < 20_000; i++)
                {
                    int pitch = 48 + random.Next(24);
                    if (random.Next(2) == 0)
                        keys.NoteOn(pitch, 0.9f);
                    else
                        keys.NoteOff(pitch);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                Volatile.Write(ref done, true);
            }
        });

        var block = new StereoFrame[256];
        producer.Start();
        while (!Volatile.Read(ref done))
            output.Render(block);
        producer.Join();

        Assert.Null(failure);

        // Whatever interleaving happened, letting go of every key must end in silence.
        keys.AllNotesOff();
        var tail = Render(output, 1.0);
        AudioAssert.IsSilent(tail.AsSpan(FramesFor(0.5)));
    }

    [Fact]
    public void Playing_and_releasing_notes_allocates_nothing_on_the_audio_thread()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.01, 0.1, 0.6, 0.2), polyphony: 4));
        var block = new StereoFrame[256];

        // Warm up: first-time execution of every path, including stealing.
        for (int round = 0; round < 3; round++)
        {
            for (int n = 0; n < 6; n++) keys.NoteOn(50 + n, 0.8f);
            for (int b = 0; b < 20; b++) output.Render(block);
            keys.AllNotesOff();
            for (int b = 0; b < 20; b++) output.Render(block);
        }

        // Only the renders are measured; pressing and releasing keys sits between them and may allocate (it is not the audio thread).
        AllocationAssert.NoneOf(() =>
        {
            long allocated = 0;
            for (int round = 0; round < 5; round++)
            {
                for (int n = 0; n < 6; n++) keys.NoteOn(50 + n, 0.8f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int b = 0; b < 20; b++) output.Render(block);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;

                keys.AllNotesOff();
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int b = 0; b < 20; b++) output.Render(block);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return allocated;
        });
    }

    [Fact]
    public void Arguments_are_validated()
    {
        var keys = new NoteInput();

        Assert.Throws<ArgumentOutOfRangeException>(() => keys.NoteOn(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.NoteOn(128));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.NoteOn(60, 1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.NoteOn(60, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.NoteOff(200));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.IsHeld(-5));
        Assert.Throws<ArgumentNullException>(() => keys.Play(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Play(v => Held(), polyphony: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => keys.Play(v => Held(), polyphony: 65));
    }

    [Fact]
    public void Releasing_a_key_that_is_not_held_does_nothing()
    {
        var keys = new NoteInput();
        using var output = Output(keys.Play(v => Held()));

        keys.NoteOff(60);

        AudioAssert.IsSilent(Render(output, 0.1));
        Assert.False(keys.IsHeld(60));
    }
}
