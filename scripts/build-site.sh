#!/usr/bin/env bash
# Builds the whole online site (API docs at /, the browser REPL at /repl/) exactly as docs.yml deploys it. docs.yml runs this
# script, so what you check locally is what CI ships. Nothing is uploaded here.
#
#   bash scripts/build-site.sh [version]      version defaults to VersionPrefix in Directory.Build.props
#   SITE_DIR=artifacts/site bash scripts/build-site.sh
#                                             build somewhere else (docs/_site is the default and what wrangler.jsonc deploys)
#   npx wrangler dev --assets artifacts/site  then browse the result locally, with the _headers and 404 page applied
set -euo pipefail
cd "$(dirname "$0")/.."

version="${1:-}"
version="${version#v}"
if [[ -z "$version" ]]; then
  version=$(tr -d '\r' < Directory.Build.props | sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p')
fi
[[ -n "$version" ]] || { echo "could not work out the version" >&2; exit 1; }

site="${SITE_DIR:-docs/_site}"
# The script empties its output folder, so it only accepts the two places it is meant to write to.
case "$site" in
  docs/_site | artifacts/site) ;;
  *) echo "SITE_DIR must be docs/_site or artifacts/site, not '$site'" >&2; exit 1 ;;
esac
echo "[site] version $version -> $site"

dotnet tool restore

# A clean start: leftovers from an earlier publish (old hashed dotnet.*.js files) would otherwise be uploaded too.
rm -rf "$site" artifacts/repl-publish

echo "[site] API docs"
# DocFX resolves --output against docs/ (where docfx.json is), so give it the path from there.
(cd docs && dotnet docfx docfx.json --warningsAsErrors --output "../$site" \
  --metadata "_appFooter=Sigil.Sound $version. MIT licensed. Examples on these pages open in the browser REPL.")

echo "[site] REPL"
dotnet publish apps/Sigil.Sound.Repl -c Release -o artifacts/repl-publish
cp -r artifacts/repl-publish/wwwroot "$site/repl"
# The REPL is served from /repl/, not from the site root.
sed -i 's|<base href="/" />|<base href="/repl/" />|' "$site/repl/index.html"
grep -q '<base href="/repl/" />' "$site/repl/index.html"

# Workers static assets never serve the .br/.gz files Blazor precompresses (they are just more files to upload);
# Cloudflare compresses at the edge instead.
find "$site/repl" \( -name '*.br' -o -name '*.gz' \) -delete

cp cloudflare/_headers cloudflare/404.html "$site/"

echo "[site] checks"
fail=0
for f in index.html repl/index.html 404.html _headers; do
  [[ -f "$site/$f" ]] || { echo "  MISSING $site/$f"; fail=1; }
done
count=$(find "$site" -type f | wc -l)
# Workers static assets: 20,000 files and 25 MiB per file at most.
(( count < 20000 )) || { echo "  too many files: $count"; fail=1; }
big=$(find "$site" -type f -size +25M | head -3)
[[ -z "$big" ]] || { echo "  file over 25 MiB:"; echo "$big"; fail=1; }
(( fail == 0 )) || exit 1

echo "[site] ok: $count files, $(du -sh "$site" | cut -f1) in $site"
