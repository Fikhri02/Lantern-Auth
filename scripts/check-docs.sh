#!/usr/bin/env bash
# Scenario pages and decision records exist and are complete; no relative link is broken.
set -euo pipefail
fail() { echo "FAIL: $*" >&2; exit 1; }

for s in a-sso b-single-logout c-roles-in-the-api d-till-two-layer-login e-one-till-per-account \
         f-deactivation g-mfa-for-admins h-branded-login i-staff-management; do
  f="docs/scenarios/$s.md"
  [[ -f "$f" ]] || fail "missing $f"
  grep -q '```mermaid' "$f" || fail "$f has no diagram"
  grep -q '^## Try it' "$f" || fail "$f has no 'Try it' section"
  grep -q '^## Tests' "$f" || fail "$f has no 'Tests' section"
done

for d in 0001 0002 0003 0004 0005 0006 0007 0008 0009; do
  compgen -G "docs/decisions/$d-*.md" > /dev/null || fail "missing decision record $d"
done

python3 - <<'EOF'
import pathlib, re, sys
broken = []
files = [pathlib.Path("README.md"), *pathlib.Path("docs").rglob("*.md")]
for md in files:
    if "superpowers" in md.parts:
        continue
    for target in re.findall(r"\]\(([^)\s#]+)(?:#[^)]*)?\)", md.read_text()):
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        if not (md.parent / target).exists():
            broken.append(f"{md}: {target}")
if broken:
    print("FAIL: broken links:\n  " + "\n  ".join(broken), file=sys.stderr)
    sys.exit(1)
EOF

echo "OK: docs complete"
