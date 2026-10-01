#!/usr/bin/env bash
set -euo pipefail
fail() { echo "FAIL: $*" >&2; exit 1; }

"$(dirname "$0")/verify-hostname.sh"

[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/health)" == "200" ]] || fail "API /health not 200"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/me)" == "401" ]] || fail "API /me without token not 401"
[[ "$(curl -s -o /dev/null -w '%{http_code}' -H 'Authorization: Bearer nope' http://localhost:5100/me)" == "401" ]] || fail "API accepted a garbage token"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8025/livez)" == "200" ]] || fail "Mailpit not up"

echo "OK: stack healthy"
