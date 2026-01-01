# Jellyfin Preview Segment Plugin - Deployment Guide

## Quick Start

### Prerequisites
- Jellyfin Server 10.9.0 or later (10.10+ recommended)
- Episodes with existing intro segments
- Access to Jellyfin server file system

### Installation Steps

1. **Build the Plugin**
   ```bash
   cd Jellyfin.Plugin.PreviewSegment
   dotnet build -c Release
   ```

2. **Locate the DLL**
   The compiled plugin will be at:
   ```
   bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll
   ```

3. **Install to Jellyfin**
   Copy the DLL to your Jellyfin plugins directory:
   
   - **Linux**: `/var/lib/jellyfin/plugins/PreviewSegment/`
   - **Windows**: `%PROGRAMDATA%\Jellyfin\Server\plugins\PreviewSegment\`
   - **Docker**: `/config/plugins/PreviewSegment/`

   Example:
   ```bash
   mkdir -p /var/lib/jellyfin/plugins/PreviewSegment
   cp bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll /var/lib/jellyfin/plugins/PreviewSegment/
   ```

4. **Restart Jellyfin**
   ```bash
   sudo systemctl restart jellyfin  # Linux with systemd
   ```

5. **Configure the Plugin**
   - Open Jellyfin web interface
   - Go to Dashboard → Plugins
   - Find "Preview Segment" and click to configure
   - Select the libraries you want to process
   - Click Save

6. **Run the Task**
   - Go to Dashboard → Scheduled Tasks
   - Find "Add Preview Segments"
   - Click the play button to run immediately
   - Or wait for the scheduled run (daily at 2:00 AM by default)

## Configuration

### Library Selection
The plugin configuration page allows you to select which Jellyfin libraries should be processed. Only episodes in the selected libraries will have preview segments added.

### Scheduled Task Settings
You can customize the task schedule from the Scheduled Tasks page:
- Trigger type (Daily, Weekly, Interval, etc.)
- Time of day
- Additional triggers

## How It Works

1. **Detection**: The task scans all episodes in configured libraries
2. **Filtering**: Identifies episodes with intro segments but no preview segments
3. **Creation**: Adds preview segment from 0 seconds to intro start time
4. **Logging**: Reports progress and results in Jellyfin logs

## Troubleshooting

### Plugin Not Appearing
- Verify DLL is in correct directory
- Check Jellyfin logs for loading errors
- Ensure Jellyfin was restarted after installation

### No Preview Segments Added
1. **Check Prerequisites**:
   - Episodes must have intro segments already
   - Jellyfin version must support MediaSegments table (10.10+)
   
2. **Check Logs**:
   ```bash
   tail -f /var/log/jellyfin/jellyfin.log  # Linux
   ```
   Look for messages like:
   - "Starting preview segment processing"
   - "Added preview segment to episode"
   - Any error messages

3. **Verify Database**:
   Check if MediaSegments table exists:
   ```bash
   sqlite3 /var/lib/jellyfin/data/library.db
   .tables
   # Should show MediaSegments table
   .quit
   ```

### Task Fails to Run
- Check library IDs are configured
- Verify database permissions
- Check Jellyfin logs for error details

## Verification

### Check Segments in Database
```bash
sqlite3 /var/lib/jellyfin/data/library.db
SELECT Type, StartTicks, EndTicks FROM MediaSegments WHERE Type = 'Preview' LIMIT 5;
.quit
```

### View in Jellyfin
- Play an episode that should have a preview segment
- The preview segment should appear in the player timeline
- Check the episode's media info page

## Advanced Configuration

### Custom Schedule
To change the task schedule:
1. Go to Dashboard → Scheduled Tasks
2. Find "Add Preview Segments"
3. Click to edit triggers
4. Add or modify triggers as needed

### Database Location
The plugin uses the Jellyfin data path automatically. If you have a custom location, ensure the Jellyfin configuration is correct.

## Uninstallation

1. Remove the plugin directory:
   ```bash
   rm -rf /var/lib/jellyfin/plugins/PreviewSegment
   ```

2. Restart Jellyfin

3. (Optional) Remove preview segments from database:
   ```sql
   DELETE FROM MediaSegments WHERE Type = 'Preview';
   ```

## Support

### Logs Location
- **Linux**: `/var/log/jellyfin/jellyfin.log`
- **Windows**: `%PROGRAMDATA%\Jellyfin\Server\log\`
- **Docker**: Check container logs

### Common Log Messages
- **Success**: "Added preview segment to episode 'Name' (S#E#) from 0 to Xs"
- **Info**: "No libraries configured for preview segment processing"
- **Warning**: "MediaSegments table does not exist in the database"
- **Error**: "Error processing episode 'Name'"

## Security Notes

- The plugin uses parameterized SQL queries to prevent injection
- Configuration page prevents XSS by using safe DOM methods
- No network access required
- Only accesses Jellyfin's own database

## Compatibility

### Tested Versions
- Jellyfin 10.9.11 (API compatibility)
- .NET 8.0 runtime

### Known Limitations
- Requires MediaSegments table (Jellyfin 10.10+)
- Only processes TV episodes (not movies or other media)
- Requires pre-existing intro segments
- Direct database access may need updates for future Jellyfin versions

## Performance

### Resource Usage
- Minimal CPU usage during scanning
- Database I/O proportional to episode count
- Memory usage: ~50-100MB during execution
- Network: No external network access

### Large Libraries
For libraries with thousands of episodes:
- Task may take several minutes
- Progress is reported in the UI
- Can be cancelled if needed
- Safe to run during normal operation

## Updates

To update the plugin:
1. Build new version
2. Stop Jellyfin
3. Replace DLL
4. Start Jellyfin
5. Verify in logs

## Development

### Building from Source
```bash
git clone https://github.com/brunobeeee/jellyfin-add-preview-segment.git
cd jellyfin-add-preview-segment/Jellyfin.Plugin.PreviewSegment
dotnet restore
dotnet build -c Release
```

### Debug Build
```bash
dotnet build -c Debug
```

### Running Tests
Currently no automated tests. Manual testing required:
1. Deploy to test instance
2. Configure test library
3. Run task
4. Verify segments in database and UI

## License

See main repository for license information.

## Contributing

See IMPLEMENTATION.md for technical details about the plugin architecture.
