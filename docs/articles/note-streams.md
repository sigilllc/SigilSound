# Note streams: receive notes, change them on the way, send them on

A [pattern](notes-and-patterns.md) is a score: a function of time that you can ask "what happens between beat 3 and 4.5?". A **note stream** is a performance: notes as they happen, each with a start when a key goes down and an end when it comes up. Live notes (a keyboard, a game, another device) arrive as a stream, you shape them with operators on their way to an instrument, and the result goes to a sound, to a host, or into a pattern.

```csharp
// Every key you press also sounds a fifth above it, and twice more a beat later, each time softer.
var keys = new NoteInput();
var plucks = keys.Stream.Harmonize(0, 7).Echo(2, Beats.Quarter, 0.5)
    .Play(v => Saw(v.Frequency).LowPass(2000).Adsr(0.003, 0.2, 0, 0.2));
keys.NoteOn("c3 e3");
plucks * 0.3
```

`NoteInput.Stream` is where a stream starts. `NoteInput.Play` stays what it was, the way to hear the notes as they are; a stream is the way to do something to them first. A stream is immutable like a `Sound`: every operator returns a new one, and nothing runs until something plays it.

## Notes have an identity

A [`NoteEvent`](../api/Sigil.Sound.NoteEvent.yml) is a start (`On`) or an end (`Off`) with an `Id` that pairs them, a pitch (the nearest MIDI note), a velocity, `Cents` (how far the note is from that pitch) and a `Channel` tag. The identity is what lets operators do something to a note and still end it correctly:

- **A note always ends.** The end of a note ends exactly the notes an operator made from it, even when the operator turned one note into five (`Harmonize`), dropped it (`Keep`), delayed it (`Delay`) or ran code of yours (`SelectMany`). You never write the end of a note; the engine sends it.
- **Everything ends on a stop.** When a stream is reset, replaced or disposed, everything still sounding is ended. Capacities are fixed (128 notes sounding per step, 512 events a block); past them a note's start is dropped and its end is never sent, never the other way round.
- **Cents travel.** A note from another tuning keeps its exact pitch through every operator: `keys.NoteOn(69, 0.8f, cents: 50)` is a quarter tone above A4 and sounds that way.

A key pressed again while it is down ends its first note before the second starts, and `keys.Start(pitch)` notes (one `PlayingNote` each) can share a pitch and end alone.

## The operators

| Group | Operators |
| --- | --- |
| Change or choose | `Select(n => ...)`, `Where(n => ...)`, and the shorthands `Transpose(semitones)` and `Velocity(v)` |
| One into several | `Harmonize(semitones)`, `Harmonize(scale, degrees)`, `SelectMany` |
| Time | `Delay`, `Echo`, `Strum`, `Length`, `Humanize` |
| Voicing | `Mono(priority)`, `Latch()` |
| Combine | `Merge(...)`, or `a + b` |
| Out | `Play(voice)`, `Gate()`, `Compile()` (for a host), and the bridges below |

Only what a function cannot say is an operator: the ones that **wait** (time), that **remember** across notes (voicing) or that **start several notes** from one. Everything per note is a small function over the note, with these helpers on `NoteEvent`: `Transpose(n)`, `Detune(cents)`, `Snap(scale)`, `InScale(scale)`, `Chance(p, seed)`, and C# `with` for the rest (`n with { Velocity = 0.5f }`). `Chance` is repeatable: which notes it picks depends only on the seed and the note.

```csharp
// Notes below middle C go an octave down; the rest are echoed softly. Two streams from one input, added.
var keys = new NoteInput();
var low = keys.Stream.Where(n => n.Pitch < 60).Transpose(-12);
var high = keys.Stream.Where(n => n.Pitch >= 60).Echo(1, Beats.Eighth, 0.4);
var sound = (low + high).Play(v => Square(v.Frequency).LowPass(1800).Adsr(0.005, 0.15, 0.3, 0.2));
keys.NoteOn("c2 c4 e4");
sound * 0.2
```

### Time

`Delay`, `Echo` and `Strum` work in beats at the tempo of the input's [`HostClock`](hosting.md) (set `NoteInput.Clock`), or at 120 bpm without one, and keep running while the transport is stopped. A delayed note keeps its length: both its start and its end move. `Strum` spreads notes that start together one after the other, in the order they came; `Length` ends every note after a fixed time whatever the key does; `Humanize` makes the playing a little late and a little different, by amounts that depend only on a seed and the note. Events are placed in the block they fall in, so timing is exact to a block (under 6 ms at the default size).

### Voicing

`Mono` keeps one note of the held keys by priority (last, lowest or highest) and changes it as keys go down and up. `Latch` keeps notes sounding after the keys come up, until the next group of keys goes down (a stop ends them).

## A pattern is a stream, too

`pattern.ToStream()` turns a score of notes into a stream on the host's beat grid, so it goes through the same operators and merges with live notes.

```csharp
// A written melody, with a harmony the stream adds to every note, played by one voice.
var melody = Mini.Notes("c3 [e3 g3]*2 ~ <b3 d4>").ToStream();
melody.Harmonize(0, 7).Strum(0.05).Play(v => Saw(v.Frequency).LowPass(2400).Adsr(0.005, 0.15, 0.4, 0.2)) * 0.2
```

## Writing against the chord that arrives

`stream.Chord()` is the chord the stream is playing, as a `Pattern<Chord>`. That makes everything the library does with a written chord work with a played one: `chord.Arp(ArpMode.Down)`, `chord.Notes()`, and **steps of the chord** (`chord.Steps(...)`, the same name as on a single chord), which is the way to write a line that follows the harmony.

```csharp
using Sigil.Sound;

// Whatever chord arrives, a run falling through it: step 0 is its lowest tone, 1 the next one up, past the top it climbs an octave.
var keys = new NoteInput { Clock = clock };
var chord = keys.Stream.Chord(switchOn: ChordSwitch.Cycle);
var run = chord.Steps(Mini.Integers("7 6 5 4 3 2 1 0")).Transpose(12);
Out.Notes(run);
```

The same steps work on a fixed chord (`Chord.Major(60).Steps("0 1 2 3")`) and on a written progression (`Mini.Chords("<c3:min7 f3:7>").Steps(Mini.Integers("0 2 4"))`). A step with no chord, or a note that would leave the MIDI range, is left out.

The three choices of `Chord` are the AutoArp device's:

| Option | Meaning |
| --- | --- |
| `settle` | Notes that start within this time are one chord (default 35 ms). A strum, or a source that releases and presses voice by voice, is never seen half changed |
| `latch` | `Infinite` keeps the last chord after the keys come up, so a line carries on over the gap between two chords. `Gated` has a chord only while a key is down |
| `switchOn` | `Instant` takes a new chord as soon as it has settled. `Beat` (default) at the next beat. `Cycle` at the next bar line, so a bar-long run is written against one chord from start to finish. The first chord is taken at once, and so is every chord while the transport is stopped |

The chord is the set of keys down when a group of presses has settled. Letting go of keys never changes it, so a chord released key by key keeps its shape. `chord.Lowest()` and `chord.Highest()` give the bass and the top as a `Pattern<int>`.

A pattern written against the chord is a **live pattern**: it answers for now, whatever time it is asked about, and it is caught up with the notes whenever it is queried (once per block, by whatever plays it). That means it cannot be written out as a score; `ToPattern` throws. Everything else works: play it, shape it with `Sometimes`, `Degrade`, `Fast`, `Every`.

## Your own functions: map, filter, expand, reduce

`Select` and `Where` take a function of yours. Three more do more than one note at a time:

```csharp
using Sigil.Sound;

var keys = new NoteInput();
var shaped = keys.Stream
    .Select(n => n with { Pitch = n.Pitch + (n.Velocity > 0.8f ? 12 : 0) })       // change a note
    .Where(n => n.Pitch % 12 != 1)                                                  // keep some
    .SelectMany((n, emit) => { emit.Add(n); emit.Add(n.Pitch + 7, 0.5f); })        // make none, one or several (Code mode)
    .Scan(0, (down, e, emit) => e.IsOn ? down + 1 : down - 1);                      // carry a state
var count = keys.Stream.Fold(0, (down, e) => e.IsOn ? down + 1 : down - 1);        // reduce to a live Pattern<int>
```

- The function sees and returns the note as it **starts**. Its `Id` and kind are ignored: the engine still ends the note when the key comes up, so a function cannot strand a note.
- A function runs on the audio thread, once per note. It must not allocate, lock, block or take long. An exception stops the stream and reaches the host that runs it.
- A host that runs scripts it did not write should hold the function to the note. The Sigil Workbench's Graph mode accepts `Select` and `Where` only when the body is an expression over the note, arithmetic and `Math` (and the note's own helpers); a call into anything else is refused, because a call such as `Mini.Notes("...")` costs 270 microseconds and 170 KB (measured), and a function runs for every note. Make such a value in a variable above the call. `SelectMany`, `Scan` and `Fold` are Code mode only there.

## Sending a stream to a host

`Play` hears a stream. A host that sends the notes somewhere else (a device's Notes out jack, a MIDI port) builds the stream to run and reads the events per block:

```csharp
using Sigil.Sound;

var runner = stream.Compile(clock, sampleRate: 48_000);

// In the audio callback, before the clock advances for the block:
foreach (var e in runner.Process(frames))
    Send(e.IsOn, e.Pitch, e.Velocity, e.Cents, e.Id);   // pair a release with its start by Id

// On stop, bypass or a locate: the ends of everything that sounds, and a fresh start.
foreach (var e in runner.Reset())
    Send(false, e.Pitch, 0, e.Cents, e.Id);
```

`Process` does not allocate once the stream is built. The returned events are valid until the next call. A runner is for one thread. `runner.Poll()` is for readers that are asked at no fixed rate (a live pattern is one); it runs the stream for the time the clock says has passed.

## Try it

The REPL has starter snippets for all of this. **Note streams** and **A tune through a stream** are self-contained; **Note streams (Keys)** and **Follow the chord (Keys)** use the keyboard under the editor, so hold a chord and hear the run follow it. In Sigilgraph mode the four **Notes out** snippets send notes with `Out.Notes`, and a strip beside the oscilloscope draws them as they sound.

## Limits

- Per step: 128 notes sounding, 512 events a block, 512 scheduled events (delays, echoes), 16 notes from one note in `SelectMany`, 8 intervals in `Harmonize`, 16 echoes.
- Operator arguments are plain numbers, fixed when the stream is built (the same as `Pattern<Note>.Transpose`): a knob cannot drive `Transpose` or `Echo`. A start that does not fit is dropped; its end is never sent.
- The stream's notes carry no bend, pressure or slide (a `PlayingNote`'s expression is for `NoteInput.Play`).
- Events are quantized to the block they fall in. A host that wants sub-block timing is a later refinement.
