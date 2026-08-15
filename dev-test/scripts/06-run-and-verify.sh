#!/usr/bin/env bash
# Configure the plugin (LibraryIds), run the scheduled task, verify a Preview segment appears.
source "$(dirname "$0")/lib.sh"

[ -s "$STATE_DIR/library_id" ] || { err "No library_id in .state (run 03 first)."; exit 1; }
[ -s "$STATE_DIR/episode_id" ] || { err "No episode_id in .state (run 03 first)."; exit 1; }
LIB_ID="$(cat "$STATE_DIR/library_id")"
EP_ID="$(cat "$STATE_DIR/episode_id")"
DB="$(jf_db_path)"

# --- 1. Set plugin config (task aborts early if LibraryIds is empty) ----------
log "Setting plugin config LibraryIds=[$LIB_ID] ..."
jf_api POST "/Plugins/$PLUGIN_GUID/Configuration" -d "{\"LibraryIds\":[\"$LIB_ID\"]}" >/dev/null \
  && ok "Plugin config saved." || warn "Could not save plugin config (is the plugin loaded?)."

# --- 2. Does Jellyfin itself recognize the seeded Intro? ----------------------
log "Jellyfin /MediaSegments for the episode BEFORE running the task:"
jf_api GET "/MediaSegments/$EP_ID" | python3 -m json.tool | sed 's/^/    /' \
  || warn "MediaSegments query failed."

# --- 3. Find + run the scheduled task ----------------------------------------
TASK_ID=$(jf_api GET /ScheduledTasks | python3 -c '
import sys,json
for t in json.load(sys.stdin):
    if t.get("Key")=="AddPreviewSegments": print(t["Id"]); break')
if [ -z "${TASK_ID:-}" ]; then
  err "Scheduled task 'AddPreviewSegments' not found -> plugin not loaded/registered."
  exit 1
fi
log "Running task AddPreviewSegments (id=$TASK_ID) ..."
jf_api POST "/ScheduledTasks/Running/$TASK_ID" >/dev/null

for i in $(seq 1 30); do
  STATE=$(jf_api GET /ScheduledTasks | python3 -c "
import sys,json
for t in json.load(sys.stdin):
    if t.get('Key')=='AddPreviewSegments': print(t.get('State'));break")
  [ "$STATE" = "Idle" ] && break
  sleep 1
done
RESULT=$(jf_api GET /ScheduledTasks | python3 -c "
import sys,json
for t in json.load(sys.stdin):
    if t.get('Key')=='AddPreviewSegments':
        r=t.get('LastExecutionResult') or {}
        print(r.get('Status'), '-', r.get('ErrorMessage'));break")
ok "Task finished. LastExecutionResult: $RESULT"

# --- 4. Verify ---------------------------------------------------------------
echo
log "=== MediaSegments table AFTER the task ==="
sqlite3 -header "$DB" "SELECT Id, ItemId, Type, StartTicks, EndTicks, SegmentProviderId FROM MediaSegments;" | sed 's/^/    /'

echo
log "Jellyfin /MediaSegments for the episode AFTER the task (what the player sees):"
jf_api GET "/MediaSegments/$EP_ID" | python3 -m json.tool | sed 's/^/    /' \
  || warn "MediaSegments query failed."

echo
log "=== Plugin log lines from this run ==="
grep -iE "preview segment|PreviewSegment" "$CONFIG_DIR"/log/*.log 2>/dev/null | tail -50 | sed 's/^/    /' \
  || warn "No plugin log lines found."

echo
ok "Done. Preview segment (Type=2) present in DB above? Shown by /MediaSegments API? -> that's the e2e result."
