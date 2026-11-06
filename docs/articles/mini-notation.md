# Mini-notation

Mini-notation is a short text language for patterns. `Mini.Notes` reads note names, `Mini.Integers` reads whole numbers (scale degrees, for example), `Mini.Numbers` reads decimals, and `Mini.Chords` reads chord names.

A pattern is one cycle of four beats. Steps in the text share that cycle equally, so `c3 e3 g3` puts each note on a quarter of it.

## Syntax

| Syntax | Meaning | Example |
| --- | --- | --- |
| space | The next step in the cycle. | `c3 e3 g3` |
| `~` | A rest. | `c3 ~ g3` |
| `[ ]` | A group that fills one step. | `c3 [e3 g3]` |
| `*n` | Repeat a step n times within its slot. | `c3*2` |
| `/n` | Stretch a step over n cycles. | `c3/2` |
| `<a b>` | One item per cycle, in turn. | `<c3 e3 g3>` |
| `(p,s)` | Euclidean rhythm: p hits across s steps. | `c3(3,8)` |
| `a@n` | Give a step n times the slot. | `c3@3 e3` |
| `a _` | Extend the previous step by one slot. | `c3 _ e3` |
| `a!n` | Repeat a step n times. `a!` repeats it twice. | `c3!3` |
| `a?` | Sometimes drop the step. | `c3?` |
| `a:v` | Velocity, 0 to 1 (notes only). | `c4:0.6` |

## Note names

Names are a letter A to G, optionally `#` or `b`, then an octave. `c4` is middle C. Octaves run from -1 to 9.

## Chords

A chord name is a root and a quality: `c3:min7`, `f3:7`, `g3:maj7`, `a3:sus4`. Qualities include `maj`, `min`, `dim`, `aug`, `sus2`, `sus4`, `7`, `maj7`, `min7`.

## Examples

```csharp
// Two bars of the same beat: the chord changes every bar, the lead picks one note per cycle.
Mini.Notes("[c3 e3 g3]*2 <f3 a3 c4> <g3 b3 d4>")
    .Play(v => Saw(v.Frequency).LowPass(1000, q: 2).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 100) * 0.2
```

```csharp
// Degrees in C major: 0 is the root, 4 the fifth above, and the group plays both.
Scale.Major(Pitch.C3).Notes(Mini.Integers("0 [2 4]*2 <5 7>"))
    .Play(v => Triangle(v.Frequency).Adsr(0.005, 0.1, 0.5, 0.2), bpm: 110) * 0.4
```

```csharp
// Numbers for a control signal: a rhythm of hits written as values.
Saw(110).LowPass(Mini.Numbers("200 2000 200 ~").ToSignal(smoothSeconds: 0.01), q: 2) * 0.4
```

A parse error is reported with its position in the text. Patterns that are too dense, or nested too deeply, are refused with a message rather than played.
