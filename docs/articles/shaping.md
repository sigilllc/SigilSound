# Shaping sound

These operators change the level, the spectrum or the shape of a sound over time. They work on any `Sound`.

## Level and mixing

`*` multiplies a sound by a level, and `+` mixes two sounds together.

```csharp
// Two saws a fifth apart, each at a third of full level.
(Saw(110) * 0.3) + (Saw(165) * 0.3)
```

```csharp
// Mixing an offset is also allowed: a constant added to a sound shifts its DC level.
(Sine(110) + 0.1) * 0.4
```

`Gain(x)` is the same as `* x`, and reads better in a chain:

```csharp
Saw(110).Gain(0.4)
```

Two sounds multiply sample by sample, which is ring modulation. The result has sidebands at the sum and difference of the two frequencies, a metallic sound rather than a volume change.

```csharp
// Ring modulation: a 220 Hz tone times a 33 Hz tone.
Sine(220) * Sine(33) * 0.6
```

## Envelopes

`Adsr(attack, decay, sustain, release, hold)` shapes a sound's level over time. `hold` sets how long the note stays open when no note is playing. Times are in seconds.

```csharp
// A plucky saw: fast attack, quick decay to half level, then held.
Saw(110).LowPass(900, q: 2).Adsr(0.01, 0.2, 0.6, 0.8, hold: 0.5) * 0.4
```

## Filters

Each filter takes a cutoff (or centre frequency) and an optional `q`, which sets the resonance. A higher `q` gives a sharper peak.

| Filter | Keeps |
| --- | --- |
| `LowPass(cutoff, q)` | Everything below the cutoff. |
| `HighPass(cutoff, q)` | Everything above the cutoff. |
| `BandPass(center, q)` | A band around the centre. |
| `Notch(center, q)` | Everything except a band around the centre. |

```csharp
// Noise through a narrow band: a whistle-like tone at 1.2 kHz.
Noise().BandPass(1200, q: 12) * 0.5
```

## Tone controls

`Peak`, `LowShelf` and `HighShelf` add a boost or cut at one frequency. `Equalize` is a three-band tone control in dB.

```csharp
// A peaking boost of 9 dB around 1 kHz.
Saw(110).Peak(1000, q: 3, gainDb: 9) * 0.3
```

```csharp
// Warmer lows, scooped mids, bright top.
(Saw(110) * 0.3).Equalize(lowDb: 3, midDb: -4, highDb: 2)
```

## Keeping levels safe

`Limit(ceiling, release)` stops peaks above the ceiling. Put it last in a chain that mixes several sounds.

```csharp
// Two loud saws, mixed and kept under the ceiling.
(Saw(110) + Saw(111.5)).Limit() * 0.8
```

`Range(min, max)` also maps a sound's -1 to 1 into a new span, which is how controls and LFOs are scaled. See [Parameters](parameters.md).
