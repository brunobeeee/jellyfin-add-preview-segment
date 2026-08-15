#!/usr/bin/env bash
# Start Jellyfin, complete the startup wizard headless, create a TV library on /media.
source "$(dirname "$0")/lib.sh"

log "Starting Jellyfin container ..."
$COMPOSE up -d
wait_for_jellyfin

# --- Startup wizard (only works while server is in "first run" state) --------
STARTUP_STATE=$(curl -fsS "$JF_URL/System/Info/Public" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("StartupWizardCompleted"))')
if [ "$STARTUP_STATE" = "True" ]; then
  ok "Startup wizard already completed, skipping."
else
  log "Running startup wizard (user: $JF_USER) ..."
  curl -fsS -X POST "$JF_URL/Startup/Configuration" -H "Content-Type: application/json" \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
  curl -fsS "$JF_URL/Startup/User" >/dev/null
  curl -fsS -X POST "$JF_URL/Startup/User" -H "Content-Type: application/json" \
    -d "{\"Name\":\"$JF_USER\",\"Password\":\"$JF_PASS\"}" >/dev/null
  curl -fsS -X POST "$JF_URL/Startup/RemoteAccess" -H "Content-Type: application/json" \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
  curl -fsS -X POST "$JF_URL/Startup/Complete" >/dev/null
  ok "Startup wizard complete."
fi

jf_login

# --- Server version (for meta.json targetAbi later) --------------------------
SERVER_VERSION=$(jf_api GET /System/Info | python3 -c 'import sys,json; print(json.load(sys.stdin)["Version"])')
echo "$SERVER_VERSION" > "$STATE_DIR/server_version"
ok "Jellyfin server version: $SERVER_VERSION"

# --- Create TV library on /media --------------------------------------------
EXISTING=$(jf_api GET /Library/VirtualFolders | python3 -c 'import sys,json; print(any(f["Name"]=="Shows" for f in json.load(sys.stdin)))')
if [ "$EXISTING" = "True" ]; then
  ok "Library 'Shows' already exists."
else
  log "Creating TV library 'Shows' -> /media ..."
  jf_api POST "/Library/VirtualFolders?name=Shows&collectionType=tvshows&paths=%2Fmedia&refreshLibrary=true" \
    -d '{"LibraryOptions":{}}' >/dev/null
  ok "Library created, refresh triggered."
fi

# --- Wait for the episode to be scanned in ----------------------------------
log "Waiting for the test episode to appear in the library ..."
EP_ID=""
for i in $(seq 1 60); do
  EP_ID=$(jf_api GET "/Items?userId=$(jf_userid)&IncludeItemTypes=Episode&Recursive=true" \
    | python3 -c 'import sys,json; items=json.load(sys.stdin).get("Items",[]); print(items[0]["Id"] if items else "")' 2>/dev/null || true)
  if [ -n "$EP_ID" ]; then break; fi
  sleep 3
done

if [ -z "$EP_ID" ]; then
  warn "Episode not found yet. Trigger a manual scan (Dashboard) or re-run this script."
  warn "You can also force a scan: curl -X POST '$JF_URL/Library/Refresh' -H 'Authorization: ...'"
else
  echo "$EP_ID" > "$STATE_DIR/episode_id"
  LIB_ID=$(jf_api GET "/Library/VirtualFolders" | python3 -c 'import sys,json; print(next(f["ItemId"] for f in json.load(sys.stdin) if f["Name"]=="Shows"))')
  echo "$LIB_ID" > "$STATE_DIR/library_id"
  ok "Episode ItemId: $EP_ID"
  ok "Library ItemId: $LIB_ID"
fi

echo
ok "Jellyfin ready at $JF_URL  (login: $JF_USER / $JF_PASS)"
