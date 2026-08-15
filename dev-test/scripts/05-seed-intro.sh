#!/usr/bin/env bash
# Seed an "Intro" media segment (Type=5) for the test episode so the plugin has
# something to act on. A fresh instance has no segments and no public POST API exists,
# so we write directly into jellyfin.db (container stopped for a safe write).
#
# This script FIRST inspects the real MediaSegments schema of the running JF version --
# that inspection is itself the core diagnosis (Type INTEGER vs TEXT? GUID casing/hyphens?).
#
# Overridable via env:
#   TYPE_VALUE=5            (Intro; integer to match EF enum storage)
#   ITEMID_STYLE=upper-hyphen | upper-nohyphen | lower-hyphen | lower-nohyphen
#   INTRO_START_S=5   INTRO_END_S=20
source "$(dirname "$0")/lib.sh"

DB="$(jf_db_path)"
[ -f "$DB" ] || { err "jellyfin.db not found at $DB (run 03 first)."; exit 1; }
[ -s "$STATE_DIR/episode_id" ] || { err "No episode_id in .state (run 03 first)."; exit 1; }
EP_ID_RAW="$(cat "$STATE_DIR/episode_id")"   # 32-char hex, no hyphens, from the API

TYPE_VALUE="${TYPE_VALUE:-5}"
ITEMID_STYLE="${ITEMID_STYLE:-upper-hyphen}"
INTRO_START_S="${INTRO_START_S:-5}"
INTRO_END_S="${INTRO_END_S:-20}"

# Format the episode GUID into the requested style.
ITEMID="$(python3 - "$EP_ID_RAW" "$ITEMID_STYLE" <<'PY'
import sys, uuid
raw, style = sys.argv[1], sys.argv[2]
g = uuid.UUID(raw)
s = str(g) if "hyphen" in style and "nohyphen" not in style else g.hex
s = s.upper() if style.startswith("upper") else s.lower()
print(s)
PY
)"

START_TICKS=$(( INTRO_START_S * 10000000 ))
END_TICKS=$(( INTRO_END_S * 10000000 ))
SEG_ID="$(python3 -c 'import uuid;print(str(uuid.uuid4()).upper())')"
PROVIDER_ID="b0338b450421c081992860f1d02f261f"   # Intro Skipper's provider id (matches plugin)

log "Stopping Jellyfin for safe DB write ..."
$COMPOSE stop >/dev/null

echo
log "=== MediaSegments schema (THE key diagnostic) ==="
sqlite3 "$DB" ".schema MediaSegments" | sed 's/^/    /' || warn "MediaSegments table not found!"
echo
log "=== Column types (PRAGMA table_info) ==="
sqlite3 "$DB" "PRAGMA table_info(MediaSegments);" | sed 's/^/    /'
echo
log "=== Existing rows (count + sample) ==="
sqlite3 "$DB" "SELECT COUNT(*) AS n FROM MediaSegments;" | sed 's/^/    count=/'
sqlite3 -header "$DB" "SELECT * FROM MediaSegments LIMIT 3;" | sed 's/^/    /' || true

echo
log "Inserting Intro segment:  ItemId=$ITEMID (style=$ITEMID_STYLE)  Type=$TYPE_VALUE  ${INTRO_START_S}s-${INTRO_END_S}s"
sqlite3 "$DB" <<SQL
INSERT INTO MediaSegments (Id, ItemId, SegmentProviderId, Type, StartTicks, EndTicks)
VALUES ('$SEG_ID', '$ITEMID', '$PROVIDER_ID', $TYPE_VALUE, $START_TICKS, $END_TICKS);
SQL

echo
log "=== Read back (with storage class via typeof) ==="
sqlite3 -header "$DB" "SELECT Id, ItemId, typeof(ItemId) AS itemid_t, Type, typeof(Type) AS type_t, StartTicks, EndTicks FROM MediaSegments;" | sed 's/^/    /'

log "Starting Jellyfin again ..."
$COMPOSE start >/dev/null
wait_for_jellyfin
ok "Intro seeded. If Jellyfin's /MediaSegments API (step 06) doesn't return it, the ItemId style/Type is wrong for this JF version -> re-run with different ITEMID_STYLE/TYPE_VALUE."
