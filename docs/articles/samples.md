# Samples

A sample is a recorded sound held in memory as an `AudioBuffer`. `Sample` plays one, at any speed, with one-shot, loop or ping-pong modes.

## In the REPL

The REPL ships with a small library of samples. Name one with `Samples.Load`:

```csharp
// One hit at the recorded pitch.
Sample(Samples.Load("pluck.wav"))
```

Drop your own `.wav` files on the REPL page to add them. The editor then offers their names as you type the quotes.

## Speed, pitch and loops

Playback speed sets the pitch: 2 is an octave up, 0.5 an octave down, and a negative speed plays backward.

```csharp
// An octave down, looping the whole sample.
Sample(Samples.Load("pluck.wav"), speed: 0.5, mode: PlaybackMode.Loop) * 0.5
```

```csharp
// Ping-pong between two points: forward, back, forward, with no jump at either end.
Sample(Samples.Load("pluck.wav"), mode: PlaybackMode.PingPong, loopStart: 0.1, loopEnd: 0.6) * 0.5
```

`PlaybackMode.OneShot` plays once and then falls silent. `Loop` jumps back to the loop start at the loop end. `loopStart` and `loopEnd` are in seconds.

## Samplers

Pitch a sample by a played note's frequency over its root:

```csharp
// The sample is recorded at C4 (261.63 Hz). Each note plays it at its own pitch.
Notes.Parse("C4 E4 G4 C5").Every(Beats.Quarter).Loop()
    .Play(v => Sample(Samples.Load("pluck.wav"), speed: v.Frequency * (1 / 261.63)), bpm: 120) * 0.5
```

## In a project

A project loads WAV files itself. The REPL's `Samples.Load` only exists inside the REPL.

```csharp
using static Sigil.Sound.Sounds;
using Sigil.Sound;

// Read a file, then play it.
var kick = AudioBuffer.FromWav("kick.wav");
Sample(kick).Render(1.0)
```

`AudioBuffer.FromWav` reads 8, 16, 24 and 32-bit PCM, and 32-bit float WAV, mono or stereo. `WavReader.ReadInfo(bytes)` returns a file's format and length without decoding it.

For a host that keeps its samples somewhere other than the file system, implement `ISampleSource` and give it to a `SampleLibrary`. The library then resolves names for `Samples.Load` in the same way.
