#!/usr/bin/env bash
# Verifies spec §8: one issuer for the browser and for containers, with back-channel
# endpoints staying on the Docker network.
set -euo pipefail

expected="http://localhost:8080/realms/lantern"
network="lantern_default"
fail() { echo "FAIL: $*" >&2; exit 1; }
json() { python3 -c "import sys,json; print(json.load(sys.stdin)$1)"; }
in_net() { docker run --rm --network "$network" curlimages/curl -fsS "$@"; }

host_cfg=$(curl -fsS "$expected/.well-known/openid-configuration") || fail "Keycloak not reachable on localhost:8080"
[[ "$(json "['issuer']" <<<"$host_cfg")" == "$expected" ]] || fail "host issuer mismatch"

net_cfg=$(in_net "http://keycloak:8080/realms/lantern/.well-known/openid-configuration")
[[ "$(json "['issuer']" <<<"$net_cfg")" == "$expected" ]] || fail "container issuer mismatch"
[[ "$(json "['token_endpoint']" <<<"$net_cfg")" == http://keycloak:8080/* ]] || fail "back-channel token endpoint left the Docker network"
[[ "$(json "['authorization_endpoint']" <<<"$net_cfg")" == http://localhost:8080/* ]] || fail "front-channel authorization endpoint is not localhost:8080"

token=$(in_net -d grant_type=client_credentials -d client_id=api-admin-svc -d client_secret=api-admin-svc-dev-secret \
  "http://keycloak:8080/realms/lantern/protocol/openid-connect/token" | json "['access_token']")
iss=$(python3 -c "import sys,json,base64; p=sys.argv[1].split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.urlsafe_b64decode(p))['iss'])" "$token")
[[ "$iss" == "$expected" ]] || fail "token minted inside the network has iss=$iss"

echo "OK: issuer is $expected from host and containers; back-channel stays on keycloak:8080"
