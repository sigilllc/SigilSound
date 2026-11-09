# Hosting: what an app hands your sound

Sigil.Sound describes sound; an app (a DAW-style host, a game, a notebook) decides when it plays, who turns the knobs and where the notes come from. Three small pieces carry that conversation. Each one is optional, costs nothing when a host does not use it, and works the same way: the script says what it wants in plain code, and the host answers.

| The script says | The host provides | Read |
| --- | --- | --- |
| "this value can be changed while I play" | a knob, slider or selector, set from its UI or a modulation signal | [Controls](controls.md) |
| "these notes are played live" | notes from a keyboard, MIDI or a game, each with bend, pressure and slide | [Live notes and expression](live-notes.md) |
| "this sequence follows your clock" | play, pause and stop for it, and a tempo | below |

## Controls

A `Control` is a named value a host can change while the sound plays. It also carries what a host needs to draw it: its range, taper, polarity and style. See [Controls](controls.md).

## Live notes

`NoteInput` is the canonical way to receive notes from a keyboard, a MIDI port or a game. Build a sound with `Play(voice, polyphony: n)`, then call `NoteOn`, `NoteOff` or `Start` from any thread. A host maps MIDI messages (note, velocity, pitch bend, channel pressure, CC74) onto those calls; the library has no MIDI vocabulary of its own. In the browser REPL, `Keys` is such a `NoteInput`, supplied by the page and played from the keyboard under the editor (the "Live notes (Keys)" example). See [Live notes and expression](live-notes.md).

## A sequence on the host's clock

A pattern played **without a tempo** follows the host: with a `HostClock` on the render context it stays on the host's beat grid at the host's tempo, so replacing the sound keeps the notes in time. A pattern given a `bpm` keeps its own time and starts when the sound does.

A host that wants to show play, pause, stop and a tempo for a script needs to know whether the script has such a sequence at all. `HostSequence.Observe` tells it. While the script runs to build its sound, the callback fires once for every sequence that follows the host:

```csharp
using Sigil.Sound;

var hasSequence = false;
using (HostSequence.Observe(() => hasSequence = true))
{
    // Run the script here. For example, this builds a sound with a host-following sequence:
    //   Notes.Parse("C4 E4 G4").Every(Beats.Eighth).Loop().Play(voice)
}

// hasSequence is true: show the transport controls.
```

Things to know:

- **Per thread.** Reports arrive on the thread that runs the script, and an observer set on one thread does not hear another. That is also why it costs nothing when no observer is set.
- **While building.** The callback fires when the script builds its sound, not on every render block and never on the audio thread.
- **Scopes nest.** Disposing the returned scope restores the observer that was set before it.
- **Only host-following sequences report.** A pattern with its own `bpm` does not.

## A host, start to finish

The browser REPL hides all of this: its page creates one `NoteInput` called `Keys`, a piano component calls `NoteOn` and `NoteOff` on it, and Web Audio pulls frames from a `SoundContext`. In your own app you write those few pieces yourself. The library has no audio driver and no MIDI library, so the two ends are yours: your audio API's callback, and whichever MIDI library you use. Everything between them is below.

An app owns four things once, whatever patch the user writes: the notes, the clock, the output, and the transport. Put them in one class.

```csharp
using Sigil.Sound;

public sealed class Host : IDisposable
{
    private readonly HostClock _clock;
    private readonly SoundContext _output;

    public Host(int sampleRate = 48_000)
    {
        _clock = new HostClock(sampleRate, bpm: 120);
        // The context advances the clock after every block, so the host never has to.
        _output = new SoundContext(sampleRate, clock: _clock);
    }

    /// <summary>Live notes. They outlive any one patch: replacing the patch keeps the held keys sounding.</summary>
    public NoteInput Keys { get; } = new();

    // Transport. Bpm may be set from any thread and takes effect on the next block.
    public double Bpm { get => _clock.Bpm; set => _clock.Bpm = value; }
    public bool Running { get => _clock.Running; set => _clock.Running = value; }

    /// <summary>
    /// Builds and plays the user's patch, a function of the live notes. Returns true when the patch contains a
    /// sequence that follows the host clock, which is when the app should show play, pause, stop and a tempo.
    /// </summary>
    public bool Use(Func<NoteInput, Sound> patch)
    {
        var followsHost = false;
        Sound sound;
        using (HostSequence.Observe(() => followsHost = true))
            sound = patch(Keys);                    // the user's code runs here, once, off the audio thread

        _output.Play(sound, fadeSeconds: 0.05);     // crossfades from whatever was playing
        return followsHost;
    }

    /// <summary>Call from the audio callback, always from the same thread. Real-time safe.</summary>
    public void Fill(Span<StereoFrame> frames) => _output.Render(frames);

    public void Dispose() => _output.Dispose();
}
```

MIDI becomes notes in one small class. It keeps one `PlayingNote` per channel and note, which is how an MPE controller works (one note per channel, so each note can bend, press and slide on its own). A plain controller sends everything on one channel, and the same code handles it.

```csharp
using Sigil.Sound;

public sealed class MidiToNotes(NoteInput keys)
{
    private readonly Dictionary<(int Channel, int Note), PlayingNote> _held = new();
    private readonly double[] _bend = new double[16];   // the last bend per channel, for notes that start after it

    // Call these from your MIDI library's callback. They are meant for one thread, the MIDI thread.

    public void NoteOn(int channel, int note, int velocity)
    {
        if (velocity == 0) { NoteOff(channel, note); return; }   // many devices send note-off as velocity 0
        if (_held.Remove((channel, note), out var previous)) previous.Release();

        var playing = keys.Start(note, velocity / 127f);
        playing.Bend = _bend[channel];
        _held[(channel, note)] = playing;
    }

    public void NoteOff(int channel, int note)
    {
        if (_held.Remove((channel, note), out var playing)) playing.Release();
    }

    /// <param name="value">The 14-bit message value, 0 to 16383, centre 8192.</param>
    /// <param name="semitones">The controller's bend range: 2 for a normal one, usually 48 for an MPE one.</param>
    public void PitchBend(int channel, int value, double semitones = 2)
    {
        _bend[channel] = (value - 8192) / 8192.0 * semitones;
        foreach (var (key, playing) in _held)
            if (key.Channel == channel) playing.Bend = _bend[channel];
    }

    public void ChannelPressure(int channel, int value)
    {
        foreach (var (key, playing) in _held)
            if (key.Channel == channel) playing.Pressure = value / 127.0;
    }

    /// <summary>CC74, the "slide" dimension of an MPE controller.</summary>
    public void Slide(int channel, int value)
    {
        foreach (var (key, playing) in _held)
            if (key.Channel == channel) playing.Slide = value / 127.0;
    }

    public void AllNotesOff()
    {
        _held.Clear();
        keys.AllNotesOff();
    }
}
```

Then the wiring is a few lines. The two comments stand for your audio API and your MIDI library.

```csharp
using Sigil.Sound;
using static Sigil.Sound.Sounds;

using var host = new Host();
var midi = new MidiToNotes(host.Keys);

// The user's patch: a function of the live notes. Slide opens the filter, pressure swells the level.
var showTransport = host.Use(keys =>
    keys.Play(v => Saw(v.Frequency)
                       .LowPass(v.Slide.Range(800, 5000))
                       .Adsr(0.01, 0.2, 0.7, 0.4) * v.Pressure.Range(0.6, 1.0),
              polyphony: 8) * 0.3);

// Your audio API:   audio.OnBuffer = frames => host.Fill(frames);
// Your MIDI library: input.NoteOn += (ch, note, vel) => midi.NoteOn(ch, note, vel);
//                    input.PitchBend += (ch, value) => midi.PitchBend(ch, value);
//                    input.ChannelPressure += (ch, value) => midi.ChannelPressure(ch, value);

// Editing the patch later is the same call. The keys still held carry over to the new sound.
```

Two rules keep this safe. `Fill` runs on the audio thread and must not wait: it never locks, and the notes are applied at the start of the next block. `Use`, `NoteOn` and the other note calls run on control threads and may take a short lock, so never call them from the audio callback.
