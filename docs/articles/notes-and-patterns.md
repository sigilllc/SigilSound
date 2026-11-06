# Notes and patterns

Notes are pitches in time. Patterns are functions of time that produce events, and a pattern can be played as a polyphonic instrument, used as a signal, or reshaped with transforms.

A pattern cycle is four beats, and the tempo comes from `bpm`. Patterns stay on the beat grid, so they stay in time with each other.

## Sequences from note names

`Notes.Parse` turns names into notes. `Every` places them in time, `Loop` repeats them, and `Play` turns the result into an instrument.

```csharp
// A rising arpeggio, one eighth note each, polyphonic.
Notes.Parse("C3 E3 G3 B3").Every(Beats.Eighth, gate: 2).Loop()
    .Play(v => Saw(v.Frequency).LowPass(1500, q: 2).Adsr(0.005, 0.1, 0.4, 0.2), bpm: 120) * 0.3
```

`gate` is how long each note is held, as a multiple of its step. Above 1, notes overlap.

## Mini-notation

`Mini.Notes` reads a compact text pattern. It is the most expressive form, and it has rests, groups, speed changes and per-note velocity. The full table is in [Mini-notation](mini-notation.md).

```csharp
// A pattern with a rest, a group played twice, and a slow alternation.
Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>")
    .Play(v => Square(v.Frequency, 0.4).Adsr(0.002, 0.08, 0.5, 0.1), bpm: 110) * 0.3
```

## Scales and chords

`Scale` maps scale degrees to pitches, so the text can stay in the key:

```csharp
// Scale degrees 0 2 4 7 in C minor, played as a bass line.
Scale.Minor(Pitch.C2).Notes(Mini.Integers("0 2 4 7"))
    .Play(v => Saw(v.Frequency).LowPass(600, q: 2).Adsr(0.005, 0.1, 0.5, 0.1), bpm: 100) * 0.4
```

`Chord` produces a chord, and `Arp` spreads it out into an arpeggio. `ArpMode` is `Up`, `Down`, `UpDown` or `DownUp`.

```csharp
// Minor sevenths, arpeggiated up and down over two octaves.
Mini.Chords("<c3:min7 f3:7>").Arp(ArpMode.UpDown, octaves: 2)
    .Play(v => Saw(v.Frequency).LowPass(1200).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 100) * 0.25
```

## Transforms

Transforms take a pattern and return a new one. They are cheap: a pattern is only evaluated when it is played.

```csharp
// Twice as fast, reversed, and quieter.
Mini.Notes("c3 e3 g3 b3").Fast(2).Rev().Velocity(0.6)
    .Play(v => Sine(v.Frequency).Adsr(0.005, 0.2, 0, 0.2), bpm: 120) * 0.5
```

```csharp
// Euclidean rhythm: three hits spread across eight steps.
Mini.Notes("c3").Euclid(3, 8)
    .Play(v => Square(v.Frequency, 0.3).Adsr(0.001, 0.05, 0, 0.05), bpm: 120) * 0.25
```

```csharp
// A transformed copy on the right channel: every other bar is an octave up.
Mini.Notes("c3 e3 g3").Jux(p => p.Transpose(12),
    v => Saw(v.Frequency).LowPass(900).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 110) * 0.3
```

`Transpose` and `Every` apply to notes too, not just to patterns:

```csharp
// The same chord pattern, moved up a fourth.
Notes.Parse("C3 E3 G3").Transpose(5).Every(Beats.Quarter).Loop()
    .Play(v => Sine(v.Frequency).Adsr(0.01, 0.3, 0.5, 0.5), bpm: 100) * 0.4
```

## Patterns as a signal

`ToSignal` turns a numeric pattern into a control signal that changes on each step. It can drive any `Param`.

```csharp
// A stepped filter sweep, smoothed by 30 ms so the steps glide.
Saw(110).LowPass(Mini.Numbers("400 900 2400 900").ToSignal(smoothSeconds: 0.03), q: 3) * 0.4
```

## Note values

`Pitch` gives named constants (`Pitch.C2`, `Pitch.Eb4`) and `Notes.NameOf(pitch)` returns the name of a MIDI-style pitch number. See [Mini-notation](mini-notation.md) for the note name format.
