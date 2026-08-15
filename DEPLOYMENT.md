# Jellyfin Preview Segment Plugin - Deployment Guide

## Overview

The plugin registers a **media segment provider** with Jellyfin. Jellyfin's built-in
**Extract Media Segments** task calls the provider for each supported episode; if the episode has an
**Intro** segment, the provider returns a **Preview** segment from `0` to the intro start. Jellyfin
stores and surfaces the result under the plugin's own registered provider id — no direct database
access is involved.

## Prerequisites

- **Jellyfin 10.11 or later** (the plugin is built against Jellyfin 10.11.x / `net9.0`).
- Episodes that already have **Intro** segments (e.g. via the Intro Skipper plugin).
- Access to the Jellyfin plugins directory.

## Installation

### Method 1: Install from GitHub Releases (recommended for Docker)

1. Download the latest `jellyfin-plugin-previewsegment_X.X.X.zip` from the
   [Releases page](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases) and extract it.
2. Copy the extracted `Jellyfin.Plugin.PreviewSegment` folder — which contains both the DLL **and**
   `meta.json` — into your Jellyfin plugins directory:
   - **Docker**: `/config/plugins/`
   - **Linux**: `/var/lib/jellyfin/plugins/`
   - **Windows**: `%PROGRAMDATA%\Jellyfin\Server\plugins\`
3. Restart Jellyfin (`docker restart jellyfin`, `sudo systemctl restart jellyfin`, or restart the service).

> Always ship the `meta.json` alongside the DLL. Installing a bare DLL still loads, but Jellyfin's
> plugin **details** page will show *"There was an error getting the plugin details from the
> repository"* because the plugin is not published in a plugin repository. This is cosmetic and does
> not affect functionality.

### Method 2: Build from source

The project targets `net9.0` and builds against the Jellyfin 10.11 NuGet packages.

```bash
cd Jellyfin.Plugin.PreviewSegment
dotnet build -c Release
# -> bin/Release/net9.0/Jellyfin.Plugin.PreviewSegment.dll
```

No local .NET SDK? Build in a container:

```bash
docker run --rm -v "$PWD":/src -w /src/Jellyfin.Plugin.PreviewSegment \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build -c Release
```

Copy the DLL (and a `meta.json`) into a `plugins/Jellyfin.Plugin.PreviewSegment/` folder and restart Jellyfin.

## Configuration & usage

There is **no plugin-specific configuration**. Applicability is controlled by Jellyfin's own
per-library settings.

1. Make sure your episodes have **Intro** segments.
2. Enable the **Preview Segment** provider for the relevant libraries under
   *Dashboard → Libraries → (your library) → Media Segment Providers* (enabled by default).
3. Run *Dashboard → Scheduled Tasks → **Extract Media Segments*** (or wait for its scheduled run,
   every 12 hours by default).

## Verification

### Via the API
Preview segments are returned by the media segments endpoint for an episode:

```bash
curl -H "Authorization: MediaBrowser Token=\"<api-key>\"" \
  "http://<server>:8096/MediaSegments/<itemId>"
# -> Items[] should contain an entry with "Type": "Preview", "StartTicks": 0
```

### In the player
Play an episode that has an intro; the preview region appears at the start of the timeline.

### In the logs
Look for lines from the provider:

```
Jellyfin.Plugin.PreviewSegment.Providers.PreviewSegmentProvider: Adding preview segment for '<name>': 0 -> <n>s
Jellyfin.Server.Implementations.MediaSegments.MediaSegmentManager: Media Segment provider "Preview Segment" found 1 for <path>
```

## Troubleshooting

### No preview segments are created
- Confirm the episodes actually have **Intro** segments (the provider needs an intro to work from).
- Confirm the **Preview Segment** provider is enabled for the library.
- Run **Extract Media Segments** and check the logs. If intros were just created in the same run,
  running the task once more guarantees the preview is generated.

### Preview created but not shown
- Ensure you are on **Jellyfin 10.11+**; the plugin is built for the 10.11 API and `net9.0`.
- Confirm the provider is enabled for the item's library (disabled providers are filtered from
  results).

### Plugin does not load
- Check the Jellyfin logs at startup for assembly load errors.
- Verify the DLL is under `plugins/Jellyfin.Plugin.PreviewSegment/` and Jellyfin was restarted.

## Local end-to-end testing

`dev-test/` provides a scriptable throwaway Jellyfin instance (Docker) that builds, installs, and
end-to-end tests the plugin against the latest Jellyfin release. See
[dev-test/README.md](dev-test/README.md).

## Uninstallation

1. Remove the plugin folder from the plugins directory and restart Jellyfin.
2. (Optional) Re-run **Extract Media Segments** with the provider disabled; Jellyfin removes the
   preview segments that were created by this provider.
