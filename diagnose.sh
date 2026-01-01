#!/bin/bash
# Jellyfin Preview Segment Plugin - Diagnostic Script
# This script helps diagnose issues with the Preview Segment plugin

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default paths (can be overridden)
JELLYFIN_DATA_PATH="/config/data"
JELLYFIN_CONFIG_PATH="/config"
JELLYFIN_LOG_PATH="/config/log"

echo "=============================================="
echo "  Jellyfin Preview Segment Plugin Diagnostic"
echo "=============================================="
echo ""

# Check if running as root or jellyfin user
if [ "$EUID" -ne 0 ] && [ "$(whoami)" != "jellyfin" ]; then
    echo -e "${YELLOW}Warning: This script should be run as root or jellyfin user for full diagnostics${NC}"
    echo ""
fi

# Allow custom paths via arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --data-path)
            JELLYFIN_DATA_PATH="$2"
            shift 2
            ;;
        --config-path)
            JELLYFIN_CONFIG_PATH="$2"
            shift 2
            ;;
        --log-path)
            JELLYFIN_LOG_PATH="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--data-path PATH] [--config-path PATH] [--log-path PATH]"
            exit 1
            ;;
    esac
done

echo "Using paths:"
echo "  Data: $JELLYFIN_DATA_PATH"
echo "  Config: $JELLYFIN_CONFIG_PATH"
echo "  Logs: $JELLYFIN_LOG_PATH"
echo ""

# Test 1: Check if Jellyfin is running
echo "1. Checking if Jellyfin is running..."
if pgrep -f jellyfin > /dev/null; then
    echo -e "   ${GREEN}✓${NC} Jellyfin is running"
else
    echo -e "   ${RED}✗${NC} Jellyfin is not running"
fi
echo ""

# Test 2: Check plugin installation
echo "2. Checking plugin installation..."
PLUGIN_DIR="$JELLYFIN_CONFIG_PATH/plugins/Jellyfin.Plugin.PreviewSegment"
if [ -d "$PLUGIN_DIR" ]; then
    echo -e "   ${GREEN}✓${NC} Plugin directory exists: $PLUGIN_DIR"
    
    if [ -f "$PLUGIN_DIR/Jellyfin.Plugin.PreviewSegment.dll" ]; then
        echo -e "   ${GREEN}✓${NC} Plugin DLL found"
        ls -lh "$PLUGIN_DIR/Jellyfin.Plugin.PreviewSegment.dll"
    else
        echo -e "   ${RED}✗${NC} Plugin DLL not found in $PLUGIN_DIR"
    fi
else
    echo -e "   ${RED}✗${NC} Plugin directory not found: $PLUGIN_DIR"
fi
echo ""

# Test 3: Check plugin loaded in logs
echo "3. Checking if plugin loaded successfully..."
LOG_FILE="$JELLYFIN_LOG_PATH/.jellyfin-log"
if [ -f "$LOG_FILE" ]; then
    if grep -q "Preview Segment" "$LOG_FILE"; then
        echo -e "   ${GREEN}✓${NC} Plugin found in logs"
        echo "   Last plugin-related log entries:"
        grep "Preview Segment" "$LOG_FILE" | tail -3 | sed 's/^/     /'
    else
        echo -e "   ${YELLOW}!${NC} Plugin not found in logs (may not be loaded)"
    fi
else
    echo -e "   ${YELLOW}!${NC} Log file not found: $LOG_FILE"
fi
echo ""

# Test 4: Check database
echo "4. Checking database..."
DB_FILE="$JELLYFIN_DATA_PATH/jellyfin.db"
if [ -f "$DB_FILE" ]; then
    echo -e "   ${GREEN}✓${NC} Database file exists: $DB_FILE"
    echo "   Database size: $(du -h "$DB_FILE" | cut -f1)"
    
    # Check if sqlite3 is available
    if command -v sqlite3 &> /dev/null; then
        # Check MediaSegments table
        if sqlite3 "$DB_FILE" ".tables" | grep -q MediaSegments; then
            echo -e "   ${GREEN}✓${NC} MediaSegments table exists"
            
            # Count segments
            INTRO_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM MediaSegments WHERE Type = 'Intro';")
            PREVIEW_COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM MediaSegments WHERE Type = 'Preview';")
            
            echo "   Segment counts:"
            echo "     - Intro segments: $INTRO_COUNT"
            echo "     - Preview segments: $PREVIEW_COUNT"
            
            # Check GUID format
            GUID_FORMATS=$(sqlite3 "$DB_FILE" "SELECT DISTINCT length(ItemId), COUNT(*) FROM MediaSegments GROUP BY length(ItemId);")
            echo "   GUID formats in database:"
            while IFS='|' read -r length count; do
                if [ "$length" = "32" ]; then
                    echo "     - Without hyphens: $count segments"
                elif [ "$length" = "36" ]; then
                    echo "     - With hyphens: $count segments"
                else
                    echo "     - Unknown format (length $length): $count segments"
                fi
            done <<< "$GUID_FORMATS"
            
        else
            echo -e "   ${RED}✗${NC} MediaSegments table not found"
            echo "   This feature requires Jellyfin 10.10 or later"
        fi
    else
        echo -e "   ${YELLOW}!${NC} sqlite3 not installed - cannot inspect database"
        echo "   Install with: apt-get install sqlite3 (Debian/Ubuntu)"
    fi
else
    echo -e "   ${RED}✗${NC} Database file not found: $DB_FILE"
fi
echo ""

# Test 5: Check plugin configuration
echo "5. Checking plugin configuration..."
CONFIG_FILE="$JELLYFIN_CONFIG_PATH/Jellyfin.Plugin.PreviewSegment.xml"
if [ -f "$CONFIG_FILE" ]; then
    echo -e "   ${GREEN}✓${NC} Configuration file exists"
    
    # Count configured libraries
    LIB_COUNT=$(grep -c "<guid>" "$CONFIG_FILE" || echo "0")
    echo "   Configured libraries: $LIB_COUNT"
    
    if [ "$LIB_COUNT" -eq 0 ]; then
        echo -e "   ${YELLOW}!${NC} No libraries configured - plugin will not process any episodes"
        echo "   Configure libraries in: Dashboard → Plugins → Preview Segment"
    fi
else
    echo -e "   ${YELLOW}!${NC} Configuration file not found (plugin may not be configured yet)"
    echo "   Configure in: Dashboard → Plugins → Preview Segment"
fi
echo ""

# Test 6: Check recent task executions
echo "6. Checking recent task executions..."
if [ -f "$LOG_FILE" ]; then
    TASK_RUNS=$(grep "Preview segment processing completed" "$LOG_FILE" | tail -5)
    if [ -n "$TASK_RUNS" ]; then
        echo -e "   ${GREEN}✓${NC} Found recent task executions:"
        echo "$TASK_RUNS" | while read -r line; do
            echo "     $line"
        done
    else
        echo -e "   ${YELLOW}!${NC} No completed task executions found in logs"
        echo "   Run the task manually from: Dashboard → Scheduled Tasks"
    fi
    
    # Check for errors
    ERROR_COUNT=$(grep -c "Error processing episode" "$LOG_FILE" || echo "0")
    if [ "$ERROR_COUNT" -gt 0 ]; then
        echo -e "   ${RED}✗${NC} Found $ERROR_COUNT episode processing errors"
        echo "   Recent errors:"
        grep "Error processing episode" "$LOG_FILE" | tail -3 | sed 's/^/     /'
    fi
else
    echo -e "   ${YELLOW}!${NC} Cannot check task executions - log file not found"
fi
echo ""

# Test 7: Check permissions
echo "7. Checking file permissions..."
if [ -d "$PLUGIN_DIR" ]; then
    PLUGIN_PERMS=$(stat -c "%a %U:%G" "$PLUGIN_DIR" 2>/dev/null || stat -f "%Lp %Su:%Sg" "$PLUGIN_DIR" 2>/dev/null)
    echo "   Plugin directory: $PLUGIN_PERMS"
fi
if [ -f "$DB_FILE" ]; then
    DB_PERMS=$(stat -c "%a %U:%G" "$DB_FILE" 2>/dev/null || stat -f "%Lp %Su:%Sg" "$DB_FILE" 2>/dev/null)
    echo "   Database file: $DB_PERMS"
fi
echo ""

# Summary
echo "=============================================="
echo "  Summary"
echo "=============================================="
echo ""

# Determine overall status
ISSUES=0

if ! pgrep -f jellyfin > /dev/null; then
    echo -e "${RED}⚠${NC} Jellyfin is not running"
    ISSUES=$((ISSUES + 1))
fi

if [ ! -f "$PLUGIN_DIR/Jellyfin.Plugin.PreviewSegment.dll" ]; then
    echo -e "${RED}⚠${NC} Plugin DLL not found - installation incomplete"
    ISSUES=$((ISSUES + 1))
fi

if [ -f "$LOG_FILE" ] && ! grep -q "Preview Segment" "$LOG_FILE"; then
    echo -e "${YELLOW}⚠${NC} Plugin not loaded - check Jellyfin logs for errors"
    ISSUES=$((ISSUES + 1))
fi

if [ ! -f "$DB_FILE" ]; then
    echo -e "${RED}⚠${NC} Database file not found"
    ISSUES=$((ISSUES + 1))
elif command -v sqlite3 &> /dev/null && ! sqlite3 "$DB_FILE" ".tables" | grep -q MediaSegments; then
    echo -e "${RED}⚠${NC} MediaSegments table missing - Jellyfin 10.10+ required"
    ISSUES=$((ISSUES + 1))
fi

if [ -f "$CONFIG_FILE" ]; then
    LIB_COUNT=$(grep -c "<guid>" "$CONFIG_FILE" || echo "0")
    if [ "$LIB_COUNT" -eq 0 ]; then
        echo -e "${YELLOW}⚠${NC} No libraries configured"
        ISSUES=$((ISSUES + 1))
    fi
else
    echo -e "${YELLOW}⚠${NC} Plugin not configured"
    ISSUES=$((ISSUES + 1))
fi

echo ""
if [ "$ISSUES" -eq 0 ]; then
    echo -e "${GREEN}✓ All checks passed - plugin appears to be working correctly${NC}"
    echo ""
    echo "Next steps:"
    echo "  1. Run the task manually: Dashboard → Scheduled Tasks → Add Preview Segments"
    echo "  2. Check logs for processing results"
    echo "  3. Verify preview segments in database or player"
else
    echo -e "${RED}✗ Found $ISSUES issue(s) - see above for details${NC}"
    echo ""
    echo "Troubleshooting steps:"
    echo "  1. Review the issues listed above"
    echo "  2. Check full logs in: $LOG_FILE"
    echo "  3. Consult TESTING.md for detailed troubleshooting"
    echo "  4. Ensure Jellyfin 10.10+ is installed for MediaSegments support"
fi

echo ""
echo "For detailed testing and troubleshooting, see TESTING.md"
echo "=============================================="
