# Contributing

Thanks for wanting to help. Sigil.Sound is a small project maintained by Sigil LLC, so this page is short.

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Report security problems privately, see [SECURITY.md](SECURITY.md).

## What helps most

Bug fixes (with a test that fails before the fix), documentation, examples and tests. For a **new operator, a change to the public API, or anything in the REPL's security rules, open an issue first**: those are decided before code is written, and other software relies on them, so a pull request can be declined even when the code is good.

## Set up

You need the .NET 10 SDK. It builds both targets of the libraries (net8.0 and net10.0); the tests run on net10.0.

```bash
git clone https://github.com/sigilllc/SigilSound.git
cd SigilSound
dotnet build
dotnet test
```

The tests render audio in memory and never open an audio device. To run the browser REPL: `dotnet run --project apps/Sigil.Sound.Repl`. To build the docs site: `dotnet tool restore`, then `pwsh scripts/docs.ps1 -Serve`.

## Before you send a pull request

- Build clean. Warnings are errors in `src/`, and every public member needs an XML comment.
- A new or changed public member needs a line in that project's `PublicAPI.Unshipped.txt`; the analyzer tells you which.
- An audible public member needs an `<example>` (see [Writing doc examples](docs/articles/writing-examples.md)). Check it with `dotnet test tests/Sigil.Sound.Repl.Tests --filter DocExampleTests`.
- Add a test for the behaviour. Where the sound changes, assert on rendered audio.
- `RenderBlock` must not allocate, lock or block ([custom operators](docs/articles/custom-operators.md)). Tests for that use `AllocationAssert`.
- Add a line to [CHANGELOG.md](CHANGELOG.md) for anything a user will notice.

Keep each pull request to one change, with a clear subject such as `fix(notes): ...`. It is squash-merged. Maintainers review as time allows, usually within a week or two.

AI tools are fine. You must understand every line you submit and be able to explain it.

## Samples, licence and name

A sample or image you contribute needs a stated licence (CC0 or one compatible with MIT) and where it came from. Contributions are licensed under the [MIT License](LICENSE), like the rest of the project. The Sigil name and logo are not included; see the [README](README.md#trademarks).
