#!/usr/bin/env bash
# Install the built DLL + a proper meta.json into config/plugins/, restart, report status.
#
# By default this mirrors the real release (only the main DLL) so we can reproduce the
# user's situation. Set COPY_DEPS=1 to also copy the build's dependency DLLs
# (Microsoft.Data.Sqlite, SQLitePCLRaw.*) — used to test the "missing dependency" theory.
source "$(dirname "$0")/lib.sh"

BUILD_OUT="$PLUGIN_PROJ_DIR/bin/Release/net9.0"
DLL="$BUILD_OUT/Jellyfin.Plugin.PreviewSegment.dll"
[ -f "$DLL" ] || { err "DLL not found. Run 01-build-plugin.sh first."; exit 1; }

PLUGIN_DIR="$CONFIG_DIR/plugins/Jellyfin.Plugin.PreviewSegment_${PLUGIN_VERSION}"
log "Installing plugin into $PLUGIN_DIR"
rm -rf "$CONFIG_DIR"/plugins/Jellyfin.Plugin.PreviewSegment_* 2>/dev/null || true
mkdir -p "$PLUGIN_DIR"
cp "$DLL" "$PLUGIN_DIR/"

# Show which dependency DLLs the build produced (relevant for the load-failure theory).
log "Dependency DLLs present in build output (NOT shipped by the real release):"
ls "$BUILD_OUT"/*.dll | grep -v 'Jellyfin.Plugin.PreviewSegment.dll' | sed 's/^/    /' || true

if [ "${COPY_DEPS:-0}" = "1" ]; then
  warn "COPY_DEPS=1 -> also copying dependency DLLs into the plugin folder."
  for d in "$BUILD_OUT"/Microsoft.Data.Sqlite.dll "$BUILD_OUT"/SQLitePCLRaw*.dll; do
    [ -f "$d" ] && cp "$d" "$PLUGIN_DIR/" && echo "    copied $(basename "$d")"
  done
fi

# --- Write meta.json ---------------------------------------------------------
TARGET_ABI="${TARGET_ABI:-10.11.0.0}"
python3 - "$PLUGIN_DIR/meta.json" "$PLUGIN_GUID" "$PLUGIN_NAME" "$PLUGIN_VERSION" "$TARGET_ABI" <<'PY'
import json, sys, datetime
path, guid, name, version, abi = sys.argv[1:6]
meta = {
    "category": "Metadata",
    "changelog": "Local dev-test build",
    "description": "Adds preview segments to episodes that already have intro segments",
    "guid": guid,
    "name": name,
    "overview": "Adds a preview segment from 0s to the intro start for episodes with an intro.",
    "owner": "brunobeeee",
    "targetAbi": abi,
    "framework": "net9.0",
    "timestamp": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
    "version": version,
    "status": "Active",
    "autoUpdate": False,
    "imagePath": "",
}
with open(path, "w") as f:
    json.dump(meta, f, indent=2)
print("wrote", path, "targetAbi=", abi)
PY

log "Restarting Jellyfin ..."
$COMPOSE restart
wait_for_jellyfin
sleep 3

# --- Report plugin load status ----------------------------------------------
log "Plugin status via API:"
jf_api GET /Plugins | python3 -m json.tool || warn "Could not read /Plugins"

echo
log "Recent plugin-related log lines:"
grep -iE "preview segment|PreviewSegment|plugin" "$CONFIG_DIR"/log/*.log 2>/dev/null | tail -40 | sed 's/^/    /' \
  || warn "No matching log lines yet (check $CONFIG_DIR/log/)."

echo
ok "Now open $JF_URL/web/#/dashboard/plugins and click 'Preview Segment' to reproduce the error."
ok "Watch logs live with:  $COMPOSE logs -f  (or tail $CONFIG_DIR/log/*.log)"
