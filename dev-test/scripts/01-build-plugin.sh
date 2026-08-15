#!/usr/bin/env bash
# Build the plugin DLL inside a dotnet SDK container (no host dotnet needed).
source "$(dirname "$0")/lib.sh"

log "Building plugin in mcr.microsoft.com/dotnet/sdk:9.0 ..."
docker run --rm \
  -v "$REPO_DIR":/src \
  -w /src/Jellyfin.Plugin.PreviewSegment \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build -c Release

DLL="$PLUGIN_PROJ_DIR/bin/Release/net9.0/Jellyfin.Plugin.PreviewSegment.dll"
if [ -f "$DLL" ]; then
  ok "Built: $DLL"
  ls -la "$PLUGIN_PROJ_DIR/bin/Release/net9.0/" | sed 's/^/    /'
else
  err "Build did not produce the expected DLL."
  exit 1
fi
