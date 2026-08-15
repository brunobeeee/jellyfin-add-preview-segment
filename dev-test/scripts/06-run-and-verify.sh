#!/usr/bin/env bash
# Verify the media-segment-provider approach end to end:
# run Jellyfin's built-in "Extract Media Segments" task (which invokes our provider) and
# confirm a Preview segment is created AND surfaced by the /MediaSegments API.
source "$(dirname "$0")/lib.sh"

[ -s "$STATE_DIR/episode_id" ] || { err "No episode_id in .state (run 03 first)."; exit 1; }
EP_ID="$(cat "$STATE_DIR/episode_id")"
DB="$(jf_db_path)"

# --- 1. Sanity: our provider must be registered + enabled for the item's library ------
log "Registered media segment providers for the episode:"
jf_api GET "/MediaSegments/Items/$EP_ID/Providers" 2>/dev/null | python3 -m json.tool 2>/dev/null | sed 's/^/    /' \
  || warn "(no /Providers endpoint on this build - providers are enabled by default)"

# --- 2. Segments BEFORE (should show the seeded Intro is NOT surfaced: unregistered provider) --
log "/MediaSegments for the episode BEFORE extraction:"
jf_api GET "/MediaSegments/$EP_ID" | python3 -m json.tool | sed 's/^/    /'

# --- 3. Run the built-in Extract Media Segments task ---------------------------
TASK_ID=$(jf_api GET /ScheduledTasks | python3 -c '
import sys,json
for t in json.load(sys.stdin):
    if t.get("Key")=="TaskExtractMediaSegments": print(t["Id"]); break')
[ -n "${TASK_ID:-}" ] || { err "Built-in task TaskExtractMediaSegments not found."; exit 1; }
log "Running 'Extract Media Segments' (id=$TASK_ID) ..."
jf_api POST "/ScheduledTasks/Running/$TASK_ID" >/dev/null

for i in $(seq 1 40); do
  STATE=$(jf_api GET /ScheduledTasks | python3 -c "
import sys,json
for t in json.load(sys.stdin):
    if t.get('Key')=='TaskExtractMediaSegments': print(t.get('State'));break")
  [ "$STATE" = "Idle" ] && break
  sleep 1
done
ok "Task finished (State=$STATE)."

# --- 4. Verify ----------------------------------------------------------------
echo
log "=== MediaSegments table AFTER (raw DB) ==="
sqlite3 -header "$DB" "SELECT Type, StartTicks/10000000 AS start_s, EndTicks/10000000 AS end_s, SegmentProviderId FROM MediaSegments ORDER BY Type;" | sed 's/^/    /'

echo
log "=== /MediaSegments API AFTER (what clients/players see) ==="
jf_api GET "/MediaSegments/$EP_ID" | python3 -m json.tool | sed 's/^/    /'

echo
log "=== Plugin log lines from this run ==="
grep -iE "preview segment|PreviewSegment" "$CONFIG_DIR"/log/*.log 2>/dev/null | tail -20 | sed 's/^/    /' || warn "No plugin log lines found."

echo
PREVIEW_VIA_API=$(jf_api GET "/MediaSegments/$EP_ID" | python3 -c 'import sys,json; print(any(i["Type"]=="Preview" for i in json.load(sys.stdin).get("Items",[])))')
if [ "$PREVIEW_VIA_API" = "True" ]; then
  ok "SUCCESS: a Preview segment is now returned by the /MediaSegments API (surfaced to clients)."
else
  err "Preview segment is NOT surfaced by the API - investigate provider registration/enablement."
fi
