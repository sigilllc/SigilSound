<!-- One change per pull request. A new operator or a public API change needs an accepted issue first (CONTRIBUTING.md). -->

## What and why

## Checklist

- [ ] It builds clean (warnings are errors in `src/`) and `dotnet test` passes.
- [ ] `RenderBlock` allocates, locks and blocks nothing; a new operator has an allocation test.
- [ ] A new or changed public member has an XML comment, an `<example>` if it is audible, and a line in `PublicAPI.Unshipped.txt`.
- [ ] The behaviour is tested, and a change to the sound is asserted on rendered audio.
- [ ] Anything a user will notice has a line in `CHANGELOG.md`.
- [ ] A contributed sample or image has a licence and a source in `THIRD-PARTY-NOTICES.md`.
- [ ] A change to the REPL's snippet rules has an adversarial test (a snippet that must be rejected).
