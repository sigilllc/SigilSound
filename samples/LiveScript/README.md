# LiveScript

A live-coding host in about 150 lines. It plays a Sigil.Sound script file and swaps in the new version every time you save the file, while it plays. Windows only, because it uses NAudio's WaveOut for the sound card.

```
dotnet run --project samples/LiveScript
```

It plays `script.cs` in this folder; pass another path to play that one. Open the file in any editor, change a number and save: the new sound fades in over the old one. A script with errors is reported, and the last good version keeps playing. A S D F G H J K play notes (W E T Y U the black keys, Z and X the octave) through `Keys`, a `NoteInput` this host adds to every script.

## What it shows

- `SoundContext` as the output. Every `Play` is a hot swap with a crossfade, and swaps can come as fast as you save.
- `SnippetCompiler` with a `SnippetProfile` that adds the host's own API (`using static LiveScript.Host;`), as an app that lets its users write scripts would.
- `NoteInput` for notes, kept for the life of the program, so a held note carries over into the next version of the script.
- The audio callback renders straight into NAudio's buffer, because a `StereoFrame` is two floats.

## On other platforms

Nothing here is Windows-specific except `WaveOutEvent`. Any audio API that asks for interleaved 32-bit float stereo works the same way: call `ExecutionGuard.Reset(frames)` and then `output.Render(frames)` from its callback.
