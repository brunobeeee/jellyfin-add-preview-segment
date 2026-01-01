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

1. Download the plugin DLL from the releases page
2. Place it in your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure the plugin in the Jellyfin dashboard under Plugins > Preview Segment

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

## License

This project is provided as-is for use with Jellyfin.
