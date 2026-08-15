# Jellyfin Preview Segment Plugin

A Jellyfin plugin that automatically adds a **Preview** media segment to episodes that already
have an **Intro** segment. The preview covers the region from the start of the episode (0s) to the
beginning of the intro, so clients can offer a "preview" skip.

## How it works

The plugin registers a **media segment provider** (`IMediaSegmentProvider`) with Jellyfin. For every
supported episode, Jellyfin's built-in **Extract Media Segments** task calls the provider, which:

1. reads the episode's existing **Intro** segment (e.g. produced by the Intro Skipper plugin), and
2. returns a **Preview** segment from `0` to the intro start.

Jellyfin stores and surfaces the segment under this plugin's own registered provider id, so it is
correctly returned to clients (`GET /MediaSegments/{itemId}`) and shown in the player timeline.

> This replaces the plugin's earlier approach of writing directly into Jellyfin's database. Direct
> DB writes stopped working because Jellyfin only surfaces segments from registered, enabled
> providers, and the internal schema changes between releases. See
> [dev-test/README.md](dev-test/README.md) for the full diagnosis.

## Requirements

- **Jellyfin 10.11 or later** (built against Jellyfin 10.11.x, targeting `net9.0`).
- Episodes must already have **Intro** segments (for example via the Intro Skipper plugin).

## Installation

### Option 1: Install from GitHub Releases (recommended for Docker)

1. Go to the [Releases page](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases).
2. Download the latest `jellyfin-plugin-previewsegment_X.X.X.zip` and extract it.
3. Copy the `Jellyfin.Plugin.PreviewSegment` folder (DLL **and** `meta.json`) into your Jellyfin
   plugins directory:
   - **Docker**: `/config/plugins/`
   - **Linux**: `/var/lib/jellyfin/plugins/`
   - **Windows**: `%PROGRAMDATA%\Jellyfin\Server\plugins\`
4. Restart Jellyfin.

## Usage

1. Ensure your episodes already have **Intro** segments (e.g. run Intro Skipper).
2. Enable the **Preview Segment** provider for the relevant libraries under
   *Dashboard → Libraries → (your library) → Media Segment Providers* (enabled by default).
3. Run *Dashboard → Scheduled Tasks → **Extract Media Segments*** (or wait for its scheduled run).
   Preview segments are created and shown by clients.

There is no plugin-specific configuration: which libraries are processed is controlled entirely by
Jellyfin's own per-library *Media Segment Providers* settings.

## Building from source

The project targets `net9.0` and builds against the Jellyfin 10.11 NuGet packages.

```bash
cd Jellyfin.Plugin.PreviewSegment
dotnet build -c Release
```

The compiled DLL will be in `bin/Release/net9.0/Jellyfin.Plugin.PreviewSegment.dll`.

No .NET SDK on your machine? Build it in a container:

```bash
docker run --rm -v "$PWD":/src -w /src/Jellyfin.Plugin.PreviewSegment \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build -c Release
```

## Local end-to-end testing

`dev-test/` contains a scriptable, throwaway Jellyfin instance (Docker) to build, install, and
end-to-end test the plugin against the latest Jellyfin release. See
[dev-test/README.md](dev-test/README.md).

## Automated builds

GitHub Actions builds the plugin on every push/PR to `main`, and on a pushed `v*.*.*` tag it creates
a release with a packaged ZIP (DLL + `meta.json`) and a SHA256 checksum. See
[RELEASE_GUIDE.md](RELEASE_GUIDE.md).

## License

This project is provided as-is for use with Jellyfin.
