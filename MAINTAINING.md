# Maintaining Sigil.Sound

Notes for the people who run the project. They are public so contributors know what to expect.

## Triage

New issues get a label within about a week. Issues that need more information are marked `needs-info` and closed after four weeks without an answer. A pull request or issue is never closed for silence on our side.

| Label | Meaning |
| --- | --- |
| `bug`, `enhancement`, `documentation` | Set by the issue forms |
| `needs-info` | Waiting for the reporter |
| `accepted` | We agree it should be done; a pull request is welcome |
| `good first issue` | Small, clear, and safe to start with |
| `api` | Touches the public API; needs the owner's decision before code |
| `security` | Never used on public issues. Real reports arrive privately (see below) |
| `wontfix`, `duplicate` | Closed with an explanation |

Questions that arrive as issues are moved to Discussions.

## Reviewing a pull request

Check, in this order:

1. **Is it agreed?** New operators and any public API change need an accepted issue. If not, say so kindly and ask for one.
2. **Does the check pass?** `PR check` builds in Release and runs the tests, and for outside contributors verifies the sign-off line on every commit.
3. **The audio thread.** Nothing in `RenderBlock` allocates, locks or blocks.
4. **Public API.** XML docs, an `<example>` if audible, and the `PublicAPI.Unshipped.txt` line. Anything that changes behaviour of an existing member is called out in `CHANGELOG.md`.
5. **Tests.** The behaviour is asserted, and audible changes assert on rendered audio.
6. **Assets.** A contributed sample or image has a licence and a source, and `THIRD-PARTY-NOTICES.md` is updated.
7. **The REPL's security rules.** Any change to `Sigil.Sound.Repl.Core` validation gets an adversarial test (a snippet that must be rejected) alongside it.

Merge with **squash**, with a Conventional Commit subject. Keep the contributor's `Signed-off-by` line in the body.

## A change to something other software relies on

The public API and the REPL's snippet rules are used by other software. Before merging a change to either, build the consumer against it (the Audio Workbench is the main one) and run its Sound Script tests. Do not merge first and find out later.

## Releasing

1. Move the `## Unreleased` changelog heading to the new version, and move `PublicAPI.Unshipped.txt` lines to `Shipped.txt`.
2. Run the whole test suite locally, and `bash scripts/release-check.sh <version>`.
3. Merge to `main`, then run the **Release Sigil.Sound** workflow by hand (use `dry_run` first), or push the tag `v<version>`. Publishing to nuget.org needs approval on the `nuget` environment.
4. Check the package pages, the symbols and the docs site.

## Security reports

Reports arrive through GitHub's private vulnerability reporting, or at the security address in [SECURITY.md](SECURITY.md). Reproduce, then fix in a private branch or a draft advisory, publish the fix, and credit the reporter. The disclosure window is 90 days from the report.

## Conduct reports

Reports arrive at the conduct address in the [Code of Conduct](CODE_OF_CONDUCT.md). Acknowledge it, handle it privately, and decide the outcome among the maintainers (a warning, removal of content, or a ban). If the report is about a maintainer, that maintainer takes no part in handling it; the reporter can also go to GitHub directly.

## Bad pull requests

A large generated change that nobody has read, a change that ignores the template, or a change that works around the sign-off rule gets one polite message pointing at [CONTRIBUTING.md](CONTRIBUTING.md), and is closed if nothing improves. Never merge something you do not understand.
