#!/usr/bin/env bash
# Generate a small test episode: media/Test Show/Season 01/Test Show S01E01.mkv
source "$(dirname "$0")/lib.sh"

EP_DIR="$MEDIA_DIR/Test Show/Season 01"
EP_FILE="$EP_DIR/Test Show S01E01.mkv"
mkdir -p "$EP_DIR"

if [ -f "$EP_FILE" ]; then
  ok "Test episode already exists: $EP_FILE"
  exit 0
fi

log "Generating 120s test episode with ffmpeg ..."
ffmpeg -y -hide_banner -loglevel error \
  -f lavfi -i "testsrc=size=640x360:rate=24:duration=120" \
  -f lavfi -i "sine=frequency=440:duration=120" \
  -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
  -c:a aac -shortest \
  "$EP_FILE"

ok "Created: $EP_FILE"
ls -la "$EP_DIR" | sed 's/^/    /'
