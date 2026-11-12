# Mini-notation

Mini-notation is a short text language for patterns. `Mini.Notes` reads note names, `Mini.Drums` reads drum names, `Mini.Integers` reads whole numbers (scale degrees, for example), `Mini.Numbers` reads decimals, and `Mini.Chords` reads chord names.

This is the mini-notation of [Strudel](https://strudel.cc/learn/mini-notation/) and TidalCycles. The operators in the table below mean the same here as they do there, so the [Strudel mini-notation guide](https://strudel.cc/learn/mini-notation/) is a good place to learn more of the language, with interactive examples. It also has operators we do not have; [Differences from Strudel](#differences-from-strudel) lists them.

A pattern is one cycle of four beats. Steps in the text share that cycle equally, so `c3 e3 g3` puts each note on a quarter of it.

## Syntax

| Syntax | Meaning | Example |
| --- | --- | --- |
| space | The next step in the cycle. | `c3 e3 g3` |
| `~` or `-` | A rest. | `c3 ~ g3` |
| `[ ]` | A group that fills one step. | `c3 [e3 g3]` |
| `,` | Layers played together, as in a chord. | `[c3,e3,g3]` |
| `*n` | Repeat a step n times within its slot. | `c3*2` |
| `/n` | Stretch a step over n cycles. | `c3/2` |
| `<a b>` | One item per cycle, in turn. | `<c3 e3 g3>` |
| `(p,s)` | Euclidean rhythm: p hits across s steps. | `c3(3,8)` |
| `(p,s,r)` | The same, rotated r steps to the left. | `c3(3,8,2)` |
| `a@n` | Give a step n times the slot. In `<>`, n cycles. | `c3@3 e3` |
| `a _` | Extend the previous step by one slot. | `c3 _ e3` |
| `a!n` | Repeat a step n times. `a!` repeats it twice. | `c3!3` |
| `a?` or `a?p` | Sometimes drop the step: half the time, or with chance p (0 to 1). | `c3?`, `c3?0.2` |
| `a:v` | Velocity, 0 to 1 (notes only). | `c4:0.6` |

Weights work in `<>` too, where a step lasts that many cycles: `<c3@3 e3>` and `<c3 _ _ e3>` both play `c3` for three cycles and `e3` for one. Inside `<>` a weight must be a whole number.

## Note names

Names are a letter A to G, optionally `#` or `b`, then an octave. `c4` is middle C. Octaves run from -1 to 9.

Names without an octave are in octave 3. The same note names are read everywhere text names a note: `Notes.Parse("c e4 g4:0.6")`, `Scale.Parse("c3 minor")`, `Chord.Parse("c3:min7")`, and `NoteInput.NoteOn("a2 g3 e4")` for [live notes](live-notes.md), where the notes of the first cycle all start at once. A MIDI number is the way in only when a number is what you have.

## Chords

A chord name is a root and a quality: `c3:min7`, `f3:7`, `g3:maj7`, `a3:sus4`. Qualities include `maj`, `min`, `dim`, `aug`, `sus2`, `sus4`, `7`, `maj7`, `min7`. `Chord.Parse("c3:min7")` builds one chord from the same text.

## Drums

`Mini.Drums` reads drum names and gives notes on the General MIDI drum map, so one pattern can drive your own sounds or a MIDI drum kit: `bd` bass drum, `sn` snare, `rim` side stick, `cp` clap, `hh` and `oh` closed and open hi-hat, `lt`, `mt` and `ht` toms, `cr` crash, `rd` ride, `cb` cowbell. `:0.4` sets the velocity (`hh:0.4`). Play each drum as its own pattern with the sound you want; the pitch only says which drum it is.

## Scales

A scale is text too, and so are its degrees: `Scale.Parse("c3 minor").Notes("0 2 <4 7> ~")` is the same as `Scale.Minor(Pitch.C3).Notes(Mini.Integers("0 2 <4 7> ~"))`.

## Examples

```csharp
// Two bars of the same beat: the chord changes every bar, the lead picks one note per cycle.
Mini.Notes("[c3 e3 g3]*2 <f3 a3 c4> <g3 b3 d4>")
    .Play(v => Saw(v.Frequency).LowPass(1000, q: 2).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 100) * 0.2
```

```csharp
// Degrees in C major: 0 is the root, 4 the fifth above, and the group plays both.
Scale.Parse("c3 major").Notes("0 [2 4]*2 <5 7>")
    .Play(v => Triangle(v.Frequency).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 110) * 0.4
```

```csharp
// Numbers for a control signal: a rhythm of hits written as values.
Saw(110).LowPass(Mini.Numbers("200 2000 200 ~").ToSignal(smoothSeconds: 0.01), q: 2) * 0.4
```

## Differences from Strudel

What the table covers behaves as in Strudel. Strudel is larger, and a few things are ours alone:

- **Not supported.** The `|` random choice. Numbers after `*`, `/`, `@` and inside `( )` are plain numbers here, not patterns, so `c3*<2 3>` and `c3(<3 5>,8)` are errors. Euclidean rhythms take no negative pulse counts. Anything on the Strudel page that is not in the table above is not supported; a parse error says where the text stops making sense.
- **`?` is not the same dice.** It drops events at random but the same ones every time, so a loop is stable and an offline render matches live playback. The events dropped are not the ones Strudel would drop.
- **`:` is a velocity.** In Strudel `bd:3` picks the third sample named `bd`. Here `c4:0.6` is a note at 60% velocity, and in chords `c3:min7` separates the root from the quality, so chord names are spelled our way.
- **Drums are notes.** Strudel names samples; `Mini.Drums` names General MIDI drum notes, so `bd`, `sn`, `hh` and the rest give a pitch that you pair with a sound of your own.
- **Strudel's functions are not here.** The text is only the pattern. What Strudel does with function calls such as `.fast(2)` or `.rev()` is done with methods on the pattern in C#; see [Notes and patterns](notes-and-patterns.md).

A parse error is reported with its position in the text. Patterns that are too dense, or nested too deeply, are refused with a message rather than played.
