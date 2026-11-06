# Defaults

Sigil.Sound is opinionated. Every method works with no arguments, and what it does is a choice we made for you: the one we would reach for first. You pass an argument only to change something. A default is never a hidden magic number: each one is written in the method's reference page and in this table, and a test fails if an optional parameter does not say what it defaults to.

```csharp
// A sine at 440 Hz. Nothing to fill in.
Sine() * 0.3
```

```csharp
// A whole instrument with no numbers: a saw, a filter, an envelope, an echo and a room.
Saw().LowPass().Adsr().Delay().Reverb() * 0.3
```

```csharp
// A plucked string with no numbers for the resonator: a noise burst into a comb tuned to 440 Hz.
Noise().Adsr(0, 0.01, 0, 0.001).Comb()
```

Arguments are named, so you change one and keep the rest:

```csharp
// The default filter, but more resonant, and an echo that repeats longer.
Saw(110).LowPass(q: 4).Delay(feedback: 0.6) * 0.3
```

## What the defaults are

### Sources

| Method | Default |
| --- | --- |
| `Sine`, `Saw`, `Square`, `Triangle` | `frequency` 440 Hz (A4). `Square` `width` 0.5, `Triangle` `skew` 0.5 |
| `Noise` | `seed` 1 |

### Shaping, filters and tone

| Method | Default |
| --- | --- |
| `Range` | `min` 0, `max` 1: a bipolar LFO becomes a 0 to 1 control |
| `Adsr` | `attack` 0.01, `decay` 0.1, `sustain` 0.7, `release` 0.3, no `hold` (it follows the note, or sustains) |
| `LowPass` | `cutoff` 1000 Hz, `q` 0.7071 |
| `HighPass` | `cutoff` 100 Hz, `q` 0.7071 |
| `BandPass`, `Notch` | `center` 1000 Hz, `q` 0.7071 |
| `Peak` | `frequency` 1000 Hz, `gainDb` +4, `q` 0.7071 |
| `LowShelf` | `frequency` 200 Hz, `gainDb` +4, `q` 0.7071 |
| `HighShelf` | `frequency` 5000 Hz, `gainDb` +4, `q` 0.7071 |
| `Equalize` | each band 0 dB (the corners are 200 Hz, 1 kHz and 5 kHz) |
| `Vowel` | `vowel` 0 (A), `resonance` 0.5 |
| `Fold` | `amount` 3, `symmetry` 0, `mix` 1 |
| `Distort` | `drive` 4, soft curve, `level` 1 |
| `BitCrush` | `bits` 8, full sample rate |

Peak and the shelves default to a boost so that calling them with nothing changes the sound. A band at 0 dB would be a no-op.

### Time and space

| Method | Default |
| --- | --- |
| `Delay` | `time` 0.375 s (a dotted eighth at 120 bpm), `feedback` 0.4, `mix` 0.5 |
| `TapeDelay` | `time` 0.375 s, `feedback` 0.5, `damping` 3000 Hz, `drive` 1, `mix` 0.35 |
| `Reverb` | `size` 1, `decay` 2.5 s, `damping` 0.4, `mix` 0.3 |
| `FdnReverb` | `decay` 3 s, `size` 1, `damping` 0.35, `predelay` 0.01 s, `sheen` 0, `mix` 0.3 |
| `Comb` | `frequency` 440 Hz, `feedback` 0.95, `damping` 0.2 |
| `Chorus` | `rate` 0.8 Hz, `depth` 0.5, `mix` 0.5, 3 voices |
| `Phaser` | `rate` 0.4 Hz, `feedback` 0.3, `mix` 0.5, 4 stages, 400 to 1500 Hz |
| `PitchShift` | `semitones` 12 (an octave up), `mix` 1 |
| `Spread` | `width` 1.5 |
| `Convolve` | `mix` 1, `normalize` `Energy` |

### Dynamics

| Method | Default |
| --- | --- |
| `Compress` | `thresholdDb` -18, `ratio` 4, `attack` 0.01 s, `release` 0.1 s, `kneeDb` 6, no makeup gain, lookahead 3 ms, keyed by itself |
| `Limit` | `ceiling` 0.95, `release` 0.08 s |
| `FollowEnvelope` | `attack` 0.01 s, `release` 0.1 s |

### Notes

| Method | Default |
| --- | --- |
| `Every` | `step` one beat (`Beats.Quarter`), `gate` 1 |
| `Play` | `bpm` follows the host clock, or 120 without one; `polyphony` 8 |

## What is required, and why

A few parameters have no default because there is nothing sensible to default to. Each is the thing the method is about:

- **What the sound is made from:** `Fn(function)`, `Constant(value)`, `Sample(buffer)`, `Convolve(impulse)`, `Through(wrap)`, `Feedback(loop)` and `Sound.FromOperator(factory)`.
- **A level or a side:** `Gain(gain)`, `Pan(position)` and `Balance(position)`. The neutral value does nothing, and any other is a guess.

A test lists these. Adding a method that forces a parameter on the user, without a documented reason, fails the build.

## Changing a default

Defaults are part of the sound of the library, so changing one is a change to the sound, not a bug fix. It is listed in the [changelog](https://github.com/sigilllc/SigilSound/blob/HEAD/CHANGELOG.md) and follows the rules in [Versioning](versioning.md).
