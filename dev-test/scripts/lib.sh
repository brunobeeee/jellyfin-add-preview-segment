#!/usr/bin/env bash
# Shared helpers for the Preview Segment dev-test harness.
# Source this from the numbered scripts:  source "$(dirname "$0")/lib.sh"

set -euo pipefail

# --- Paths -------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEVTEST_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_DIR="$(cd "$DEVTEST_DIR/.." && pwd)"
PLUGIN_PROJ_DIR="$REPO_DIR/Jellyfin.Plugin.PreviewSegment"

CONFIG_DIR="$DEVTEST_DIR/config"
MEDIA_DIR="$DEVTEST_DIR/media"
STATE_DIR="$DEVTEST_DIR/.state"   # holds token / ids between scripts (gitignored via dev-test/config? no -> add)
mkdir -p "$STATE_DIR"

# --- Jellyfin / plugin constants --------------------------------------------
JF_URL="http://localhost:8096"
JF_USER="admin"
JF_PASS="previewsegment"
PLUGIN_GUID="8f9c5d9e-7a6b-4c3d-8e1f-2a3b4c5d6e7f"
PLUGIN_NAME="Preview Segment"
PLUGIN_VERSION="1.0.0.0"
COMPOSE="docker compose -f $DEVTEST_DIR/docker-compose.yml"

# Emby-Authorization header value (no token) used for the login call.
AUTH_HEADER_BASE='MediaBrowser Client="devtest", Device="cli", DeviceId="previewsegment-devtest", Version="1.0.0"'

log()  { printf '\033[1;34m[*]\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m[✓]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[x]\033[0m %s\n' "$*" >&2; }

# Wait until Jellyfin is fully ready. /System/Info/Public answers early (during DB
# migrations) while other controllers still return 503, so we additionally require a
# non-503 response from a real controller before declaring readiness.
wait_for_jellyfin() {
  log "Waiting for Jellyfin to become fully ready on $JF_URL ..."
  for i in $(seq 1 90); do
    if curl -fsS "$JF_URL/System/Info/Public" >/dev/null 2>&1; then
      # Probe a controller that 503s during startup migrations.
      code=$(curl -s -o /dev/null -w "%{http_code}" "$JF_URL/Startup/Configuration")
      if [ "$code" != "503" ] && [ "$code" != "000" ]; then
        ok "Jellyfin is up (probe HTTP $code)."
        return 0
      fi
    fi
    sleep 2
  done
  err "Jellyfin did not become fully ready in time."
  return 1
}

# Authenticate and cache the access token in $STATE_DIR/token.
jf_login() {
  log "Authenticating as $JF_USER ..."
  local resp
  resp=$(curl -fsS -X POST "$JF_URL/Users/AuthenticateByName" \
    -H "Content-Type: application/json" \
    -H "Authorization: $AUTH_HEADER_BASE" \
    -d "{\"Username\":\"$JF_USER\",\"Pw\":\"$JF_PASS\"}")
  echo "$resp" | python3 -c 'import sys,json; print(json.load(sys.stdin)["AccessToken"])' > "$STATE_DIR/token"
  echo "$resp" | python3 -c 'import sys,json; print(json.load(sys.stdin)["User"]["Id"])' > "$STATE_DIR/userid"
  ok "Token + userId cached in .state/"
}

jf_userid() {
  if [ ! -s "$STATE_DIR/userid" ]; then jf_login; fi
  cat "$STATE_DIR/userid"
}

# Read the cached token (login first if missing).
jf_token() {
  if [ ! -s "$STATE_DIR/token" ]; then jf_login; fi
  cat "$STATE_DIR/token"
}

# Authenticated curl. Usage: jf_api GET /Plugins   |  jf_api POST /path -d '{...}'
jf_api() {
  local method="$1"; shift
  local path="$1"; shift
  curl -fsS -X "$method" "$JF_URL$path" \
    -H "Authorization: $AUTH_HEADER_BASE, Token=\"$(jf_token)\"" \
    -H "Content-Type: application/json" \
    "$@"
}

# Path to the live jellyfin.db (10.10+ EF Core DB).
jf_db_path() {
  echo "$CONFIG_DIR/data/jellyfin.db"
}
