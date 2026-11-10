# Contributing to Sigil.Sound

Thank you for wanting to help. This page says what we welcome, how to set up, what a good change looks like, and what happens after you open a pull request. Sigil.Sound has a single maintainer, who reviews and merges every pull request and makes the final call, so a clear process helps both sides.

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Security problems are reported privately, never as issues: see [SECURITY.md](SECURITY.md). Questions belong in [Discussions](https://github.com/sigilllc/SigilSound/discussions); see [SUPPORT.md](SUPPORT.md).

## What we welcome

Send a pull request straight away for:

- bug fixes, with a test that fails before the fix;
- documentation, typos and clearer wording;
- examples (see [Writing doc examples](docs/articles/writing-examples.md));
- tests, and performance improvements that come with numbers.

**Open an issue first** for anything bigger, so nobody spends a weekend on something we cannot take:

- a new operator or a new public member;
- anything that changes `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt`;
- changes to the REPL's security rules or to `Sigil.Sound.Repl.Core`.

Other software is built on the public API and on the REPL's snippet rules, so we protect them closely. A pull request can be declined for that reason even when the code is good. We will say why.

## Set up

You need the **.NET 10 SDK**. It builds both targets of the libraries (net8.0 and net10.0); the tests run on net10.0.

```bash
git clone https://github.com/sigilllc/SigilSound.git
cd SigilSound
dotnet build
dotnet test
```

The tests render audio in memory and never open an audio device, so they are safe to run anywhere. `PerfBaselineTests` does nothing unless `SIGIL_PERF=1` is set.

To run the browser REPL locally:

```bash
dotnet run --project apps/Sigil.Sound.Repl
```

To build the documentation site (DocFX) and serve it with the REPL beside it:

```bash
dotnet tool restore
pwsh scripts/docs.ps1 -Serve        # or: bash scripts/build-site.sh
```

## What a good change looks like

- **Warnings are errors** in the libraries under `src/`. Build clean.
- **Every public member has an XML comment.** A missing one fails the build.
- **The public API is tracked.** Adding or changing a public member needs a matching line in that project's `PublicAPI.Unshipped.txt`; the analyzer tells you what to add. Maintainers move lines to `Shipped.txt` on release.
- **Every audible public member has an `<example>`**, and `DocExampleTests` compiles and renders each one. Run it before you push:

  ```bash
  dotnet test tests/Sigil.Sound.Repl.Tests --filter DocExampleTests
  ```
- **A test for the behaviour.** Where the sound changes, assert on rendered audio (see `Sigil.Sound.Testing`) instead of only on code paths.
- **The audio thread is sacred.** `RenderBlock` must not allocate, lock or block. Allocate in the factory that builds the operator, which runs off the audio thread. [Custom operators](docs/articles/custom-operators.md) explains the rules.
- **Docs in the same pull request as the code**, and a line in [CHANGELOG.md](CHANGELOG.md) for anything a user will notice. If there is no heading for the next version yet, add `## Unreleased`; a maintainer renames it on release.

## Commits and pull requests

1. Fork the repository and branch from `main`.
2. Make one change per pull request. Small is easier to review and faster to merge.
3. Write commit subjects as [Conventional Commits](https://www.conventionalcommits.org/): `feat(sound): ...`, `fix(notes): ...`, `docs: ...`, `refactor(repl): ...`, `test: ...`, `build: ...`.
4. **Sign off every commit** with `git commit -s`. That adds a `Signed-off-by:` line, which says you wrote the change or have the right to submit it under this project's licence (the [Developer Certificate of Origin](https://developercertificate.org/)). There is no contributor licence agreement to sign.
5. Open the pull request against `main`, link the issue, and fill in the template. A check builds the solution and runs the tests; it must pass.

Pull requests are squash-merged, so the subject line of the pull request becomes the commit. The maintainer reviews as time allows; expect a first answer within a week or two. A pull request that waits is not a rejection. If it needs changes we will ask, and one that has been quiet for a long time after our questions may be closed, and you are welcome to reopen it.

## Using AI tools

Allowed, with two conditions. Say so in the pull request template (one checkbox and a sentence), and make sure you understand every line you submit and can explain it in review. Large generated changes that nobody has read will be closed.

## Audio files and other assets

A sample, impulse response or image you contribute needs a stated licence (CC0 or one compatible with MIT) and where it came from, written in the pull request. We list bundled material in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence and name

Your contribution is licensed under the [MIT License](LICENSE), like the rest of the project. Contributing does not grant use of the Sigil name or logo; see the trademark note in the [README](README.md#trademarks).
