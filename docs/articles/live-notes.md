# Live notes and per-note expression

`NoteInput` is the generic way a host hands notes to a sound while it plays: a keyboard, a game, a touch screen, a MIDI controller, another program. The library defines the receiver and the voices; your app decides where the notes come from. (It is the notes counterpart of [Controls](controls.md), which does the same for plain values.)

## Play notes

`Play` turns an input into sound. Each note starts a voice built from your lambda, the voice sounds while the note is held, and its `Adsr` releases when the note ends. Voices are built up front and reused, so playing allocates nothing.

```csharp
// A chord held on a NoteInput: three voices of the same sound, at three pitches.
var keys = new NoteInput();
var synth = keys.Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.01, 0.2, 0.7, 0.4), polyphony: 6);
keys.NoteOn(48);
keys.NoteOn(55);
keys.NoteOn(64);
synth * 0.25
```

`NoteOn(pitch)` and `NoteOff(pitch)` name a note by its pitch, which is all a simple keyboard needs. The voice reads the note from the `Voice` signals: `Frequency`, `Pitch`, `Velocity` and `Gate`.

## A note you can keep shaping

When the player does more than press and release, start the note with `Start`. It returns a `PlayingNote`: the note is its own object, so two notes on the same pitch are two notes, and each has its own **bend**, **pressure** and **slide**. This is the model an MPE controller uses, one note per channel, without the library knowing anything about MIDI.

| On the note | In the voice | Meaning |
| --- | --- | --- |
| `Bend` | `v.Bend` | Semitones the pitch is moved (up to 96 either way). `v.Frequency` and `v.Pitch` already include it |
| `Pressure` | `v.Pressure` | How hard the note is pressed after it started, 0 to 1 |
| `Slide` | `v.Slide` | A third dimension, 0 to 1; what it does is up to the sound, and brightness is the usual choice |

```csharp
// Two notes at the same pitch, bent apart by a third of a semitone: they beat slowly, as two strings would.
var keys = new NoteInput();
var synth = keys.Play(v => Saw(v.Frequency).LowPass(1500).Adsr(0.01, 0.1, 0.8, 0.3), polyphony: 4);
keys.Start(57);
keys.Start(57).Bend = 0.33;
synth * 0.3
```

The setters work from any thread without a lock. The sound reads them once per block and glides to each new value over about 10 ms, so a controller sending hundreds of updates a second is heard as a smooth curve, not as buzzing steps. A note you release keeps listening while it rings out, so a bend can continue into the release.

A sound that only uses `v.Frequency` needs no change to answer to a bending player. To do something more with the gesture, read the other signals:

```csharp
// Sliding up the key opens the filter from 400 Hz to 4 kHz; pressing harder makes the note louder.
var keys = new NoteInput();
var synth = keys.Play(v => (Saw(v.Frequency).LowPass(400 + v.Slide * 3600, q: 2) * (0.3 + v.Pressure * 0.7))
    .Adsr(0.01, 0.1, 0.9, 0.3));
var note = keys.Start(52);
note.Slide = 0.7;
note.Pressure = 0.8;
synth * 0.3
```

## A note that changes by itself

Not every change over a note's life comes from the player. Inside a voice, `Fn(t => ...)` starts its clock again for every note, so a function of `t` is a function of that note's age. That is enough to write a note modifier, a small function that takes a signal and returns it changed over the note's life:

```csharp
// A note modifier: the pitch falls by one octave over the first second of every note, whatever else happens to it.
Sound Fall(Sound pitch, double octaves, double seconds) =>
    pitch * Fn(t => Math.Pow(2, -octaves * Math.Min(t / seconds, 1.0)));

var keys = new NoteInput();
var synth = keys.Play(v => Sine(Fall(v.Frequency, octaves: 1, seconds: 1)).Adsr(0.005, 0.1, 0.8, 0.3), polyphony: 4);
keys.Start(81);
keys.Start(76);
synth * 0.3
```

Each note falls from its own start, so a note played later starts high again. A bend from the player adds to the fall, since `v.Frequency` already includes it.

## What the library leaves to you

Where the notes come from, and how a gesture becomes bend, pressure and slide, is your app's job. A MIDI host maps a pitch-bend message, channel pressure and CC74 onto the three numbers; a touch screen maps finger position; a game maps whatever it likes. The library gives you the receiver, the voices and the smoothing.
