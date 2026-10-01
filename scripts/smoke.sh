#!/usr/bin/env bash
set -euo pipefail
fail() { echo "FAIL: $*" >&2; exit 1; }

"$(dirname "$0")/verify-hostname.sh"

for svc_port in keycloak:8080 mailpit:8025 api:8080 till:8080 backoffice:8080 outlet-admin:8080; do
  binding=$(docker compose port "${svc_port%%:*}" "${svc_port##*:}")
  [[ "$binding" == 127.0.0.1:* ]] || fail "${svc_port} is published on $binding, not 127.0.0.1"
done

[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/health)" == "200" ]] || fail "API /health not 200"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5100/me)" == "401" ]] || fail "API /me without token not 401"
[[ "$(curl -s -o /dev/null -w '%{http_code}' -H 'Authorization: Bearer nope' http://localhost:5100/me)" == "401" ]] || fail "API accepted a garbage token"
[[ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8025/livez)" == "200" ]] || fail "Mailpit not up"

till_page=$(curl -fsS http://localhost:5400/) || fail "Till not reachable on localhost:5400"
grep -q 'data-testid="not-registered"' <<<"$till_page" || fail "Till did not show its sign-in prompt"

for app in 5200 5300; do
  location=$(curl -s -o /dev/null -w '%{redirect_url}' "http://localhost:$app/")
  [[ "$location" == http://localhost:8080/realms/lantern/* ]] || fail "app on :$app did not redirect to Keycloak (got '$location')"
done

echo "OK: stack healthy"
