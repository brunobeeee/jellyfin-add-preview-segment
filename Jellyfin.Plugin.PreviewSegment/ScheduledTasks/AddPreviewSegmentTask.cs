using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PreviewSegment.ScheduledTasks;

/// <summary>
/// Scheduled task to add preview segments to episodes.
/// </summary>
public class AddPreviewSegmentTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<AddPreviewSegmentTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPreviewSegmentTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="appPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{AddPreviewSegmentTask}"/> interface.</param>
    public AddPreviewSegmentTask(ILibraryManager libraryManager, IApplicationPaths appPaths, ILogger<AddPreviewSegmentTask> logger)
    {
        _libraryManager = libraryManager;
        _appPaths = appPaths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Add Preview Segments";

    /// <inheritdoc />
    public string Key => "AddPreviewSegments";

    /// <inheritdoc />
    public string Description => "Adds preview segments to episodes that have intro segments but no preview segments.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || config.LibraryIds.Length == 0)
        {
            _logger.LogInformation("No libraries configured for preview segment processing");
            return;
        }

        _logger.LogInformation("Starting preview segment processing for {Count} libraries", config.LibraryIds.Length);

        var episodesToProcess = new List<Episode>();

        // Get all episodes from the selected libraries
        foreach (var libraryId in config.LibraryIds)
        {
            var library = _libraryManager.GetItemById(libraryId);
            if (library == null)
            {
                _logger.LogWarning("Library with ID {LibraryId} not found", libraryId);
                continue;
            }

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Parent = library,
                Recursive = true
            };

            var episodes = _libraryManager.GetItemsResult(query).Items.OfType<Episode>();
            episodesToProcess.AddRange(episodes);
        }

        _logger.LogInformation("Found {Count} episodes to check", episodesToProcess.Count);

        if (episodesToProcess.Count == 0)
        {
            _logger.LogInformation("No episodes found to process");
            return;
        }

        var processedCount = 0;
        var addedCount = 0;
        var dbPath = System.IO.Path.Combine(_appPaths.DataPath, "library.db");

        if (!System.IO.File.Exists(dbPath))
        {
            _logger.LogError("Database file not found at path: {DbPath}", dbPath);
            return;
        }

        _logger.LogInformation("Opening database connection to: {DbPath}", dbPath);

        SqliteConnection? connection = null;
        try
        {
            connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Check if the MediaSegments table exists before processing episodes
            var tableExists = await CheckTableExistsAsync(connection, "MediaSegments", cancellationToken).ConfigureAwait(false);
            if (!tableExists)
            {
                _logger.LogWarning("MediaSegments table does not exist in the database. This feature may require Jellyfin 10.10 or later.");
                return;
            }

            _logger.LogInformation("MediaSegments table found, starting to process episodes");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening database connection to {DbPath}", dbPath);
            connection?.Dispose();
            return;
        }

        using (connection)
        {
            foreach (var episode in episodesToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Query segments for this episode
                var segments = await GetSegmentsAsync(connection, episode.Id, cancellationToken).ConfigureAwait(false);
                
                var hasIntro = segments.Any(s => s.Type == "Intro");
                var hasPreview = segments.Any(s => s.Type == "Preview");

                if (hasIntro && !hasPreview)
                {
                    var introSegment = segments.FirstOrDefault(s => s.Type == "Intro");
                    
                    // Validate intro segment before adding preview
                    if (introSegment != null && introSegment.StartTicks > 0)
                    {
                        // Additional validation: ensure intro starts at least 1 second into the episode
                        if (introSegment.StartTicks >= TimeSpan.FromSeconds(1).Ticks)
                        {
                            await AddSegmentAsync(connection, episode.Id, "Preview", 0, introSegment.StartTicks, cancellationToken).ConfigureAwait(false);
                            addedCount++;
                            _logger.LogInformation(
                                "Added preview segment to episode '{Name}' (S{Season}E{Episode}) from 0 to {Duration}s",
                                episode.Name,
                                episode.ParentIndexNumber,
                                episode.IndexNumber,
                                TimeSpan.FromTicks(introSegment.StartTicks).TotalSeconds);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Skipping episode '{Name}' (S{Season}E{Episode}) - intro starts too early at {Duration}s",
                                episode.Name,
                                episode.ParentIndexNumber,
                                episode.IndexNumber,
                                TimeSpan.FromTicks(introSegment.StartTicks).TotalSeconds);
                        }
                    }
                    else if (introSegment != null)
                    {
                        _logger.LogDebug(
                            "Skipping episode '{Name}' (S{Season}E{Episode}) - intro starts at 0 ticks",
                            episode.Name,
                            episode.ParentIndexNumber,
                            episode.IndexNumber);
                    }
                }
                else if (hasIntro && hasPreview)
                {
                    _logger.LogDebug(
                        "Episode '{Name}' (S{Season}E{Episode}) already has preview segment",
                        episode.Name,
                        episode.ParentIndexNumber,
                        episode.IndexNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing episode '{Name}' (ID: {Id})", episode.Name, episode.Id);
                // Continue processing other episodes even if one fails
            }

            processedCount++;
            progress?.Report((double)processedCount / episodesToProcess.Count * 100);
        }

        _logger.LogInformation("Preview segment processing completed. Processed: {Processed}, Added: {Added}", processedCount, addedCount);
        }
    }

    private async Task<bool> CheckTableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@tableName";
        command.Parameters.AddWithValue("@tableName", tableName);
        
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null;
    }

    private async Task<List<SegmentInfo>> GetSegmentsAsync(SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        var segments = new List<SegmentInfo>();
        
        // Try both GUID formats - with and without hyphens
        var itemIdWithoutHyphens = itemId.ToString("N");
        var itemIdWithHyphens = itemId.ToString("D");
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, StreamIndex, Type, StartTicks, EndTicks FROM MediaSegments WHERE ItemId = @itemId1 OR ItemId = @itemId2";
        command.Parameters.AddWithValue("@itemId1", itemIdWithoutHyphens);
        command.Parameters.AddWithValue("@itemId2", itemIdWithHyphens);
        
        try
        {
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                segments.Add(new SegmentInfo
                {
                    Id = reader.GetInt32(0),
                    StreamIndex = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    Type = reader.GetString(2),
                    StartTicks = reader.GetInt64(3),
                    EndTicks = reader.GetInt64(4)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading segments for ItemId {ItemId}", itemId);
            throw;
        }
        
        return segments;
    }

    private async Task AddSegmentAsync(SqliteConnection connection, Guid itemId, string type, long startTicks, long endTicks, CancellationToken cancellationToken)
    {
        // First, determine which GUID format is used in the database by checking existing segments
        var existingItemIdFormat = await GetItemIdFormatAsync(connection, itemId, cancellationToken).ConfigureAwait(false);
        
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO MediaSegments (ItemId, StreamIndex, Type, StartTicks, EndTicks)
            VALUES (@itemId, NULL, @type, @startTicks, @endTicks)";
        command.Parameters.AddWithValue("@itemId", existingItemIdFormat);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@startTicks", startTicks);
        command.Parameters.AddWithValue("@endTicks", endTicks);
        
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Successfully inserted preview segment for ItemId {ItemId} using format: {Format}", itemId, existingItemIdFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting segment for ItemId {ItemId}", itemId);
            throw;
        }
    }

    private async Task<string> GetItemIdFormatAsync(SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        // Check which format is used in the database for this item
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ItemId FROM MediaSegments WHERE ItemId = @itemId1 OR ItemId = @itemId2 LIMIT 1";
        var itemIdWithoutHyphens = itemId.ToString("N");
        var itemIdWithHyphens = itemId.ToString("D");
        command.Parameters.AddWithValue("@itemId1", itemIdWithoutHyphens);
        command.Parameters.AddWithValue("@itemId2", itemIdWithHyphens);
        
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        
        // If we found an existing segment, use that format
        if (result != null)
        {
            return result.ToString() ?? itemIdWithoutHyphens;
        }
        
        // Default to without hyphens (Jellyfin's typical format)
        return itemIdWithoutHyphens;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(2).Ticks
            }
        };
    }

    private class SegmentInfo
    {
        public int Id { get; set; }
        public int? StreamIndex { get; set; }
        public string Type { get; set; } = string.Empty;
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }
    }
}
