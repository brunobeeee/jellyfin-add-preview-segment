# Testing Guide for Preview Segment Plugin

## Overview
This guide provides comprehensive testing procedures to validate the Preview Segment plugin functionality in a real Jellyfin instance. Use this to identify and diagnose production issues.

## Prerequisites

### System Requirements
- Jellyfin Server 10.9.0 or later (10.10+ recommended for MediaSegments support)
- Episodes with existing intro segments
- Access to Jellyfin server file system and logs
- SQLite3 command-line tool (for database inspection)

### Required Setup
1. At least one library with TV shows
2. Episodes with intro segments already detected
3. Access to Jellyfin logs directory

## Installation for Testing

### Step 1: Build the Plugin
```bash
cd Jellyfin.Plugin.PreviewSegment
dotnet build -c Release
```

The compiled DLL will be in: `bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll`

### Step 2: Install to Jellyfin

#### For Linux/Docker:
```bash
# Create plugin directory
sudo mkdir -p /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment

# Copy the DLL
sudo cp bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll \
    /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment/

# Set correct permissions
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment
```

#### For Docker:
```bash
# Copy to config/plugins directory (adjust path as needed)
cp bin/Release/net8.0/Jellyfin.Plugin.PreviewSegment.dll \
    /path/to/jellyfin/config/plugins/Jellyfin.Plugin.PreviewSegment/
```

#### For Windows:
```powershell
# Copy to Jellyfin plugins directory
Copy-Item bin\Release\net8.0\Jellyfin.Plugin.PreviewSegment.dll `
    "$env:PROGRAMDATA\Jellyfin\Server\plugins\Jellyfin.Plugin.PreviewSegment\"
```

### Step 3: Restart Jellyfin
```bash
# Linux with systemd
sudo systemctl restart jellyfin

# Docker
docker restart jellyfin

# Windows (as Administrator)
Restart-Service JellyfinServer
```

## Pre-Testing Verification

### 1. Verify Plugin Loaded
Check Jellyfin logs for plugin initialization:

```bash
# Linux
tail -f /var/log/jellyfin/jellyfin.log | grep "Preview Segment"

# Docker
docker logs -f jellyfin | grep "Preview Segment"
```

Expected output:
```
[INF] Loaded plugin: Preview Segment 1.0.0.0
```

### 2. Check Plugin Appears in Dashboard
1. Open Jellyfin web interface
2. Navigate to **Dashboard** → **Plugins**
3. Verify "Preview Segment" is listed
4. Click on it to access configuration

### 3. Verify Scheduled Task
1. Navigate to **Dashboard** → **Scheduled Tasks**
2. Find "Add Preview Segments" in the Library category
3. Verify it shows the default schedule (Daily at 2:00 AM)

## Database Pre-Testing

### 1. Verify MediaSegments Table Exists
```bash
# Access the Jellyfin database
sqlite3 /var/lib/jellyfin/data/library.db

# Check if table exists
.tables

# Should show MediaSegments in the list
# Exit
.quit
```

If MediaSegments table doesn't exist, your Jellyfin version may not support this feature.

### 2. Check Existing Segments
```bash
sqlite3 /var/lib/jellyfin/data/library.db

# View existing intro segments
SELECT 
    ItemId,
    Type,
    StartTicks,
    EndTicks,
    datetime(StartTicks/10000000, 'unixepoch') as StartTime,
    datetime(EndTicks/10000000, 'unixepoch') as EndTime
FROM MediaSegments 
WHERE Type = 'Intro'
LIMIT 5;

# Check GUID format used in database
SELECT DISTINCT length(ItemId) as GuidLength, ItemId
FROM MediaSegments 
LIMIT 3;

.quit
```

Expected GUID formats:
- 32 characters = Without hyphens (e.g., `a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6`)
- 36 characters = With hyphens (e.g., `a1b2c3d4-e5f6-g7h8-i9j0-k1l2m3n4o5p6`)

Note: The plugin now handles both formats automatically.

## Configuration Testing

### 1. Configure Libraries
1. Go to **Dashboard** → **Plugins** → **Preview Segment**
2. Select one or more libraries containing TV shows
3. Click **Save**
4. Verify confirmation message appears

### 2. Verify Configuration Saved
```bash
# Check the configuration file
cat /var/lib/jellyfin/config/Jellyfin.Plugin.PreviewSegment.xml

# Or for Docker
cat /config/config/Jellyfin.Plugin.PreviewSegment.xml
```

Expected content:
```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <LibraryIds>
    <guid>library-id-1</guid>
    <guid>library-id-2</guid>
  </LibraryIds>
</PluginConfiguration>
```

## Manual Task Execution Test

### 1. Run Task from Dashboard
1. Go to **Dashboard** → **Scheduled Tasks**
2. Find "Add Preview Segments"
3. Click the **Play** button (▶) to run immediately
4. Watch the progress bar

### 2. Monitor Logs During Execution
Open a new terminal and monitor logs in real-time:

```bash
# Linux
tail -f /var/log/jellyfin/jellyfin.log | grep -i "preview\|segment"

# Docker
docker logs -f jellyfin 2>&1 | grep -i "preview\|segment"
```

Expected log messages:
```
[INF] Starting preview segment processing for 1 libraries
[INF] Found 150 episodes to check
[INF] Opening database connection to: /var/lib/jellyfin/data/library.db
[INF] MediaSegments table found, starting to process episodes
[INF] Added preview segment to episode 'Episode Name' (S1E1) from 0 to 30s
[INF] Preview segment processing completed. Processed: 150, Added: 45
```

### 3. Check for Errors
Look for error messages in logs:

```bash
# Filter for errors
tail -n 1000 /var/log/jellyfin/jellyfin.log | grep -i "error\|exception\|fail" | grep -i "preview\|segment"
```

## Database Verification After Task

### 1. Verify Preview Segments Created
```bash
sqlite3 /var/lib/jellyfin/data/library.db

# Count preview segments
SELECT COUNT(*) as PreviewCount 
FROM MediaSegments 
WHERE Type = 'Preview';

# View sample preview segments
SELECT 
    ItemId,
    Type,
    StartTicks/10000000.0 as StartSeconds,
    EndTicks/10000000.0 as EndSeconds,
    (EndTicks - StartTicks)/10000000.0 as DurationSeconds
FROM MediaSegments 
WHERE Type = 'Preview'
LIMIT 10;

# Compare intro and preview segments for same episode
SELECT 
    ItemId,
    Type,
    StartTicks/10000000.0 as StartSeconds,
    EndTicks/10000000.0 as EndSeconds
FROM MediaSegments 
WHERE ItemId IN (
    SELECT ItemId FROM MediaSegments WHERE Type = 'Preview' LIMIT 1
)
ORDER BY Type, StartTicks;

.quit
```

Expected results:
- Preview segment should start at 0 seconds
- Preview segment should end where intro starts
- Both segments should have same ItemId

### 2. Cross-Reference with Jellyfin Items
```bash
sqlite3 /var/lib/jellyfin/data/library.db

# Find episode name for a preview segment
SELECT 
    ms.Type,
    ms.StartTicks/10000000.0 as StartSeconds,
    ms.EndTicks/10000000.0 as EndSeconds,
    i.Name,
    i.ParentIndexNumber as Season,
    i.IndexNumber as Episode
FROM MediaSegments ms
JOIN Items2 i ON i.guid = ms.ItemId OR i.guid = REPLACE(ms.ItemId, '-', '')
WHERE ms.Type = 'Preview'
LIMIT 5;

.quit
```

## UI Verification

### 1. Check Episode Player
1. Navigate to a TV show with episodes that should have preview segments
2. Start playing an episode
3. Look for segment markers in the player timeline
4. Verify preview segment shows in the timeline

### 2. Check Episode Details
1. Click on an episode to view details
2. Look for media info section
3. Check if segments are displayed

## Common Issues and Troubleshooting

### Issue 1: Plugin Not Loading
**Symptoms:**
- Plugin doesn't appear in Dashboard → Plugins
- No log messages about plugin loading

**Diagnosis:**
```bash
# Check plugin directory exists
ls -la /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment/

# Check DLL exists
ls -la /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment/*.dll

# Check permissions
ls -la /var/lib/jellyfin/plugins/Jellyfin.Plugin.PreviewSegment/
# Should be owned by jellyfin user
```

**Solutions:**
1. Verify DLL is in correct directory
2. Check file permissions (should be readable by jellyfin user)
3. Ensure Jellyfin was restarted after installation
4. Check Jellyfin logs for assembly loading errors

### Issue 2: MediaSegments Table Not Found
**Symptoms:**
- Log message: "MediaSegments table does not exist in the database"
- Task completes immediately with no segments added

**Diagnosis:**
```bash
sqlite3 /var/lib/jellyfin/data/library.db ".tables" | grep MediaSegments
```

**Solutions:**
1. Upgrade Jellyfin to version 10.10 or later
2. Check if intro detection plugins are properly installed
3. Verify database path is correct

### Issue 3: No Preview Segments Added
**Symptoms:**
- Task runs successfully
- Log shows "Processed: X, Added: 0"
- No preview segments in database

**Diagnosis:**
```bash
# Check if episodes have intro segments
sqlite3 /var/lib/jellyfin/data/library.db

SELECT COUNT(*) as IntroCount 
FROM MediaSegments 
WHERE Type = 'Intro';

# Check if episodes already have preview segments
SELECT COUNT(*) as PreviewCount 
FROM MediaSegments 
WHERE Type = 'Preview';

# Check intro segment start times
SELECT 
    ItemId,
    StartTicks/10000000.0 as StartSeconds,
    EndTicks/10000000.0 as EndSeconds
FROM MediaSegments 
WHERE Type = 'Intro'
LIMIT 10;

.quit
```

**Possible causes:**
1. No intro segments exist in the configured libraries
2. Intro segments start at 0 seconds (plugin skips these)
3. Intro segments start less than 1 second into episode (plugin skips these)
4. Episodes already have preview segments
5. Wrong libraries configured

**Solutions:**
1. Run intro detection first (use intro skip plugins)
2. Verify library selection in plugin configuration
3. Check that episodes have valid intro segments (start > 1 second)

### Issue 4: GUID Format Mismatch
**Symptoms:**
- Task runs but no segments found for episodes
- Database has segments but plugin doesn't see them

**Diagnosis:**
```bash
# Check GUID format in database
sqlite3 /var/lib/jellyfin/data/library.db

SELECT DISTINCT 
    length(ItemId) as GuidLength,
    CASE 
        WHEN length(ItemId) = 32 THEN 'Without hyphens'
        WHEN length(ItemId) = 36 THEN 'With hyphens'
        ELSE 'Unknown format'
    END as Format,
    COUNT(*) as Count
FROM MediaSegments
GROUP BY length(ItemId);

.quit
```

**Solutions:**
The latest version of the plugin automatically handles both GUID formats (with and without hyphens). If you're still seeing issues:
1. Update to the latest plugin version
2. Check logs for GUID-related errors
3. Verify database integrity

### Issue 5: Database Permission Errors
**Symptoms:**
- Log message: "Error opening database connection"
- Exception about file access denied

**Diagnosis:**
```bash
# Check database file permissions
ls -la /var/lib/jellyfin/data/library.db

# Check if database is locked
lsof /var/lib/jellyfin/data/library.db
```

**Solutions:**
1. Ensure jellyfin user has read/write access to database
2. Check if database is locked by another process
3. Restart Jellyfin service

### Issue 6: Task Fails Silently
**Symptoms:**
- Task shows as completed but no logs
- No errors in logs

**Diagnosis:**
```bash
# Check log level
cat /var/lib/jellyfin/config/logging.json

# Should have at least "Information" level for the plugin
```

**Solutions:**
1. Increase log level to "Debug" in logging configuration
2. Check system logs (journalctl -u jellyfin)
3. Verify task actually ran (check last execution time)

## Performance Testing

### Test with Large Libraries
1. Configure plugin with a library containing 1000+ episodes
2. Run the task manually
3. Monitor system resources during execution:

```bash
# Monitor CPU and memory usage
top -p $(pgrep -f jellyfin)

# Monitor disk I/O
iostat -x 1
```

Expected performance:
- CPU: ~10-30% usage during processing
- Memory: +50-100MB during task execution
- Disk I/O: Moderate reads from database
- Processing speed: ~10-50 episodes per second

## Integration Testing

### Test 1: Multiple Libraries
1. Configure plugin with multiple TV libraries
2. Run task
3. Verify segments added to episodes across all libraries

### Test 2: Mixed Content
1. Configure library with both movies and TV shows
2. Run task
3. Verify only TV episodes are processed (movies ignored)

### Test 3: Scheduled Execution
1. Configure task to run at a specific time
2. Wait for scheduled execution
3. Check logs for automatic execution

### Test 4: Cancellation
1. Start task manually
2. Click stop/cancel button while running
3. Verify task stops gracefully
4. Check partial results in database

## Automated Test Script

Create a test script to automate verification:

```bash
#!/bin/bash
# test-plugin.sh

echo "=== Preview Segment Plugin Test ==="
echo ""

# Check plugin loaded
echo "1. Checking if plugin is loaded..."
if grep -q "Preview Segment" /var/log/jellyfin/jellyfin.log; then
    echo "   ✓ Plugin loaded"
else
    echo "   ✗ Plugin not found in logs"
fi

# Check MediaSegments table
echo "2. Checking MediaSegments table..."
if sqlite3 /var/lib/jellyfin/data/library.db ".tables" | grep -q MediaSegments; then
    echo "   ✓ MediaSegments table exists"
else
    echo "   ✗ MediaSegments table not found"
fi

# Count segments
echo "3. Counting segments..."
INTRO_COUNT=$(sqlite3 /var/lib/jellyfin/data/library.db "SELECT COUNT(*) FROM MediaSegments WHERE Type = 'Intro';")
PREVIEW_COUNT=$(sqlite3 /var/lib/jellyfin/data/library.db "SELECT COUNT(*) FROM MediaSegments WHERE Type = 'Preview';")
echo "   Intro segments: $INTRO_COUNT"
echo "   Preview segments: $PREVIEW_COUNT"

# Check configuration
echo "4. Checking configuration..."
if [ -f /var/lib/jellyfin/config/Jellyfin.Plugin.PreviewSegment.xml ]; then
    echo "   ✓ Configuration file exists"
    LIB_COUNT=$(grep -c "<guid>" /var/lib/jellyfin/config/Jellyfin.Plugin.PreviewSegment.xml)
    echo "   Configured libraries: $LIB_COUNT"
else
    echo "   ✗ Configuration file not found"
fi

echo ""
echo "=== Test Complete ==="
```

Make executable and run:
```bash
chmod +x test-plugin.sh
sudo ./test-plugin.sh
```

## Debug Mode

### Enable Detailed Logging
Edit Jellyfin logging configuration:

```bash
# Edit logging config
nano /var/lib/jellyfin/config/logging.json
```

Add or modify:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Jellyfin.Plugin.PreviewSegment": "Debug"
      }
    }
  }
}
```

Restart Jellyfin to apply changes.

### View Debug Logs
```bash
tail -f /var/log/jellyfin/jellyfin.log | grep "Jellyfin.Plugin.PreviewSegment"
```

Debug logs will show:
- Database queries
- GUID format detection
- Episodes skipped and reasons
- Detailed error information

## Reporting Issues

When reporting issues, include:

1. **System Information:**
   - Jellyfin version
   - Operating system
   - Plugin version
   - .NET runtime version

2. **Configuration:**
   - Number of libraries configured
   - Total number of episodes
   - Library types

3. **Database Information:**
   ```bash
   sqlite3 /var/lib/jellyfin/data/library.db "SELECT COUNT(*) FROM MediaSegments;"
   sqlite3 /var/lib/jellyfin/data/library.db "SELECT Type, COUNT(*) FROM MediaSegments GROUP BY Type;"
   ```

4. **Relevant Logs:**
   - Last 100 lines containing "Preview Segment"
   - Any error or exception messages
   - Task execution logs

5. **Steps to Reproduce:**
   - Detailed steps that lead to the issue
   - Expected vs actual behavior

## Success Criteria

The plugin is working correctly if:

- ✓ Plugin appears in Dashboard → Plugins
- ✓ Scheduled task appears in Dashboard → Scheduled Tasks
- ✓ Task runs without errors
- ✓ Preview segments are added to database
- ✓ Preview segments start at 0 and end where intro starts
- ✓ Logs show successful processing
- ✓ No duplicate preview segments are created
- ✓ Player shows preview segments in timeline

## Additional Resources

- **Jellyfin Documentation:** https://jellyfin.org/docs/
- **MediaSegments API:** Check Jellyfin API documentation
- **Plugin Development:** https://jellyfin.org/docs/general/server/plugins/
- **SQLite Documentation:** https://www.sqlite.org/docs.html

## Contact

For issues or questions:
- Open an issue on GitHub
- Include all information from "Reporting Issues" section
- Attach relevant logs and database queries
