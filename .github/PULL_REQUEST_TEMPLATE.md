## What and why

<!-- One change per pull request. Link the issue: "Fixes #123". New operators and public API need an agreed issue first. -->

## Checklist

- [ ] `dotnet build` is clean (warnings are errors in `src/`) and `dotnet test` passes
- [ ] New or changed behaviour has a test; audible changes assert on rendered audio
- [ ] New public members have XML docs, an `<example>` if audible, and a line in `PublicAPI.Unshipped.txt`
- [ ] Docs and `CHANGELOG.md` are updated for anything a user will notice
- [ ] Nothing in `RenderBlock` allocates, locks or blocks
- [ ] Every commit is signed off (`git commit -s`)
- [ ] Any contributed sample or image has a stated licence and source (CC0 or MIT-compatible)

## AI assistance

- [ ] I used AI tools for part of this change, and I understand and can explain every line.

<!-- If you ticked the box, one sentence on what the tool did. -->
