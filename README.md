# Jellyfin Preview Segment Plugin

A Jellyfin plugin that automatically adds preview segments to episodes that already have an intro segment.

## Features

- **Scheduled Task**: Adds a periodic task that runs automatically
- **Library Selection**: Configure which libraries should be processed through the plugin settings
- **Automatic Preview Segments**: For episodes with intro segments but no preview segments, automatically creates a preview segment from 0 seconds to the start of the intro

## Requirements

- Jellyfin 10.9.0 or later
- Episodes must already have intro segments detected (e.g., using intro skip plugins or manual configuration)
- The MediaSegments feature must be available in your Jellyfin version (10.10+)

## Installation

### Option 1: Install from GitHub Releases (Recommended for Docker)

1. Go to the [Releases page](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases)
2. Download the latest `jellyfin-plugin-previewsegment_X.X.X.zip` file
3. Extract the zip file
4. Copy the `Jellyfin.Plugin.PreviewSegment` folder to your Jellyfin plugins directory:
   - **Docker**: `/config/plugins/` (or wherever your config volume is mounted)
   - **Linux**: `/var/lib/jellyfin/plugins/`
   - **Windows**: `%PROGRAMDATA%\Jellyfin\Server\plugins\`
5. Restart Jellyfin
6. Configure the plugin in the Jellyfin dashboard under Plugins > Preview Segment

### Option 2: Install DLL Directly

1. Download the `Jellyfin.Plugin.PreviewSegment.dll` from the [Releases page](https://github.com/brunobeeee/jellyfin-add-preview-segment/releases)
2. Create the directory structure: `plugins/Jellyfin.Plugin.PreviewSegment/`
3. Place the DLL in that directory
4. Restart Jellyfin
5. Configure the plugin in the Jellyfin dashboard under Plugins > Preview Segment

## Configuration

1. Go to Dashboard > Plugins > Preview Segment
2. Select the libraries you want to process
3. Save the configuration

## Scheduled Task

The plugin adds a scheduled task called "Add Preview Segments" which:
- Runs daily at 2:00 AM by default
- Can be triggered manually from Dashboard > Scheduled Tasks
- Processes all episodes in the configured libraries
- Checks each episode for intro segments without preview segments
- Adds preview segments from 0s to the intro start time

## Building from Source

```bash
cd Jellyfin.Plugin.PreviewSegment
dotnet build
```

The compiled DLL will be in `bin/Debug/net8.0/Jellyfin.Plugin.PreviewSegment.dll`

## Automated Builds

This plugin uses GitHub Actions to automatically build and release:

- **Continuous Integration**: Every push to main triggers a build to ensure the plugin compiles successfully
- **Automated Releases**: When a version tag (e.g., `v1.0.0`) is pushed, GitHub Actions automatically:
  - Builds the plugin
  - Creates a release with pre-packaged ZIP file
  - Includes the standalone DLL file
  - Generates SHA256 checksums for verification

This makes it easy to install the plugin on Docker and other platforms without needing to build from source.

## License

This project is provided as-is for use with Jellyfin.
